using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using PortPilot.Core.Infrastructure;
using PortPilot.Core.Mac;
using PortPilot.Core.Models;
using PortPilot.Core.Services;

namespace PortPilot.Mac;

public sealed record EndpointRow(ConnectionSnapshot Connection, ProcessSnapshot? Process)
{
    public string Key => Connection.Key;
    public string Protocol => Connection.Protocol;
    public string Local => Connection.Local;
    public string Remote => Connection.Remote;
    public string State => Connection.State;
    public int Pid => Connection.Pid;
    public string Name => Process?.Name ?? "Unavailable";
    public string User => Process?.User ?? "Unavailable";
    public string Path => Process?.Path ?? "Unavailable";
    public double Cpu => Process?.Cpu ?? 0;
    public double Memory => Math.Round((Process?.Memory ?? 0) / 1048576d, 1);
}

public sealed partial class MainViewModel : ObservableObject
{
    public MacProcessService ProcessesService { get; } = new();
    public MacKillService KillService { get; } = new();
    public PortableStore Store { get; }
    public UserDataService Data { get; }
    public ScanSnapshot Snapshot { get; private set; } = new([], [], DateTimeOffset.Now, []);
    public ObservableCollection<EndpointRow> Ports { get; } = [];
    public ObservableCollection<ProcessSnapshot> Processes { get; } = [];
    public ObservableCollection<Favorite> Favorites { get; } = [];
    public ObservableCollection<HistoryEntry> History { get; } = [];
    public string[] SearchFields { get; } = ["全部字段", "本地端口", "远程端口", "PID", "进程名称", "程序路径", "IP 地址"];
    public string[] Protocols { get; } = ["全部协议", "TCP", "UDP"];
    public int[] Intervals { get; } = [1, 2, 3, 5, 10, 30];
    public string[] Themes { get; } = ["system", "light", "dark"];
    public string DataLocation => Store.DataDirectory;
    [ObservableProperty] private string searchText = "";
    [ObservableProperty] private int fieldIndex;
    [ObservableProperty] private bool exact;
    [ObservableProperty] private int protocolIndex;
    [ObservableProperty] private bool listeningOnly;
    [ObservableProperty] private bool autoRefresh;
    [ObservableProperty] private int interval;
    [ObservableProperty] private string theme = "system";
    [ObservableProperty] private string status = "准备扫描…";
    [ObservableProperty] private string summary = "PortPilot · macOS";
    [ObservableProperty] private string details = "选择一个端口或进程，然后点击“查看详情”。";
    [ObservableProperty] private string note = "";
    [ObservableProperty] private string favoriteName = "";
    [ObservableProperty] private string favoriteNote = "";
    public SearchQuery ActiveQuery { get; private set; } = SearchQuery.Parse("");
    public bool Busy { get; private set; }

