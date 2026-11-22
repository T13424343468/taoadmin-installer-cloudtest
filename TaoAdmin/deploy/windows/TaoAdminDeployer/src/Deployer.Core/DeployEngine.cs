// DeployEngine: orchestrates every deployment step with full remediation.
// Ported from install-windows.ps1 (WIN-INSTALLER-V1/V8) and hardened:
//   - every real-machine failure mode gets an automatic fix ("create the condition")
//   - every step reports progress to the UI
namespace TaoAdmin.Deployer.Core;

public sealed class RebootRequiredException : Exception
{
    public RebootRequiredException(string message) : base(message) { }
}

public sealed record StepDef(string Id, string Name, int Weight);

public sealed record PortConflictInfo(int Port, int Pid, string ProcessName);

public sealed record StatusItem(string Name, int Port, bool Running);

public sealed record StatusReport(IReadOnlyList<StatusItem> Items, IReadOnlyList<string> Containers);

public sealed class DeployOptions
{
    public string Route { get; set; } = "native"; // native | docker
    public bool Force { get; set; }
    public bool KeepRuntimes { get; set; }
    public bool AssumeYes { get; set; }

    public const string DbPassword = "Micro@2026";
    public const string MySQLVersion = "8.0.37";
    public const string NodeVersion = "v22.23.5";

    public static class Urls
    {
        public const string DotnetInstall = "https://dot.net/v1/dotnet-install.ps1";
        public const string NodeZip = "https://nodejs.org/dist/" + NodeVersion + "/node-" + NodeVersion + "-win-x64.zip";
        public const string MysqlZip = "https://cdn.mysql.com/Downloads/MySQL-8.0/mysql-" + MySQLVersion + "-winx64.zip";
        public const string RedisZip = "https://github.com/tporadowski/redis/releases/download/v5.0.14.1/Redis-x64-5.0.14.1.zip";
        public const string DockerSetup = "https://desktop.docker.com/win/main/stable/Docker%20Desktop%20Installer.exe";
    }
}

public sealed class DeployEngine
{
    // Windows-native tools (dism.exe) emit localized GBK output; everything else we run is UTF-8.
    private static readonly System.Text.Encoding GbkOutput = CreateGbkEncoding();

    private static System.Text.Encoding CreateGbkEncoding()
    {
        try
        {
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            return System.Text.Encoding.GetEncoding(936);
        }
        catch { return System.Text.Encoding.UTF8; }
    }

    private readonly DeployPaths _p;
    private readonly StateStore _state;
    private readonly DeployOptions _opt;

    public Action<string>? Log { get; set; }
    public Action<string, string>? StepOutput { get; set; }   // (stepId, line)
    public Action<string>? StepStarted { get; set; }          // stepId
    public Action<string>? StepSucceeded { get; set; }        // stepId
    public Action<int, int>? ProgressChanged { get; set; }    // (doneWeight, totalWeight)
    public Func<PortConflictInfo, bool>? ConfirmStopProcess { get; set; }

    private string CurrentStepId { get; set; } = "";

    public DeployEngine(DeployPaths paths, DeployOptions options)
    {
        _p = paths;
        _opt = options;
        _state = new StateStore(paths.StateFile);
    }

    private void L(string msg) => Log?.Invoke(msg);
    private void Out(string line) { if (!string.IsNullOrWhiteSpace(line)) StepOutput?.Invoke(CurrentStepId, line); }

    // ---------------- plans ----------------

    public static IReadOnlyList<StepDef> NativePlan(DeployOptions opt) => new List<StepDef>
    {
        new("env-check", "环境体检", 2),
        new("dotnet-sdk", "安装 .NET 10 SDK", 12),
        new("node", "安装 Node.js", 5),
        new("mysql", "安装并启动 MySQL " + DeployOptions.MySQLVersion, 18),
        new("redis", "安装并启动 Redis", 3),
        new("backend", "编译后端与文件中心", 20),
        new("frontend", "编译前端", 12),
        new("db-init", "初始化数据库", 3),
        new("start", "拉起全部服务", 5),
        new("health", "健康检查", 5),
    };

    public static IReadOnlyList<StepDef> DockerPlan(DeployOptions opt) => new List<StepDef>
    {
        new("env-check", "环境体检", 2),
        new("docker-install", "准备 Docker（缺失时自动安装）", 30),
        new("docker-mode", "确认 Linux 容器模式", 5),
        new("compose-up", "一键拉起全部服务", 15),
        new("docker-health", "健康检查", 8),
    };

    public IReadOnlyList<StepDef> Plan() =>
        _opt.Route == "docker" ? DockerPlan(_opt) : NativePlan(_opt);

    // ---------------- deploy ----------------

    public async Task DeployAsync(CancellationToken ct)
    {
        _state.EnsureStateFile();
        var plan = Plan();
        var total = plan.Sum(s => s.Weight);
        var done = _state.Load();
        var doneW = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        foreach (var step in plan)
        {
            ct.ThrowIfCancellationRequested();
            if (done.Contains(step.Id) && !_opt.Force)
            {
                L($"跳过（上次已完成）: {step.Name}");
                doneW += step.Weight;
                ProgressChanged?.Invoke(doneW, total);
                continue;
            }

            CurrentStepId = step.Id;
            StepStarted?.Invoke(step.Id);
            L($"==== {step.Name} ====");
            try
            {
                await RunStepAsync(step.Id, ct);
                _state.MarkDone(step.Id);
                StepSucceeded?.Invoke(step.Id);
            }
            catch (RebootRequiredException)
            {
                throw;
            }
            doneW += step.Weight;
            ProgressChanged?.Invoke(doneW, total);
        }

        L("========================================================");
        L("部署完成！");
        if (_opt.Route == "docker")
        {
            L("  前端:   http://localhost:8080");
            L("  网关:   http://localhost:5000");
            L("  文件中心: http://localhost:5003");
        }
        else
        {
            L("  前端:   http://localhost:8080");
            L("  网关:   http://localhost:5000");
        }
        L("  账号:   admin / Admin@123（请登录后立即修改默认口令）");
        L($"  日志:   {_p.LogDir}");
    }

