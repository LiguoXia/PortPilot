using System.Diagnostics;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PortPilot.Core.Models;
using PortPilot.Core.Services;

namespace PortPilot.ViewModels;

public sealed partial class MainViewModel
{
    partial void OnSelectedRowChanged(InspectorRow? value)
    {
        OnPropertyChanged(nameof(CanNote));
        KillCommand.NotifyCanExecuteChanged();
        if (value != null) _ = ShowDetailsAsync(value);
    }
    [RelayCommand] private async Task InspectAsync(InspectorRow? row) { if (row != null) { SelectedRow = row; await ShowDetailsAsync(row); } }
    private async Task ShowDetailsAsync(InspectorRow row)
    {
        detailToken?.Cancel(); detailToken = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var token = detailToken.Token;
        HasDetails = true; IsDetailBusy = true; DetailTitle = row.Name; DetailSubtitle = $"PID {row.Pid} · 正在读取…";
        PortNote = data.Notes.GetValueOrDefault(row.Port) ?? "";
        DetailCommandLine = "正在读取启动参数…"; DetailModules = "正在读取…"; DetailEnvironment = "正在读取…"; DetailFields.Clear();
        UpdateDetailConnections(row.Pid);
        var parent = snapshot.Processes.FirstOrDefault(p => p.Pid == row.ParentPid && p.StartTicks < row.Process.StartTicks);
        DetailFamily = $"Parent: {parent?.Name ?? "已退出 / 不可核实"} · PID {row.ParentPid}\n\nChildren:\n" + string.Join("\n", snapshot.Processes.Where(p => p.ParentPid == row.Pid && p.StartTicks >= row.Process.StartTicks).Select(p => $"{p.Name} · PID {p.Pid}"));
        try
        {
            var d = await Task.Run(() => processes.Details(row.Process, token), token);
            token.ThrowIfCancellationRequested();
            DetailCommandLine = d.CommandLine; DetailModules = d.Modules.Count == 0 ? "无可用模块 / Access Denied" : string.Join("\n", d.Modules);
            DetailEnvironment = "仅显示变量名，隐藏可能包含密钥的值。\n\n" + (d.EnvironmentNames.Count == 0 ? "Unavailable / Access Denied" : string.Join("\n", d.EnvironmentNames));
            DetailSubtitle = $"PID {row.Pid} · {d.Architecture} · {d.Elevated}";
            var fields = new List<DetailField>
            {
                new("Executable path", row.Path), new("Working directory", d.WorkingDirectory), new("Started", row.Started), new("User", row.User),
                new("Parent PID", row.ParentPid.ToString()), new("CPU / Memory (读取时)", $"{row.Cpu:0.0}% / {row.Memory:0.0} MB"),
                new("Threads / Handles", $"{row.Threads} / {row.Handles}"), new("Digital signature", d.Signature), new("Company", d.Company),
                new("Description", d.Description), new("File version", d.Version), new("Product", d.Product)
            };
            if (row.Connection != null) fields.InsertRange(0, [new("Endpoint", row.Local), new("Protocol / State", $"{row.Protocol} / {row.State}"), new("Remote", row.Remote)]);
            foreach (var f in fields) DetailFields.Add(f);
            Record("ViewPID", $"{row.Name} · PID {row.Pid}");
        }
        catch (OperationCanceledException) { /* Selection changed: the next request owns the panel. */ }
        catch (Exception ex) { if (!token.IsCancellationRequested) { DetailSubtitle = ex is System.ComponentModel.Win32Exception w && w.NativeErrorCode == 5 ? "Access Denied · 可使用管理员权限读取" : ex.Message; DetailCommandLine = "Unavailable / Access Denied"; DetailModules = "Unavailable"; DetailEnvironment = "Unavailable"; logger.LogWarning(ex, "Details failed PID {Pid}", row.Pid); } }
        finally { if (!token.IsCancellationRequested) IsDetailBusy = false; }
    }
    private void UpdateDetailConnections(int pid)
    {
        var rows = Ports.Where(r => r.Pid == pid).ToArray(); var keys = rows.Select(r => r.Key).ToHashSet();
        foreach (var old in DetailConnections.Where(r => !keys.Contains(r.Key)).ToArray()) DetailConnections.Remove(old);
        var present = DetailConnections.Select(r => r.Key).ToHashSet(); foreach (var row in rows) if (!present.Contains(row.Key)) DetailConnections.Add(row);
    }
    [RelayCommand] private void ViewPidPorts() { if (SelectedRow == null) return; var pid = SelectedRow.Pid; SelectedSearchField = SearchField.Pid; SearchText = pid.ToString(); Filter = "All"; Page = "Ports"; RefreshViews(); }
    [RelayCommand] private void Copy(string? kind)
    {
        if (SelectedRow is not { } r) return;
        var text = kind switch { "port" => r.Port.ToString(), "pid" => r.Pid.ToString(), "name" => r.Name, "path" => r.Path, "command" => DetailCommandLine, _ => r.CopyText() + "\nCommand: " + DetailCommandLine };
        dialogs.Copy(text); Status = "已复制到剪贴板";
    }
    [RelayCommand] private void OpenLocation() { if (SelectedRow is { } r) dialogs.OpenLocation(r.Path); }
    [RelayCommand] private void SaveNote() { if (SelectedRow?.Connection == null) return; data.SetNote(SelectedRow.Port, PortNote); Status = "备注已保存"; _ = RefreshAsync(); }
    [RelayCommand] private void FavoritePort()
    {
        var value = IsEmpty && !HasDetails ? CurrentSearch.LocalPort?.ToString() : SelectedRow?.Connection != null ? SelectedRow.Port.ToString() : CurrentSearch.LocalPort?.ToString();
        if (!int.TryParse(value, out var port) || port is < 1 or > 65535) { Status = "请选择端口或输入 1–65535 的端口号"; return; }
        AddFavorite("port", value, data.Notes.GetValueOrDefault(port) ?? $"Port {value}");
    }
    [RelayCommand] private void FavoriteProcess(string kind)
    { if (SelectedRow is { } r) AddFavorite(kind, kind == "pid" ? r.Pid.ToString() : r.Name, r.Name); }
    private void AddFavorite(string type, string value, string name)
    {
        if (Favorites.Any(f => f.Type == type && f.Value == value)) { Status = "已在收藏中"; return; }
        var f = new Favorite { Type = type, Value = value, Name = name }; Favorites.Add(f); data.Favorites.Add(f); data.SaveFavorites(); UpdateFavorites();
        Record("Favorite", $"{type}: {value}"); Status = "已收藏，自动刷新时持续监控";
    }
    [RelayCommand] private void SaveFavorite()
    { if (SelectedFavorite is { } f) { f.Name = FavoriteName; f.Note = FavoriteNote; data.SaveFavorites(); var i = Favorites.IndexOf(f); Favorites.RemoveAt(i); Favorites.Insert(i, f); SelectedFavorite = f; Status = "收藏已更新"; } }
    [RelayCommand] private void RemoveFavorite(Favorite? favorite)
    { if (favorite == null) return; Favorites.Remove(favorite); data.Favorites.Remove(favorite); data.SaveFavorites(); }
    [RelayCommand] private void QueryFavorite(Favorite? favorite)
    { if (favorite == null) return; SelectedSearchField = favorite.Type switch { "port" => SearchField.LocalPort, "pid" => SearchField.Pid, _ => SearchField.ProcessName }; IsExactSearch = true; SearchText = favorite.Value; Filter = "All"; Page = favorite.Type == "port" ? "Ports" : "Search"; RefreshViews(); }
    private void UpdateFavorites()
    {
        foreach (var f in Favorites)
        {
            var owners = f.Type == "port" ? Ports.Where(r => r.Port.ToString() == f.Value).Select(r => r.Process).DistinctBy(p => p.Identity).ToArray()
                : snapshot.Processes.Where(p => f.Type == "pid" ? p.Pid.ToString() == f.Value : p.Name.Equals(f.Value, StringComparison.OrdinalIgnoreCase)).ToArray();
            var state = snapshot.Warnings.Count > 0 && f.Type == "port" ? "Unknown" : owners.Length == 0 ? "Free" : "Occupied";
            if (f.Status is "Free" or "Occupied" && f.Status != state && state != "Unknown")
            { Status = $"{f.Value} · {(state == "Free" ? "Released" : "New")} · {DateTime.Now:HH:mm:ss}"; Record("Monitor", Status); }
            f.Status = state; f.Owner = owners.Length == 0 ? "—" : string.Join(", ", owners.Select(p => $"{p.Name} ({p.Pid})"));
        }
    }
    private bool CanKill(string? mode) => SelectedRow is { IsProtected: false } && SelectedRow.Process.StartTicks > 0;
    [RelayCommand(CanExecute = nameof(CanKill))] private async Task KillAsync(string? mode) => await KillRowsAsync(SelectedRow == null ? [] : [SelectedRow], mode ?? "normal");
    public async Task KillRowsAsync(IReadOnlyList<InspectorRow> rows, string mode)
    {
        try
        {
            var plan = killer.CreatePlan(rows.Select(r => r.Process), snapshot, mode == "tree", mode is "force" or "tree" or "release");
            if (!dialogs.ConfirmKill(plan)) return;
            if (plan.Tree && !dialogs.Confirm("再次确认结束进程树", $"将强制结束已列出的 {plan.Targets.Count} 个进程。未保存的工作可能丢失。是否继续？")) return;
            var results = await Task.Run(() => killer.ExecuteAsync(plan, lifetime.Token), lifetime.Token);
            foreach (var r in results) Record(r.Success ? "KillProcess" : "KillFailed", $"PID {r.Pid} · {r.Message}");
            Status = string.Join(" / ", results.Select(r => $"{r.Pid}: {r.Message}"));
            if (results.Any(r => r.AccessDenied) && dialogs.Confirm("需要管理员权限", "当前权限不足。是否打开管理员操作窗口重试？Windows 将显示 UAC 授权提示。"))
            {
                var failed = results.Where(r => r.AccessDenied).Select(r => r.Pid).ToHashSet();
                var elevatedResults = await dialogs.ElevateOperationAsync(plan with { Targets = plan.Targets.Where(t => failed.Contains(t.Identity.Pid)).ToArray() });
                foreach (var r in elevatedResults) Record(r.Success ? "KillProcess (admin)" : "KillFailed (admin)", $"PID {r.Pid} · {r.Message}");
                Status = "管理员操作窗口已关闭，请查看刷新后的进程状态。";
            }
            await RefreshAsync();
        }
        catch (OperationCanceledException) { Status = "操作已取消"; }
        catch (Exception ex) { logger.LogWarning(ex, "Kill request rejected"); dialogs.Notify("无法结束进程", ex.Message); Status = ex.Message; }
    }
    public void CopyRows(IReadOnlyList<InspectorRow> rows) { dialogs.Copy(string.Join("\n\n", rows.Select(r => r.CopyText()))); Status = $"已复制 {rows.Count} 条"; }
    public void FavoriteRows(IReadOnlyList<InspectorRow> rows) { foreach (var r in rows.Where(r => r.Connection != null).DistinctBy(r => r.Port)) AddFavorite("port", r.Port.ToString(), $"Port {r.Port}"); }
    private bool CanExport() => Page != "Settings";
    [RelayCommand(CanExecute = nameof(CanExport))] private async Task ExportAsync()
    {
        if (Page == "History")
            await ExportValuesAsync(History.Select(h => new Dictionary<string, string> { ["Time"] = h.Time.ToString("O"), ["Action"] = h.Action, ["Detail"] = h.Detail }).ToArray());
        else if (Page == "Favorites")
            await ExportValuesAsync(Favorites.Select(f => new Dictionary<string, string> { ["Type"] = f.Type, ["Value"] = f.Value, ["Name"] = f.Name, ["Note"] = f.Note, ["Status"] = f.Status, ["Owner"] = f.Owner }).ToArray());
        else if (Page == "Search")
            await ExportRowsAsync(PortsView.Cast<InspectorRow>().Concat(ProcessesView.Cast<InspectorRow>()).ToArray());
        else await ExportRowsAsync((Page == "Processes" ? ProcessesView : Page == "Connections" ? ConnectionsView : PortsView).Cast<InspectorRow>().ToArray());
    }
    public async Task ExportRowsAsync(IReadOnlyList<InspectorRow> rows)
        => await ExportValuesAsync(rows.Select(r => r.Export()).ToArray());
    private async Task ExportValuesAsync(IReadOnlyList<Dictionary<string, string>> values)
    {
        var path = dialogs.ExportPath(); if (path == null) return;
        try { await ExportService.WriteAsync(path, values, lifetime.Token); Status = $"已导出 {values.Count} 条到 {path}"; Record("Export", $"{values.Count} 条 · {path}"); }
        catch (Exception ex) { logger.LogError(ex, "Export failed"); dialogs.Notify("导出失败", ex.Message); }
    }
    [RelayCommand] private void ApplySettings()
    { data.SaveSettings(); themes.Apply(Settings.Theme); timer.Interval = TimeSpan.FromMilliseconds(Settings.RefreshInterval); if (Settings.AutoRefresh) timer.Start(); else timer.Stop(); RefreshViews(); Status = "设置已保存"; }
    [RelayCommand] private void OpenDirectory(string? kind) => dialogs.OpenDirectory(kind == "logs" ? System.IO.Path.Combine(DataDirectory, "logs") : DataDirectory);
    [RelayCommand] private void ClearHistory() { if (dialogs.Confirm("清空历史", "删除本机操作历史？此操作无法撤销。")) { data.ClearHistory(); UpdateHistory(); Status = "历史已清空"; } }
    [RelayCommand] private void ClearCache() { processes.ClearDetailsCache(); Status = "进程详情内存缓存已清除"; }
    [RelayCommand] private void ElevateReader() => dialogs.ElevateReader();
    [RelayCommand] private async Task RunPaletteAsync(string? command)
    {
        IsPaletteOpen = false;
        switch (command)
        {
            case "Search Port": SelectedSearchField = SearchField.LocalPort; SearchText = ""; Page = "Ports"; dialogs.FocusSearch(); break;
            case "Search PID": SelectedSearchField = SearchField.Pid; SearchText = ""; Page = "Search"; dialogs.FocusSearch(); break;
            case "Refresh": await RefreshAsync(); break;
            case "Kill Process": await KillAsync("normal"); break;
            case "Open Settings": Page = "Settings"; break;
            case "Toggle Theme": Settings.Theme = Settings.Theme == "dark" ? "light" : "dark"; ApplySettings(); break;
            case "Clear History": ClearHistory(); break;
            case "Export": await ExportAsync(); break;
        }
    }
}
