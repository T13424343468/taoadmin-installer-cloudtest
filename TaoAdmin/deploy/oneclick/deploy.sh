#!/usr/bin/env bash
# TaoAdmin 单机一键部署（幂等 + 断点续跑）
# 用法: ./deploy.sh [--force]   --force 忽略断点从头执行
# 状态: /var/tmp/taoadmin-deploy.state 记录已完成步骤，中断重跑自动续。
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"   # TaoAdmin 根目录
STATE=/var/tmp/taoadmin-deploy.state
LOG=/var/tmp/taoadmin-deploy.log
BACKEND_DIR="$ROOT/backend"
TF_ROOT="${TAOFILES_ROOT:-$ROOT/../TaoFiles}"   # 文件中心与 TaoAdmin 为兄弟目录
FRONTEND_DIR="$ROOT/frontend"
PUBLISH_DIR="$ROOT/deploy/publish"
RUN_DIR="$ROOT/deploy/run"
DOTNET_ROOT_LOCAL="/root/workspace/.local/dotnet10"
DB_PWD="Micro@2026"   # 与 appsettings 默认连接串一致

[[ -n "${DOTNET_ROOT:-}" ]] || export DOTNET_ROOT="$DOTNET_ROOT_LOCAL"
export PATH="$DOTNET_ROOT:$PATH"
mkdir -p "$PUBLISH_DIR" "$RUN_DIR" "$(dirname "$STATE")"
touch "$STATE" "$LOG"

log()  { echo "[$(date '+%F %T')] $*" | tee -a "$LOG"; }
fail() { log "[失败] $*"; echo "修复指引见: $LOG 与 deploy/oneclick/README.md"; exit 1; }
done_step() { grep -qx "$1" "$STATE" 2>/dev/null; }
mark() { echo "$1" >> "$STATE"; }

STEP_START=$(date +%s)
step() {  # step <名称> <命令...>  —— 已完成则跳过
  local name="$1"; shift
  if done_step "$name" && [[ "${FORCE:-0}" != "1" ]]; then log "跳过（已完成）: $name"; return 0; fi
  log "== $name =="
  "$@" >> "$LOG" 2>&1 || fail "$name 执行失败，详见日志末尾"
  mark "$name"
  log "== $name 完成 =="
}

# —— 步骤 1: 依赖检查 ——
check_deps() {
  command -v dotnet >/dev/null || fail "未找到 dotnet（.NET 10 SDK）。请安装或设置 DOTNET_ROOT=$DOTNET_ROOT_LOCAL"
  command -v node   >/dev/null || fail "未找到 node（前端构建需要）。请安装 Node.js 20+"
  command -v npm    >/dev/null || fail "未找到 npm"
  dotnet --version | grep -q "^10\." || log "[提示] dotnet 版本非 10.x: $(dotnet --version)"
}
step 依赖检查 check_deps

# —— 步骤 2: 后端发布（按服务分目录：多服务同目录发布会撞 appsettings.json，NETSDK1152）——
publish_backend() {
  local ok=1
  dotnet publish "$BACKEND_DIR/src/TaoAdmin.IdentityService" -c Release -o "$PUBLISH_DIR/backend/identity" --nologo || ok=0
  dotnet publish "$BACKEND_DIR/src/TaoAdmin.GatewayService"  -c Release -o "$PUBLISH_DIR/backend/gateway"  --nologo || ok=0
  dotnet publish "$BACKEND_DIR/src/TaoAdmin.TaskService"     -c Release -o "$PUBLISH_DIR/backend/task"     --nologo || ok=0
  dotnet publish "$TF_ROOT/src/TaoFiles.Api"                 -c Release -o "$PUBLISH_DIR/backend/taofiles" --nologo || ok=0
  [[ $ok == 1 ]] || return 1
}
step 后端发布 publish_backend

# —— 步骤 3: 前端构建 ——
build_frontend() {
  # NFS 环境下 node_modules 可能残留占用文件导致 npm ci 失败；已有可构建依赖时跳过重装
  if (cd "$FRONTEND_DIR" && [ -d node_modules ] && ls node_modules/.bin/vite >/dev/null 2>&1); then
    log "node_modules 已就绪，跳过 npm ci（避免 NFS 残留文件阻塞）"
  else
    (cd "$FRONTEND_DIR" && npm ci --no-audit --no-fund) || return 1
  fi
  (cd "$FRONTEND_DIR" && npm run build) || return 1
}
step 前端构建 build_frontend

