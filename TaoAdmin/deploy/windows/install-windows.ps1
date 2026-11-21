# ============================================================================
# TaoAdmin + 文件中心 Windows 一键安装 / 一键卸载 / 状态查看
# PACKAGE VERSION: WIN-INSTALLER-V1 (2026-11-20)
#
# 用法（双击 install.bat / clean.bat / status.bat，或命令行）:
#   powershell -ExecutionPolicy Bypass -File install-windows.ps1                 # 交互安装
#   powershell -ExecutionPolicy Bypass -File install-windows.ps1 -Action clean   # 一键卸载
#   powershell -ExecutionPolicy Bypass -File install-windows.ps1 -Action status  # 查看状态
# 可选参数:
#   -Route docker|native   跳过菜单直接指定路线
#   -Force                 卸载不询问 / 安装忽略断点从头执行
#   -KeepRuntimes          卸载时保留下载的运行时（.NET/Node/MySQL/Redis）
#   -DryRun                演练模式：只打印将执行的动作，不做任何改动
#
# 路线说明:
#   容器路线 —— 自动安装 Docker Desktop（如缺失，需管理员权限，可能要求重启），
#               然后 docker compose 一键拉起全部 9 个服务
#   原生路线 —— 不用任何容器：.NET 10 SDK / Node 20 / MySQL 8 / Redis 全部
#               便携安装到 deploy/windows/runtime，直接以 Windows 进程运行
# 断点续跑: runtime\.install-state 记录已完成步骤，中断后重跑自动续
# ============================================================================
param(
  [ValidateSet('install', 'clean', 'status')][string]$Action = 'install',
  [ValidateSet('', 'docker', 'native')][string]$Route = '',
  [switch]$Force,
  [switch]$KeepRuntimes,
  [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

$script:Root       = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path   # TaoAdmin 根目录
$script:WinDir     = Join-Path $Root 'deploy\windows'
$script:RuntimeDir = Join-Path $WinDir 'runtime'
$script:PidDir     = Join-Path $RuntimeDir 'pids'
$script:LogDir     = Join-Path $RuntimeDir 'logs'
$script:DlDir      = Join-Path $RuntimeDir 'downloads'
$script:PublishDir = Join-Path $Root 'deploy\publish'
$script:StateFile  = Join-Path $RuntimeDir '.install-state'
$script:SqlFile    = Join-Path $Root 'deploy\sql\tao_admin_init.sql'
$script:ComposeDir = Join-Path $Root 'deploy'
$script:DbPwd      = 'Micro@2026'
$script:MySQLVer   = '8.0.37'
$script:NodeVer    = 'v22.23.5'

# —— 官方下载源（2026-11-20 逐一核验 200）——
$script:Urls = @{
  dotnetInstall = 'https://dot.net/v1/dotnet-install.ps1'
  nodeZip       = "https://nodejs.org/dist/$($script:NodeVer)/node-$($script:NodeVer)-win-x64.zip"
  mysqlZip      = "https://cdn.mysql.com/Downloads/MySQL-8.0/mysql-$($script:MySQLVer)-winx64.zip"
  redisZip      = 'https://github.com/tporadowski/redis/releases/download/v5.0.14.1/Redis-x64-5.0.14.1.zip'
  dockerSetup   = 'https://desktop.docker.com/win/main/stable/Docker%20Desktop%20Installer.exe'
}

function Write-Log([string]$msg) { Write-Host "[$(Get-Date -Format 'HH:mm:ss')] $msg" }

# ---------------- 断点状态 ----------------
function Get-DoneSet {
  $set = @{}
  if (Test-Path $script:StateFile) {
    Get-Content $script:StateFile | ForEach-Object { $set[$_.Trim()] = $true }
  }
  return $set
}
function Mark-Done([string]$stepId) {
  if (-not (Test-Path $script:StateFile)) { return }
  $cur = Get-Content $script:StateFile
  if ($cur -notcontains $stepId) { Add-Content -Path $script:StateFile -Value $stepId }
}

# ---------------- 进度与整体预估 ----------------
function Show-Progress([int]$doneWeight, [int]$totalWeight, [TimeSpan]$elapsed) {
  $pct = [Math]::Min(100, [int](100.0 * $doneWeight / [Math]::Max(1, $totalWeight)))
  $remainSec = 0
  if ($doneWeight -gt 0) {
    $remainSec = [int]($elapsed.TotalSeconds * ($totalWeight - $doneWeight) / $doneWeight)
  }
  $eta = if ($remainSec -ge 60) { "约 $([int]($remainSec / 60)) 分钟" } else { "约 $remainSec 秒" }
  Write-Host ("  进度 [{0}{1}] {2}%   整体预计剩余 {3}" -f ('#' * [int]($pct / 5)), ('.' * (20 - [int]($pct / 5))), $pct, $eta)
}

# ---------------- 通用工具 ----------------
function Test-Port([int]$port) {
  try {
    $c = New-Object System.Net.Sockets.TcpClient
    $c.Connect('127.0.0.1', $port); $c.Close(); return $true
  } catch { return $false }
}
function Test-Admin {
  if (-not $IsWindows) { return $false }
  $id = [Security.Principal.WindowsIdentity]::GetCurrent()
  return (New-Object Security.Principal.WindowsPrincipal($id)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}
function Invoke-Download([string]$url, [string]$outFile) {
  if (Test-Path $outFile) { Write-Log "已存在，跳过下载: $(Split-Path $outFile -Leaf)"; return }
  Write-Log "下载: $url"
  if ($DryRun) { Write-Host "  [DryRun] 将下载 -> $outFile"; return }
  [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
  Invoke-WebRequest -Uri $url -OutFile $outFile -UseBasicParsing
  if (-not (Test-Path $outFile) -or (Get-Item $outFile).Length -lt 1024) { throw "下载校验失败: $url" }
}
function Expand-Zip([string]$zip, [string]$dest) {
  if ($DryRun) { Write-Host "  [DryRun] 将解压 $(Split-Path $zip -Leaf) -> $dest"; return }
  Expand-Archive -Path $zip -DestinationPath $dest -Force
}
function Wait-HttpOk([string]$url, [int]$tries, [int]$sleepSec) {
  for ($i = 1; $i -le $tries; $i++) {
    try {
      Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 3 | Out-Null
      Write-Log "健康检查通过（第 $i 次尝试）: $url"
      return $true
    } catch { Start-Sleep -Seconds $sleepSec }
  }
  return $false
}

# ---------------- 路线选择（用户自己选） ----------------
function Select-Route {
  if ($Route) { Write-Log "路线（参数指定）: $Route"; return $Route }
  if ($DryRun -or -not [Environment]::UserInteractive) { Write-Log "非交互环境，默认走原生路线"; return 'native' }
  Write-Host ''
  Write-Host '==================== 请选择安装路线 ===================='
  Write-Host '  [1] 容器路线 —— 使用 Docker 一键编排全部服务'
  Write-Host '        （未安装 Docker Desktop 会自动安装：需要管理员权限，可能要求重启）'
  Write-Host '  [2] 原生路线 —— 不用任何容器，MySQL/Redis 直接以 Windows 进程运行'
  Write-Host '        （运行时全部便携安装到部署目录，不污染系统）'
  Write-Host '========================================================'
  $choice = Read-Host '输入 1 或 2 后回车（直接回车默认选 2 原生路线）'
  if ($choice -eq '1') { return 'docker' }
  return 'native'
}

# ---------------- Docker 路线 ----------------
function Wait-DockerEngine {
  # 等待 Docker 引擎就绪（最长 5 分钟），返回 $true/$false
  param([int]$MaxSeconds = 300)
  $deadline = (Get-Date).AddSeconds($MaxSeconds)
  while ((Get-Date) -lt $deadline) {
    if (Get-Command docker -ErrorAction SilentlyContinue) {
      docker info 2>$null | Out-Null
      if ($LASTEXITCODE -eq 0) { return $true }
    }
    Start-Sleep -Seconds 5
  }
  return $false
}

function Start-DockerDesktopApp {
  $dd = 'C:\Program Files\Docker\Docker\Docker Desktop.exe'
  if (Test-Path $dd) { Start-Process -FilePath $dd }
}

function Install-DockerDesktop {
  if (Get-Command docker -ErrorAction SilentlyContinue) {
    # docker CLI 存在不代表引擎在跑（刚装完/刚重启都可能没起来），必须实测引擎
    docker info 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) {
      Write-Log '检测到 Docker 引擎已在运行，跳过安装'
      return
    }
    Write-Log '检测到 docker 命令，但引擎未运行——启动 Docker Desktop 并等待就绪……'
    if (-not $DryRun) {
      Start-DockerDesktopApp
      if (Wait-DockerEngine) { Write-Log 'Docker 引擎已就绪'; return }
      throw 'Docker 引擎 5 分钟内未就绪。若刚安装完 Docker Desktop，请先重启电脑（启用虚拟机平台必须重启），再重跑本脚本（自动续跑）'
    } else { Write-Host '  [DryRun] 将启动 Docker Desktop 并等待引擎就绪'; return }
  }
  Write-Log '未检测到 Docker Desktop，开始自动安装……'
  if (-not $DryRun -and -not (Test-Admin)) {
    throw '安装 Docker Desktop 需要管理员权限：请右键“以管理员身份运行”本脚本后重选容器路线'
  }
  $winget = Get-Command winget -ErrorAction SilentlyContinue
  if ($winget) {
    Write-Log '通过 winget 安装 Docker Desktop（体积较大，请耐心等待）……'
    if (-not $DryRun) {
      & winget install -e --id Docker.DockerDesktop --silent --accept-package-agreements --accept-source-agreements
      if ($LASTEXITCODE -ne 0) {
        # winget 对"已安装"等场景可能报非零退出码：以 docker CLI 实际可用性为准
        $dockerBin0 = 'C:\Program Files\Docker\Docker\resources\bin'
        if (Test-Path $dockerBin0) { $env:PATH = "$dockerBin0;$env:PATH" }
        if (Get-Command docker -ErrorAction SilentlyContinue) {
          Write-Log "winget 退出码 $LASTEXITCODE，但 docker CLI 实际可用，继续"
        } else {
          throw "winget 安装 Docker Desktop 失败（exit=$LASTEXITCODE）"
        }
      }
    } else { Write-Host '  [DryRun] 将执行 winget install Docker.DockerDesktop' }
  } else {
    $exe = Join-Path $DlDir 'DockerDesktopInstaller.exe'
    Invoke-Download $script:Urls.dockerSetup $exe
    Write-Log '运行 Docker Desktop 安装器（静默模式）……'
    if (-not $DryRun) {
      $p = Start-Process -FilePath $exe -ArgumentList 'install', '--quiet', '--accept-license' -Wait -PassThru
      if ($p.ExitCode -ne 0) { throw "Docker Desktop 安装器退出码 $($p.ExitCode)（若提示需重启，请重启后重跑本脚本，会自动续跑）" }
    } else { Write-Host '  [DryRun] 将静默运行 Docker Desktop 安装器' }
  }
  # 刷新 PATH：安装器写入的机器 PATH 当前进程感知不到，docker CLI 已知路径直接补上
  $dockerBin = 'C:\Program Files\Docker\Docker\resources\bin'
  if (Test-Path $dockerBin) { $env:PATH = "$dockerBin;$env:PATH" }
  $machinePath = [Environment]::GetEnvironmentVariable('Path', 'Machine')
  if ($machinePath) { $env:PATH = "$env:PATH;$machinePath" }
  Write-Log '启动 Docker Desktop 并等待引擎就绪（首次启动最长等 5 分钟）……'
  if (-not $DryRun) {
    Start-DockerDesktopApp
    if (-not (Wait-DockerEngine)) { throw 'Docker 引擎未就绪。若系统提示需启用 WSL2/虚拟化或重启，请处理后重跑本脚本（自动续跑）' }
  } else { Write-Host '  [DryRun] 将等待 Docker 引擎就绪' }
  # 全新安装的 Docker Desktop 默认 Windows 容器模式；本套件镜像均为 Linux 镜像，必须切换
  if (-not $DryRun) {
    $osType = docker info --format '{{.OSType}}' 2>$null
    if ($osType -ne 'linux') {
      Write-Log "Docker 当前为 $osType 容器模式，切换为 Linux 容器模式……"
      $dockerCli = 'C:\Program Files\Docker\Docker\DockerCli.exe'
      $switched = $false
      if (Test-Path $dockerCli) {
        for ($s = 1; $s -le 4 -and -not $switched; $s++) {
          Write-Log "切换尝试 $s……"
          & $dockerCli -SwitchDaemon 2>&1 | ForEach-Object { Write-Host $_ }
          for ($w = 0; $w -lt 10; $w++) {
            Start-Sleep -Seconds 6
            $t = docker info --format '{{.OSType}}' 2>$null
            if ($t -eq 'linux') { $switched = $true; break }
          }
          if ($switched) { break }
          if ($s -eq 2) {
            Write-Log '切换未生效，重启 Docker Desktop 后再试……'
            Stop-Process -Name 'Docker Desktop' -Force -ErrorAction SilentlyContinue
            Stop-Process -Name 'com.docker.backend' -Force -ErrorAction SilentlyContinue
            Start-Sleep -Seconds 5
            Start-Process 'C:\Program Files\Docker\Docker\Docker Desktop.exe'
            Start-Sleep -Seconds 20
          }
        }
      }
      if (-not $switched) {
        $osType = docker info --format '{{.OSType}}' 2>$null
        throw "Docker 容器模式切换失败（当前 OSType=$osType），无法拉起 Linux 镜像"
      }
      Write-Log '已切换为 Linux 容器模式'
    }
  }
  Write-Log 'Docker Desktop 就绪'
}

function Invoke-DockerRoute {
  Install-DockerDesktop
  Write-Log '容器路线：docker compose 一键拉起全部服务（mysql/redis/rabbitmq/nacos/es/kibana/gateway/identity/task/frontend）……'
  if (-not $DryRun) {
    Push-Location $script:ComposeDir
    try { docker compose up -d; if ($LASTEXITCODE -ne 0) { throw "docker compose up 失败（exit=$LASTEXITCODE）" } }
    finally { Pop-Location }
  } else { Write-Host "  [DryRun] 将在 $($script:ComposeDir) 执行 docker compose up -d" }
  Write-Log '等待健康检查（网关/文件中心/前端，每项最多等 2 分钟）……'
  if (-not $DryRun) {
    $targets = @(
      @{ Name = '网关';     Url = 'http://127.0.0.1:5000/health' },
      @{ Name = '文件中心'; Url = 'http://127.0.0.1:5003/health' },
      @{ Name = '前端';     Url = 'http://127.0.0.1:8080/' }
    )
    foreach ($t in $targets) {
      if (-not (Wait-HttpOk $t.Url 30 4)) { throw "容器路线健康检查未通过: $($t.Name)（$($t.Url)），请查看: docker compose logs" }
      Write-Log "容器路线健康检查通过: $($t.Name)"
    }
  } else { Write-Host '  [DryRun] 将等待网关健康检查' }
  Write-Log '容器路线部署完成：前端 http://localhost:8080  网关 http://localhost:5000  文件中心 http://localhost:5003'
}

# ---------------- 原生路线 ----------------
function Ensure-DotNet {
  $dotnetDir = Join-Path $RuntimeDir 'dotnet10'
  $env:DOTNET_ROOT = $dotnetDir
  $env:PATH = "$dotnetDir;$env:PATH"
  $have = $false
  try { $v = & dotnet --version 2>$null; if ($v -match '^10\.') { $have = $true } } catch { }
  if ($have) { Write-Log '.NET 10 SDK 已就绪'; return }
  Write-Log '安装 .NET 10 SDK（便携模式，约 200MB）……'
  $installer = Join-Path $DlDir 'dotnet-install.ps1'
  Invoke-Download $script:Urls.dotnetInstall $installer
  if (-not $DryRun) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File $installer -Channel 10.0 -InstallDir $dotnetDir
    if ($LASTEXITCODE -ne 0) { throw 'dotnet-install 安装失败' }
    $v = & (Join-Path $dotnetDir 'dotnet.exe') --version
    if ($v -notmatch '^10\.') { throw ".NET SDK 版本校验失败: $v" }
  } else { Write-Host "  [DryRun] 将安装 .NET 10 SDK -> $dotnetDir" }
}

function Ensure-Node {
  $nodeDir = Join-Path $RuntimeDir 'node20'
  $env:PATH = "$nodeDir;$env:PATH"
  if (Test-Path (Join-Path $nodeDir 'node.exe')) { Write-Log 'Node.js 20 已就绪'; return }
  Write-Log '安装 Node.js 20（便携模式）……'
  $zip = Join-Path $DlDir "node-$($script:NodeVer)-win-x64.zip"
  Invoke-Download $script:Urls.nodeZip $zip
  $tmp = Join-Path $DlDir 'node-extract'
  Expand-Zip $zip $tmp
  if (-not $DryRun) {
    $inner = Get-ChildItem $tmp -Directory | Select-Object -First 1
    Move-Item $inner.FullName $nodeDir -Force
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
    if (-not (Test-Path (Join-Path $nodeDir 'node.exe'))) { throw 'Node.js 解压校验失败' }
  }
}

function Ensure-MySQL {
  $myDir = Join-Path $RuntimeDir 'mysql8'
  $myBin = Join-Path $myDir 'bin\mysqld.exe'
  $dataDir = Join-Path $myDir 'data'
  if (-not (Test-Path $myBin)) {
    Write-Log "安装 MySQL $($script:MySQLVer)（便携模式，约 230MB，最大的一步）……"
    $zip = Join-Path $DlDir "mysql-$($script:MySQLVer)-winx64.zip"
    Invoke-Download $script:Urls.mysqlZip $zip
    $tmp = Join-Path $DlDir 'mysql-extract'
    Expand-Zip $zip $tmp
    if (-not $DryRun) {
      $inner = Get-ChildItem $tmp -Directory | Select-Object -First 1
      Move-Item $inner.FullName $myDir -Force
      Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
      if (-not (Test-Path $myBin)) { throw 'MySQL 解压校验失败' }
    }
  }
  if (-not (Test-Port 3306)) {
    $ini = Join-Path $myDir 'my.ini'
    $iniContent = @"
[mysqld]
basedir=$($myDir.Replace('\','/'))
datadir=$($dataDir.Replace('\','/'))
port=3306
character-set-server=utf8mb4
collation-server=utf8mb4_general_ci
max_allowed_packet=64M
innodb_buffer_pool_size=256M
"@
    if (-not $DryRun) { Set-Content -Path $ini -Value $iniContent -Encoding ASCII }
    if (-not (Test-Path $dataDir)) {
      Write-Log '初始化 MySQL 数据目录（首次）……'
      if (-not $DryRun) {
        & $myBin --defaults-file="$ini" --initialize-insecure 2>&1 | Out-Null
        if (-not (Test-Path $dataDir)) { throw 'MySQL 数据目录初始化失败' }
      } else { Write-Host '  [DryRun] 将执行 mysqld --initialize-insecure' }
    }
    Write-Log '启动 MySQL（端口 3306）……'
    if (-not $DryRun) {
      $p = Start-Process -FilePath $myBin -ArgumentList "--defaults-file=$ini" -WindowStyle Hidden -PassThru
      New-Item -ItemType Directory -Force -Path $PidDir | Out-Null
      Set-Content (Join-Path $PidDir 'mysql.pid') $p.Id
    }
  } else { Write-Log 'MySQL 已在运行（3306），跳过启动' }
  if (-not $DryRun) {
    $ok = $false
    for ($i = 1; $i -le 30; $i++) { if (Test-Port 3306) { $ok = $true; break }; Start-Sleep -Seconds 2 }
    if (-not $ok) { throw 'MySQL 端口 3306 未就绪' }
  }
}

function Ensure-Redis {
  $rDir = Join-Path $RuntimeDir 'redis'
  $rExe = Join-Path $rDir 'redis-server.exe'
  if (-not (Test-Path $rExe)) {
    Write-Log '安装 Redis 5.0.14（Windows 原生版）……'
    $zip = Join-Path $DlDir 'Redis-x64-5.0.14.1.zip'
    Invoke-Download $script:Urls.redisZip $zip
    Expand-Zip $zip $rDir
    if (-not $DryRun -and -not (Test-Path $rExe)) { throw 'Redis 解压校验失败' }
  }
  if (-not (Test-Port 6379)) {
    Write-Log '启动 Redis（端口 6379）……'
    if (-not $DryRun) {
      $p = Start-Process -FilePath $rExe -ArgumentList '--port', '6379' -WorkingDirectory $rDir -WindowStyle Hidden -PassThru
      New-Item -ItemType Directory -Force -Path $PidDir | Out-Null
      Set-Content (Join-Path $PidDir 'redis.pid') $p.Id
    }
  } else { Write-Log 'Redis 已在运行（6379），跳过启动' }
}

function Build-Backend {
  if (-not $DryRun) { $env:PATH = "$(Join-Path $RuntimeDir 'dotnet10');$env:PATH"; $env:DOTNET_ROOT = Join-Path $RuntimeDir 'dotnet10' }
  $jobs = @(
    @{ Proj = 'TaoAdmin.IdentityService'; Out = 'identity' },
    @{ Proj = 'TaoAdmin.GatewayService';  Out = 'gateway' },
    @{ Proj = 'TaoAdmin.TaskService';     Out = 'task' }
  )
  foreach ($j in $jobs) {
    Write-Log "编译后端: $($j.Proj) ……"
    if ($DryRun) { Write-Host "  [DryRun] dotnet publish $($j.Proj)"; continue }
    $out = & dotnet publish (Join-Path $Root "backend\src\$($j.Proj)") -c Release -o (Join-Path $PublishDir "backend\$($j.Out)") --nologo 2>&1
    $errCount = ($out | Select-String -Pattern ': error ').Count
    if ($LASTEXITCODE -ne 0 -or $errCount -gt 0) {
      $out | Select-Object -Last 30 | ForEach-Object { Write-Host $_ }
      throw "$($j.Proj) 编译失败：错误 $errCount 个（exit=$LASTEXITCODE）"
    }
    Write-Log "$($j.Proj) 编译通过（0 错误）"
  }
  # 文件中心独立服务
  Write-Log '编译文件中心: TaoFiles.Api ……'
  if ($DryRun) { Write-Host '  [DryRun] dotnet publish TaoFiles.Api'; return }
  $tfRoot = (Resolve-Path (Join-Path $Root '..\TaoFiles')).Path
  $out = & dotnet publish (Join-Path $tfRoot 'src\TaoFiles.Api') -c Release -o (Join-Path $PublishDir 'backend\taofiles') --nologo 2>&1
  $errCount = ($out | Select-String -Pattern ': error ').Count
  if ($LASTEXITCODE -ne 0 -or $errCount -gt 0) {
    $out | Select-Object -Last 30 | ForEach-Object { Write-Host $_ }
    throw "TaoFiles.Api 编译失败：错误 $errCount 个（exit=$LASTEXITCODE）"
  }
  Write-Log 'TaoFiles.Api 编译通过（0 错误）'
}

function Build-Frontend {
  if (-not $DryRun) { $env:PATH = "$(Join-Path $RuntimeDir 'node20');$env:PATH" }
  $feDir = Join-Path $Root 'frontend'
  Write-Log '安装前端依赖（npm install，首次较慢）……'
  if ($DryRun) { Write-Host '  [DryRun] npm install + npm run build'; return }
  Push-Location $feDir
  try {
    & npm install --no-audit --no-fund
    if ($LASTEXITCODE -ne 0) { throw 'npm install 失败' }
    & npm run build
    if ($LASTEXITCODE -ne 0) {
      # npm 可选依赖缺陷（npm/cli#4828）：原生绑定可能漏装，按官方口径清缓存重装重试一次
      Write-Log '前端构建首次失败，按 npm 官方口径清理 node_modules 与 lock 后重装重试……'
      Remove-Item (Join-Path $feDir 'node_modules') -Recurse -Force -ErrorAction SilentlyContinue
      Remove-Item (Join-Path $feDir 'package-lock.json') -Force -ErrorAction SilentlyContinue
      & npm install --no-audit --no-fund
      if ($LASTEXITCODE -ne 0) { throw 'npm install 重试失败' }
      & npm run build
      if ($LASTEXITCODE -ne 0) { throw '前端构建失败（重试后仍失败）' }
    }
  } finally { Pop-Location }
  $distIndex = Join-Path $feDir 'dist\index.html'
  if (-not (Test-Path $distIndex)) { throw '前端产物校验失败：dist/index.html 缺失' }
  New-Item -ItemType Directory -Force -Path (Join-Path $PublishDir 'frontend') | Out-Null
  Copy-Item (Join-Path $feDir 'dist\*') (Join-Path $PublishDir 'frontend') -Recurse -Force
  Write-Log '前端构建完成，产物已落位'
}

function Initialize-Database {
  if ($DryRun) { Write-Host '  [DryRun] 将执行数据库初始化 SQL（幂等）'; return }
  $mysql = Join-Path $RuntimeDir 'mysql8\bin\mysql.exe'
  if (-not (Test-Path $mysql)) { throw "未找到 mysql 客户端: $mysql" }
  Write-Log '设置数据库口令并初始化（幂等，可重复执行）……'
  & $mysql --user=root --port=3306 -e "ALTER USER 'root'@'localhost' IDENTIFIED BY '$($script:DbPwd)'; CREATE DATABASE IF NOT EXISTS tao_files CHARACTER SET utf8mb4;" 2>$null
  if (-not (Test-Path $script:SqlFile)) { throw "缺初始化 SQL: $script:SqlFile" }
  & $mysql --user=root --password=$($script:DbPwd) --port=3306 --default-character-set=utf8mb4 -e "source $($script:SqlFile.Replace('\','/'))"
  if ($LASTEXITCODE -ne 0) { throw '数据库初始化失败' }
  Write-Log '数据库初始化完成（tao_admin + tao_files）'
}

function Start-Services {
  if ($DryRun) { Write-Host '  [DryRun] 将拉起 identity/gateway/task/taofiles/前端服务'; return }
  New-Item -ItemType Directory -Force -Path $PidDir, $LogDir | Out-Null
  # 兜底：文件中心存储根目录必须存在，否则 PhysicalFileProvider 启动即抛 DirectoryNotFoundException
  New-Item -ItemType Directory -Force -Path (Join-Path $PublishDir 'backend\taofiles\uploads') | Out-Null
  $env:PATH = "$(Join-Path $RuntimeDir 'dotnet10');$(Join-Path $RuntimeDir 'node20');$env:PATH"
  $env:DOTNET_ROOT = Join-Path $RuntimeDir 'dotnet10'
  $svcs = @(
    @{ Name = 'identity'; Dll = 'TaoAdmin.IdentityService.dll'; Port = 5001 },
    @{ Name = 'gateway';  Dll = 'TaoAdmin.GatewayService.dll';  Port = 5000 },
    @{ Name = 'task';     Dll = 'TaoAdmin.TaskService.dll';     Port = 5002 },
    @{ Name = 'taofiles'; Dll = 'TaoFiles.Api.dll';             Port = 5003 }
  )
  foreach ($s in $svcs) {
    if (Test-Port $s.Port) { Write-Log "$($s.Name) 已在运行（端口 $($s.Port)），跳过"; continue }
    $dir = Join-Path $PublishDir "backend\$($s.Name)"
    $p = Start-Process -FilePath 'dotnet' -ArgumentList "`"$(Join-Path $dir $s.Dll)`"" -WorkingDirectory $dir -WindowStyle Hidden -PassThru `
      -RedirectStandardOutput (Join-Path $LogDir "$($s.Name).log") -RedirectStandardError (Join-Path $LogDir "$($s.Name).err.log")
    Set-Content (Join-Path $PidDir "$($s.Name).pid") $p.Id
    Write-Log "$($s.Name) 已拉起（PID $($p.Id)）"
  }
  # 前端静态服务（原生路线用 frontend-server.js 托管 + /api 反代）
  if (-not (Test-Port 8080)) {
    $env:FRONTEND_DIST = Join-Path $PublishDir 'frontend'
    $p = Start-Process -FilePath 'node' -ArgumentList "`"$(Join-Path $WinDir 'frontend-server.js')`"" -WindowStyle Hidden -PassThru `
      -RedirectStandardOutput (Join-Path $LogDir 'frontend.log') -RedirectStandardError (Join-Path $LogDir 'frontend.err.log')
    Set-Content (Join-Path $PidDir 'frontend.pid') $p.Id
    Write-Log "前端服务已拉起（PID $($p.Id)，端口 8080）"
  } else { Write-Log '前端服务已在运行（8080），跳过' }
}

function Test-AllHealth {
  if ($DryRun) { Write-Host '  [DryRun] 将执行五项健康检查'; return }
  Write-Log '健康检查（每项最多等 2 分钟）……'
  foreach ($h in @(
    @{ Name = '网关';     Url = 'http://127.0.0.1:5000/health' },
    @{ Name = '认证服务'; Url = 'http://127.0.0.1:5001/health' },
    @{ Name = '任务服务'; Url = 'http://127.0.0.1:5002/health' },
    @{ Name = '文件中心'; Url = 'http://127.0.0.1:5003/health' },
    @{ Name = '前端';     Url = 'http://127.0.0.1:8080/' }
  )) {
    if (-not (Wait-HttpOk $h.Url 40 3)) { throw "$($h.Name) 健康检查未通过: $($h.Url)（日志见 $LogDir）" }
  }
}

function Invoke-NativeRoute {
  $steps = @(
    @{ Id = 'env-check';  Name = '环境检查';                    W = 2 },
    @{ Id = 'dotnet-sdk'; Name = '安装 .NET 10 SDK';            W = 12 },
    @{ Id = 'node';       Name = '安装 Node.js 20';             W = 5 },
    @{ Id = 'mysql';      Name = "安装并启动 MySQL $($script:MySQLVer)"; W = 18 },
    @{ Id = 'redis';      Name = '安装并启动 Redis';            W = 3 },
    @{ Id = 'backend';    Name = '编译后端与文件中心';          W = 20 },
    @{ Id = 'frontend';   Name = '编译前端';                    W = 12 },
    @{ Id = 'db-init';    Name = '初始化数据库';                W = 3 },
    @{ Id = 'start';      Name = '拉起全部服务';                W = 5 },
    @{ Id = 'health';     Name = '健康检查';                    W = 5 }
  )
  $totalW = ($steps | Measure-Object -Property W -Sum).Sum
  $done = Get-DoneSet
  $sw = [Diagnostics.Stopwatch]::StartNew()
  $doneW = 0
  foreach ($s in $steps) {
    if ($done.ContainsKey($s.Id) -and -not $Force) {
      Write-Log "跳过（已完成）: $($s.Name)"
      $doneW += $s.W
      Show-Progress $doneW $totalW $sw.Elapsed
      continue
    }
    Write-Log "==== $($s.Name) ===="
    switch ($s.Id) {
      'env-check' {
        if (-not $IsWindows -and -not $DryRun) { throw '本脚本仅支持 Windows（Linux 请用 deploy/oneclick/install.sh）' }
        if (-not (Test-Path $script:SqlFile)) { throw "源码不完整：缺 $script:SqlFile" }
      }
      'dotnet-sdk' { Ensure-DotNet }
      'node'       { Ensure-Node }
      'mysql'      { Ensure-MySQL }
      'redis'      { Ensure-Redis }
      'backend'    { Build-Backend }
      'frontend'   { Build-Frontend }
      'db-init'    { Initialize-Database }
      'start'      { Start-Services }
      'health'     { Test-AllHealth }
    }
    if (-not $DryRun) { Mark-Done $s.Id }
    $doneW += $s.W
    Show-Progress $doneW $totalW $sw.Elapsed
  }
  Write-Log '========================================================'
  Write-Log '原生路线部署完成！'
  Write-Log '  前端:   http://localhost:8080'
  Write-Log '  网关:   http://localhost:5000'
  Write-Log '  账号:   admin / Admin@123（请立即修改默认口令）'
  Write-Log "  日志:   $LogDir"
  Write-Log '  卸载:   双击 clean.bat（或 -Action clean），卸载干干净净'
}

# ---------------- 一键卸载 ----------------
function Stop-ByPidFiles {
  if (-not (Test-Path $PidDir)) { return }
  Get-ChildItem $PidDir -Filter '*.pid' | ForEach-Object {
    $pidToStop = (Get-Content $_.FullName).Trim()
    if ($pidToStop -match '^\d+$') {
      if ($DryRun) { Write-Host "  [DryRun] 将停止进程 $pidToStop（$($_.BaseName)）" }
      else {
        try { Stop-Process -Id ([int]$pidToStop) -Force -ErrorAction SilentlyContinue; Write-Log "已停止 $($_.BaseName)（PID $pidToStop）" } catch { }
      }
    }
  }
}
function Stop-ByPorts {
  if (-not $IsWindows) { return }
  foreach ($port in @(5000, 5001, 5002, 5003, 8080, 3306, 6379)) {
    try {
      $conns = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue
      foreach ($c in $conns) {
        $opid = $c.OwningProcess
        if ($opid -and $opid -ne $PID) {
          $pname = (Get-Process -Id $opid -ErrorAction SilentlyContinue).ProcessName
          if ($pname -in @('dotnet', 'mysqld', 'redis-server', 'node', 'TaoAdmin.GatewayService', 'TaoAdmin.IdentityService', 'TaoAdmin.TaskService', 'TaoFiles.Api')) {
            if ($DryRun) { Write-Host "  [DryRun] 将停止端口 $port 上的进程 $pname（PID $opid）" }
            else { Stop-Process -Id $opid -Force -ErrorAction SilentlyContinue; Write-Log "已停止端口 $port 上的 $pname（PID $opid）" }
          }
        }
      }
    } catch { }
  }
}
function Invoke-Clean {
  Write-Log '==================== 一键卸载 ===================='
  if (-not $Force -and -not $DryRun) {
    $ans = Read-Host '将停止全部服务并删除部署产物/运行时/数据库数据（源码保留）。确认继续？(y/N)'
    if ($ans -notmatch '^[Yy]') { Write-Log '已取消卸载'; return }
  }
  # 1) 容器路线：连卷一起删（数据库数据在卷里）
  if (Get-Command docker -ErrorAction SilentlyContinue) {
    if ($DryRun) { Write-Host '  [DryRun] 将执行 docker compose down -v --remove-orphans' }
    else {
      Write-Log '容器路线：docker compose down -v（删除容器与数据卷）……'
      Push-Location $script:ComposeDir
      try { docker compose down -v --remove-orphans 2>$null | Out-Null } finally { Pop-Location }
    }
  }
  # 2) 停止全部原生进程
  Stop-ByPidFiles
  Stop-ByPorts
  # 3) 删除产物与运行时
  $targets = @()
  $targets += @{ Path = $script:PublishDir; Desc = '发布产物' }
  if (-not $KeepRuntimes) { $targets += @{ Path = $script:RuntimeDir; Desc = '运行时与数据库数据' } }
  else { $targets += @{ Path = $PidDir; Desc = '进程记录' }; $targets += @{ Path = $LogDir; Desc = '运行日志' } }
  $targets += @{ Path = Join-Path $Root 'frontend\node_modules'; Desc = '前端依赖缓存' }
  $targets += @{ Path = Join-Path $Root 'frontend\dist'; Desc = '前端构建产物' }
  foreach ($t in $targets) {
    if (Test-Path $t.Path) {
      if ($DryRun) { Write-Host "  [DryRun] 将删除 $($t.Desc): $($t.Path)" }
      else { Remove-Item $t.Path -Recurse -Force -ErrorAction SilentlyContinue; Write-Log "已删除 $($t.Desc)" }
    }
  }
  # 4) 清理断点状态
  if (Test-Path $script:StateFile) {
    if ($DryRun) { Write-Host "  [DryRun] 将删除状态文件: $($script:StateFile)" }
    else { Remove-Item $script:StateFile -Force; Write-Log '已清除断点状态' }
  }
  Write-Log '========================================================'
  Write-Log '卸载完成：服务已停止，部署产物/运行时/数据库数据/状态已清除，源码完整保留。'
  if ($KeepRuntimes) { Write-Log '（按要求保留了运行时，重装会快很多）' }
}

# ---------------- 状态查看 ----------------
function Show-Status {
  Write-Host '==================== TaoAdmin 状态 ===================='
  $items = @(
    @{ Name = '网关(5000)';     Port = 5000 },
    @{ Name = '认证服务(5001)'; Port = 5001 },
    @{ Name = '任务服务(5002)'; Port = 5002 },
    @{ Name = '文件中心(5003)'; Port = 5003 },
    @{ Name = '前端(8080)';     Port = 8080 },
    @{ Name = 'MySQL(3306)';    Port = 3306 },
    @{ Name = 'Redis(6379)';    Port = 6379 }
  )
  foreach ($i in $items) {
    $up = Test-Port $i.Port
    $tag = if ($up) { '运行中' } else { '未运行' }
    Write-Host ("  {0,-16} {1}" -f $i.Name, $tag)
  }
  if (Get-Command docker -ErrorAction SilentlyContinue) {
    $ps = docker ps --format '{{.Names}} {{.Status}}' 2>$null
    if ($ps) { Write-Host '  容器:'; $ps | ForEach-Object { Write-Host "    $_" } }
  }
  Write-Host '======================================================='
}

# ---------------- 主入口 ----------------
New-Item -ItemType Directory -Force -Path $RuntimeDir, $DlDir | Out-Null
if (-not (Test-Path $script:StateFile)) { New-Item -ItemType File -Path $script:StateFile | Out-Null }

switch ($Action) {
  'install' {
    $chosen = Select-Route
    Write-Log "选定路线: $(if ($chosen -eq 'docker') { '容器（Docker）' } else { '原生（无容器）' })"
    if ($chosen -eq 'docker') { Invoke-DockerRoute }
    else { Invoke-NativeRoute }
  }
  'clean'  { Invoke-Clean }
  'status' { Show-Status }
}
exit 0
