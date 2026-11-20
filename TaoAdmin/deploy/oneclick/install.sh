#!/usr/bin/env bash
# ============================================================================
# TaoAdmin + 文件中心 Linux 一键安装（路线自选：容器 / 原生）
# PACKAGE VERSION: LINUX-INSTALLER-V1 (2026-11-20)
#
# 用法:
#   ./install.sh                    # 交互菜单选路线
#   ./install.sh --route docker     # 直接走容器路线
#   ./install.sh --route native     # 直接走原生路线
#   ./install.sh --force            # 忽略断点从头执行
# 卸载: ./uninstall.sh
#
# 容器路线 —— 自动安装 Docker（如缺失，需 sudo），然后 docker compose 一键起全部服务
# 原生路线 —— 不用容器：检查/补装 .NET 10 SDK、Node 22、MySQL/MariaDB、Redis，
#             然后调用 deploy.sh 完成编译、初始化与服务拉起（幂等 + 断点续跑）
# ============================================================================
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
STATE=/var/tmp/taoadmin-deploy.state
ROUTE=""
FORCE=0

while [[ $# -gt 0 ]]; do
  case "$1" in
    --route) ROUTE="$2"; shift 2 ;;
    --force) FORCE=1; shift ;;
    *) echo "未知参数: $1"; exit 1 ;;
  esac
done

log()  { echo "[$(date '+%F %T')] $*"; }
fail() { log "[失败] $*"; exit 1; }

have() { command -v "$1" >/dev/null 2>&1; }

# —— 路线选择（用户自己选）——
choose_route() {
  if [[ -n "$ROUTE" ]]; then log "路线（参数指定）: $ROUTE"; return; fi
  echo ""
  echo "==================== 请选择安装路线 ===================="
  echo "  [1] 容器路线 —— Docker 一键编排全部服务"
  echo "        （未安装 Docker 会自动安装：需要 sudo 权限）"
  echo "  [2] 原生路线 —— 不用任何容器，直接编译运行"
  echo "        （缺依赖会提示自动补装：.NET 10 SDK / Node 22 / MySQL / Redis）"
  echo "========================================================"
  read -rp "输入 1 或 2 后回车（直接回车默认选 2 原生路线）: " choice
  if [[ "$choice" == "1" ]]; then ROUTE="docker"; else ROUTE="native"; fi
}

# —— 容器路线 ——
docker_ok() { have docker && docker info >/dev/null 2>&1; }

install_docker() {
  if docker_ok; then log "Docker 引擎已就绪，跳过安装"; return; fi
  if ! have docker; then
    log "未检测到 Docker，开始自动安装（需要 sudo）……"
    if have apt-get; then
      curl -fsSL https://get.docker.com | sudo sh || fail "Docker 自动安装失败，可手动安装后重跑（自动续跑）"
    elif have yum; then
      curl -fsSL https://get.docker.com | sudo sh || fail "Docker 自动安装失败，可手动安装后重跑（自动续跑）"
    else
      fail "未识别的包管理器，请手动安装 Docker 后重跑本脚本"
    fi
  fi
  log "启动 Docker 引擎并等待就绪……"
  sudo systemctl enable --now docker 2>/dev/null || true
  for i in $(seq 1 60); do
    docker_ok && { log "Docker 引擎就绪"; return; }
    sleep 5
  done
  fail "Docker 引擎未就绪，请检查后重跑本脚本（自动续跑）"
}

route_docker() {
  install_docker
  log "容器路线：docker compose 一键拉起全部服务……"
  (cd "$ROOT" && docker compose up -d) || fail "docker compose up 失败"
  log "等待网关健康检查……"
  for i in $(seq 1 30); do
    curl -sf http://127.0.0.1:5000/health >/dev/null 2>&1 && {
      log "容器路线部署完成：前端 http://localhost:80  网关 http://localhost:5000"
      log "账号: admin / Admin@123（请立即修改默认口令）"
      return 0
    }
    sleep 4
  done
  fail "容器路线健康检查未通过，请查看: docker compose logs gateway"
}

# —— 原生路线 ——
RUNTIME_DIR="$ROOT/deploy/runtime"
DB_PWD="Micro@2026"   # 与 appsettings 默认连接串一致

dotnet_ok() { have dotnet && dotnet --version 2>/dev/null | grep -q "^10\."; }

# Node 引擎需满足 rolldown 要求：^20.19.0 || >=22.12.0（不满足时 npm 静默跳过原生绑定）
node_ok() {
  have node || return 1
  node -e 'const v=process.versions.node.split(".").map(Number);const ok=(v[0]===20&&(v[1]>19||(v[1]===19)))||(v[0]>22)||(v[0]===22&&v[1]>=12);process.exit(ok?0:1)' 2>/dev/null
}

