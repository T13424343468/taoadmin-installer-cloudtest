using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using TaoAdmin.Deployer.Core;
using TaoAdmin.Deployer.Core.Distributed;
using TaoAdmin.Deployer.Core.Net;

namespace TaoAdmin.Deployer.App;

public partial class MainWindow : Window
{
    private DeployPaths _paths = null!;
    private ObservableCollection<string> _envItems = new();
    private ObservableCollection<string> _stepItems = new();
    private System.Windows.Threading.DispatcherTimer? _etaTimer;
    private System.Windows.Threading.DispatcherTimer? _statusTimer;
    private System.Windows.Threading.DispatcherTimer? _clusterTimer;
    private DateTime _deployStart;
    private int _doneWeight, _totalWeight;
    private CancellationTokenSource? _cts;
    private string _chosenRoute = "native";
    private bool _deployRunning;

    // ---------------- 分布式中控状态 ----------------
    private readonly ClusterTopology _clusterTopo = new();
    private readonly ClusterInstallViewModel _clusterVm = new();
    private readonly NodeProgressLedger _clusterLedger = new();
    private readonly AgentNodeExecutor _clusterExecutor = new();
    private ClusterOrchestrator? _clusterOrchestrator;
    private bool _clusterRunning;

    public MainWindow()
    {
        InitializeComponent();
        _paths = DeployPaths.FromExecutable(Environment.ProcessPath ?? AppContext.BaseDirectory);
        EnvList.ItemsSource = _envItems;
        StepList.ItemsSource = _stepItems;
        Loaded += (_, _) => RefreshEnvironment();
        // Greet users who came back after a reboot mid-install.
        if (File.Exists(_paths.RebootMarker))
        {
            _envItems.Insert(0, "↻ 检测到上次安装等待重启——现在继续即可，会从断点自动续装。");
        }
    }

    // ---------------- environment detection ----------------

    private void RefreshEnvironment()
    {
        _envItems.Clear();
        _envItems.Add($"部署目录: {_paths.Root}");
        _envItems.Add($"管理员权限: {(IsAdmin() ? "✓ 已获取" : "✗ 未获取（部分功能需要）")}");
        _envItems.Add($".NET 10 SDK: {(File.Exists(DeployPaths.Join(_paths.RuntimeDir, "dotnet10", "dotnet.exe")) ? "✓ 已就绪" : "未安装（部署时自动安装）")}");
        _envItems.Add($"Node.js: {(File.Exists(DeployPaths.Join(_paths.RuntimeDir, "node20", "node.exe")) ? "✓ 已就绪" : "未安装（部署时自动安装）")}");
        _envItems.Add($"MySQL: {(Ports.PortOpen(3306) ? "✓ 运行中" : "未运行（部署时自动安装启动）")}");
        _envItems.Add($"Redis: {(Ports.PortOpen(6379) ? "✓ 运行中" : "未运行（部署时自动安装启动）")}");
        _envItems.Add($"Docker: {(Ports.DockerEngineRunning() ? "✓ 引擎运行中" : "未运行（容器路线会自动安装/启动）")}");
        _envItems.Add("建议: 日常使用选「原生路线」，无需容器、无需重启、装完即用。");
    }

    private static bool IsAdmin()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            using var id = System.Security.Principal.WindowsIdentity.GetCurrent();
            return new System.Security.Principal.WindowsPrincipal(id)
                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    // ---------------- navigation ----------------

    private void ShowPage(string page)
    {
        PageHome.Visibility = page == "home" ? Visibility.Visible : Visibility.Collapsed;
        PageRoute.Visibility = page == "route" ? Visibility.Visible : Visibility.Collapsed;
        PageProgress.Visibility = page == "progress" ? Visibility.Visible : Visibility.Collapsed;
        PageDone.Visibility = page == "done" ? Visibility.Visible : Visibility.Collapsed;
        PageFail.Visibility = page == "fail" ? Visibility.Visible : Visibility.Collapsed;
        PageUninstall.Visibility = page == "uninstall" ? Visibility.Visible : Visibility.Collapsed;
        PageStatus.Visibility = page == "status" ? Visibility.Visible : Visibility.Collapsed;
        PageCluster.Visibility = page == "cluster" ? Visibility.Visible : Visibility.Collapsed;
        TitleText.Text = page switch
        {
            "route" => "选择部署路线",
            "progress" => "部署进行中",
            "done" => "部署完成",
            "fail" => "部署遇到问题",
            "uninstall" => "一键卸载",
            "status" => "服务状态",
            "cluster" => "分布式部署（中控）",
            _ => "TaoAdmin 一键部署器",
        };
        if (page == "home") RefreshEnvironment();
        // 状态页每 10 秒自动刷新；离开页面即停表
        if (page == "status")
        {
            RefreshStatus();
            if (_statusTimer == null)
            {
                _statusTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
                _statusTimer.Tick += (_, _) => RefreshStatus();
            }
            _statusTimer.Start();
        }
        else
        {
            _statusTimer?.Stop();
        }
    }