    private async Task RunStepAsync(string id, CancellationToken ct)
    {
        switch (id)
        {
            case "env-check": StepEnvCheck(ct); break;
            case "dotnet-sdk": await StepDotnet(ct); break;
            case "node": await StepNode(ct); break;
            case "mysql": await StepMysql(ct); break;
            case "redis": await StepRedis(ct); break;
            case "backend": await StepBackend(ct); break;
            case "frontend": await StepFrontend(ct); break;
            case "db-init": await StepDbInit(ct); break;
            case "start": await StepStartServices(ct); break;
            case "health": await StepHealth(ct); break;
            case "docker-install": await StepDockerInstall(ct); break;
            case "docker-mode": await StepDockerMode(ct); break;
            case "compose-up": await StepComposeUp(ct); break;
            case "docker-health": await StepDockerHealth(ct); break;
            default: throw new InvalidOperationException($"unknown step {id}");
        }
    }

    // ---------------- native route steps ----------------

    private void StepEnvCheck(CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("本部署器仅支持 Windows（Linux 请使用 deploy/oneclick/install.sh）");
        if (!File.Exists(_p.SqlFile))
            throw new FileNotFoundException($"源码不完整：缺少 {_p.SqlFile}");
        L("环境体检通过：源码完整，系统为 Windows");
    }

    private async Task StepDotnet(CancellationToken ct)
    {
        var dotnetDir = DeployPaths.Join(_p.RuntimeDir, "dotnet10");
        var dotnetExe = DeployPaths.Join(dotnetDir, "dotnet.exe");
        if (File.Exists(dotnetExe))
        {
            var r = await ProcessRunner.RunAsync(dotnetExe, "--version", onLine: l => Out(l), ct: ct);
            if (r.Output.Any(l => l.TrimStart().StartsWith("10.")))
            {
                L(".NET 10 SDK 已就绪");
                return;
            }
        }
        L("安装 .NET 10 SDK（便携模式，约 200MB）……");
        var installer = DeployPaths.Join(_p.DlDir, "dotnet-install.ps1");
        await Downloader.DownloadAsync(DeployOptions.Urls.DotnetInstall, installer, L, ct: ct);
        var r2 = await ProcessRunner.RunAsync(
            "powershell", $"-NoProfile -ExecutionPolicy Bypass -File \"{installer}\" -Channel 10.0 -InstallDir \"{dotnetDir}\"",
            onLine: Out, ct: ct);
        if (!r2.Success) throw new InvalidOperationException(".NET 10 SDK 安装失败（exit=" + r2.ExitCode + "）");
        var v = await ProcessRunner.RunAsync(dotnetExe, "--version", ct: ct);
        if (!v.Output.Any(l => l.TrimStart().StartsWith("10.")))
            throw new InvalidOperationException(".NET SDK 版本校验失败: " + string.Join(" ", v.Output));
        L(".NET 10 SDK 安装完成");
    }

    private async Task StepNode(CancellationToken ct)
    {
        var nodeDir = DeployPaths.Join(_p.RuntimeDir, "node20");
        if (File.Exists(DeployPaths.Join(nodeDir, "node.exe")))
        {
            L("Node.js 已就绪");
            return;
        }
        L("安装 Node.js " + DeployOptions.NodeVersion + "（便携模式）……");
        var zip = DeployPaths.Join(_p.DlDir, "node-" + DeployOptions.NodeVersion + "-win-x64.zip");
        await Downloader.DownloadAsync(DeployOptions.Urls.NodeZip, zip, L, ct: ct);
        var tmp = DeployPaths.Join(_p.DlDir, "node-extract");
        ExtractWithRetry(zip, tmp, L);
        var inner = Directory.GetDirectories(tmp).FirstOrDefault()
            ?? throw new InvalidOperationException("Node.js 解压校验失败");
        MoveWithRetry(inner, nodeDir);
        TryDeleteDir(tmp);
        if (!File.Exists(DeployPaths.Join(nodeDir, "node.exe")))
            throw new InvalidOperationException("Node.js 解压校验失败");
        L("Node.js 安装完成");
    }

    private async Task StepMysql(CancellationToken ct)
    {
        var myDir = DeployPaths.Join(_p.RuntimeDir, "mysql8");
        var myBin = DeployPaths.Join(myDir, "bin", "mysqld.exe");
        var dataDir = DeployPaths.Join(myDir, "data");

        if (!File.Exists(myBin))
        {
            L($"安装 MySQL {DeployOptions.MySQLVersion}（便携模式，约 230MB，最大的一步）……");
            var zip = DeployPaths.Join(_p.DlDir, $"mysql-{DeployOptions.MySQLVersion}-winx64.zip");
            await Downloader.DownloadAsync(DeployOptions.Urls.MysqlZip, zip, L, ct: ct);
            var tmp = DeployPaths.Join(_p.DlDir, "mysql-extract");
            ExtractWithRetry(zip, tmp, L);
            var inner = Directory.GetDirectories(tmp).FirstOrDefault()
                ?? throw new InvalidOperationException("MySQL 解压校验失败");
            MoveWithRetry(inner, myDir);
            TryDeleteDir(tmp);
            if (!File.Exists(myBin)) throw new InvalidOperationException("MySQL 解压校验失败");
        }

        if (!Net.Ports.PortOpen(3306))
        {
            var ini = DeployPaths.Join(myDir, "my.ini");
            var iniContent =
                "[mysqld]\n" +
                "basedir=" + myDir.Replace('\\', '/') + "\n" +
                "datadir=" + dataDir.Replace('\\', '/') + "\n" +
                "port=3306\n" +
                "character-set-server=utf8mb4\n" +
                "collation-server=utf8mb4_general_ci\n" +
                "max_allowed_packet=64M\n" +
                "innodb_buffer_pool_size=256M\n";
            await File.WriteAllTextAsync(ini, iniContent, ct);

            if (!Directory.Exists(dataDir))
            {
                L("初始化 MySQL 数据目录（首次）……");
                var r = await ProcessRunner.RunAsync(myBin, $"--defaults-file=\"{ini}\" --initialize-insecure", onLine: Out, ct: ct);
                if (!Directory.Exists(dataDir))
                    throw new InvalidOperationException("MySQL 数据目录初始化失败（exit=" + r.ExitCode + "）");
            }
            else
            {
                // Real-machine pitfall: half-initialized data dir left by a previous failed run.
                L("检测到残留数据目录，尝试直接启动……");
            }

            L("启动 MySQL（端口 3306）……");
            Directory.CreateDirectory(_p.PidDir);
            var started = ProcessRunner.RunAsync(
                myBin, $"--defaults-file=\"{ini}\"",
                onLine: Out, ct: ct); // mysqld runs in foreground; keep task alive
            _ = Task.Run(async () =>
            {
                try { await started; } catch { }
            }, CancellationToken.None);

            var ok = await Net.Ports.WaitPortOpenAsync(3306, TimeSpan.FromSeconds(60), ct);
            if (!ok) throw new InvalidOperationException("MySQL 端口 3306 未就绪（60 秒超时）");
        }
        else
        {
            // 3306 is occupied — make sure it is OUR portable instance, not the
            // user's own MySQL. Touching a foreign MySQL could rewrite its root
            // password or pollute its data during db-init, so fail fast clearly.
            if (!IsOurMysqldRunning())
                throw new InvalidOperationException(
                    "检测到 3306 端口被其他 MySQL 占用（可能是您自行安装的 MySQL 服务）。" +
                    "为避免改动您现有数据库的口令和数据，请先停止该 MySQL 服务（服务名通常为 MySQL 或 MySQL80），" +
                    "再重新运行部署器；或将其卸载/改端口后重试。");
            L("MySQL 已在运行（3306），跳过启动");
        }
        L("MySQL 就绪");
    }