ensure_dotnet() {
  if dotnet_ok; then log ".NET 10 SDK 已就绪"; return; fi
  log "安装 .NET 10 SDK（便携模式 -> $RUNTIME_DIR/dotnet10）……"
  mkdir -p "$RUNTIME_DIR"
  local script="$RUNTIME_DIR/dotnet-install.sh"
  curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$script" || fail "dotnet-install.sh 下载失败"
  bash "$script" --channel 10.0 --install-dir "$RUNTIME_DIR/dotnet10" >> /var/tmp/taoadmin-dotnet-install.log 2>&1 || fail ".NET 10 SDK 安装失败（日志见 /var/tmp/taoadmin-dotnet-install.log）"
  export DOTNET_ROOT="$RUNTIME_DIR/dotnet10"
  export PATH="$DOTNET_ROOT:$PATH"
  dotnet --version | grep -q "^10\." || fail ".NET SDK 版本校验失败"
}

ensure_node() {
  if node_ok; then log "Node.js 已就绪 ($(node --version))"; return; fi
  log "安装 Node.js 22（便携模式 -> $RUNTIME_DIR/node22）……"
  local ver="v22.23.5"
  local tgz="node-$ver-linux-x64.tar.xz"
  mkdir -p "$RUNTIME_DIR/node22"
  curl -fsSL "https://nodejs.org/dist/$ver/$tgz" -o "/tmp/$tgz" || fail "Node $ver 下载失败"
  tar -xJf "/tmp/$tgz" -C "$RUNTIME_DIR/node22" --strip-components=1 || fail "Node 解压失败"
  rm -f "/tmp/$tgz"
  export PATH="$RUNTIME_DIR/node22/bin:$PATH"
  node --version || fail "Node 校验失败"
}

# 数据库客户端无关：mariadb 与 mysql 二选一（Ubuntu 预装可能是 mysql 客户端）
db_admin() { # root socket 免密管理入口
  if have mariadb; then sudo mariadb "$@"; else sudo mysql "$@"; fi
}
db_tcp() { # 应用同款 TCP+口令入口
  if have mariadb; then mariadb "$@"; else mysql "$@"; fi
}

ensure_mysql() {
  if ! have mysql && ! have mariadb; then
    log "安装 MariaDB（apt）……"
    sudo apt-get update -y >> /var/tmp/taoadmin-apt.log 2>&1
    sudo apt-get install -y mariadb-server >> /var/tmp/taoadmin-apt.log 2>&1 || fail "mariadb 安装失败（日志见 /var/tmp/taoadmin-apt.log）"
  fi
  # 服务端二进制检测：PATH 或 /usr/sbin 下找 mariadbd/mysqld
  local server_bin=""
  for c in mariadbd mysqld; do
    if have "$c" || [[ -x "/usr/sbin/$c" ]]; then server_bin="$c"; break; fi
  done
  sudo service mariadb start 2>/dev/null || sudo service mysql start 2>/dev/null || true
  local ready=0
  for i in $(seq 1 15); do
    if db_admin -e "SELECT 1" >/dev/null 2>&1; then ready=1; break; fi
    # 服务未注册/未起：第 5 轮起初始化数据目录（如未初始化）并直接拉起服务端
    if [[ $i -eq 5 && -n "$server_bin" ]]; then
      if [[ -d /var/lib/mysql ]] && ! ls /var/lib/mysql/mysql >/dev/null 2>&1; then
        # 数据目录存在但未初始化（预装残留）：挪开残留后全新初始化
        log "数据目录存在但未初始化，挪开残留目录后重新初始化……"
        sudo mv /var/lib/mysql "/var/lib/mysql.residual.$(date +%s)" 2>/dev/null || sudo rm -rf /var/lib/mysql
      fi
      if ! ls /var/lib/mysql/mysql >/dev/null 2>&1; then
        log "初始化数据库数据目录……"
        if have mariadb-install-db; then
          sudo mariadb-install-db --user=mysql --datadir=/var/lib/mysql >> /var/tmp/taoadmin-apt.log 2>&1 || true
        else
          sudo "$server_bin" --initialize-insecure --user=mysql --datadir=/var/lib/mysql >> /var/tmp/taoadmin-apt.log 2>&1 || true
        fi
      fi
      local sb="/usr/sbin/$server_bin"
      [[ -x "$sb" ]] || sb="$server_bin"
      sudo "$sb" --user=mysql --daemonize >> /var/tmp/taoadmin-apt.log 2>&1 || true
    fi
    sleep 2
  done
  if [[ $ready != 1 ]]; then
    if [[ -z "$server_bin" ]]; then
      log "未检测到数据库服务端，补装（mariadb-server）……"
      sudo apt-get install -y mariadb-server >> /var/tmp/taoadmin-apt.log 2>&1 || fail "mariadb-server 安装失败（日志见 /var/tmp/taoadmin-apt.log）"
      sudo service mariadb start 2>/dev/null || sudo service mysql start 2>/dev/null || true
      for i in $(seq 1 15); do db_admin -e "SELECT 1" >/dev/null 2>&1 && { ready=1; break; }; sleep 2; done
    fi
    if [[ $ready != 1 ]]; then
      # 诊断信息进主日志，便于定位（不改动用户环境）
      log "数据库启动失败诊断：已安装包 -> $(dpkg -l 2>/dev/null | grep -iE 'mysql|mariadb' | awk '{print $2"="$3}' | tr '\n' ' ')"
      log "数据目录 -> $(ls -la /var/lib/mysql 2>/dev/null | head -8 | tr '\n' ' ')"
      log "错误日志尾部 -> $(tail -20 /var/log/mysql/error.log /var/lib/mysql/*.err 2>/dev/null | tr '\n' ' ')"
      fail "数据库服务未就绪（socket/TCP 均无法连接，详见 /var/tmp/taoadmin-apt.log）"
    fi
  fi
  # Ubuntu 默认 root 走 unix_socket 免密，应用走 TCP+口令；统一设为与 appsettings 一致的口令
  if ! db_tcp -uroot -p"$DB_PWD" -h127.0.0.1 -e "SELECT 1" >/dev/null 2>&1; then
    db_admin -e "ALTER USER 'root'@'localhost' IDENTIFIED BY '$DB_PWD'; FLUSH PRIVILEGES;" || fail "数据库 root 口令设置失败"
  fi
  db_tcp -uroot -p"$DB_PWD" -h127.0.0.1 -e "SELECT 1" >/dev/null 2>&1 || fail "数据库连接校验失败"
  log "MySQL/MariaDB 已就绪（root 口令与 appsettings 一致）"
}

