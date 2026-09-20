using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PortPilot.Core.Models;
using PortPilot.Core.Native;

namespace PortPilot.Core.Services;

public sealed class ProcessService(ILogger<ProcessService> logger)
{
    private readonly Dictionary<ProcessIdentity, (string Path, string User)> metadata = [];
    private readonly Dictionary<ProcessIdentity, (TimeSpan Cpu, long Time)> cpuSamples = [];
    private readonly Dictionary<ProcessIdentity, ProcessDetails> details = [];
    private readonly object detailGate = new();
    public IReadOnlyList<ProcessSnapshot> Scan(CancellationToken cancellationToken)
    {
        var parents = ProcessNativeApi.Parents();
        var result = new List<ProcessSnapshot>();
        var live = new HashSet<ProcessIdentity>();
        foreach (var (pid, entry) in parents)
        {
                cancellationToken.ThrowIfCancellationRequested();
                var name = entry.Name; long ticks = 0, memory = 0; int threads = entry.Threads, handles = 0;
                var path = "Access Denied"; var user = "Access Denied"; var state = "Available"; double cpu = 0;
                try
                {
                    using var handle = ProcessNativeApi.OpenProcess(ProcessNativeApi.Query, false, pid);
                    if (handle.IsInvalid) throw new Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error());
                    ticks = ProcessNativeApi.StartTicks(handle);
                    var identity = new ProcessIdentity(pid, ticks); live.Add(identity);
                    if (!metadata.TryGetValue(identity, out var meta))
                    {
                        path = ProcessNativeApi.ImagePath(handle);
                        try { user = ProcessNativeApi.Token(handle).User; }
                        catch (Win32Exception ex) { logger.LogDebug(ex, "Token unavailable for PID {Pid}", pid); }
                        meta = (path, user); metadata[identity] = meta;
                    }
                    path = meta.Path; user = meta.User;
                    var resources = ProcessNativeApi.Resources(handle);
                    var total = resources.Cpu; var now = Stopwatch.GetTimestamp();
                    if (cpuSamples.TryGetValue(identity, out var previous))
                    {
                        var elapsed = Stopwatch.GetElapsedTime(previous.Time, now).TotalSeconds;
                        if (elapsed > 0) cpu = Math.Clamp((total - previous.Cpu).TotalSeconds / elapsed / Environment.ProcessorCount * 100, 0, 100);
                    }
                    cpuSamples[identity] = (total, now);
                    memory = resources.Memory; handles = resources.Handles;
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or ArgumentException)
                {
                    state = ex is Win32Exception w && w.NativeErrorCode == 5 ? "Access Denied" : "Process exited / unavailable";
                    logger.LogDebug(ex, "Process snapshot unavailable for PID {Pid}", pid);
                }
                result.Add(new(pid, ticks, name, path, entry.Parent, user, Math.Round(cpu, 1), memory, threads, handles, state));
        }
        foreach (var key in metadata.Keys.Where(k => !live.Contains(k)).ToArray()) metadata.Remove(key);
        foreach (var key in cpuSamples.Keys.Where(k => !live.Contains(k)).ToArray()) cpuSamples.Remove(key);
        lock (detailGate) foreach (var key in details.Keys.Where(k => !live.Contains(k)).ToArray()) details.Remove(key);
        return result;
    }
    public ProcessDetails Details(ProcessSnapshot process, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var handle = ProcessNativeApi.OpenProcess(ProcessNativeApi.Query, false, process.Pid);
        if (handle.IsInvalid) throw new Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error());
        ProcessNativeApi.EnsureRunning(handle);
        if (process.StartTicks <= 0 || ProcessNativeApi.StartTicks(handle) != process.StartTicks) throw new InvalidOperationException("Process no longer exists / PID 已复用，请刷新。");
        lock (detailGate) if (details.TryGetValue(process.Identity, out var cached)) return cached with { Process = process };
        string Try(string field, Func<string> read)
        {
            try { token.ThrowIfCancellationRequested(); return read(); }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException or ArgumentException)
            { logger.LogDebug(ex, "{Field} unavailable for PID {Pid}", field, process.Pid); return "Unavailable / Access Denied"; }
        }
        var command = Try("Command", () => ProcessNativeApi.CommandLine(handle));
        var architecture = Try("Architecture", () => ProcessNativeApi.Architecture(handle));
        var elevated = Try("Elevation", () => ProcessNativeApi.Token(handle).Elevated);
        var directory = "Unavailable / Access Denied"; string[] environment = [];
        _ = Try("Parameters", () => { var p = ProcessNativeApi.Parameters(process.Pid); directory = p.Directory; environment = p.EnvironmentNames; return "ok"; });
        string company = "—", description = "—", version = "—", product = "—";
        _ = Try("File info", () => { var info = FileVersionInfo.GetVersionInfo(process.Path); company = info.CompanyName ?? "—"; description = info.FileDescription ?? "—"; version = info.FileVersion ?? "—"; product = info.ProductName ?? "—"; return "ok"; });
        var signature = Try("Signature", () => File.Exists(process.Path) ? SignatureApi.Verify(process.Path) : "Unavailable");
        var modules = new List<string>();
        _ = Try("Modules", () => { using var p = Process.GetProcessById(process.Pid); foreach (ProcessModule m in p.Modules) { token.ThrowIfCancellationRequested(); modules.Add(m.FileName); } return "ok"; });
        ProcessNativeApi.EnsureRunning(handle);
        if (ProcessNativeApi.StartTicks(handle) != process.StartTicks) throw new InvalidOperationException("Process exited");
        var result = new ProcessDetails(process, command, directory, architecture, elevated, signature, company, description, version, product, modules, environment);
        lock (detailGate) details[process.Identity] = result;
        return result;
    }
    public void ClearDetailsCache() { lock (detailGate) details.Clear(); }
}
