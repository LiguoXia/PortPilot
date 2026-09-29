using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using PortPilot.Core.Models;

namespace PortPilot.Core.Mac;

public sealed class MacProcessService
{
    private readonly Dictionary<ProcessIdentity, (TimeSpan Cpu, long Time)> samples = [];
    [DllImport("/usr/lib/libproc.dylib", SetLastError = true)]
    private static extern int proc_pidpath(int pid, byte[] buffer, uint size);

    public static string ExecutablePath(int pid)
    {
        var buffer = new byte[4096];
        var length = proc_pidpath(pid, buffer, (uint)buffer.Length);
        if (length <= 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        return Encoding.UTF8.GetString(buffer, 0, length).TrimEnd('\0');
    }

    public async Task<IReadOnlyList<ProcessSnapshot>> ScanAsync(CancellationToken token)
    {
        var output = await MacCommand.RunAsync("/bin/ps", ["-axo", "pid=,ppid=,user=,comm="], token).ConfigureAwait(false);
        if (output.ExitCode != 0) throw new IOException("无法读取进程快照：" + output.Error);
        var result = new List<ProcessSnapshot>(); var live = new HashSet<ProcessIdentity>();
        foreach (var line in output.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            token.ThrowIfCancellationRequested();
            var fields = line.Trim().Split((char[]?)null, 4, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 4 || !int.TryParse(fields[0], out var pid) || !int.TryParse(fields[1], out var parent)) continue;
            long ticks = 0, memory = 0; double cpu = 0; string status = "Available", path = fields[3];
            try
            {
                using var p = Process.GetProcessById(pid);
                ticks = p.StartTime.ToUniversalTime().Ticks;
                path = ExecutablePath(pid);
                var total = p.TotalProcessorTime; memory = p.WorkingSet64;
                var key = new ProcessIdentity(pid, ticks); live.Add(key);
                var now = Stopwatch.GetTimestamp();
                if (samples.TryGetValue(key, out var previous))
                {
                    var elapsed = Stopwatch.GetElapsedTime(previous.Time, now).TotalSeconds;
                    if (elapsed > 0) cpu = Math.Clamp((total - previous.Cpu).TotalSeconds / elapsed / Environment.ProcessorCount * 100, 0, 100);
                }
                samples[key] = (total, now);
                if (p.HasExited) continue;
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or ArgumentException or NotSupportedException)
            { status = "受限 / 已退出"; }
            result.Add(new(pid, ticks, Path.GetFileName(path), path, parent, fields[2], Math.Round(cpu, 1), memory, 0, 0, status));
        }
        foreach (var key in samples.Keys.Where(k => !live.Contains(k)).ToArray()) samples.Remove(key);
        return result;
    }

    public static void Validate(ProcessSnapshot expected)
    {
        using var p = Process.GetProcessById(expected.Pid);
        if (p.HasExited || expected.StartTicks <= 0 || expected.StartTicks != p.StartTime.ToUniversalTime().Ticks)
            throw new InvalidOperationException("原进程已退出或 PID 已复用，请刷新。");
        if (!StringComparer.Ordinal.Equals(ExecutablePath(expected.Pid), expected.Path))
            throw new InvalidOperationException("进程可执行文件已变化，请刷新。");
    }

    public async Task<ProcessDetails> DetailsAsync(ProcessSnapshot process, CancellationToken token)
    {
        Validate(process);
        var command = await MacCommand.RunAsync("/bin/ps", ["-ww", "-p", process.Pid.ToString(), "-o", "command="], token).ConfigureAwait(false);
        var files = await MacCommand.RunAsync("/usr/sbin/lsof", ["-n", "-P", "-a", "-p", process.Pid.ToString(), "-d", "cwd,txt", "-F0fn"], token).ConfigureAwait(false);
        string directory = "Unavailable / Access Denied", descriptor = ""; var modules = new HashSet<string>();
        foreach (var raw in files.Output.Split('\0'))
        {
            var field = raw.TrimStart('\r', '\n');
            if (field.Length < 2) continue;
            if (field[0] == 'f') descriptor = field[1..];
            if (field[0] == 'n' && descriptor == "cwd") directory = field[1..];
            if (field[0] == 'n' && descriptor == "txt") modules.Add(field[1..]);
        }
        Validate(process);
        return new(process, command.ExitCode == 0 ? command.Output.Trim() : "Unavailable", directory,
            "未检测（可执行文件可能包含多种架构）", process.User == "root" ? "root" : "Standard", "未校验", "—", "—", "—", "—", modules.ToArray(), []);
    }
}
