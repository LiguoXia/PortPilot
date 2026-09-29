using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using PortPilot.Core.Models;
using PortPilot.Core.Services;

namespace PortPilot.Mac;

public partial class MainWindow : Window
{
    private readonly MainViewModel vm;
    private readonly CancellationTokenSource lifetime = new();
    private readonly DispatcherTimer timer = new();
    private ProcessSnapshot? inspected;
    private int? inspectedPort;
    private long inspectorRequest;
    private bool actionBusy;

    public MainWindow() : this([]) { }
    public MainWindow(string[] args)
    {
        InitializeComponent();
        var smoke = args.Contains("--smoke-test");
        vm = new MainViewModel(smoke ? Path.Combine(Path.GetTempPath(), "PortPilot-smoke-" + Guid.NewGuid().ToString("N")) : null);
        DataContext = vm; ApplyTheme();
        timer.Interval = TimeSpan.FromSeconds(vm.Interval);
        timer.Tick += async (_, _) => { if (vm.AutoRefresh && !actionBusy) await Guard(Refresh); };
        Opened += async (_, _) =>
        {
            if (smoke) await SmokeAsync(args); else { await Guard(Refresh); timer.Start(); }
        };
        Closed += (_, _) => { timer.Stop(); lifetime.Cancel(); inspectorRequest++; };
        KeyDown += async (_, e) =>
        {
            if ((e.KeyModifiers & KeyModifiers.Meta) != 0 && e.Key == Key.F) { SearchBox.Focus(); e.Handled = true; }
            if ((e.KeyModifiers & KeyModifiers.Meta) != 0 && e.Key == Key.R || e.Key == Key.F5) { e.Handled = true; await Guard(Refresh); }
            if (e.Key == Key.Escape) CloseInspector();
        };
    }
    private async Task Guard(Func<Task> action)
    {
        try { await action(); }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            vm.Status = "操作失败：" + ex.Message;
            if (Inspector.IsVisible && vm.Details == "正在读取…") vm.Details = ex.Message;
        }
    }
    private Task Run(Action action) { action(); return Task.CompletedTask; }
    private async Task Refresh()
    {
        var ports = PortsGrid.SelectedItems.Cast<EndpointRow>().Select(r => r.Key).ToHashSet();
        var processes = ProcessesGrid.SelectedItems.Cast<ProcessSnapshot>().Select(p => p.Identity).ToHashSet();
        var favorite = FavoritesGrid.SelectedItem as Favorite;
        await vm.RefreshAsync(lifetime.Token);
        foreach (var row in vm.Ports.Where(r => ports.Contains(r.Key))) PortsGrid.SelectedItems.Add(row);
        foreach (var row in vm.Processes.Where(p => processes.Contains(p.Identity))) ProcessesGrid.SelectedItems.Add(row);
        if (favorite != null && FavoritesGrid.SelectedItem != favorite) FavoritesGrid.SelectedItem = favorite;
    }
    private async void RefreshClick(object? sender, RoutedEventArgs e) => await Guard(Refresh);
    private void SearchClick(object? sender, RoutedEventArgs e) { vm.SubmitSearch(); Pages.SelectedIndex = 1; CloseInspector(); }
    private void SearchKeyDown(object? sender, KeyEventArgs e) { if (e.Key == Key.Enter) { SearchClick(sender, e); e.Handled = true; } }
    private void QuickPortClick(object? sender, RoutedEventArgs e)
    { vm.FieldIndex = (int)SearchField.LocalPort; vm.SearchText = ((Button)sender!).Tag?.ToString() ?? ""; SearchClick(sender, e); }
    private ProcessSnapshot[] Selected() => Pages.SelectedIndex == 2
        ? ProcessesGrid.SelectedItems.Cast<ProcessSnapshot>().ToArray()
        : PortsGrid.SelectedItems.Cast<EndpointRow>().Select(r => r.Process).OfType<ProcessSnapshot>().DistinctBy(p => p.Identity).ToArray();
    private ProcessSnapshot FirstSelected() => Selected().FirstOrDefault() ?? throw new InvalidOperationException("请选择能够读取身份的进程或端口。");
    private async void DetailsClick(object? sender, RoutedEventArgs e) => await Guard(async () =>
    {
        var process = FirstSelected(); var request = ++inspectorRequest;
        inspected = process; inspectedPort = Pages.SelectedIndex == 1 ? (PortsGrid.SelectedItem as EndpointRow)?.Connection.LocalPort : null;
        vm.Note = inspectedPort is { } port ? vm.Data.Notes.GetValueOrDefault(port, "") : "";
        Inspector.IsVisible = true; vm.Details = "正在读取…";
        var details = await Task.Run(() => vm.ProcessesService.DetailsAsync(process, lifetime.Token), lifetime.Token);
        if (request != inspectorRequest) return;
        var builder = new StringBuilder($"{process.Name}\nPID {process.Pid} · 父 PID {process.ParentPid}\n\n路径：{process.Path}\n用户：{process.User}\n启动：{process.Started}\nCPU：{process.Cpu}% · 内存：{process.Memory / 1048576d:F1} MiB（快照）\n\n启动命令：\n{details.CommandLine}\n\n工作目录：{details.WorkingDirectory}\n架构：{details.Architecture}\n签名：{details.Signature}\n\n网络端点：\n");
        foreach (var c in vm.Snapshot.Connections.Where(c => c.Pid == process.Pid)) builder.AppendLine($"{c.Protocol} {c.Local} → {c.Remote} {c.State}");
        builder.AppendLine("\n可见的程序 / 映射文件："); foreach (var module in details.Modules) builder.AppendLine(module);
        vm.Details = builder.ToString(); vm.Data.Record("查看", $"{process.Name} · PID {process.Pid}"); vm.UpdateUserData();
    });
    private void CloseInspector() { inspectorRequest++; Inspector.IsVisible = false; inspected = null; inspectedPort = null; vm.Details = ""; }
    private void CloseInspectorClick(object? sender, RoutedEventArgs e) => CloseInspector();
    private void PortSelectionChanged(object? sender, SelectionChangedEventArgs e) { }
    private async void PidPortsClick(object? sender, RoutedEventArgs e) => await Guard(RunAction(() =>
    { var process = FirstSelected(); vm.FieldIndex = (int)SearchField.Pid; vm.SearchText = process.Pid.ToString(); vm.ProtocolIndex = 0; vm.ListeningOnly = false; SearchClick(sender, e); }));
    private Func<Task> RunAction(Action action) => () => Run(action);
    private void AddFavorite(string type, string value)
    {
        if (!vm.Data.Favorites.Any(f => f.Type == type && f.Value == value)) vm.Data.Favorites.Add(new() { Type = type, Value = value, Name = value });
        vm.Data.SaveFavorites(); vm.UpdateUserData(); vm.Status = "已添加收藏。";
    }
    private async void FavoritePortClick(object? sender, RoutedEventArgs e) => await Guard(RunAction(() =>
    { foreach (var row in PortsGrid.SelectedItems.Cast<EndpointRow>()) AddFavorite("port", row.Connection.LocalPort.ToString()); }));
    private async void FavoriteProcessClick(object? sender, RoutedEventArgs e) => await Guard(RunAction(() =>
    { foreach (var p in Selected()) AddFavorite("process", p.Name); }));
    private async void FavoriteQueryClick(object? sender, RoutedEventArgs e) => await Guard(RunAction(() =>
    { var q = SearchQuery.Parse(vm.SearchText, (SearchField)vm.FieldIndex, vm.Exact); if (q.LocalPort is { } port) AddFavorite("port", port.ToString()); else throw new InvalidOperationException("请在搜索框输入本地端口号。"); }));
    private void FavoriteSelectionChanged(object? sender, SelectionChangedEventArgs e)
    { if (FavoritesGrid.SelectedItem is Favorite f) { vm.FavoriteName = f.Name; vm.FavoriteNote = f.Note; } }
    private void OpenFavoriteClick(object? sender, RoutedEventArgs e)
    { if (FavoritesGrid.SelectedItem is Favorite f) { vm.FieldIndex = (int)(f.Type == "port" ? SearchField.LocalPort : f.Type == "pid" ? SearchField.Pid : SearchField.ProcessName); vm.SearchText = f.Value; vm.Exact = true; SearchClick(sender, e); } }
    private async void SaveFavoriteClick(object? sender, RoutedEventArgs e) => await Guard(RunAction(() =>
    { if (FavoritesGrid.SelectedItem is Favorite f) { f.Name = vm.FavoriteName; f.Note = vm.FavoriteNote; vm.Data.SaveFavorites(); vm.UpdateUserData(); } }));
    private async void RemoveFavoriteClick(object? sender, RoutedEventArgs e) => await Guard(RunAction(() =>
    { if (FavoritesGrid.SelectedItem is Favorite f) { vm.Data.Favorites.Remove(f); vm.Data.SaveFavorites(); vm.UpdateUserData(); } }));
    private async void SaveNoteClick(object? sender, RoutedEventArgs e) => await Guard(RunAction(() =>
    { if (inspectedPort is not { } port) throw new InvalidOperationException("请从端口列表查看详情。"); vm.Data.SetNote(port, vm.Note); vm.Status = "端口备注已保存。"; }));
    private async void CopyDetailsClick(object? sender, RoutedEventArgs e) => await Guard(async () => { if (Clipboard is { } clipboard) await clipboard.SetTextAsync(vm.Details); });
    private async void OpenFileClick(object? sender, RoutedEventArgs e) => await Guard(async () =>
    { if (inspected is not { } process) return; var result = await Core.Mac.MacCommand.RunAsync("/usr/bin/open", ["-R", process.Path], lifetime.Token); if (result.ExitCode != 0) throw new IOException(result.Error); });
    private async void OpenDataClick(object? sender, RoutedEventArgs e) => await Guard(async () =>
    { var result = await Core.Mac.MacCommand.RunAsync("/usr/bin/open", [vm.DataLocation], lifetime.Token); if (result.ExitCode != 0) throw new IOException(result.Error); });
    private void ApplyTheme() => Application.Current!.RequestedThemeVariant = vm.Theme switch { "light" => ThemeVariant.Light, "dark" => ThemeVariant.Dark, _ => ThemeVariant.Default };
    private async void SaveSettingsClick(object? sender, RoutedEventArgs e) => await Guard(RunAction(() =>
    { vm.SaveSettings(); timer.Interval = TimeSpan.FromSeconds(vm.Interval); ApplyTheme(); vm.Status = "设置已保存。"; }));
    private async void TerminateClick(object? sender, RoutedEventArgs e) => await Guard(() => Terminate(false, false));
    private async void ForceClick(object? sender, RoutedEventArgs e) => await Guard(() => Terminate(false, true));
    private async void TreeClick(object? sender, RoutedEventArgs e) => await Guard(() => Terminate(true, false));
    private async Task Terminate(bool tree, bool force)
    {
        if (actionBusy) return;
        actionBusy = true;
        try
        {
            var plan = vm.KillService.CreatePlan(Selected(), vm.Snapshot, tree, force);
            var text = string.Join("\n", plan.Targets.Select(t => $"{t.Name} · PID {t.Identity.Pid}\n端口：{string.Join(", ", t.Ports)}"));
            if (!await Confirm("确认结束进程", $"将发送 {(force ? "SIGKILL（强制结束，可能丢失未保存数据）" : "SIGTERM（正常结束请求）")}。\n影响以下进程的全部连接：\n\n{text}")) return;
            if (tree && !await Confirm("再次确认进程树", $"本次将结束 {plan.Targets.Count} 个进程，只操作以上已确认目标。")) return;
            var results = await vm.KillService.ExecuteAsync(plan, lifetime.Token);
            foreach (var r in results) vm.Data.Record("结束进程", $"PID {r.Pid}：{r.Message}");
            await Refresh(); vm.Status = string.Join(" · ", results.Select(r => $"PID {r.Pid}：{r.Message}"));
        }
        finally { actionBusy = false; }
    }
    private async Task<bool> Confirm(string title, string message)
    {
        var dialog = new Window { Title = title, Width = 540, Height = 430, WindowStartupLocation = WindowStartupLocation.CenterOwner, CanResize = true };
        var yes = new Button { Content = "确认结束", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
        var no = new Button { Content = "取消" };
        yes.Click += (_, _) => dialog.Close(true); no.Click += (_, _) => dialog.Close(false);
        var grid = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), Margin = new Thickness(24) };
        grid.Children.Add(new ScrollViewer { Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap } });
        var buttons = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 12, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0), Children = { no, yes } };
        Grid.SetRow(buttons, 1); grid.Children.Add(buttons); dialog.Content = grid;
        return await dialog.ShowDialog<bool>(this);
    }
    private async void ExportClick(object? sender, RoutedEventArgs e) => await Guard(async () =>
    {
        IReadOnlyList<Dictionary<string, string>> rows;
        if (Pages.SelectedIndex == 2)
        {
            var source = ProcessesGrid.SelectedItems.Count > 0 ? ProcessesGrid.SelectedItems.Cast<ProcessSnapshot>() : vm.Processes;
            rows = source.Select(p => new Dictionary<string, string> { ["PID"] = p.Pid.ToString(), ["Process"] = p.Name, ["Path"] = p.Path, ["User"] = p.User, ["CPU"] = p.Cpu.ToString(), ["Memory"] = p.Memory.ToString(), ["ParentPID"] = p.ParentPid.ToString(), ["Started"] = p.Started }).ToArray();
        }
        else
        {
            var source = PortsGrid.SelectedItems.Count > 0 ? PortsGrid.SelectedItems.Cast<EndpointRow>() : vm.Ports;
            rows = source.Select(r => new Dictionary<string, string> { ["Protocol"] = r.Protocol, ["Local"] = r.Local, ["Remote"] = r.Remote, ["State"] = r.State, ["PID"] = r.Pid.ToString(), ["Process"] = r.Name, ["Path"] = r.Path }).ToArray();
        }
        var file = await StorageProvider.SaveFilePickerAsync(new() { Title = "导出选中行（未选择时导出当前列表）", SuggestedFileName = "PortPilot.csv", DefaultExtension = "csv", ShowOverwritePrompt = true,
            FileTypeChoices = [new("CSV") { Patterns = ["*.csv"] }, new("JSON") { Patterns = ["*.json"] }, new("Text") { Patterns = ["*.txt"] }] });
        if (file?.TryGetLocalPath() is { } path) { await ExportService.WriteAsync(path, rows, lifetime.Token); vm.Status = "已导出：" + path; }
    });
    private async Task SmokeAsync(string[] args)
    {
        int code = 0;
        try
        {
            await Refresh();
            if (!vm.Snapshot.Processes.Any(p => p.Pid == Environment.ProcessId && p.StartTicks > 0)) throw new InvalidOperationException("Smoke: current process not found");
            if (string.IsNullOrEmpty(SearchBox.Watermark)) throw new InvalidOperationException("Smoke: view not initialized");
            foreach (var theme in new[] { "light", "dark" })
            {
                vm.Theme = theme; ApplyTheme();
                for (int page = 0; page < Pages.ItemCount; page++) { Pages.SelectedIndex = page; await Task.Delay(100); }
            }
            vm.FieldIndex = (int)SearchField.Pid; vm.SearchText = Environment.ProcessId.ToString(); vm.SubmitSearch();
            if (vm.Processes.Count != 1) throw new InvalidOperationException("Smoke: PID filtering failed");
            Pages.SelectedIndex = 2; ProcessesGrid.SelectedItem = vm.Processes[0];
            var details = await vm.ProcessesService.DetailsAsync(vm.Processes[0], lifetime.Token);
            if (string.IsNullOrWhiteSpace(details.CommandLine)) throw new InvalidOperationException("Smoke: empty command line");
            vm.Theme = "light"; ApplyTheme(); Pages.SelectedIndex = 0; await Task.Delay(300);
            var index = Array.IndexOf(args, "--screenshot");
            if (index >= 0 && index + 1 < args.Length)
            {
                using var bitmap = new RenderTargetBitmap(new PixelSize((int)Bounds.Width, (int)Bounds.Height)); bitmap.Render(this); bitmap.Save(args[index + 1]);
            }
            Console.WriteLine("PortPilot macOS UI smoke passed (all tabs, themes, PID search, native details).");
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); code = 1; }
        finally { (Application.Current!.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)!.Shutdown(code); }
    }
}
