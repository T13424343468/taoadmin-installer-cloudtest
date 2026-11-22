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

# ---------- 2) 真实登录（走网关，业务码 + token 双断言） ----------
$loginBody = @{ username = $Username; password = $Password } | ConvertTo-Json
$login = Invoke-RestMethod -Uri "$Gateway/api/identity/auth/login" -Method Post -Body $loginBody -ContentType 'application/json' -TimeoutSec 20
if ($login.code -ne 200 -or -not $login.data.token) { throw "login failed: code=$($login.code) msg=$($login.message)" }
$token = $login.data.token
Write-Host "login OK (token acquired)"
$headers = @{ Authorization = "Bearer $token" }

# ---------- 3) 业务接口逐项验证（业务码必须 200） ----------
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

# ---------- 4) 前端页面是真实应用页 ----------
$fe = Invoke-WebRequest $Frontend -UseBasicParsing -TimeoutSec 15
if ($fe.Content -notmatch '<div id="app"') { throw 'frontend page does not look like the app entry' }
Write-Host "frontend page OK"

Write-Host 'ALL FUNCTION CHECKS PASSED'
exit 0
