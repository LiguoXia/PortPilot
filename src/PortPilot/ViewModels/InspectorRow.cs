using CommunityToolkit.Mvvm.ComponentModel;
using System.ComponentModel;
using PortPilot.Core.Models;

namespace PortPilot.ViewModels;

public sealed class InspectorRow : ObservableObject
{
    public string Key { get; }
    public ConnectionSnapshot? Connection { get; private set; }
    public ProcessSnapshot Process { get; private set; }
    public DateTimeOffset FirstSeen { get; }
    public string PortLabel { get; private set; } = "";
    public int ConnectionCount { get; private set; }
    public string ListeningPorts { get; private set; } = "—";
    public int Pid => Process.Pid;
    public string Name => Process.Name;
    public string Path => Process.Path;
    public string User => Process.User;
    public int ParentPid => Process.ParentPid;
    public int Threads => Process.Threads;
    public int Handles => Process.Handles;
    public double Cpu => Process.Cpu;
    public double Memory => Math.Round(Process.Memory / 1048576d, 1);
    public string Started => Process.Started;
    public string Protocol => Connection?.Protocol ?? "—";
    public int Port => Connection?.LocalPort ?? 0;
    public string LocalAddress => Connection?.LocalAddress ?? "—";
    public string RemoteAddress => Connection?.RemoteAddress ?? "—";
    public int RemotePort => Connection?.RemotePort ?? 0;
    public string Local => Connection?.Local ?? "—";
    public string Remote => Connection?.Remote ?? "—";
    public string State => Connection?.State ?? Process.Status;
    public string Duration => (DateTimeOffset.Now - FirstSeen).ToString(@"hh\:mm\:ss");
    public bool IsProtected => ProcessProtection.IsProtected(Pid, Name);
    public bool IsSystem => Process.IsSystem;
    public string TreeLabel => $"{Name}   {Pid}   ·   {Cpu:0.0}%   ·   {Memory:0.0} MB   ·   {ConnectionCount} connections";
    public InspectorRow(string key, ProcessSnapshot process, ConnectionSnapshot? connection, DateTimeOffset time)
    { Key = key; Process = process; Connection = connection; FirstSeen = time; }
    public void Update(ProcessSnapshot process, ConnectionSnapshot? connection, string label, int count, string listening)
    {
        var oldProcess = Process; var oldConnection = Connection;
        var oldLabel = PortLabel; var oldCount = ConnectionCount; var oldListening = ListeningPorts;
        Process = process; Connection = connection; PortLabel = label; ConnectionCount = count; ListeningPorts = listening;
        // Resource sampling must not invalidate identity/endpoint bindings and live filters.
        if (oldProcess != process)
        {
            Changed(nameof(Process));
            if (oldProcess.Cpu != process.Cpu) Changed(nameof(Cpu));
            if (Math.Round(oldProcess.Memory / 1048576d, 1) != Memory) Changed(nameof(Memory));
            if (oldProcess.Threads != process.Threads) Changed(nameof(Threads));
            if (oldProcess.Handles != process.Handles) Changed(nameof(Handles));
            if (oldProcess.Name != process.Name) Changed(nameof(Name));
            if (oldProcess.Path != process.Path) Changed(nameof(Path));
            if (oldProcess.User != process.User) Changed(nameof(User));
            if (oldProcess.Pid != process.Pid) Changed(nameof(Pid));
            if (oldProcess.ParentPid != process.ParentPid) Changed(nameof(ParentPid));
            if (oldProcess.StartTicks != process.StartTicks || process.StartTicks <= 0 && oldProcess.Status != process.Status) Changed(nameof(Started));
            if (oldProcess.Pid != process.Pid || oldProcess.Name != process.Name) Changed(nameof(IsProtected));
            if (oldProcess.IsSystem != process.IsSystem) Changed(nameof(IsSystem));
        }
        if (oldConnection != connection)
        {
            Changed(nameof(Connection));
            if (oldConnection?.Protocol != connection?.Protocol) Changed(nameof(Protocol));
            if (oldConnection?.LocalAddress != connection?.LocalAddress) Changed(nameof(LocalAddress));
            if (oldConnection?.LocalPort != connection?.LocalPort) Changed(nameof(Port));
            if (oldConnection?.RemoteAddress != connection?.RemoteAddress) Changed(nameof(RemoteAddress));
            if (oldConnection?.RemotePort != connection?.RemotePort) Changed(nameof(RemotePort));
            if (oldConnection?.LocalAddress != connection?.LocalAddress || oldConnection?.LocalPort != connection?.LocalPort) Changed(nameof(Local));
            if (oldConnection?.RemoteAddress != connection?.RemoteAddress || oldConnection?.RemotePort != connection?.RemotePort) Changed(nameof(Remote));
        }
        if ((oldConnection?.State ?? oldProcess.Status) != State) Changed(nameof(State));
        if (oldLabel != label) Changed(nameof(PortLabel));
        if (oldCount != count) Changed(nameof(ConnectionCount));
        if (oldListening != listening) Changed(nameof(ListeningPorts));
        if (oldProcess.Name != Name || oldProcess.Pid != Pid || oldProcess.Cpu != Cpu || oldProcess.Memory != process.Memory || oldCount != count) Changed(nameof(TreeLabel));
        Changed(nameof(Duration));
    }
    private static readonly IReadOnlyDictionary<string, PropertyChangedEventArgs> Events = typeof(InspectorRow).GetProperties().ToDictionary(p => p.Name, p => new PropertyChangedEventArgs(p.Name));
    private void Changed(string property) => OnPropertyChanged(Events[property]);
    public Dictionary<string, string> Export() => new()
    {
        ["Protocol"] = Protocol, ["Local"] = Local, ["Remote"] = Remote, ["State"] = State,
        ["PID"] = Pid.ToString(), ["Process"] = Name, ["Path"] = Path, ["User"] = User,
        ["CPU %"] = Cpu.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture), ["Memory MB"] = Memory.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture),
        ["Started"] = Started, ["Parent PID"] = ParentPid.ToString(), ["Port note / hint"] = PortLabel
    };
    public string CopyText() => string.Join(Environment.NewLine, Export().Select(p => $"{p.Key}: {p.Value}"));
}

public sealed class ProcessTreeNode(InspectorRow row)
{
    public InspectorRow Row { get; } = row;
    public string Key => Row.Key;
    public System.Collections.ObjectModel.ObservableCollection<ProcessTreeNode> Children { get; } = [];
}
public sealed record DetailField(string Label, string Value);