    private bool IsOurMysqldRunning()
    {
        if (!OperatingSystem.IsWindows()) return true;
        var prefix = Path.GetFullPath(_p.RuntimeDir).TrimEnd(Path.DirectorySeparatorChar)
                     + Path.DirectorySeparatorChar;
        foreach (var proc in System.Diagnostics.Process.GetProcessesByName("mysqld"))
        {
            try
            {
                var path = proc.MainModule?.FileName;
                if (!string.IsNullOrEmpty(path) &&
                    path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            catch { }
        }
        return false;
    }

    private async Task StepRedis(CancellationToken ct)
    {
        var rDir = DeployPaths.Join(_p.RuntimeDir, "redis");
        var rExe = DeployPaths.Join(rDir, "redis-server.exe");
        if (!File.Exists(rExe))
        {
            L("安装 Redis 5.0.14（Windows 原生版）……");
            var zip = DeployPaths.Join(_p.DlDir, "Redis-x64-5.0.14.1.zip");
            await Downloader.DownloadAsync(DeployOptions.Urls.RedisZip, zip, L, ct: ct);
            ExtractWithRetry(zip, rDir, L);
            if (!File.Exists(rExe)) throw new InvalidOperationException("Redis 解压校验失败");
        }
        if (!Net.Ports.PortOpen(6379))
        {
            L("启动 Redis（端口 6379）……");
            Directory.CreateDirectory(_p.PidDir);
            var started = ProcessRunner.RunAsync(rExe, "--port 6379", workingDirectory: rDir, onLine: Out, ct: ct);
            _ = Task.Run(async () =>
            {
                try { await started; } catch { }
            }, CancellationToken.None);
            var ok = await Net.Ports.WaitPortOpenAsync(6379, TimeSpan.FromSeconds(90), ct);
            if (!ok) throw new InvalidOperationException("Redis 端口 6379 未就绪");
        }
        else
        {
            L("Redis 已在运行（6379），跳过启动");
        }
        L("Redis 就绪");
    }

    private async Task StepBackend(CancellationToken ct)
    {
        var dotnetDir = DeployPaths.Join(_p.RuntimeDir, "dotnet10");
        var dotnetExe = DeployPaths.Join(dotnetDir, "dotnet.exe");
        var jobs = new List<(string Proj, string Out)>
        {
            ("TaoAdmin.IdentityService", "identity"),
            ("TaoAdmin.GatewayService", "gateway"),
            ("TaoAdmin.TaskService", "task"),
        };
        foreach (var (proj, outDir) in jobs)
        {
            L($"编译后端: {proj} ……");
            var r = await ProcessRunner.RunAsync(
                dotnetExe,
                $"publish -nr:false -p:UseSharedCompilation=false \"{DeployPaths.Join(_p.Root, "backend", "src", proj)}\" -c Release -o \"{DeployPaths.Join(_p.PublishDir, "backend", outDir)}\" --nologo",
                onLine: Out, ct: ct);
            var errCount = r.Output.Count(l => l.Contains(": error "));
            if (!r.Success || errCount > 0)
                throw new InvalidOperationException($"{proj} 编译失败：错误 {errCount} 个（exit={r.ExitCode}）");
            L($"{proj} 编译通过（0 错误）");
        }

        L("编译文件中心: TaoFiles.Api ……");
        var tfRoot = DeployPaths.ParentDir(_p.Root); // TaoFiles sits next to TaoAdmin in the package
        var tfProj = DeployPaths.Join(tfRoot, "TaoFiles", "src", "TaoFiles.Api");
        if (!Directory.Exists(tfProj))
            tfProj = DeployPaths.Join(_p.Root, "..", "TaoFiles", "src", "TaoFiles.Api");
        var rf = await ProcessRunner.RunAsync(
            dotnetExe,
            $"publish -nr:false -p:UseSharedCompilation=false \"{tfProj}\" -c Release -o \"{DeployPaths.Join(_p.PublishDir, "backend", "taofiles")}\" --nologo",
            onLine: Out, ct: ct);
        var errF = rf.Output.Count(l => l.Contains(": error "));
        if (!rf.Success || errF > 0)
            throw new InvalidOperationException($"TaoFiles.Api 编译失败：错误 {errF} 个（exit={rf.ExitCode}）");
        L("TaoFiles.Api 编译通过（0 错误）");
    }

    private async Task StepFrontend(CancellationToken ct)
    {
        var nodeDir = DeployPaths.Join(_p.RuntimeDir, "node20");
        var npmCmd = DeployPaths.Join(nodeDir, "npm.cmd");
        var feDir = _p.FrontendDir;
        L("安装前端依赖（npm install，首次较慢）……");

        async Task<bool> TryInstallBuild(string registryArg = "")
        {
            var r1 = await ProcessRunner.RunAsync(npmCmd, $"install --no-audit --no-fund{registryArg}", workingDirectory: feDir, onLine: Out, ct: ct);
            if (!r1.Success) return false;
            var r2 = await ProcessRunner.RunAsync(npmCmd, "run build", workingDirectory: feDir, onLine: Out, ct: ct);
            return r2.Success;
        }

        if (!await TryInstallBuild())
        {
            // npm/cli#4828: optional native bindings silently skipped — official remedy is a clean reinstall.
            L("前端构建首次失败，清理 node_modules 与 lock 后重装重试……");
            TryDeleteDir(DeployPaths.Join(feDir, "node_modules"));
            TryDeleteFile(DeployPaths.Join(feDir, "package-lock.json"));
            // Same fallback the Dockerfile uses: retry against the npmmirror registry
            // for slow/blocked international network conditions.
            if (!await TryInstallBuild(" --registry=https://registry.npmmirror.com"))
                throw new InvalidOperationException("前端构建失败（重试后仍失败，已尝试国内镜像源）");
        }

        var distIndex = DeployPaths.Join(feDir, "dist", "index.html");
        if (!File.Exists(distIndex)) throw new InvalidOperationException("前端产物校验失败：dist/index.html 缺失");
        Directory.CreateDirectory(DeployPaths.Join(_p.PublishDir, "frontend"));
        CopyDir(DeployPaths.Join(feDir, "dist"), DeployPaths.Join(_p.PublishDir, "frontend"));
        L("前端构建完成，产物已落位");
    }

    private async Task StepDbInit(CancellationToken ct)
    {
        var mysql = DeployPaths.Join(_p.RuntimeDir, "mysql8", "bin", "mysql.exe");
        if (!File.Exists(mysql)) throw new FileNotFoundException("未找到 mysql 客户端: " + mysql);
        L("设置数据库口令并初始化（幂等，可重复执行）……");
        var r0 = await ProcessRunner.RunAsync(mysql,
            $"--user=root --port=3306 -e \"ALTER USER 'root'@'localhost' IDENTIFIED BY '{DeployOptions.DbPassword}'; CREATE DATABASE IF NOT EXISTS tao_files CHARACTER SET utf8mb4;\"",
            onLine: Out, ct: ct);
        // Password may already be set from a previous run; only fail if the follow-up init also fails.
        var r = await ProcessRunner.RunAsync(mysql,
            $"--user=root --password={DeployOptions.DbPassword} --port=3306 --default-character-set=utf8mb4 -e \"source {_p.SqlFile.Replace('\\', '/')}\"",
            onLine: Out, ct: ct);
        if (!r.Success)
        {
            if (!r0.Success)
                throw new InvalidOperationException("数据库初始化失败：无法连接 MySQL（exit=" + r0.ExitCode + "）");
            throw new InvalidOperationException("数据库初始化失败（exit=" + r.ExitCode + "）");
        }
        L("数据库初始化完成（tao_admin + tao_files）");
    }

    private async Task StepStartServices(CancellationToken ct)
    {
        Directory.CreateDirectory(_p.PidDir);
        Directory.CreateDirectory(_p.LogDir);
        // Pitfall #56: PhysicalFileProvider crashes on a fresh publish dir without uploads/.
        Directory.CreateDirectory(DeployPaths.Join(_p.PublishDir, "backend", "taofiles", "uploads"));

        var dotnetDir = DeployPaths.Join(_p.RuntimeDir, "dotnet10");
        var nodeDir = DeployPaths.Join(_p.RuntimeDir, "node20");

        var svcs = new List<(string Name, string Dll, int Port)>
        {
            ("identity", "TaoAdmin.IdentityService.dll", 5001),
            ("gateway", "TaoAdmin.GatewayService.dll", 5000),
            ("task", "TaoAdmin.TaskService.dll", 5002),
            ("taofiles", "TaoFiles.Api.dll", 5003),
        };

        foreach (var (name, dll, port) in svcs)
        {
            if (Net.Ports.PortOpen(port))
            {
                L($"{name} 已在运行（端口 {port}），跳过");
                continue;
            }
            await Net.Ports.EnsurePortFreeAsync(port, ConfirmStopProcess, L, ct);
            var dir = DeployPaths.Join(_p.PublishDir, "backend", name);
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = DeployPaths.Join(dotnetDir, "dotnet.exe"),
                Arguments = $"\"{DeployPaths.Join(dir, dll)}\"",
                WorkingDirectory = dir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8,
            };
            psi.EnvironmentVariables["DOTNET_ROOT"] = dotnetDir;
            var p = System.Diagnostics.Process.Start(psi)
                ?? throw new InvalidOperationException($"拉起 {name} 失败");
            _ = PumpToLog(p, name);
            File.WriteAllText(DeployPaths.Join(_p.PidDir, name + ".pid"), p.Id.ToString());
            L($"{name} 已拉起（PID {p.Id}）");
        }

        if (!Net.Ports.PortOpen(8080))
        {
            await Net.Ports.EnsurePortFreeAsync(8080, ConfirmStopProcess, L, ct);
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = DeployPaths.Join(nodeDir, "node.exe"),
                Arguments = $"\"{DeployPaths.Join(_p.WinDir, "frontend-server.js")}\"",
                WorkingDirectory = _p.WinDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8,
            };
            psi.EnvironmentVariables["FRONTEND_DIST"] = DeployPaths.Join(_p.PublishDir, "frontend");
            var p = System.Diagnostics.Process.Start(psi)
                ?? throw new InvalidOperationException("拉起前端服务失败");
            _ = PumpToLog(p, "frontend");
            File.WriteAllText(DeployPaths.Join(_p.PidDir, "frontend.pid"), p.Id.ToString());
            L($"前端服务已拉起（PID {p.Id}，端口 8080）");
        }
        else
        {
            L("前端服务已在运行（8080），跳过");
        }
        await Task.CompletedTask;
    }