    private void OnHomeClick(object sender, RoutedEventArgs e) => ShowPage("home");

    private void OnDeployClick(object sender, RoutedEventArgs e)
    {
        RouteNative.IsChecked = true; // recommended default
        ShowPage("route");
    }

    private void OnRouteBackClick(object sender, RoutedEventArgs e) => ShowPage("home");

    private void OnStartDeployClick(object sender, RoutedEventArgs e)
    {
        _chosenRoute = RouteDocker.IsChecked == true ? "docker" : "native";
        StartDeploy();
    }

    private void OnRetryClick(object sender, RoutedEventArgs e) => StartDeploy();

    private void OnUninstallClick(object sender, RoutedEventArgs e)
    {
        UninstallLog.Clear();
        ShowPage("uninstall");
    }

    private void OnStatusClick(object sender, RoutedEventArgs e)
    {
        RefreshStatus();
        ShowPage("status");
    }

    // ---------------- 分布式中控 ----------------

    private void OnClusterClick(object sender, RoutedEventArgs e) => ShowPage("cluster");

    private void OnClusterAddNodeClick(object sender, RoutedEventArgs e)
    {
        var host = ClusterNodeHost.Text.Trim();
        if (host.Length == 0)
        {
            MessageBox.Show(this, "请填写机器 IP。", "分布式部署", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (_clusterTopo.Nodes.Any(n => n.Host == host))
        {
            MessageBox.Show(this, "该机器已在清单中。", "分布式部署", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var node = new ClusterNode
        {
            Id = $"n{_clusterTopo.Nodes.Count + 1}",
            Name = $"机器{_clusterTopo.Nodes.Count + 1}",
            Host = host,
            SshUser = string.IsNullOrWhiteSpace(ClusterNodeUser.Text) ? "root" : ClusterNodeUser.Text.Trim(),
            State = NodeState.Pending,
        };
        _clusterTopo.Nodes.Add(node);

        // 默认角色集：中间件 + 网关 + 前端 + 应用（推荐布局自动分配）
        if (_clusterTopo.Roles.Count == 0)
        {
            _clusterTopo.Roles.Add(new ServiceRole { Id = "mysql", Name = "MySQL", Kind = ServiceKind.Database });
            _clusterTopo.Roles.Add(new ServiceRole { Id = "redis", Name = "Redis", Kind = ServiceKind.Cache });
            _clusterTopo.Roles.Add(new ServiceRole { Id = "gateway", Name = "网关", Kind = ServiceKind.Gateway });
            _clusterTopo.Roles.Add(new ServiceRole { Id = "frontend", Name = "前端", Kind = ServiceKind.Frontend });
            _clusterTopo.Roles.Add(new ServiceRole { Id = "app", Name = "应用服务", Kind = ServiceKind.AppService, Instances = 2 });
        }
        _clusterTopo.Placements = PlacementPlanner.Recommend(_clusterTopo);

        ClusterNodeHost.Clear();
        RefreshClusterMatrix();
    }

    private void RefreshClusterMatrix()
    {
        _clusterVm.Load(_clusterTopo);
        ClusterNodeList.Items.Clear();
        foreach (var n in _clusterTopo.Nodes)
            ClusterNodeList.Items.Add($"{n.Name}  {n.Host}（{n.SshUser}）  状态：{n.State}");
        ClusterMatrixText.Text = _clusterTopo.Placements.Count == 0
            ? "添加机器后自动生成推荐布局（中间件→数据机、网关前端→边缘机、应用实例轮转均摊）。"
            : "分配矩阵：" + string.Join("；", _clusterTopo.Placements
                .Select(p => $"{p.RoleId}#{p.InstanceIndex}→{_clusterTopo.Nodes.First(n => n.Id == p.NodeId).Name}"));
    }

    private async void OnClusterStartClick(object sender, RoutedEventArgs e)
    {
        if (_clusterRunning) return;
        if (_clusterTopo.Nodes.Count == 0)
        {
            MessageBox.Show(this, "请先添加机器。", "分布式部署", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _clusterRunning = true;
        _cts = new CancellationTokenSource();
        _clusterVm.ResetFailures();
        _clusterOrchestrator = new ClusterOrchestrator(_clusterExecutor, _clusterLedger);
        StartClusterTimer();

        try
        {
            // 并行安装：所有在线机器同时下发，进度经账本+视图模型实时上屏
            var results = await _clusterOrchestrator.InstallAllAsync(_clusterTopo, "cluster-install", failFast: false, _cts.Token);
            foreach (var r in results) _clusterVm.ApplyResult(r);
            ClusterOverallText.Text = _clusterVm.AnyFailed ? "部分机器安装失败，见红行提示" : "✓ 全部机器安装完成";
        }
        catch (OperationCanceledException)
        {
            ClusterOverallText.Text = "安装已取消";
        }
        catch (Exception ex)
        {
            ClusterOverallText.Text = $"安装失败：{ex.Message}";
        }
        finally
        {
            _clusterRunning = false;
            _clusterTimer?.Stop();
        }
    }

    private async void OnClusterUpgradeClick(object sender, RoutedEventArgs e)
    {
        if (_clusterRunning) return;
        if (_clusterTopo.Nodes.Count == 0)
        {
            MessageBox.Show(this, "请先添加机器。", "分布式部署", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _clusterRunning = true;
        _cts = new CancellationTokenSource();
        _clusterVm.ResetFailures();
        _clusterOrchestrator ??= new ClusterOrchestrator(_clusterExecutor, _clusterLedger);
        StartClusterTimer();

        try
        {
            // 滚动升级：逐台更新，坏一台自动停住不往下走
            var order = _clusterTopo.Nodes.Select(n => n.Id).ToList();
            var results = await _clusterOrchestrator.RollingUpgradeAsync(_clusterTopo, order, "cluster-upgrade", _cts.Token);
            foreach (var r in results) _clusterVm.ApplyResult(r);
            ClusterOverallText.Text = "✓ 滚动升级完成";
        }
        catch (OperationCanceledException)
        {
            ClusterOverallText.Text = "升级已取消";
        }
        catch (Exception ex)
        {
            ClusterOverallText.Text = $"滚动停住：{ex.Message}";
        }
        finally
        {
            _clusterRunning = false;
            _clusterTimer?.Stop();
        }
    }

    private void StartClusterTimer()
    {
        // 进度轮询：每 2 秒拉一次各机代理进度入账并渲染（装环境→拉镜像→起服务→健康检查四阶段）
        _clusterTimer ??= new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _clusterTimer.Tick += async (_, _) =>
        {
            foreach (var n in _clusterTopo.Nodes)
                await _clusterExecutor.PollProgressAsync(n, _clusterLedger, CancellationToken.None);
            foreach (var p in _clusterLedger.Snapshot())
                _clusterVm.ApplyProgress(p);
            RenderClusterProgress();
        };
        _clusterTimer.Start();
    }

    private void RenderClusterProgress()
    {
        ClusterNodeList.Items.Clear();
        foreach (var r in _clusterVm.Rows)
        {
            var flag = r.Failed ? "✗" : (r.Percent >= 100 ? "✓" : "⏳");
            ClusterNodeList.Items.Add(
                $"{flag} {r.Name}  {r.Host}  [{r.PhaseText}] {r.Percent}%  {r.Message}");
        }
        ClusterOverallBar.Value = _clusterVm.OverallPercent;
        ClusterOverallText.Text = $"整体 {_clusterVm.OverallPercent}%";
    }

    private void OnStatusRefreshClick(object sender, RoutedEventArgs e) => RefreshStatus();

    private void OnOpenLogDirClick(object sender, RoutedEventArgs e)
    {
        try
        {
            System.IO.Directory.CreateDirectory(_paths.LogDir);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_paths.LogDir}\"") { UseShellExecute = true });
        }
        catch { }
    }

    private void OnOpenFrontendClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("http://localhost:8080") { UseShellExecute = true });
        }
        catch { }
    }

    // ---------------- deploy flow ----------------

    private async void StartDeploy()
    {
        if (_deployRunning) return;
        _deployRunning = true;
        _cts = new CancellationTokenSource();
        _stepItems.Clear();
        LogBox.Clear();
        OverallBar.Value = 0;
        EtaText.Text = "";
        ShowPage("progress");

        var opt = new DeployOptions
        {
            Route = _chosenRoute,
            Force = false, // resume from checkpoint
        };
        var engine = new DeployEngine(_paths, opt);
        WireEngine(engine);

        _deployStart = DateTime.UtcNow;
        _etaTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _etaTimer.Tick += (_, _) => UpdateEta();
        _etaTimer.Start();

        try
        {
            await engine.DeployAsync(_cts.Token);
            _etaTimer?.Stop();
            DoneDetail.Text = _chosenRoute == "docker"
                ? "前端 http://localhost:8080　网关 http://localhost:5000　文件中心 http://localhost:5003\n账号 admin / Admin@123（请登录后立即修改默认口令）"
                : "前端 http://localhost:8080　网关 http://localhost:5000\n账号 admin / Admin@123（请登录后立即修改默认口令）";
            ShowPage("done");
        }
        catch (RebootRequiredException ex)
        {
            _etaTimer?.Stop();
            FailReason.Text = ex.Message;
            FailHint.Text = "重启电脑后重新打开部署器，点「一键部署」会从断点自动继续，不会重头再来。";
            ShowPage("fail");
        }
        catch (OperationCanceledException)
        {
            _etaTimer?.Stop();
            FailReason.Text = "部署已取消。";
            FailHint.Text = "已完成的步骤会记住，下次点「一键部署」从断点继续。";
            ShowPage("fail");
        }
        catch (Exception ex)
        {
            _etaTimer?.Stop();
            FailReason.Text = ex.Message;
            FailHint.Text = "点「重试」会从断点继续（已完成的步骤不会重做）。多数问题重试即可解决；反复失败请打开日志目录把日志发我。";
            ShowPage("fail");
        }
        finally
        {
            _deployRunning = false;
        }
    }

    private void WireEngine(DeployEngine engine)
    {
        var plan = engine.Plan();
        foreach (var s in plan) _stepItems.Add($"○  {s.Name}");

        engine.StepStarted = id =>
        {
            Dispatcher.Invoke(() =>
            {
                var idx = plan.ToList().FindIndex(s => s.Id == id);
                if (idx >= 0) _stepItems[idx] = $"⏳  {plan[idx].Name} ……";
                AppendLog($"—— {plan[Math.Max(0, idx)].Name} ——");
            });
        };
        engine.StepSucceeded = id =>
        {
            Dispatcher.Invoke(() =>
            {
                var idx = plan.ToList().FindIndex(s => s.Id == id);
                if (idx >= 0) _stepItems[idx] = $"✓  {plan[idx].Name}";
            });
        };
        engine.StepOutput = (_, line) => Dispatcher.Invoke(() => AppendLog("  " + line));
        engine.Log = msg => Dispatcher.Invoke(() => AppendLog(msg));
        engine.ProgressChanged = (done, total) => Dispatcher.Invoke(() =>
        {
            _doneWeight = done;
            _totalWeight = total;
            OverallBar.Value = total <= 0 ? 0 : 100.0 * done / total;
            UpdateEta();
        });
        engine.ConfirmStopProcess = info => Dispatcher.Invoke(() =>
            MessageBox.Show(this,
                $"端口 {info.Port} 被进程 {info.ProcessName}（PID {info.Pid}）占用。\n\n是否结束该进程以继续部署？",
                "端口被占用", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes);
    }

    private void UpdateEta()
    {
        if (_doneWeight <= 0 || _totalWeight <= 0) { EtaText.Text = "准备中……"; return; }
        var elapsed = DateTime.UtcNow - _deployStart;
        var remainSec = (int)(elapsed.TotalSeconds * (_totalWeight - _doneWeight) / _doneWeight);
        EtaText.Text = remainSec >= 60 ? $"整体预计剩余 约 {remainSec / 60} 分钟" : $"整体预计剩余 约 {remainSec} 秒";
    }

    private void AppendLog(string line)
    {
        LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {line}{Environment.NewLine}");
        LogBox.ScrollToEnd();
    }

    // ---------------- uninstall flow ----------------

    private async void OnConfirmUninstallClick(object sender, RoutedEventArgs e)
    {
        BtnConfirmUninstall.IsEnabled = false;
        try
        {
            var opt = new DeployOptions { KeepRuntimes = KeepRuntimesCheck.IsChecked == true };
            var engine = new DeployEngine(_paths, opt);
            engine.Log = msg => Dispatcher.Invoke(() => UninstallLog.AppendText(msg + Environment.NewLine));
            engine.StepOutput = (_, line) => Dispatcher.Invoke(() => UninstallLog.AppendText("  " + line + Environment.NewLine));
            await engine.UninstallAsync(CancellationToken.None);
            UninstallLog.AppendText(Environment.NewLine + "✓ 卸载完成，源码完整保留。" + Environment.NewLine);
        }
        catch (Exception ex)
        {
            UninstallLog.AppendText(Environment.NewLine + "卸载出现问题: " + ex.Message + Environment.NewLine);
        }
        finally
        {
            BtnConfirmUninstall.IsEnabled = true;
        }
    }

    // ---------------- status ----------------

    private void RefreshStatus()
    {
        var engine = new DeployEngine(_paths, new DeployOptions());
        var s = engine.Status();
        StatusList.Items.Clear();
        foreach (var i in s.Items)
            StatusList.Items.Add($"{i.Name,-16} {(i.Running ? "● 运行中" : "○ 未运行")}");
        foreach (var c in s.Containers)
            StatusList.Items.Add("  容器: " + c);
        StatusRefreshTime.Text = $"每 10 秒自动刷新 · 上次刷新 {DateTime.Now:HH:mm:ss}";
    }

    // ---------------- cancel ----------------

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "确定要取消部署吗？已完成的步骤会记住，下次从断点继续。",
                "取消部署", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            _cts?.Cancel();
        }
    }
}
