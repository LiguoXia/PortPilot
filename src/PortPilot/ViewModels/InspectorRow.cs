using CommunityToolkit.Mvvm.ComponentModel;
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
    public string TreeLabel => $"{Name}   {Pid}   ·   {Cpu:0.0}%   ·   {Memory:0.0} MB   ·   {ConnectionCount} connections";
    public InspectorRow(string key, ProcessSnapshot process, ConnectionSnapshot? connection, DateTimeOffset time)
    { Key = key; Process = process; Connection = connection; FirstSeen = time; }
    public void Update(ProcessSnapshot process, ConnectionSnapshot? connection, string label, int count, string listening)
    {
        var changed = Process != process || Connection != connection || PortLabel != label || ConnectionCount != count || ListeningPorts != listening;
        Process = process; Connection = connection; PortLabel = label; ConnectionCount = count; ListeningPorts = listening;
        if (changed) OnPropertyChanged(string.Empty); else OnPropertyChanged(nameof(Duration));
    }
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
