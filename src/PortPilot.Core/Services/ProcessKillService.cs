using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;
using PortPilot.Core.Models;
using PortPilot.Core.Native;

namespace PortPilot.Core.Services;

public sealed class ProcessKillService(ILogger<ProcessKillService> logger)
{
    public KillPlan CreatePlan(IEnumerable<ProcessSnapshot> selected, ScanSnapshot snapshot, bool tree, bool force)
    {
        var targets = selected.DistinctBy(p => p.Identity).ToDictionary(p => p.Pid);
        if (tree)
        {
            bool changed;
            do
            {
                changed = false;
                foreach (var p in snapshot.Processes)
                    if (!targets.ContainsKey(p.Pid) && targets.TryGetValue(p.ParentPid, out var parent) && p.StartTicks >= parent.StartTicks)
                    { targets[p.Pid] = p; changed = true; }
            } while (changed);
        }
        if (targets.Count == 0) throw new InvalidOperationException("请先选择进程。");
        foreach (var p in targets.Values)
        {
            if (ProcessProtection.IsProtected(p.Pid, p.Name)) throw new InvalidOperationException($"已保护 {p.Name} (PID {p.Pid})。结束 Windows 核心进程可能导致注销、蓝屏或重启。");
            if (p.StartTicks <= 0) throw new InvalidOperationException($"无法核实 PID {p.Pid} 的启动时间。请先以管理员身份读取后重试。");
        }
        // Children first. Only the exact confirmed snapshot is acted on, never future children.
        return new(targets.Values.Reverse().Select(p => new KillTarget(p.Identity, p.Name, p.Path,
            snapshot.Connections.Where(c => c.Pid == p.Pid).Select(c => c.LocalPort).Distinct().Order().ToArray())).ToArray(), tree, force);
    }
    public static void ValidateIdentity(ProcessIdentity expected, long actual)
    { if (expected.StartTicks <= 0 || expected.StartTicks != actual) throw new InvalidOperationException("PID 已复用或原进程已退出；为避免误操作，已取消。"); }

    public async Task<IReadOnlyList<KillResult>> ExecuteAsync(KillPlan plan, CancellationToken token)
    {
        var results = new List<KillResult>();
        foreach (var target in plan.Targets.DistinctBy(t => t.Identity))
        {
            token.ThrowIfCancellationRequested();
            var pid = target.Identity.Pid;
            try
            {
                if (ProcessProtection.IsProtected(pid, target.Name)) throw new InvalidOperationException("关键进程保护：操作已禁止。");
                using var handle = ProcessNativeApi.OpenProcess(ProcessNativeApi.Query | (plan.Force ? ProcessNativeApi.Terminate : 0), false, pid);
                if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
                ProcessNativeApi.EnsureRunning(handle);
                ValidateIdentity(target.Identity, ProcessNativeApi.StartTicks(handle));
                var actualName = Path.GetFileNameWithoutExtension(ProcessNativeApi.ImagePath(handle));
                if (ProcessProtection.IsProtected(pid, actualName)) throw new InvalidOperationException("关键进程保护：操作已禁止。");
                if (!ProcessNativeApi.IsProcessCritical(handle, out var critical)) throw new Win32Exception(Marshal.GetLastWin32Error());
                if (critical) throw new InvalidOperationException("Windows 标记该进程为关键进程，禁止结束。");
                if (plan.Force)
                {
                    if (!ProcessNativeApi.TerminateProcess(handle, 1)) throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                else
                {
                    using var process = Process.GetProcessById(pid);
                    ValidateIdentity(target.Identity, process.StartTime.ToUniversalTime().Ticks);
                    if (!process.CloseMainWindow()) throw new InvalidOperationException("进程没有可关闭的主窗口。可在确认影响范围后使用 Force Kill。");
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(2500);
                    try { await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new InvalidOperationException("进程未响应关闭请求。可使用 Force Kill。"); }
                }
                results.Add(new(pid, true, false, plan.Force ? "已发送强制结束请求" : "已正常结束"));
                logger.LogInformation("Process operation succeeded PID {Pid}, start {Start}, force {Force}", pid, target.Identity.StartTicks, plan.Force);
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or ArgumentException)
            {
                var denied = ex is Win32Exception w && w.NativeErrorCode == 5;
                results.Add(new(pid, false, denied, denied ? "当前权限不足，需要管理员权限执行该操作。" : ex.Message));
                logger.LogWarning(ex, "Process operation failed PID {Pid}", pid);
            }
        }
        return results;
    }
}
