using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PortPilot.Core.Infrastructure;
using PortPilot.Core.Models;
using PortPilot.Core.Services;
using PortPilot.Services;

namespace PortPilot.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly NetworkService network;
    private readonly ProcessService processes;
    private readonly ProcessKillService killer;
    private readonly UserDataService data;
    private readonly PortableStore store;
    private readonly DialogService dialogs;
    private readonly ThemeService themes;
    private readonly ILogger<MainViewModel> logger;
    private readonly SemaphoreSlim refreshGate = new(1);
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? detailToken;
    private readonly DispatcherTimer timer;
    private readonly DispatcherTimer searchTimer;
    private readonly Dictionary<string, InspectorRow> portIndex = [], processIndex = [];
    private ScanSnapshot snapshot = new([], [], DateTimeOffset.Now, []);
    private bool firstScan = true;
    private SearchQuery activeSearch = SearchQuery.Parse("");
    public ObservableCollection<InspectorRow> Ports { get; } = [];
    public ObservableCollection<InspectorRow> Processes { get; } = [];
    public ICollectionView PortsView { get; }
    public ICollectionView ProcessesView { get; }
    public ICollectionView ConnectionsView { get; }
    public ObservableCollection<Favorite> Favorites { get; } = [];
    public ObservableCollection<HistoryEntry> History { get; } = [];
    public ObservableCollection<HistoryEntry> RecentHistory { get; } = [];
    public ObservableCollection<DetailField> DetailFields { get; } = [];
    public ObservableCollection<InspectorRow> DetailConnections { get; } = [];
    public ObservableCollection<ProcessTreeNode> ProcessTree { get; } = [];
    public ObservableCollection<HistoryEntry> RecentKills { get; } = [];
    public ObservableCollection<HistoryEntry> RecentPids { get; } = [];
    public ObservableCollection<string> RecentPorts { get; } = [];
    public string[] CommonPorts { get; } = ["8080", "3306", "6379", "5432", "8848", "9200", "27017", "443"];
    public string[] Filters { get; } = ["All", "TCP", "UDP", "Listening", "Established", "Localhost", "System", "User Process"];
    public string[] ThemeOptions { get; } = ["system", "light", "dark"];
    public SearchOption[] SearchOptions { get; } =
    [
        new(SearchField.All, "全部字段"), new(SearchField.LocalPort, "本地端口"), new(SearchField.RemotePort, "远程端口"),
        new(SearchField.Pid, "PID"), new(SearchField.ProcessName, "进程名称"), new(SearchField.Path, "程序路径"), new(SearchField.IpAddress, "IP 地址")
    ];
    public int[] IntervalOptions { get; } = [1000, 2000, 3000, 5000, 10000, 30000];
    public AppSettings Settings => data.Settings;
    public string DataDirectory => store.DataDirectory;
    public string Privilege => Core.Native.ProcessNativeApi.IsAdministrator() ? "Administrator" : "Standard";
    public string About => $"PortPilot 1.0.1\nA lightweight Windows port & process inspector.\n\n{Environment.OSVersion}\n.NET {Environment.Version} · {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}\n{Privilege}\n\nData: {DataDirectory}";
    [ObservableProperty] private string page = "Dashboard";
    [ObservableProperty] private string searchText = "";
    [ObservableProperty] private SearchField selectedSearchField = SearchField.All;
    [ObservableProperty] private bool isExactSearch;
    [ObservableProperty] private string filter = "All";
    [ObservableProperty] private string status = "正在读取本机网络与进程…";
    [ObservableProperty] private string lastUpdated = "尚未刷新";
    [ObservableProperty] private string scanHealth = "正在连接 Windows API";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool hasDetails;
    [ObservableProperty] private bool treeMode;
    [ObservableProperty] private bool onlyNetworkProcesses;
    [ObservableProperty] private InspectorRow? selectedRow;
    [ObservableProperty] private string detailTitle = "Process inspector";
    [ObservableProperty] private string detailSubtitle = "";
    [ObservableProperty] private string detailCommandLine = "";
    [ObservableProperty] private string detailModules = "";
    [ObservableProperty] private string detailEnvironment = "";
    [ObservableProperty] private string detailFamily = "";
    [ObservableProperty] private string portNote = "";
    [ObservableProperty] private bool isDetailBusy;
    [ObservableProperty] private string favoriteName = "";
    [ObservableProperty] private string favoriteNote = "";
    [ObservableProperty] private Favorite? selectedFavorite;
    [ObservableProperty] private int listeningCount;
    [ObservableProperty] private int tcpCount;
    [ObservableProperty] private int udpCount;
    [ObservableProperty] private int processCount;
    [ObservableProperty] private int riskCount;
    [ObservableProperty] private int visibleCount;
    [ObservableProperty] private bool isPaletteOpen;
    [ObservableProperty] private string paletteQuery = "";
    public string[] PaletteCommands { get; } = ["Search Port", "Search PID", "Refresh", "Kill Process", "Open Settings", "Toggle Theme", "Clear History", "Export"];
    public IEnumerable<string> FilteredPaletteCommands => PaletteCommands.Where(c => c.Contains(PaletteQuery, StringComparison.OrdinalIgnoreCase));
    public string PageDescription => Page switch
    {
        "Dashboard" => "本机网络与进程，一目了然。", "Ports" => "从端口找到进程，快速定位占用来源。",
        "Processes" => "从 PID 出发，了解每个进程及其网络活动。", "Connections" => "实时连接快照 · 时长自首次观测开始计算。",
        "Favorites" => "关注常用端口与进程，持续观察状态变化。", "History" => "查询、收藏与进程操作的本地记录。", "Search" => "端口、PID、进程与收藏的分组搜索结果。", _ => "按照你的习惯，配置 PortPilot。"
    };
    public bool IsEmpty => VisibleCount == 0 && !IsBusy;
    public string EmptyTitle => snapshot.Warnings.Count > 0 ? "部分数据读取失败" : CurrentSearch.LocalPort is { } port ? $"Port {port} · 当前快照无匹配记录" : "没有匹配的结果";
    private SearchQuery CurrentSearch => SearchQuery.Parse(SearchText, SelectedSearchField, IsExactSearch);
    public bool CanFavoriteQuery => CurrentSearch.LocalPort != null;
    public string SearchPlaceholder => SelectedSearchField switch
    {
        SearchField.LocalPort => "本地端口，如 8080", SearchField.RemotePort => "远程端口，如 443", SearchField.Pid => "进程 PID，如 1234",
        SearchField.ProcessName => IsExactSearch ? "完整进程名，如 java.exe" : "进程名称，如 java",
        SearchField.Path => IsExactSearch ? "完整可执行文件路径" : "程序路径或路径片段",
        SearchField.IpAddress => "IP 地址，如 127.0.0.1", _ => "搜索端口、PID、进程名称…"
    };
    public string SearchHint => $"{SearchPlaceholder}\nCtrl + F 聚焦；Enter 查看分组结果。\n“精确”完整匹配字段，忽略大小写；端口与 PID 始终按数字精确匹配。\n全部字段模式也支持 port:8080、pid:1234、process:java.exe、path:完整路径。";
    public string EmptyDescription => snapshot.Warnings.Count > 0 ? "请查看状态信息并重试，无法判断端口是否空闲。" : "尝试调整搜索或筛选。未观测到端点不代表端口一定可绑定。";
    public bool CanNote => SelectedRow?.Connection != null;
    public MainViewModel(NetworkService network, ProcessService processes, ProcessKillService killer, UserDataService data, PortableStore store, DialogService dialogs, ThemeService themes, ILogger<MainViewModel> logger)
    {
        this.network = network; this.processes = processes; this.killer = killer; this.data = data; this.store = store; this.dialogs = dialogs; this.themes = themes; this.logger = logger;
        PortsView = new ListCollectionView(Ports) { Filter = o => Matches((InspectorRow)o, false) };
        ConnectionsView = new ListCollectionView(Ports) { Filter = o => Matches((InspectorRow)o, true) };
        ProcessesView = new ListCollectionView(Processes) { Filter = o => Matches((InspectorRow)o, false) && (!OnlyNetworkProcesses || ((InspectorRow)o).ConnectionCount > 0) };
        foreach (var view in new[] { PortsView, ConnectionsView, ProcessesView }.Cast<ListCollectionView>())
        {
            view.IsLiveFiltering = true; view.IsLiveSorting = true;
            foreach (var property in new[] { "State", "Name", "Path", "Pid", "Port", "Protocol", "LocalAddress", "Process", "ConnectionCount" }) view.LiveFilteringProperties.Add(property);
        }
        foreach (var f in data.Favorites) Favorites.Add(f);
        UpdateHistory();
        timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Settings.RefreshInterval) };
        timer.Tick += TimerTick;
        searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        searchTimer.Tick += SearchTick;
    }
    public async Task InitializeAsync() { themes.Apply(Settings.Theme); await RefreshAsync(); if (Settings.AutoRefresh) timer.Start(); }
    private async void TimerTick(object? sender, EventArgs e) => await RefreshAsync();
    private void SearchTick(object? sender, EventArgs e) { searchTimer.Stop(); RefreshViews(); }
    partial void OnSearchTextChanged(string value)
    {
        searchTimer.Stop(); searchTimer.Start();
        if (Page == "Dashboard" && !string.IsNullOrWhiteSpace(value)) Page = "Search";
        OnPropertyChanged(nameof(EmptyTitle)); OnPropertyChanged(nameof(EmptyDescription));
        OnPropertyChanged(nameof(CanFavoriteQuery));
    }
    partial void OnSelectedSearchFieldChanged(SearchField value) => SearchOptionsChanged();
    partial void OnIsExactSearchChanged(bool value) => SearchOptionsChanged();
    private void SearchOptionsChanged()
    {
        OnPropertyChanged(nameof(SearchPlaceholder)); OnPropertyChanged(nameof(SearchHint));
        OnPropertyChanged(nameof(EmptyTitle)); OnPropertyChanged(nameof(CanFavoriteQuery));
        RefreshViews();
    }
    partial void OnFilterChanged(string value) => RefreshViews();
    partial void OnOnlyNetworkProcessesChanged(bool value) => RefreshViews();
    partial void OnPaletteQueryChanged(string value) => OnPropertyChanged(nameof(FilteredPaletteCommands));
    partial void OnPageChanged(string value) { OnPropertyChanged(nameof(PageDescription)); ExportCommand.NotifyCanExecuteChanged(); UpdateVisibleCount(); if (value == "Processes" && TreeMode) UpdateTree(); }
    partial void OnTreeModeChanged(bool value) { if (value) UpdateTree(); }
    partial void OnSelectedFavoriteChanged(Favorite? value) { FavoriteName = value?.Name ?? ""; FavoriteNote = value?.Note ?? ""; }
    private bool Matches(InspectorRow r, bool connections)
    {
        if (!Settings.ShowSystemProcesses && r.Process.IsSystem) return false;
        if (!SearchService.Match(activeSearch, r.Connection, r.Process)) return false;
        if (connections && r.Connection == null) return false;
        return Filter switch
        {
            "TCP" => r.Protocol.StartsWith("TCP"), "UDP" => r.Protocol.StartsWith("UDP"), "Listening" => r.Connection?.Listening == true,
            "Established" => r.State == "ESTABLISHED", "Localhost" => r.LocalAddress is "127.0.0.1" or "::1", "System" => r.Process.IsSystem, "User Process" => !r.Process.IsSystem, _ => true
        };
    }
    private void RefreshViews()
    {
        activeSearch = CurrentSearch;
        using (PortsView.DeferRefresh()) { }
        using (ConnectionsView.DeferRefresh()) { }
        using (ProcessesView.DeferRefresh()) { }
        PortsView.Refresh(); ConnectionsView.Refresh(); ProcessesView.Refresh(); UpdateVisibleCount();
        OnPropertyChanged(nameof(SearchFavorites));
        if (TreeMode && Page == "Processes") UpdateTree();
    }
    public IEnumerable<Favorite> SearchFavorites => Favorites.Where(f => SearchService.MatchFavorite(activeSearch, f));
    private void UpdateVisibleCount()
    { VisibleCount = Page == "Processes" ? ProcessesView.Cast<object>().Count() : Page == "Connections" ? ConnectionsView.Cast<object>().Count() : PortsView.Cast<object>().Count(); OnPropertyChanged(nameof(IsEmpty)); }
    [RelayCommand] private void Navigate(string page) { Page = page; HasDetails = false; Filter = "All"; }
    [RelayCommand] private void SearchPort(string port) { SelectedSearchField = SearchField.LocalPort; Filter = "All"; SearchText = port; Page = "Ports"; Record("SearchPort", port); RefreshViews(); }
    [RelayCommand] private void SubmitSearch() { Page = "Search"; Record("Search", $"{SearchOptions.First(o => o.Field == SelectedSearchField).Label} · {(IsExactSearch ? "精确" : "包含")} · {SearchText}"); RefreshViews(); }
    [RelayCommand] private void SetFilter(string value) => Filter = value;
    [RelayCommand] private void CloseDetails() { HasDetails = false; detailToken?.Cancel(); }
    [RelayCommand] private void OpenPalette() { IsPaletteOpen = true; PaletteQuery = ""; }
    [RelayCommand] private void ClosePalette() => IsPaletteOpen = false;

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (!await refreshGate.WaitAsync(0)) return;
        IsBusy = true; OnPropertyChanged(nameof(IsEmpty));
        try
        {
            var next = await Task.Run(() => network.Scan(lifetime.Token), lifetime.Token);
            snapshot = next;
            var byPid = next.Processes.ToDictionary(p => p.Pid);
            var groups = next.Connections.GroupBy(c => c.Pid).ToDictionary(g => g.Key, g => g.ToArray());
            var seenPorts = new HashSet<string>(); var seenProcesses = new HashSet<string>();
            foreach (var c in next.Connections.DistinctBy(c => c.Key))
            {
                var p = byPid.GetValueOrDefault(c.Pid) ?? new ProcessSnapshot(c.Pid, 0, "Process exited", "Unavailable", 0, "—", 0, 0, 0, 0, "Process exited");
                var key = c.Key + "|" + p.StartTicks; seenPorts.Add(key);
                if (!portIndex.TryGetValue(key, out var row)) { row = new(key, p, c, next.Time); portIndex[key] = row; Ports.Add(row); }
                row.Update(p, c, data.Notes.GetValueOrDefault(c.LocalPort) ?? PortCatalog.Hint(c.LocalPort), groups[c.Pid].Length, Listening(groups[c.Pid]));
            }
            RemoveMissing(Ports, portIndex, seenPorts);
            foreach (var p in next.Processes)
            {
                var key = $"{p.Pid}:{p.StartTicks}"; seenProcesses.Add(key);
                if (!processIndex.TryGetValue(key, out var row)) { row = new(key, p, null, next.Time); processIndex[key] = row; Processes.Add(row); }
                var connections = groups.GetValueOrDefault(p.Pid) ?? [];
                row.Update(p, null, "", connections.Length, Listening(connections));
            }
            RemoveMissing(Processes, processIndex, seenProcesses);
            ListeningCount = next.Connections.Where(c => c.Listening).DistinctBy(c => (c.Protocol, c.LocalAddress, c.LocalPort)).Count();
            TcpCount = next.Connections.Count(c => c.Protocol.StartsWith("TCP") && !c.Listening);
            UdpCount = next.Connections.Count(c => c.Protocol.StartsWith("UDP")); ProcessCount = next.Processes.Count;
            RiskCount = next.Connections.Count(c => c.Listening && c.LocalAddress is "0.0.0.0" or "::" && new[] {21, 23, 135, 139, 445, 3389, 6379, 27017}.Contains(c.LocalPort));
            UpdateVisibleCount(); UpdateFavorites(); OnPropertyChanged(nameof(SearchFavorites));
            if (TreeMode && Page == "Processes") UpdateTree();
            if (HasDetails && SelectedRow != null)
            {
                if (!byPid.TryGetValue(SelectedRow.Pid, out var current) || current.Identity != SelectedRow.Process.Identity) DetailSubtitle = "Process exited / PID 已变化";
                else UpdateDetailConnections(current.Pid);
            }
            LastUpdated = $"更新于 {next.Time:HH:mm:ss}";
            ScanHealth = next.Warnings.Count == 0 ? "本机实时快照" : "部分数据不可用";
            if (firstScan) { Status = store.Notices.Count > 0 ? string.Join(" ", store.Notices) : "就绪 · 数据仅保存在本机"; firstScan = false; }
            if (next.Warnings.Count > 0) Status = string.Join(" / ", next.Warnings);
        }
        catch (OperationCanceledException) { Status = "刷新已取消"; }
        catch (Exception ex) { logger.LogError(ex, "Refresh failed"); Status = "读取失败：" + ex.Message; ScanHealth = "扫描失败 · 显示上次数据"; }
        finally { IsBusy = false; OnPropertyChanged(nameof(IsEmpty)); refreshGate.Release(); }
    }
    private static string Listening(IEnumerable<ConnectionSnapshot> rows) => string.Join(", ", rows.Where(c => c.Listening).Select(c => c.LocalPort).Distinct().Order());
    private static void RemoveMissing(ObservableCollection<InspectorRow> rows, Dictionary<string, InspectorRow> index, HashSet<string> seen)
    { foreach (var key in index.Keys.Where(k => !seen.Contains(k)).ToArray()) { rows.Remove(index[key]); index.Remove(key); } }
    private void UpdateTree()
    {
        var existing = Flatten(ProcessTree).ToDictionary(n => n.Key);
        var desired = ProcessesView.Cast<InspectorRow>().ToDictionary(r => r.Pid);
        var nodes = desired.Values.ToDictionary(r => r.Pid, r => existing.GetValueOrDefault(r.Key) ?? new ProcessTreeNode(r));
        var children = nodes.Keys.ToDictionary(k => k, _ => new List<ProcessTreeNode>()); var roots = new List<ProcessTreeNode>();
        foreach (var (pid, node) in nodes)
        {
            var parent = node.Row.ParentPid;
            if (parent != pid && nodes.TryGetValue(parent, out var pn) && pn.Row.Process.StartTicks > 0 && pn.Row.Process.StartTicks < node.Row.Process.StartTicks) children[parent].Add(node);
            else roots.Add(node);
        }
        foreach (var (pid, node) in nodes) SyncNodes(node.Children, children[pid]);
        SyncNodes(ProcessTree, roots);
    }
    private static IEnumerable<ProcessTreeNode> Flatten(IEnumerable<ProcessTreeNode> roots) => roots.SelectMany(n => new[] {n}.Concat(Flatten(n.Children)));
    private static void SyncNodes(ObservableCollection<ProcessTreeNode> target, List<ProcessTreeNode> source)
    {
        var keys = source.Select(n => n.Key).ToHashSet(); foreach (var old in target.Where(n => !keys.Contains(n.Key)).ToArray()) target.Remove(old);
        var present = target.Select(n => n.Key).ToHashSet(); foreach (var n in source.OrderBy(n => n.Row.Name)) if (!present.Contains(n.Key)) target.Add(n);
    }
    private void Record(string action, string detail) { data.Record(action, detail); UpdateHistory(); }
    private void UpdateHistory()
    {
        History.Clear(); foreach (var h in data.History) History.Add(h);
        RecentHistory.Clear(); foreach (var h in data.History.Take(5)) RecentHistory.Add(h);
        RecentKills.Clear(); foreach (var h in data.History.Where(h => h.Action.Contains("Kill")).Take(3)) RecentKills.Add(h);
        RecentPids.Clear(); foreach (var h in data.History.Where(h => h.Action == "ViewPID").DistinctBy(h => h.Detail).Take(3)) RecentPids.Add(h);
        RecentPorts.Clear(); foreach (var h in data.History.Where(h => h.Action == "SearchPort").Select(h => h.Detail).Distinct().Take(8)) RecentPorts.Add(h);
    }
    public void Dispose() { timer.Stop(); searchTimer.Stop(); lifetime.Cancel(); detailToken?.Cancel(); }
}
