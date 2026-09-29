using System.ComponentModel;
using System.Runtime.InteropServices;
using PortPilot.Core.Models;

namespace PortPilot.Core.Mac;

public sealed class MacKillService
{
    private static readonly HashSet<string> ProtectedNames = new(StringComparer.OrdinalIgnoreCase)
    { "kernel_task", "launchd", "WindowServer", "loginwindow", "logind", "securityd", "sandboxd", "watchdogd", "runningboardd", "powerd", "opendirectoryd", "tccd", "syspolicyd", "amfid", "trustd" };
    [DllImport("/usr/lib/libSystem.B.dylib", SetLastError = true)]
    private static extern int kill(int pid, int signal);
    public static bool IsProtected(ProcessSnapshot p) => p.Pid <= 4 || p.Pid == Environment.ProcessId || p.User == "root" || ProtectedNames.Contains(p.Name);

    public KillPlan CreatePlan(IEnumerable<ProcessSnapshot> selected, ScanSnapshot snapshot, bool tree, bool force)
    {
        var targets = selected.DistinctBy(p => p.Pid).ToDictionary(p => p.Pid);
        var ordered = targets.Values.ToList();
        if (tree)
        {
            for (int i = 0; i < ordered.Count; i++)
                foreach (var child in snapshot.Processes.Where(p => p.ParentPid == ordered[i].Pid && p.StartTicks >= ordered[i].StartTicks))
                    if (targets.TryAdd(child.Pid, child)) ordered.Add(child);
        }
        if (ordered.Count == 0) throw new InvalidOperationException("请先选择进程或端口。");
        foreach (var p in ordered)
            if (IsProtected(p) || p.StartTicks <= 0) throw new InvalidOperationException($"{p.Name} (PID {p.Pid}) 受保护或无法核实身份，禁止结束。");
        ordered.Reverse();
        return new(ordered.Select(p => new KillTarget(p.Identity, p.Name, p.Path,
            snapshot.Connections.Where(c => c.Pid == p.Pid).Select(c => c.LocalPort).Distinct().Order().ToArray())).ToArray(), tree, force);
    }

    public async Task<IReadOnlyList<KillResult>> ExecuteAsync(KillPlan plan, CancellationToken token)
    {
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException();
        var results = new List<KillResult>();
        foreach (var target in plan.Targets.DistinctBy(t => t.Identity))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                // Re-read owner and executable, never trust stale or caller-provided names.
                var current = (await new MacProcessService().ScanAsync(token).ConfigureAwait(false)).SingleOrDefault(p => p.Pid == target.Identity.Pid)
                    ?? throw new InvalidOperationException("进程已退出。");
                if (current.Identity != target.Identity || current.Path != target.Path) throw new InvalidOperationException("PID 已复用或程序已变化，已取消。");
                if (IsProtected(current)) throw new InvalidOperationException("系统进程保护：禁止结束。");
                MacProcessService.Validate(current);
                if (kill(current.Pid, plan.Force ? 9 : 15) != 0) throw new Win32Exception(Marshal.GetLastWin32Error());
                results.Add(new(current.Pid, true, false, plan.Force ? "已发送 SIGKILL" : "已发送 SIGTERM（不保证进程已退出）"));
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or ArgumentException)
            { results.Add(new(target.Identity.Pid, false, ex is Win32Exception w && w.NativeErrorCode is 1 or 13, ex.Message)); }
        }
        return results;
    }
}
