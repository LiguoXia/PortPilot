using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PortPilot.Core.Infrastructure;
using PortPilot.Core.Models;
using PortPilot.Core.Native;
using PortPilot.Core.Services;
using PortPilot.Services;
using PortPilot.ViewModels;
using PortPilot.Views;

namespace PortPilot;

public partial class App : Application
{
    private ServiceProvider? services;
    private ILogger<App>? logger;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            logger?.LogError(args.Exception, "Unhandled UI error");
            if (e.Args.Contains("--smoke-test") || e.Args.Contains("--perf-test"))
            {
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "smoke-error.txt"), args.Exception.ToString());
                args.Handled = true; Shutdown(1); return;
            }
            MessageBox.Show(args.Exception.Message, "PortPilot · 操作未完成", MessageBoxButton.OK, MessageBoxImage.Warning);
            args.Handled = true;
        };
        try
        {
            var store = new PortableStore();
            var collection = new ServiceCollection();
            collection.AddSingleton(store);
            collection.AddLogging(b => b.ClearProviders().AddProvider(new PortableLoggerProvider(store)));
            collection.AddSingleton<ProcessService>(); collection.AddSingleton<NetworkService>(); collection.AddSingleton<ProcessKillService>();
            collection.AddSingleton<UserDataService>(); collection.AddSingleton<ThemeService>(); collection.AddSingleton<DialogService>();
            collection.AddSingleton<MainViewModel>(); collection.AddSingleton<MainWindow>();
            services = collection.BuildServiceProvider(); logger = services.GetRequiredService<ILogger<App>>();
            logger.LogInformation("PortPilot starting. Admin {Admin}", ProcessNativeApi.IsAdministrator());
            if (e.Args.FirstOrDefault() == "--elevated-operation") { ShutdownMode = ShutdownMode.OnExplicitShutdown; await RunElevatedAsync(e.Args); Shutdown(); return; }
            var window = services.GetRequiredService<MainWindow>(); MainWindow = window; window.Show();
            await window.ViewModel.InitializeAsync();
            if (e.Args.Contains("--perf-test")) { await Infrastructure.PerformanceRun.RunAsync(window, Path.Combine(store.DataDirectory, "cache")); Shutdown(); return; }
            if (e.Args.Contains("--smoke-test")) await SmokeTestAsync(window, store);
        }
        catch (Exception ex)
        {
            logger?.LogCritical(ex, "Startup failed");
            if (e.Args.Contains("--smoke-test") || e.Args.Contains("--perf-test")) { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "smoke-error.txt"), ex.ToString()); Shutdown(1); return; }
            MessageBox.Show("PortPilot 无法启动。请确保 EXE 所在目录可写。\n\n" + ex.Message, "PortPilot", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
    private async Task RunElevatedAsync(string[] args)
    {
        if (!ProcessNativeApi.IsAdministrator() || args.Length != 3 || !Guid.TryParseExact(args[2], "N", out var operation)) throw new InvalidOperationException("管理员操作参数无效。");
        var plan = JsonSerializer.Deserialize<KillPlan>(Encoding.UTF8.GetString(Convert.FromBase64String(args[1]))) ?? throw new InvalidDataException("Invalid operation");
        if (plan.Targets.Count is 0 or > 500) throw new InvalidDataException("Invalid target count");
        var fresh = await Task.Run(() => services!.GetRequiredService<NetworkService>().Scan(CancellationToken.None));
        var verified = plan.Targets.Select(target => fresh.Processes.FirstOrDefault(p => p.Identity == target.Identity)
            ?? throw new InvalidOperationException($"PID {target.Identity.Pid} 已退出或启动时间已变化，请重新选择。")).ToArray();
        var verifiedPlan = services!.GetRequiredService<ProcessKillService>().CreatePlan(verified, fresh, false, plan.Force);
        plan = verifiedPlan with { Tree = plan.Tree };
        // The elevated helper independently confirms and validates every target; the payload is not trusted.
        var dialog = new ConfirmDialog("管理员进程操作", (plan.Force ? "将强制结束以下进程。" : "将请求以下进程正常关闭。") + "\n以下名称、路径与端口均已重新读取。所有端口和未保存数据都会受到影响。\n\n" + string.Join("\n\n", plan.Targets.Select(t => $"{t.Name} · PID {t.Identity.Pid}\n{t.Path}\n端口：{string.Join(", ", t.Ports)}")));
        if (dialog.ShowDialog() != true) return;
        var results = await Task.Run(() => services!.GetRequiredService<ProcessKillService>().ExecuteAsync(plan, CancellationToken.None));
        await File.WriteAllTextAsync(Path.Combine(AppContext.BaseDirectory, "data", "cache", "operation-" + operation.ToString("N") + ".json"), JsonSerializer.Serialize(results));
        // A separate journal avoids overwriting the unelevated application's in-memory history.
        logger!.LogInformation("Elevated operation result: {Result}", string.Join("; ", results.Select(r => $"{r.Pid}: {r.Message}")));
        MessageBox.Show(string.Join("\n", results.Select(r => $"PID {r.Pid}: {r.Message}")), "PortPilot · 执行结果");
    }
    private async Task SmokeTestAsync(MainWindow window, PortableStore store)
    {
        var vm = window.ViewModel;
        using var fixture = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        fixture.Start();
        await vm.RefreshAsync();
        var folder = Path.Combine(store.DataDirectory, "cache", "smoke"); Directory.CreateDirectory(folder);
        var themes = services!.GetRequiredService<ThemeService>();
        var result = new List<string> { $"Processes: {vm.ProcessCount}", $"TCP: {vm.Ports.Count(r => r.Protocol.StartsWith("TCP"))}", $"UDP: {vm.UdpCount}" };
        if (vm.ProcessCount == 0 || vm.Ports.Count == 0) throw new InvalidOperationException("Smoke scan returned no data");
        var collectionResets = 0; var viewResets = 0;
        vm.Ports.CollectionChanged += (_, e) => { if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset) collectionResets++; };
        vm.PortsView.CollectionChanged += (_, e) => { if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset) viewResets++; };
        var stableProcess = vm.Processes.First(p => p.Pid == Environment.ProcessId);
        await vm.RefreshAsync(); await vm.RefreshAsync();
        if (collectionResets != 0 || viewResets != 0 || !ReferenceEquals(stableProcess, vm.Processes.First(p => p.Pid == Environment.ProcessId))) throw new InvalidOperationException("Diff refresh replaced stable rows or reset collection views.");
        result.Add("Diff refresh: stable row identities, no collection/view Reset events");
        async Task Capture(string name)
        {
            await Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.Render);
            await Task.Delay(150);
            var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(window); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(folder, name + ".png")); encoder.Save(file);
        }
        themes.Apply("light"); await Capture("dashboard-light");
        Infrastructure.IconUiSmoke.Verify(window, themes, folder, result);
        foreach (var page in new[] {"Ports", "Processes", "Connections", "Favorites", "History", "Settings", "Search"})
        { vm.NavigateCommand.Execute(page); await Capture(page.ToLowerInvariant()); result.Add(page + ": rendered"); }
        vm.NavigateCommand.Execute("Processes"); vm.TreeMode = true; await Capture("process-tree"); vm.TreeMode = false;
        vm.NavigateCommand.Execute("Ports"); vm.SearchText = "PID:" + Environment.ProcessId; vm.SubmitSearchCommand.Execute(null); await Task.Delay(100);
        var fixturePort = ((System.Net.IPEndPoint)fixture.LocalEndpoint).Port;
        var own = vm.Ports.First(r => r.Pid == Environment.ProcessId && r.Port == fixturePort); vm.SelectedRow = own;
        for (var i = 0; i < 100 && vm.IsDetailBusy; i++) await Task.Delay(100);
        if (!vm.DetailCommandLine.Contains("--smoke-test")) throw new InvalidOperationException("Own command line not retrieved: " + vm.DetailCommandLine);
        result.Add("Own command line: verified"); await Capture("inspector");
        await Infrastructure.InspectorUiSmoke.VerifyAsync(window, result); await Capture("inspector-network"); window.InspectorTabs.SelectedIndex = 0;
        themes.Apply("dark"); await Capture("inspector-dark"); vm.HasDetails = false; vm.SearchText = ""; vm.SubmitSearchCommand.Execute(null); vm.NavigateCommand.Execute("Ports"); await Task.Delay(100); await Capture("ports-dark");
        vm.HasDetails = false; vm.SearchText = ""; vm.NavigateCommand.Execute("Dashboard"); themes.Apply("dark"); await Capture("dashboard-dark");
        foreach (var scale in new[] {1.25, 1.5, 1.75, 2.0})
        {
            var bitmap = new RenderTargetBitmap((int)(window.ActualWidth * scale), (int)(window.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            bitmap.Render(window); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(folder, $"dpi-{scale * 100:0}.png")); encoder.Save(file);
        }
        result.Add("DPI rendering: 125%, 150%, 175%, 200% (offscreen render, not physical monitor transitions)");
        window.Width = 900; window.Height = 600; await Capture("minimum-size-dark"); window.Width = 1200; window.Height = 760;
        await Infrastructure.SearchUiSmoke.VerifyAsync(window, themes, folder, fixturePort, result);
        await Infrastructure.LiveFilteringSmoke.VerifyAsync(window, result);
        vm.SelectedSearchField = SearchField.ProcessName; vm.IsExactSearch = true; vm.SearchText = "PortPilot.exe";
        vm.SubmitSearchCommand.Execute(null); await Task.Delay(100); await Capture("search-exact-process");
        vm.SearchText = ""; vm.SelectedSearchField = SearchField.All; vm.IsExactSearch = false;
        vm.OpenPaletteCommand.Execute(null); await Capture("command-palette");
        File.WriteAllLines(Path.Combine(folder, "results.txt"), result);
        logger!.LogInformation("UI smoke test completed"); Shutdown();
    }
    protected override void OnExit(ExitEventArgs e) { logger?.LogInformation("PortPilot stopped"); services?.Dispose(); base.OnExit(e); }
}
