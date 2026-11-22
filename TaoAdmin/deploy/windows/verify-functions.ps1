# verify-functions.ps1 — 部署后功能全检：真实登录 + 各业务接口 + 前端页面
# 任一步失败即非零退出，云测视为红灯。
param(
    [string]$Gateway = 'http://127.0.0.1:5000',
    [string]$Files = 'http://127.0.0.1:5003',
    [string]$Frontend = 'http://127.0.0.1:8080',
    [string]$Username = 'admin',
    [string]$Password = 'Admin@123'
)
$ErrorActionPreference = 'Stop'

function Wait-Ready($url, $seconds = 300) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        try {
            $r = Invoke-WebRequest $url -UseBasicParsing -TimeoutSec 5
            if ($r.StatusCode -lt 500) { return $r }
        } catch { }
        Start-Sleep -Seconds 4
    }
    throw "not ready: $url"
}

# ---------- 1) 服务可达 ----------
foreach ($u in "$Gateway/health", "$Files/health", $Frontend) {
    Wait-Ready $u | Out-Null
    Write-Host "health OK: $u"
}

# ---------- 2) 关闭验证码开关（仅测试环境；服务侧配置缓存 60 秒内生效） ----------
# sys_site_config columns are PascalCase (ConfigKey/ConfigValue) per tao_admin_init.sql.
$sql = "UPDATE sys_site_config SET ConfigValue='0' WHERE ConfigKey='loginCaptchaRequired';"
$sql += "INSERT INTO sys_site_config (Id, ConfigKey, ConfigValue, CreatedAt, UpdatedAt, IsDeleted) SELECT 5280300000000000999,'loginCaptchaRequired','0',NOW(),NOW(),0 WHERE NOT EXISTS (SELECT 1 FROM sys_site_config WHERE ConfigKey='loginCaptchaRequired');"
# Pipe SQL via stdin (mysql reads the script from standard input).
$sqlFile = Join-Path $env:TEMP 'disable-captcha.sql'
Set-Content -Path $sqlFile -Value $sql -Encoding utf8
$nativeMysql = 'TaoAdmin/deploy/windows/runtime/mysql8/bin/mysql.exe'
if (Test-Path $nativeMysql) {
    Write-Host "native route: disabling captcha via portable mysql client (stdin)"
    # pwsh splits bare '-h127.0.0.1' into '-h127' + '.0.0.1' (mysql then prints usage) —
    # proven on pwsh 7.4.1; long options pass through intact.
    Get-Content $sqlFile | & $nativeMysql --host=127.0.0.1 --user=root --password=Micro@2026 tao_admin 2>&1 | ForEach-Object { Write-Host "  $_" }
    if ($LASTEXITCODE -ne 0) { throw "native mysql exec failed (exit $LASTEXITCODE)" }
} else {
    Write-Host "container route: disabling captcha via docker exec (stdin)"
    # Fresh Docker Desktop installs mid-job are not on the runner's fixed PATH for
    # later steps — fall back to the standard install location.
    $dockerCmd = 'docker'
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
        $dockerCmd = 'C:\Program Files\Docker\Docker\resources\bin\docker.exe'
        if (-not (Test-Path $dockerCmd)) { throw "docker not found on PATH or at $dockerCmd" }
        Write-Host "docker resolved from install path: $dockerCmd"
    }
    Get-Content $sqlFile | & $dockerCmd exec -i taoadmin-mysql mysql --user=root --password=Micro@2026 tao_admin 2>&1 | ForEach-Object { Write-Host "  $_" }
    if ($LASTEXITCODE -ne 0) { throw "docker exec mysql failed (exit $LASTEXITCODE)" }
}

# ---------- 3) 真实登录（走网关，业务码 + token 双断言；配置缓存最长 60 秒） ----------
$loginBody = @{ username = $Username; password = $Password } | ConvertTo-Json
$token = $null
$deadline = (Get-Date).AddSeconds(120)
while ((Get-Date) -lt $deadline) {
    try {
        $login = Invoke-RestMethod -Uri "$Gateway/api/identity/auth/login" -Method Post -Body $loginBody -ContentType 'application/json' -TimeoutSec 20
        if ($login.code -eq 200 -and $login.data.token) { $token = $login.data.token; break }
        if ($login.message -notmatch '验证码') { throw "login failed: code=$($login.code) msg=$($login.message)" }
        Write-Host "captcha still enforced (config cache), retrying..."
    } catch {
        if ($_.Exception.Message -notmatch '验证码') { throw }
        Write-Host "captcha still enforced (config cache), retrying..."
    }
    Start-Sleep -Seconds 10
}
if (-not $token) { throw 'login failed: captcha never cleared within 120s' }
Write-Host "login OK (token acquired)"
$headers = @{ Authorization = "Bearer $token" }

# ---------- 4) 业务接口逐项验证（业务码必须 200） ----------
$checks = @(
    @{ Name = '用户分页';   Url = "$Gateway/api/identity/users?pageIndex=1&pageSize=10" },
    @{ Name = '角色分页';   Url = "$Gateway/api/identity/roles?pageIndex=1&pageSize=10" },
    @{ Name = '菜单树';     Url = "$Gateway/api/identity/menus/tree" },
    @{ Name = '字典类型';   Url = "$Gateway/api/identity/dict/types" },
    @{ Name = '操作日志';   Url = "$Gateway/api/identity/operation-logs?pageIndex=1&pageSize=10" },
    @{ Name = '定时任务';   Url = "$Gateway/api/job/jobs?pageIndex=1&pageSize=10" },
    @{ Name = '文件列表';   Url = "$Files/api/files?pageIndex=1&pageSize=10" }
)
foreach ($c in $checks) {
    $r = Invoke-RestMethod -Uri $c.Url -Headers $headers -TimeoutSec 20
    if ($r.code -ne 200) { throw "functional check FAILED [$($c.Name)]: code=$($r.code) msg=$($r.message)" }
    Write-Host "function OK: $($c.Name)"
}

# ---------- 5) 前端页面是真实应用页 ----------
$fe = Invoke-WebRequest $Frontend -UseBasicParsing -TimeoutSec 15
if ($fe.Content -notmatch '<div id="app"') { throw 'frontend page does not look like the app entry' }
Write-Host "frontend page OK"

Write-Host 'ALL FUNCTION CHECKS PASSED'
exit 0
