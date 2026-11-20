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
# 原生路线 —— 不用容器：检查/补装 .NET 10 SDK、Node 20、MySQL/MariaDB、Redis，
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
  echo "        （缺依赖会提示自动补装：.NET 10 SDK / Node 20 / MySQL / Redis）"
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
ensure_deps() {
  local missing=()
  have dotnet || missing+=("dotnet")
  have node   || missing+=("node")
  have mysql || have mariadb || missing+=("mysql")
  have redis-server || missing+=("redis")
  if [[ ${#missing[@]} -eq 0 ]]; then log "依赖齐全，跳过补装"; return; fi
  log "检测到缺失依赖: ${missing[*]}"
  if [[ "${AUTO_DEPS:-0}" != "1" ]]; then
    read -rp "是否自动补装（需要 sudo，Debian/Ubuntu 用 apt）？(y/N): " ans
    [[ "$ans" =~ ^[Yy] ]] || fail "请手动安装缺失依赖后重跑本脚本（自动续跑）"
  fi
  if have apt-get; then
    sudo apt-get update -y
    for dep in "${missing[@]}"; do
      case "$dep" in
        dotnet) log ".NET 10 SDK 请按官方脚本安装: https://dot.net/v1/dotnet-install.sh -Channel 10.0";;
        node)   sudo apt-get install -y nodejs npm || fail "nodejs 安装失败";;
        mysql)  sudo apt-get install -y mariadb-server || fail "mariadb 安装失败";;
        redis)  sudo apt-get install -y redis-server || fail "redis 安装失败";;
      esac
    done
  else
    fail "非 apt 系发行版，请手动安装: ${missing[*]}"
  fi
}

route_native() {
  ensure_deps
  log "原生路线：调用 deploy.sh 完成编译、初始化与服务拉起……"
  if [[ $FORCE == 1 ]]; then
    (cd "$ROOT/deploy/oneclick" && FORCE=1 ./deploy.sh) || fail "deploy.sh 执行失败"
  else
    (cd "$ROOT/deploy/oneclick" && ./deploy.sh) || fail "deploy.sh 执行失败"
  fi
  log "原生路线部署完成：网关 http://127.0.0.1:5000"
  log "前端产物: $ROOT/deploy/publish/frontend（用 Nginx 托管并反代 /api → 5000，配置见 deploy/docker-compose.yml 同目录说明）"
  log "账号: admin / Admin@123（请立即修改默认口令）"
  log "卸载: ./uninstall.sh（一键卸载，卸载干干净净）"
}

choose_route
if [[ "$ROUTE" == "docker" ]]; then route_docker; else route_native; fi