    private async Task PumpToLog(System.Diagnostics.Process p, string name)
    {
        var logFile = DeployPaths.Join(_p.LogDir, name + ".log");
        try
        {
            using var sw = new StreamWriter(logFile, append: false);
            var so = PumpFileAsync(p.StandardOutput, sw);
            var se = PumpFileAsync(p.StandardError, sw);
            await Task.WhenAll(so, se);
        }
        catch { }
    }

    private static async Task PumpFileAsync(StreamReader reader, StreamWriter writer)
    {
        string? line;
        while ((line = await reader.ReadLineAsync()) is not null)
        {
            await writer.WriteLineAsync(line);
            await writer.FlushAsync();
        }
    }

    private async Task StepHealth(CancellationToken ct)
    {
        L("健康检查（每项最多等 15 分钟，等待期间每 10 秒报一次进度）……");
        var targets = new List<(string Name, string Url)>
        {
            ("网关", "http://127.0.0.1:5000/health"),
            ("认证服务", "http://127.0.0.1:5001/health"),
            ("任务服务", "http://127.0.0.1:5002/health"),
            ("文件中心", "http://127.0.0.1:5003/health"),
            ("前端", "http://127.0.0.1:8080/"),
        };
        foreach (var (name, url) in targets)
        {
            if (!await Net.Http.WaitOkAsync(url, TimeSpan.FromMinutes(15), ct,
                    waited => L($"仍在等待 {name} 就绪……已等 {waited / 60} 分 {waited % 60} 秒（上限 15 分钟）")))
                throw new InvalidOperationException($"{name} 健康检查未通过: {url}（日志见 {_p.LogDir}）");
            L($"健康检查通过: {name}");
        }
    }