ensure_redis() {
  if have redis-server && redis-cli ping >/dev/null 2>&1; then log "Redis 已就绪"; return; fi
  if ! have redis-server; then
    log "安装 Redis（apt）……"
    sudo apt-get update -y >> /var/tmp/taoadmin-apt.log 2>&1
    sudo apt-get install -y redis-server >> /var/tmp/taoadmin-apt.log 2>&1 || fail "redis 安装失败（日志见 /var/tmp/taoadmin-apt.log）"
  fi
  sudo service redis-server start 2>/dev/null || sudo redis-server --daemonize yes || true
  for i in $(seq 1 15); do redis-cli ping >/dev/null 2>&1 && { log "Redis 已就绪"; return; }; sleep 2; done
  fail "Redis 未就绪"
}

ensure_deps() {
  local missing=()
  dotnet_ok || missing+=("dotnet")
  node_ok   || missing+=("node")
  # MySQL/Redis 不做"已安装即跳过"：预装实例可能未设口令/未启动，
  # 无条件走幂等就绪校验（统一 root 口令、拉起服务），保证与应用配置一致
  if [[ ${#missing[@]} -gt 0 ]]; then
    log "检测到缺失/不达标依赖: ${missing[*]}"
    if [[ "${AUTO_DEPS:-0}" != "1" ]]; then
      read -rp "是否自动补装（需要 sudo，Debian/Ubuntu 用 apt）？(y/N): " ans
      [[ "$ans" =~ ^[Yy] ]] || fail "请手动安装缺失依赖后重跑本脚本（自动续跑）"
    fi
    if ! have apt-get; then
      fail "非 apt 系发行版，请手动安装: ${missing[*]}"
    fi
    for dep in "${missing[@]}"; do
      case "$dep" in
        dotnet) ensure_dotnet ;;
        node)   ensure_node ;;
      esac
    done
  else
    log ".NET / Node 依赖齐全，跳过补装"
  fi
  ensure_mysql
  ensure_redis
}

route_native() {
  ensure_deps
  log "原生路线：调用 deploy.sh 完成编译、初始化与服务拉起……"
  if [[ $FORCE == 1 ]]; then
    (cd "$ROOT/deploy/oneclick" && FORCE=1 ./deploy.sh) || fail "deploy.sh 执行失败"
  else
    (cd "$ROOT/deploy/oneclick" && ./deploy.sh) || fail "deploy.sh 执行失败"
  fi
  log "原生路线部署完成：网关 http://127.0.0.1:5000  前端 http://127.0.0.1:8080（四服务 + 前端全部拉起）"
  log "账号: admin / Admin@123（请立即修改默认口令）"
  log "卸载: ./uninstall.sh（一键卸载，卸载干干净净）"
}

choose_route
if [[ "$ROUTE" == "docker" ]]; then route_docker; else route_native; fi