    public MainViewModel(string? testRoot = null)
    {
        var root = testRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "PortPilot");
        Store = new PortableStore(root); Data = new UserDataService(Store);
        AutoRefresh = Data.Settings.AutoRefresh; Interval = Data.Settings.RefreshInterval / 1000; Theme = Data.Settings.Theme;
        UpdateUserData();
    }
    public void SubmitSearch() { ActiveQuery = SearchQuery.Parse(SearchText, (SearchField)FieldIndex, Exact); Filter(); }
    partial void OnProtocolIndexChanged(int value) => Filter();
    partial void OnListeningOnlyChanged(bool value) => Filter();
    public async Task RefreshAsync(CancellationToken token)
    {
        if (Busy) return;
        Busy = true;
        try
        {
            // Enumeration, parsing and resource sampling never run on the UI thread.
            Snapshot = await Task.Run(() => new MacNetworkService(ProcessesService).ScanAsync(token), token);
            Filter(); UpdateUserData();
            Summary = $"{Snapshot.Connections.Count(c => c.Listening)} 监听    ·    {Snapshot.Connections.Count(c => c.Protocol.StartsWith("TCP"))} TCP    ·    {Snapshot.Connections.Count(c => c.Protocol.StartsWith("UDP"))} UDP    ·    {Snapshot.Processes.Count} 进程";
            Status = $"{Snapshot.Time:HH:mm:ss} 已刷新 · {string.Join(" ", Snapshot.Warnings)}";
        }
        finally { Busy = false; }
    }
    public void Filter()
    {
        var byPid = Snapshot.Processes.ToDictionary(p => p.Pid);
        Replace(Ports, Snapshot.Connections.Where(c => (ProtocolIndex == 0 || c.Protocol.StartsWith(ProtocolIndex == 1 ? "TCP" : "UDP")) && (!ListeningOnly || c.Listening))
            .Select(c => new EndpointRow(c, byPid.GetValueOrDefault(c.Pid))).Where(r => SearchService.Match(ActiveQuery, r.Connection, r.Process)).OrderBy(r => r.Connection.LocalPort));
        var matching = Snapshot.Connections.Where(c => SearchService.Match(ActiveQuery, c, byPid.GetValueOrDefault(c.Pid))).Select(c => c.Pid).ToHashSet();
        Replace(Processes, Snapshot.Processes.Where(p => SearchService.Match(ActiveQuery, null, p) || matching.Contains(p.Pid)).OrderBy(p => p.Name));
    }
    public void UpdateUserData()
    {
        foreach (var favorite in Data.Favorites)
        {
            var owners = favorite.Type switch
            {
                "port" => Snapshot.Connections.Where(c => c.LocalPort.ToString() == favorite.Value).Select(c => c.Pid).Distinct().ToArray(),
                "pid" => Snapshot.Processes.Where(p => p.Pid.ToString() == favorite.Value).Select(p => p.Pid).ToArray(),
                _ => Snapshot.Processes.Where(p => p.Name.Equals(favorite.Value, StringComparison.OrdinalIgnoreCase)).Select(p => p.Pid).ToArray()
            };
            favorite.Status = owners.Length > 0 ? "Occupied" : "Unknown";
            favorite.Owner = owners.Length > 0 ? string.Join(", ", owners) : "当前快照未发现";
        }
        // Keep the same selection while refreshing status; do not erase a note being edited.
        if (!Favorites.SequenceEqual(Data.Favorites)) Replace(Favorites, Data.Favorites);
        if (!History.SequenceEqual(Data.History)) Replace(History, Data.History);
    }
    public void SaveSettings()
    {
        Data.Settings.AutoRefresh = AutoRefresh; Data.Settings.RefreshInterval = Interval * 1000; Data.Settings.Theme = Theme; Data.SaveSettings();
    }
    public void LoadPreview()
    {
        // Explicit UI preview mode uses synthetic data and an isolated temporary store.
        var process = new ProcessSnapshot(8123, DateTime.UtcNow.AddHours(-1).Ticks, "node", "/usr/local/bin/node", 8000, "developer", 1.2, 64 * 1048576, 0, 0, "Available");
        Snapshot = new([new("TCP", "127.0.0.1", 8080, "", 0, "LISTENING", process.Pid), new("TCP6", "::1", 5432, "", 0, "LISTENING", process.Pid), new("TCP", "127.0.0.1", 51000, "192.0.2.1", 443, "ESTABLISHED", process.Pid)], [process], DateTimeOffset.Now, []);
        Data.Favorites.Add(new() { Type = "port", Value = "8080", Name = "开发服务", Note = "Local API" });
        Data.History.Add(new(DateTimeOffset.Now, "查看", "node · PID 8123"));
        Summary = "2 监听    ·    3 TCP    ·    0 UDP    ·    1 进程"; Status = "界面预览 · 合成数据";
        Filter(); UpdateUserData();
    }
    private static void Replace<T>(ObservableCollection<T> list, IEnumerable<T> values)
    { var next = values.ToArray(); list.Clear(); foreach (var value in next) list.Add(value); }
}