# —— 步骤 4: 数据库初始化（幂等）——
init_db() {
  local sql="$ROOT/deploy/sql/tao_admin_init.sql"
  [[ -f "$sql" ]] || { echo "缺初始化 SQL: $sql"; return 1; }
  local client=""
  if command -v mariadb >/dev/null; then client="mariadb"; elif command -v mysql >/dev/null; then client="mysql"; else
    echo "未找到 mariadb/mysql 客户端"; return 1
  fi
  # 口令优先（与 appsettings 一致，install.sh 已统一设置），socket 免密兜底（沙箱/本机 unix_socket 场景）
  if $client -uroot -p"$DB_PWD" -h127.0.0.1 < "$sql" >/dev/null 2>&1; then return 0; fi
  if $client -uroot --socket=/run/mysqld/mysqld.sock < "$sql" >/dev/null 2>&1; then return 0; fi
  $client -uroot < "$sql"
}
step 数据库初始化 init_db

# —— 步骤 5: 服务拉起（已存活则跳过）——
start_services() {
  local svc asm dir port
  for spec in "Identity:TaoAdmin.IdentityService:5001" "Gateway:TaoAdmin.GatewayService:5000" "Task:TaoAdmin.TaskService:5002" "Taofiles:TaoFiles.Api:5003"; do
    svc="${spec%%:*}"; asm="${spec#*:}"; asm="${asm%%:*}"; port="${spec##*:}"
    if curl -sf "http://127.0.0.1:$port/health" >/dev/null 2>&1; then
      echo "$svc 已在运行，跳过"; continue
    fi
    local dir="$PUBLISH_DIR/backend/$(echo "$svc" | tr 'A-Z' 'a-z')"
    # 文件中心存储根目录必须存在，否则 PhysicalFileProvider 启动即抛 DirectoryNotFoundException
    [[ $svc == Taofiles ]] && mkdir -p "$dir/uploads"
    # 必须以服务目录为工作目录启动，否则读不到 appsettings.json（连接串等配置为空）
    (cd "$dir" && nohup setsid dotnet "./${asm}.dll" >> "$RUN_DIR/${svc}.log" 2>&1 &)
    sleep 0.2
    pgrep -f "${asm}.dll" | tail -1 >> "$RUN_DIR/${svc}.pid"
  done
  # 冷启动可能含首次建库迁移，健康检查重试至多 120s
  local ok=1
  for i in $(seq 1 60); do
    ok=1
    for port in 5000 5001 5002 5003; do
      curl -sf "http://127.0.0.1:$port/health" >/dev/null 2>&1 || { ok=0; break; }
    done
    [[ $ok == 1 ]] && { log "四服务健康检查通过（第 ${i} 次尝试）"; break; }
    sleep 2
  done
  if [[ $ok != 1 ]]; then
    for svc in Identity Gateway Task Taofiles; do
      tail -20 "$RUN_DIR/${svc}.log" >> "$LOG" 2>/dev/null || true
    done
    for port in 5000 5001 5002 5003; do
      curl -sf "http://127.0.0.1:$port/health" >/dev/null 2>&1 || { echo "端口 $port 健康检查未过"; return 1; }
    done
  fi
}
step 服务拉起 start_services

# —— 步骤 6: 前端产物落位 ——
place_frontend() {
  mkdir -p "$PUBLISH_DIR/frontend" && cp -r "$FRONTEND_DIR/dist/." "$PUBLISH_DIR/frontend/"
  [[ -f "$PUBLISH_DIR/frontend/index.html" ]] || { echo "index.html 缺失"; return 1; }
}
step 前端落位 place_frontend

# —— 步骤 7: 前端静态服务（Node 托管 + /api 反代网关，免 Nginx）——
start_frontend() {
  if curl -sf http://127.0.0.1:8080/ >/dev/null 2>&1; then echo "前端已在运行，跳过"; return 0; fi
  local server="$ROOT/deploy/windows/frontend-server.js"
  [[ -f "$server" ]] || { echo "缺前端服务脚本: $server"; return 1; }
  (cd "$PUBLISH_DIR" && FRONTEND_DIST="$PUBLISH_DIR/frontend" nohup setsid node "$server" >> "$RUN_DIR/frontend.log" 2>&1 &)
  local i
  for i in $(seq 1 15); do
    curl -sf http://127.0.0.1:8080/ >/dev/null 2>&1 && { log "前端健康检查通过（8080）"; return 0; }
    sleep 2
  done
  tail -20 "$RUN_DIR/frontend.log" >> "$LOG" 2>/dev/null || true
  echo "前端健康检查未过"; return 1
}
step 前端服务 start_frontend

STEP_END=$(date +%s)
log "部署完成，用时 $((STEP_END-STEP_START))s。网关 http://127.0.0.1:5000  前端产物 $PUBLISH_DIR/frontend"
log "运维: deploy/oneclick/ops.sh {selfheal|monitor|backup|inspect}"
log "[安全提示] 请立即修改默认管理员口令并配置强密码策略。"