    // ---------------- docker route steps ----------------

    private async Task StepDockerInstall(CancellationToken ct)
    {
        // docker CLI present AND engine actually running → nothing to do.
        if (Net.Ports.DockerEngineRunning())
        {
            L("检测到 Docker 引擎已在运行，跳过安装");
            return;
        }

        var dockerCliPath = @"C:\Program Files\Docker\Docker\resources\bin\docker.exe";
        var haveCli = File.Exists(dockerCliPath) || ProcessRunner.Available("docker");

        if (!haveCli)
        {
            L("未检测到 Docker Desktop，开始自动安装……");
            // Real-machine pitfall: stale installer processes / locked temp files break extraction.
            PreCleanDockerInstall(L);

            var wingetOk = false;
            if (ProcessRunner.Available("winget"))
            {
                L("通过 winget 安装 Docker Desktop（体积较大，请耐心等待）……");
                var r = await ProcessRunner.RunAsync("winget",
                    "install -e --id Docker.DockerDesktop --silent --accept-package-agreements --accept-source-agreements",
                    onLine: Out, ct: ct);
                if (File.Exists(dockerCliPath)) wingetOk = true;
                else if (r.Success) wingetOk = true;
                else L($"winget 退出码 {r.ExitCode}，改用官方安装器直装……");
            }

            if (!wingetOk)
            {
                var exe = DeployPaths.Join(_p.DlDir, "DockerDesktopInstaller.exe");
                await Downloader.DownloadAsync(DeployOptions.Urls.DockerSetup, exe, L, ct: ct);
                L("运行 Docker Desktop 安装器（静默模式）……");
                var r = await ProcessRunner.RunAsync(exe, "install --quiet --accept-license", onLine: Out, ct: ct);
                if (r.ExitCode != 0 && !File.Exists(dockerCliPath))
                    throw new InvalidOperationException(
                        $"Docker Desktop 安装器退出码 {r.ExitCode}（若提示需重启，重启后重新打开部署器会自动续装）");
            }
        }
        else
        {
            L("检测到 docker 命令，但引擎未运行——启动 Docker Desktop 并等待就绪……");
        }

        // Refresh PATH from machine so the fresh install is visible.
        RefreshPathFromMachine();

        // WSL prerequisite: Docker Desktop (WSL2 backend) cannot start without it.
        // Detect it BEFORE launching Docker Desktop so the user never sees the
        // "WSL not installed" dialog; enable the features and request a reboot right away.
        if (!await WslStatusOkAsync(ct))
        {
            L("检测到缺少 WSL 组件（Docker 运行的前提）——先启用系统功能并安装，随后需要重启一次……");
            await EnableVirtualizationAndRequestReboot(ct);
        }

        L("启动 Docker Desktop 并等待引擎就绪（首次启动最长等 5 分钟）……");
        var dd = @"C:\Program Files\Docker\Docker\Docker Desktop.exe";
        if (File.Exists(dd)) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dd) { UseShellExecute = true });

        var ready = await Net.Ports.WaitDockerEngineAsync(TimeSpan.FromMinutes(5), ct);
        if (!ready)
        {
            // "Create the condition": enable virtualization features ourselves.
            await EnableVirtualizationAndRequestReboot(ct);
            throw new InvalidOperationException("Docker 引擎 5 分钟内未就绪。已尝试启用系统虚拟化功能；若提示重启，请重启电脑后重新打开部署器（自动续装）");
        }
        L("Docker Desktop 就绪");
    }

    private static void PreCleanDockerInstall(Action<string> log)
    {
        foreach (var name in new[] { "Docker Desktop Installer", "Docker Desktop", "com.docker.backend", "docker" })
        {
            try
            {
                foreach (var p in System.Diagnostics.Process.GetProcessesByName(name))
                {
                    log($"结束残留进程: {name} (PID {p.Id})");
                    p.Kill(entireProcessTree: true);
                    p.Dispose();
                }
            }
            catch { }
        }
        try
        {
            var tmp = Path.GetTempPath();
            foreach (var d in Directory.GetDirectories(tmp, "*docker*"))
            {
                try { Directory.Delete(d, recursive: true); log("清理残留临时目录: " + d); } catch { }
            }
        }
        catch { }
    }

    private async Task<bool> WslStatusOkAsync(CancellationToken ct)
    {
        // wsl.exe itself may be absent on older Win10 — Process.Start would throw
        // Win32Exception instead of returning a failure result. Treat any probe
        // failure (missing exe, non-zero exit) as "WSL not installed".
        try
        {
            var r = await ProcessRunner.RunAsync("wsl.exe", "--status", onLine: Out, ct: ct);
            return r.Success;
        }
        catch (Exception ex)
        {
            L($"WSL 检测不可用（{ex.Message}），按未安装处理");
            return false;
        }
    }

    private async Task EnableVirtualizationAndRequestReboot(CancellationToken ct)
    {
        L("检测系统虚拟化功能状态……");
        var wslOk = await WslStatusOkAsync(ct);
        if (wslOk)
        {
            // Engine still not up after 5 min with WSL present — likely needs reboot after feature enable.
            L("WSL 已存在但引擎未就绪，可能需要重启使虚拟化功能生效");
        }
        else
        {
            L("启用系统虚拟化功能（需要管理员权限）……");
            await ProcessRunner.RunAsync("dism.exe", "/online /enable-feature /featurename:Microsoft-Windows-Subsystem-Linux /all /norestart", onLine: Out, outputEncoding: GbkOutput, ct: ct);
            await ProcessRunner.RunAsync("dism.exe", "/online /enable-feature /featurename:VirtualMachinePlatform /all /norestart", onLine: Out, outputEncoding: GbkOutput, ct: ct);
            await ProcessRunner.RunAsync("wsl.exe", "--install --no-distribution", onLine: Out, ct: ct);
        }
        // Leave a marker so the app can greet the user with "continue after reboot".
        try
        {
            Directory.CreateDirectory(_p.RuntimeDir);
            await File.WriteAllTextAsync(_p.RebootMarker, "docker-route", ct);
        }
        catch { }
        throw new RebootRequiredException("需要重启电脑以启用虚拟化功能。重启后重新打开部署器，会从断点自动继续安装。");
    }

    private async Task StepDockerMode(CancellationToken ct)
    {
        // Fresh Docker Desktop installs default to Windows containers; our images are all Linux.
        var osType = await DockerOsType(ct);
        if (osType == "linux") { L("Docker 已是 Linux 容器模式"); return; }
        L($"Docker 当前为 {osType} 容器模式，切换为 Linux 容器模式……");
        var dockerCli = @"C:\Program Files\Docker\Docker\DockerCli.exe";
        var switched = false;
        if (File.Exists(dockerCli))
        {
            for (var s = 1; s <= 4 && !switched; s++)
            {
                L($"切换尝试 {s}……");
                await ProcessRunner.RunAsync(dockerCli, "-SwitchDaemon", onLine: Out, ct: ct);
                for (var w = 0; w < 10; w++)
                {
                    await Task.Delay(6000, ct);
                    if (await DockerOsType(ct) == "linux") { switched = true; break; }
                }
                if (switched) break;
                if (s == 2)
                {
                    L("切换未生效，重启 Docker Desktop 后再试……");
                    foreach (var pn in new[] { "Docker Desktop", "com.docker.backend" })
                    {
                        try { foreach (var p in System.Diagnostics.Process.GetProcessesByName(pn)) p.Kill(entireProcessTree: true); } catch { }
                    }
                    await Task.Delay(5000, ct);
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(@"C:\Program Files\Docker\Docker\Docker Desktop.exe") { UseShellExecute = true });
                    await Task.Delay(20000, ct);
                }
            }
        }
        if (!switched)
        {
            osType = await DockerOsType(ct);
            throw new InvalidOperationException($"Docker 容器模式切换失败（当前 OSType={osType}），无法拉起 Linux 镜像");
        }
        L("已切换为 Linux 容器模式");
    }

    private async Task<string> DockerOsType(CancellationToken ct)
    {
        var r = await ProcessRunner.RunAsync("docker", "info --format '{{.OSType}}'", onLine: null, ct: ct);
        // Real-machine pitfall: depending on shell quoting the value can come back wrapped in
        // quotes ("linux" / 'linux'). Strip them before comparing, or a Linux-mode engine is
        // misread as unknown and the switcher needlessly toggles daemons.
        var line = r.Output.FirstOrDefault(l => !string.IsNullOrWhiteSpace(l))?.Trim() ?? "unknown";
        return line.Trim('"', '\'').Trim();
    }

    private async Task StepComposeUp(CancellationToken ct)
    {
        // Domestic registry mirrors so image pulls never depend on a VPN.
        // Covers compose images AND Dockerfile FROM layers (all docker.io).
        EnsureDockerMirrorConfig(ct);
        // Real-machine pitfall: after the mirror restart — or on a manual retry right
        // after Docker was killed/closed — compose can run while the engine pipe is
        // gone ("The system cannot find the file specified"). Probe first, start
        // Docker Desktop if needed, and only then run compose.
        await EnsureDockerEngineReadyAsync(ct);
        L("容器路线：docker compose 一键拉起全部服务（按当前源码重建镜像）……");
        // Real-machine pitfall (2026-11-22): plain `up -d` reuses images built by an
        // earlier run from older sources, so source fixes never reach the containers
        // (TaskService kept crashing on a stale image). Always rebuild so the images
        // match the sources shipped in this package; unchanged layers stay cached.
        var r = await ProcessRunner.RunAsync("docker", "compose up -d --build", workingDirectory: _p.ComposeDir, onLine: Out, ct: ct);
        if (!r.Success)
        {
            // Surface the real reason (pull failure, build error, port conflict…)
            // on the fail page instead of a bare exit code.
            var tail = string.Join(" | ", r.Output
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .TakeLast(4)
                .Select(l => l.Trim()));
            var detail = tail.Length > 0 ? "：" + tail : "（无输出，常见原因：网络拉不动镜像或 compose 文件缺失，详见日志）";
            throw new InvalidOperationException($"docker compose up 失败（exit={r.ExitCode}）{detail}");
        }
    }

    private async Task EnsureDockerEngineReadyAsync(CancellationToken ct)
    {
        var probe = await ProcessRunner.RunAsync("docker", "info --format '{{.OSType}}'", onLine: null, ct: ct);
        if (probe.Success) return;

        L("Docker 引擎未就绪，启动 Docker Desktop 并等待（最长 5 分钟）……");
        var dd = @"C:\Program Files\Docker\Docker\Docker Desktop.exe";
        if (File.Exists(dd))
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dd) { UseShellExecute = true });
        var ready = await Net.Ports.WaitDockerEngineAsync(TimeSpan.FromMinutes(5), ct);
        if (!ready)
            throw new InvalidOperationException("Docker 引擎 5 分钟内未就绪。请手动打开 Docker Desktop，等它启动完成后点「重试」继续");
        L("Docker 引擎已就绪");
    }

    private static readonly string[] DockerMirrors =
    {
        "https://docker.xuanyuan.me",
        "https://docker.1ms.run",
        "https://docker.m.daocloud.io",
    };

    private void EnsureDockerMirrorConfig(CancellationToken ct)
    {
        var daemonPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".docker", "daemon.json");
        try
        {
            var json = File.Exists(daemonPath) ? File.ReadAllText(daemonPath) : "{}";
            var node = System.Text.Json.Nodes.JsonNode.Parse(json) as System.Text.Json.Nodes.JsonObject
                       ?? new System.Text.Json.Nodes.JsonObject();

            var existing = new List<string>();
            if (node["registry-mirrors"] is System.Text.Json.Nodes.JsonArray arr)
                foreach (var t in arr)
                    if (t is not null) existing.Add(t.GetValue<string>());

            if (existing.Any(e => DockerMirrors.Contains(e, StringComparer.OrdinalIgnoreCase)))
            {
                L("Docker 镜像加速源已配置，跳过");
                return;
            }

            var merged = new System.Text.Json.Nodes.JsonArray();
            foreach (var m in DockerMirrors) merged.Add(m);
            foreach (var e in existing.Where(e => !DockerMirrors.Contains(e, StringComparer.OrdinalIgnoreCase)))
                merged.Add(e);
            node["registry-mirrors"] = merged;

            Directory.CreateDirectory(Path.GetDirectoryName(daemonPath)!);
            File.WriteAllText(daemonPath, node.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            L("已写入国内镜像加速源（轩辕/毫秒/DaoCloud），重启 Docker 使其生效……");
            RestartDockerForMirror(ct);
        }
        catch (Exception ex)
        {
            L($"[WARN] 镜像加速源配置未完成：{ex.Message}（不阻断部署；如拉取仍失败，可在 Docker Desktop 设置中手动配置）");
        }
    }

    private void RestartDockerForMirror(CancellationToken ct)
    {
        foreach (var name in new[] { "Docker Desktop", "com.docker.backend", "docker" })
            try
            {
                foreach (var p in System.Diagnostics.Process.GetProcessesByName(name))
                {
                    p.Kill(entireProcessTree: true);
                    p.Dispose();
                }
            }
            catch { }
        Thread.Sleep(3000);
        var dd = @"C:\Program Files\Docker\Docker\Docker Desktop.exe";
        if (File.Exists(dd))
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dd) { UseShellExecute = true });
        var ready = Net.Ports.WaitDockerEngineAsync(TimeSpan.FromMinutes(5), ct).GetAwaiter().GetResult();
        if (!ready)
            throw new InvalidOperationException("配置镜像加速后 Docker 引擎未恢复（5 分钟超时），请手动启动 Docker Desktop 后重试");
        L("Docker 引擎已恢复，加速源生效");
    }

    private async Task StepDockerHealth(CancellationToken ct)
    {
        L("等待健康检查（网关/认证/任务/文件中心/前端，每项最多等 15 分钟）……");
        var targets = new List<(string Name, string Url)>
        {
            ("网关", "http://127.0.0.1:5000/health"),
            ("认证服务", "http://127.0.0.1:5001/health"),
            ("任务服务", "http://127.0.0.1:5002/health"),
            ("文件中心", "http://127.0.0.1:5003/health"),
            ("前端", "http://127.0.0.1:8080/"),
        };
        foreach (var (name, url) in targets)
        {
            // 15 min each: first boot runs CodeFirst table creation + seed data inserts
            // (identity alone took >5 min on the CI runner); 8GB machines also pull/start Nacos+ES.
            if (!await Net.Http.WaitOkAsync(url, TimeSpan.FromMinutes(15), ct))
                throw new InvalidOperationException($"容器路线健康检查未通过: {name}（{url}）");
            L($"健康检查通过: {name}");
        }
    }

    // ---------------- uninstall ----------------

    public async Task UninstallAsync(CancellationToken ct)
    {
        L("==================== 一键卸载 ====================");

        // 1) Container route: remove containers AND volumes (database data lives in volumes).
        if (Net.Ports.DockerEngineRunning())
        {
            L("容器路线：docker compose down -v（删除容器与数据卷）……");
            await ProcessRunner.RunAsync("docker", "compose down -v --remove-orphans", workingDirectory: _p.ComposeDir, onLine: Out, ct: ct);
        }

        // 2) Stop all native processes (pid files first, then by port).
        StopByPidFiles();
        await StopByPorts(ct);

        // Give killed processes time to fully exit and release file handles,
        // otherwise the runtime directory delete below fails partially.
        await Net.Ports.WaitPortsClosedAsync(
            new[] { 5000, 5001, 5002, 5003, 8080, 3306, 6379 }, TimeSpan.FromSeconds(20), ct);

        // 2.5) Kill lingering build/worker processes running from the runtime dir
        // (e.g. MSBuild node-reuse workers holding dotnet.exe open).
        KillProcessesUnder(_p.RuntimeDir);

        // 3) Delete artifacts and runtimes (source code is kept).
        var targets = new List<(string Path, string Desc)>
        {
            (_p.PublishDir, "发布产物"),
            (DeployPaths.Join(_p.FrontendDir, "node_modules"), "前端依赖缓存"),
            (DeployPaths.Join(_p.FrontendDir, "dist"), "前端构建产物"),
        };
        if (!_opt.KeepRuntimes)
            targets.Add((_p.RuntimeDir, "运行时与数据库数据"));
        else
        {
            targets.Add((_p.PidDir, "进程记录"));
            targets.Add((_p.LogDir, "运行日志"));
        }
        foreach (var (path, desc) in targets)
        {
            if (Directory.Exists(path))
            {
                TryDeleteDir(path, L);
                if (!Directory.Exists(path)) L($"已删除 {desc}");
                else L($"[WARN] {desc}未能完全删除：{path}");
            }
        }

        // 4) Clear checkpoint state and reboot marker.
        _state.Clear();
        try { if (File.Exists(_p.RebootMarker)) File.Delete(_p.RebootMarker); } catch { }

        L("========================================================");
        L("卸载完成：服务已停止，部署产物/运行时/数据库数据/状态已清除，源码完整保留。");
        if (_opt.KeepRuntimes) L("（按要求保留了运行时，重装会快很多）");
        await Task.CompletedTask;
    }

    private void KillProcessesUnder(string dir)
    {
        if (!OperatingSystem.IsWindows() || !Directory.Exists(dir)) return;
        var prefix = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar)
                     + Path.DirectorySeparatorChar;
        foreach (var proc in System.Diagnostics.Process.GetProcesses())
        {
            try
            {
                string? path;
                try { path = proc.MainModule?.FileName; } catch { continue; }
                if (string.IsNullOrEmpty(path) ||
                    !path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                var pname = proc.ProcessName;
                proc.Kill(entireProcessTree: true);
                try { proc.WaitForExit(5000); } catch { }
                L($"已停止运行时目录内的进程 {pname}（PID {proc.Id}）");
            }
            catch { }
        }
    }

    private void StopByPidFiles()
    {
        if (!Directory.Exists(_p.PidDir)) return;
        foreach (var f in Directory.GetFiles(_p.PidDir, "*.pid"))
        {
            var content = File.ReadAllText(f).Trim();
            if (int.TryParse(content, out var pid))
            {
                try
                {
                    var p = System.Diagnostics.Process.GetProcessById(pid);
                    p.Kill(entireProcessTree: true);
                    try { p.WaitForExit(5000); } catch { }
                    L($"已停止 {Path.GetFileNameWithoutExtension(f)}（PID {pid}）");
                }
                catch { }
            }
        }
    }

    private async Task StopByPorts(CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) return;
        foreach (var port in new[] { 5000, 5001, 5002, 5003, 8080, 3306, 6379 })
        {
            var conns = System.Net.NetworkInformation.IPGlobalProperties
                .GetIPGlobalProperties().GetActiveTcpListeners();
            foreach (var c in conns)
            {
                if (c.Port != port) continue;
                // Known service names are stopped unconditionally; foreign processes need confirmation.
                var pid = Net.Ports.FindOwningProcess(port);
                if (pid <= 0 || pid == Environment.ProcessId) continue;
                var pname = Net.Ports.ProcessName(pid);
                var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "dotnet", "mysqld", "redis-server", "node",
                    "TaoAdmin.GatewayService", "TaoAdmin.IdentityService", "TaoAdmin.TaskService", "TaoFiles.Api",
                };
                var stop = known.Contains(pname) ||
                           (ConfirmStopProcess?.Invoke(new PortConflictInfo(port, pid, pname)) ?? false);
                if (stop)
                {
                    try
                    {
                        var p = System.Diagnostics.Process.GetProcessById(pid);
                        p.Kill(entireProcessTree: true);
                        try { p.WaitForExit(5000); } catch { }
                        L($"已停止端口 {port} 上的 {pname}（PID {pid}）");
                    }
                    catch { }
                }
            }
            await Task.CompletedTask;
        }
    }

    // ---------------- status ----------------

    public StatusReport Status()
    {
        var items = new List<StatusItem>
        {
            new("网关(5000)", 5000, Net.Ports.PortOpen(5000)),
            new("认证服务(5001)", 5001, Net.Ports.PortOpen(5001)),
            new("任务服务(5002)", 5002, Net.Ports.PortOpen(5002)),
            new("文件中心(5003)", 5003, Net.Ports.PortOpen(5003)),
            new("前端(8080)", 8080, Net.Ports.PortOpen(8080)),
            new("MySQL(3306)", 3306, Net.Ports.PortOpen(3306)),
            new("Redis(6379)", 6379, Net.Ports.PortOpen(6379)),
        };
        var containers = new List<string>();
        if (Net.Ports.DockerEngineRunning())
        {
            // Task.Run: never block-and-resume on a captured UI context (dispatcher deadlock).
            // Double quotes: Windows child-process parsing treats single quotes as literal
            // characters, so '{{.Names}} {{.Status}}' split into two args and docker ps
            // rejected the stray positional argument ('docker ps' accepts no arguments).
            var r = Task.Run(() => ProcessRunner.RunAsync("docker", "ps --format \"{{.Names}} {{.Status}}\"")).GetAwaiter().GetResult();
            containers.AddRange(r.Output.Where(l => !string.IsNullOrWhiteSpace(l)));
        }
        return new StatusReport(items, containers);
    }

    // ---------------- helpers ----------------

    private static void ExtractWithRetry(string zip, string dest, Action<string>? log)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                if (attempt > 1)
                {
                    log?.Invoke($"解压重试（第 {attempt} 次）……");
                    System.Threading.Thread.Sleep(3000 * attempt);
                }
                System.IO.Compression.ZipFile.ExtractToDirectory(zip, dest, overwriteFiles: true);
                return;
            }
            catch (Exception ex) when (attempt < 3 && ex is IOException or UnauthorizedAccessException)
            {
                log?.Invoke($"解压失败（文件可能被占用）: {ex.Message}");
                TryDeleteDir(dest);
            }
        }
        throw new IOException($"解压多次失败: {Path.GetFileName(zip)}（文件可能被杀毒软件占用，请暂时关闭实时防护后重试）");
    }

    private static void MoveWithRetry(string src, string dest)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                if (attempt > 1) System.Threading.Thread.Sleep(3000 * attempt);
                if (Directory.Exists(dest)) TryDeleteDir(dest);
                Directory.Move(src, dest);
                return;
            }
            catch (IOException) when (attempt < 3) { }
            catch (UnauthorizedAccessException) when (attempt < 3) { }
        }
        // Last resort: copy instead of move.
        CopyDir(src, dest);
        TryDeleteDir(src);
    }

    private static void CopyDir(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.GetFiles(src))
            File.Copy(f, DeployPaths.Join(dst, Path.GetFileName(f)), overwrite: true);
        foreach (var d in Directory.GetDirectories(src))
            CopyDir(d, DeployPaths.Join(dst, Path.GetFileName(d)));
    }

    private static void TryDeleteDir(string dir, Action<string>? log = null)
    {
        // Retry: freshly killed processes may still hold file handles for a moment.
        for (var attempt = 1; attempt <= 6; attempt++)
        {
            if (!Directory.Exists(dir)) return;
            try
            {
                Directory.Delete(dir, recursive: true);
                return;
            }
            catch (Exception ex)
            {
                if (attempt >= 6)
                {
                    log?.Invoke($"[WARN] 删除失败（{ex.GetType().Name}: {ex.Message}）：{dir}");
                    try
                    {
                        foreach (var f in Directory.EnumerateFileSystemEntries(dir, "*", SearchOption.AllDirectories).Take(10))
                            log?.Invoke($"  残留: {f}");
                    }
                    catch { }
                }
                else System.Threading.Thread.Sleep(1500);
            }
        }
    }

    private static void TryDeleteFile(string file)
    {
        try { if (File.Exists(file)) File.Delete(file); } catch { }
    }

    private static void RefreshPathFromMachine()
    {
        var machinePath = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine);
        if (string.IsNullOrEmpty(machinePath)) return;
        var current = Environment.GetEnvironmentVariable("Path") ?? "";
        var dockerBin = @"C:\Program Files\Docker\Docker\resources\bin";
        var parts = current.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (Directory.Exists(dockerBin) && !parts.Contains(dockerBin, StringComparer.OrdinalIgnoreCase))
            parts.Insert(0, dockerBin);
        foreach (var seg in machinePath.Split(';', StringSplitOptions.RemoveEmptyEntries))
            if (!parts.Contains(seg, StringComparer.OrdinalIgnoreCase)) parts.Add(seg);
        Environment.SetEnvironmentVariable("Path", string.Join(';', parts));
    }
}
