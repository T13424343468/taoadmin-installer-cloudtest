#!/usr/bin/env bash
# TaoAdmin 无人值守运维：selfheal 自愈重启 / monitor 监控告警 / backup 自动备份 / inspect 每日巡检
# 用法: ./ops.sh selfheal|monitor|backup|inspect   （配合 crontab 定时执行）
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
RUN_DIR="$ROOT/deploy/run"
OPS_DIR="$ROOT/deploy/ops"
PUBLISH_DIR="$ROOT/deploy/publish"
BACKUP_DIR="$ROOT/deploy/backups"
LOG="$OPS_DIR/ops.log"
ALERT="$OPS_DIR/alerts.log"
DOTNET_ROOT_LOCAL="/root/workspace/.local/dotnet10"
BACKUP_KEEP=7
CPU_ALERT=90; MEM_ALERT=90; DISK_ALERT=90

[[ -n "${DOTNET_ROOT:-}" ]] || export DOTNET_ROOT="$DOTNET_ROOT_LOCAL"
export PATH="$DOTNET_ROOT:$PATH"
mkdir -p "$RUN_DIR" "$OPS_DIR" "$BACKUP_DIR"

log()   { echo "[$(date '+%F %T')] $*" >> "$LOG"; }
alert() { echo "[$(date '+%F %T')] [告警] $*" >> "$ALERT"; log  "[告警] $*"; }
# notify_hook: 预留通知口子（短信/邮件/群机器人），存在即调用
notify_hook() { [[ -x "$OPS_DIR/notify.sh" ]] && "$OPS_DIR/notify.sh" "$1" || true; }

svc_port() { [[ $1 == Gateway ]] && echo 5000 || echo 5001; }

selfheal() {
  local svc
  for svc in Identity Gateway; do
    local port; port=$(svc_port "$svc")
    if ! curl -sf "http://127.0.0.1:$port/health" >/dev/null 2>&1; then
      log "$svc 健康检查失败，尝试拉起"
      local pid_file="$RUN_DIR/${svc}.pid"
      if [[ -f "$pid_file" ]] && kill -0 "$(cat "$pid_file")" 2>/dev/null; then
        kill "$(cat "$pid_file")" 2>/dev/null; sleep 1
      fi
      local asm="$([[ $svc == Gateway ]] && echo TaoAdmin.GatewayService || echo TaoAdmin.IdentityService)"
      local dir="$PUBLISH_DIR/backend/$(echo "$svc" | tr 'A-Z' 'a-z')"
      (cd "$dir" && nohup setsid dotnet "./${asm}.dll" >> "$RUN_DIR/${svc}.log" 2>&1 &)
      sleep 0.2
      pgrep -f "${asm}.dll" | tail -1 > "$pid_file"
      sleep 4
      curl -sf "http://127.0.0.1:$port/health" >/dev/null 2>&1 \
        && log "$svc 自愈成功" \
        || { alert "$svc 自愈失败，需人工介入"; notify_hook "$svc 自愈失败"; }
    else
      log "$svc 正常"
    fi
  done
}

monitor() {
  local load mem_used mem_total disk_usage
  load=$(awk '{printf "%d", $1*100}' /proc/loadavg)
  read -r mem_used mem_total <<<"$(free -m | awk '/^Mem:/{print $3, $2}')"
  mem_used=$((mem_used * 100 / mem_total))
  disk_usage=$(df -P / | awk 'NR==2{gsub("%","");print $5}')
  (( load >= CPU_ALERT ))  && { alert "CPU 负载 ${load}% >= ${CPU_ALERT}%"; notify_hook "CPU 高负载"; }
  (( mem_used >= MEM_ALERT )) && { alert "内存 ${mem_used}% >= ${MEM_ALERT}%"; notify_hook "内存吃紧"; }
  (( disk_usage >= DISK_ALERT )) && { alert "磁盘 ${disk_usage}% >= ${DISK_ALERT}%"; notify_hook "磁盘将满"; }
  selfheal
  log "monitor 完成: 负载${load}% 内存${mem_used}% 磁盘${disk_usage}%"
}

backup() {
  local ts; ts=$(date +%Y%m%d-%H%M%S)
  local out="$BACKUP_DIR/taoadmin-$ts.sql.gz"
  # 凭据不落代码：优先环境变量 MYSQL_PWD（部署时注入），其次 /root/.my.cnf
  if [[ -z "${MYSQL_PWD:-}" && ! -f /root/.my.cnf ]]; then
    alert "备份缺少数据库凭据：请设置环境变量 MYSQL_PWD 或写入 /root/.my.cnf"; return 1
  fi
  if command -v mariadb-dump >/dev/null; then
    mariadb-dump -uroot --all-databases 2>"$OPS_DIR/backup.err" | gzip > "$out"
  elif command -v mysqldump >/dev/null; then
    mysqldump -uroot --all-databases 2>"$OPS_DIR/backup.err" | gzip > "$out"
  else
    alert "未找到备份工具 mariadb-dump/mysqldump"; return 1
  fi
  local tables
  tables=$(zcat "$out" 2>/dev/null | grep -c "CREATE TABLE")
  if (( tables > 0 )); then
    log "备份完成: $out ($(du -h "$out" | cut -f1), $tables 张表)"
  else
    alert "备份无效（0 张表）：$(head -1 "$OPS_DIR/backup.err" 2>/dev/null)"; rm -f "$out"; return 1
  fi
  ls -1t "$BACKUP_DIR"/taoadmin-*.sql.gz 2>/dev/null | tail -n +$((BACKUP_KEEP + 1)) | xargs -r rm -f --
  log "保留策略: 最近 $BACKUP_KEEP 份"
}

inspect() {
  local report="$OPS_DIR/inspect-$(date +%F).md"
  {
    echo "# TaoAdmin 每日巡检 $(date '+%F %T')"
    echo "## 服务状态"
    for svc in Identity Gateway; do
      curl -sf "http://127.0.0.1:$(svc_port "$svc")/health" >/dev/null 2>&1 \
        && echo "- $svc: UP" || echo "- $svc: **DOWN**"
    done
    echo "## 备份新鲜度"
    if ls -1t "$BACKUP_DIR"/taoadmin-*.sql.gz >/dev/null 2>&1; then
      local latest; latest=$(ls -1t "$BACKUP_DIR"/taoadmin-*.sql.gz | head -1)
      local age; age=$(( ($(date +%s) - $(stat -c %Y "$latest")) / 3600 ))
      echo "- 最新备份: $(basename "$latest")（$age 小时前）"
      (( age > 26 )) && echo "- **备份超期（>26h），检查 backup 定时任务**"
    else
      echo "- **无备份文件**"
    fi
    echo "## 近 24h 错误扫描"
    grep -ci "error\|exception" "$RUN_DIR"/*.log 2>/dev/null | sed 's/^/- /' || echo "- 无错误日志"
    echo "## 告警记录（近 24h）"
    find "$ALERT" -mmin -1440 -exec tail -5 {} \; 2>/dev/null || echo "- 无"
  } > "$report"
  log "巡检报告: $report"
}

case "${1:-}" in
  selfheal) selfheal ;;
  monitor)  monitor ;;
  backup)   backup ;;
  inspect)  inspect ;;
  *) echo "用法: $0 selfheal|monitor|backup|inspect"; exit 1 ;;
esac
