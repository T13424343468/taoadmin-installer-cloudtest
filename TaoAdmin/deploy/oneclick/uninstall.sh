#!/usr/bin/env bash
# ============================================================================
# TaoAdmin + 文件中心 Linux 一键卸载（配套 install.sh）
# PACKAGE VERSION: LINUX-UNINSTALLER-V1 (2026-11-20)
#
# 用法:
#   ./uninstall.sh              # 交互确认后卸载
#   ./uninstall.sh --force      # 不询问直接卸载
#   ./uninstall.sh --keep-data  # 保留数据库数据（只停服务删产物）
#
# 卸载口径：停全部服务 → 容器路线 down -v 删卷 → 删发布产物/运行日志/断点状态
#           → 数据库数据删除前二次确认 → 源码完整保留
# ============================================================================
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
RUN_DIR="$ROOT/deploy/run"
PUBLISH_DIR="$ROOT/deploy/publish"
STATE=/var/tmp/taoadmin-deploy.state
DEPLOY_LOG=/var/tmp/taoadmin-deploy.log
FORCE=0
KEEP_DATA=0

while [[ $# -gt 0 ]]; do
  case "$1" in
    --force) FORCE=1; shift ;;
    --keep-data) KEEP_DATA=1; shift ;;
    *) echo "未知参数: $1"; exit 1 ;;
  esac
done

log()  { echo "[$(date '+%F %T')] $*"; }

confirm() {
  [[ $FORCE == 1 ]] && return 0
  read -rp "$1 (y/N): " ans
  [[ "$ans" =~ ^[Yy] ]]
}

log "==================== 一键卸载 ===================="

# 1) 容器路线：连容器与数据卷一起删
if command -v docker >/dev/null 2>&1; then
  if confirm "检测到 Docker：将执行 docker compose down -v（删除容器与数据卷，含数据库数据）。继续？"; then
    (cd "$ROOT" && docker compose down -v --remove-orphans 2>/dev/null) && log "容器与数据卷已删除"
  else
    log "跳过容器清理"
  fi
fi

# 2) 停止原生服务（按 PID 文件 + 端口兜底）
for f in "$RUN_DIR"/*.pid; do
  [[ -f "$f" ]] || continue
  pid=$(cat "$f" | tail -1)
  if [[ "$pid" =~ ^[0-9]+$ ]] && kill -0 "$pid" 2>/dev/null; then
    kill "$pid" 2>/dev/null && log "已停止 $(basename "$f" .pid)（PID $pid）"
  fi
done
for port in 5000 5001 5002 5003; do
  pids=$(ss -tlnp 2>/dev/null | grep ":$port " | grep -oP 'pid=\K[0-9]+' | sort -u)
  for pid in $pids; do
    kill "$pid" 2>/dev/null && log "已停止端口 $port 上的进程（PID $pid）"
  done
done

# 3) 删除发布产物 / 运行日志 / 断点状态 / 前端构建缓存
rm -rf "$PUBLISH_DIR" && log "已删除发布产物"
rm -rf "$ROOT/frontend/node_modules" "$ROOT/frontend/dist" && log "已删除前端依赖缓存与构建产物"
rm -rf "$RUN_DIR" && log "已删除运行目录（PID/日志）"
rm -f "$STATE" "$DEPLOY_LOG" && log "已清除断点状态与部署日志"

# 4) 数据库数据（原生路线）：删除前二次确认
if [[ $KEEP_DATA != 1 ]] && (command -v mysql >/dev/null 2>&1 || command -v mariadb >/dev/null 2>&1); then
  if confirm "是否删除数据库 tao_admin / tao_files（数据不可恢复）？"; then
    MYSQL_CLI=$(command -v mysql || command -v mariadb)
    "$MYSQL_CLI" -uroot -e "DROP DATABASE IF EXISTS tao_admin; DROP DATABASE IF EXISTS tao_files;" 2>/dev/null \
      && log "数据库已删除" || log "数据库删除未执行（可能需要口令或服务未运行，可手动清理）"
  else
    log "已保留数据库数据（--keep-data 同效）"
  fi
fi

log "=================================================="
log "卸载完成：服务已停止，产物/状态已清除，源码完整保留。"
