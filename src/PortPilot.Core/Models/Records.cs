using CommunityToolkit.Mvvm.ComponentModel;

namespace PortPilot.Core.Models;

public sealed record ProcessIdentity(int Pid, long StartTicks);
public sealed record ProcessSnapshot(int Pid, long StartTicks, string Name, string Path, int ParentPid,
    string User, double Cpu, long Memory, int Threads, int Handles, string Status)
{
    public ProcessIdentity Identity => new(Pid, StartTicks);
    public bool IsSystem => Pid <= 4 || User.EndsWith("\\SYSTEM", StringComparison.OrdinalIgnoreCase)
        || User.Contains("SERVICE", StringComparison.OrdinalIgnoreCase) || ProcessProtection.IsProtected(Pid, Name);
    public string Started => StartTicks > 0 ? new DateTime(StartTicks, DateTimeKind.Utc).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : Status;
}

public sealed record ConnectionSnapshot(string Protocol, string LocalAddress, int LocalPort,
    string RemoteAddress, int RemotePort, string State, int Pid)
{
    public string Key => $"{Protocol}|{LocalAddress}|{LocalPort}|{RemoteAddress}|{RemotePort}|{Pid}";
    public bool Listening => State == "LISTENING";
    public string Local => FormatEndpoint(LocalAddress, LocalPort);
    public string Remote => RemotePort == 0 ? "—" : FormatEndpoint(RemoteAddress, RemotePort);
    public static string FormatEndpoint(string ip, int port) => ip.Contains(':') ? $"[{ip}]:{port}" : $"{ip}:{port}";
}

public sealed record ScanSnapshot(IReadOnlyList<ConnectionSnapshot> Connections, IReadOnlyList<ProcessSnapshot> Processes, DateTimeOffset Time, IReadOnlyList<string> Warnings);
public sealed record ProcessDetails(ProcessSnapshot Process, string CommandLine, string WorkingDirectory,
    string Architecture, string Elevated, string Signature, string Company, string Description,
    string Version, string Product, IReadOnlyList<string> Modules, IReadOnlyList<string> EnvironmentNames);

public sealed class AppSettings
{
    public string Theme { get; set; } = "system";
    public bool AutoRefresh { get; set; } = true;
    public int RefreshInterval { get; set; } = 2000;
    public bool ConfirmBeforeKill { get; set; } = true;
    public bool ShowSystemProcesses { get; set; } = true;
    public int HistoryLimit { get; set; } = 1000;
    public void Normalize()
    {
        if (Theme is not ("system" or "light" or "dark")) Theme = "system";
        if (!new[] {1000, 2000, 3000, 5000, 10000, 30000}.Contains(RefreshInterval)) RefreshInterval = 2000;
        HistoryLimit = Math.Clamp(HistoryLimit, 1, 1000);
        ConfirmBeforeKill = true; // Destructive actions always require confirmation.
    }
}

public sealed partial class Favorite : ObservableObject
{
    public string Type { get; set; } = "port";
    public string Value { get; set; } = "";
    public string Name { get; set; } = "";
    public string Note { get; set; } = "";
    [property: System.Text.Json.Serialization.JsonIgnore]
    [ObservableProperty] private string status = "Unknown";
    [property: System.Text.Json.Serialization.JsonIgnore]
    [ObservableProperty] private string owner = "—";
}
public sealed record HistoryEntry(DateTimeOffset Time, string Action, string Detail);
public sealed record KillTarget(ProcessIdentity Identity, string Name, string Path, int[] Ports);
public sealed record KillPlan(IReadOnlyList<KillTarget> Targets, bool Tree, bool Force);
public sealed record KillResult(int Pid, bool Success, bool AccessDenied, string Message);

public static class ProcessProtection
{
    private static readonly HashSet<string> Names = new(StringComparer.OrdinalIgnoreCase)
    { "System", "Registry", "Idle", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "lsaiso", "svchost", "secure system", "memory compression" };
    public static bool IsProtected(int pid, string name) => pid <= 4 || pid == Environment.ProcessId || Names.Contains(System.IO.Path.GetFileNameWithoutExtension(name));
}

public static class PortCatalog
{
    public static readonly IReadOnlyDictionary<int, string> Names = new Dictionary<int, string>
    { [21]="FTP", [22]="SSH", [25]="SMTP", [53]="DNS", [80]="HTTP", [443]="HTTPS", [3306]="MySQL", [5432]="PostgreSQL", [6379]="Redis", [8080]="HTTP / Java", [8081]="HTTP / Dev", [8848]="Nacos", [9092]="Kafka", [9200]="Elasticsearch", [27017]="MongoDB" };
    public static string Hint(int port) => Names.TryGetValue(port, out var name) ? name + " · 惯例" : "";
}
