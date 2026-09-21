using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Windows.Threading;
using PortPilot.Views;

namespace PortPilot.Infrastructure;

internal static class PerformanceRun
{
    public static async Task RunAsync(MainWindow window, string directory)
    {
        var vm = window.ViewModel;
        vm.Settings.AutoRefresh = false; vm.ApplySettingsCommand.Execute(null);
        var samples = new List<object>(); var latencies = new List<double>();
        using var stop = new CancellationTokenSource();
        var heartbeat = Task.Run(async () =>
        {
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    await Task.Delay(50, stop.Token); var queued = Stopwatch.GetTimestamp();
                    await window.Dispatcher.InvokeAsync(() => latencies.Add(Stopwatch.GetElapsedTime(queued).TotalMilliseconds), DispatcherPriority.Input, stop.Token);
                }
            }
            catch (OperationCanceledException) { }
        });
        var sockets = new List<TcpListener>();
        var phaseSeconds = int.TryParse(Environment.GetEnvironmentVariable("PORTPILOT_PERF_PHASE_SECONDS"), out var seconds) ? Math.Clamp(seconds, 10, 900) : 40;
        var fixtureCount = int.TryParse(Environment.GetEnvironmentVariable("PORTPILOT_PERF_ENDPOINTS"), out var count) ? Math.Clamp(count, 0, 2000) : 400;
        try
        {
            for (var i = 0; i < fixtureCount; i++) { var socket = new TcpListener(IPAddress.Loopback, 0); socket.Start(); sockets.Add(socket); }
            await vm.RefreshAsync(); await Task.Delay(2000);
            using var self = Process.GetCurrentProcess();
            var started = Stopwatch.StartNew(); var cpuStart = self.TotalProcessorTime;
            foreach (var phase in new[] { "dashboard", "ports", "network", "selection-churn", "cooldown" })
            {
                if (phase == "ports") vm.NavigateCommand.Execute("Ports");
                if (phase == "network")
                {
                    await vm.InspectCommand.ExecuteAsync(vm.Processes.First(p => p.Pid == Environment.ProcessId));
                    window.NetworkTab.IsSelected = true;
                }
                if (phase == "cooldown")
                {
                    vm.CloseDetailsCommand.Execute(null); vm.NavigateCommand.Execute("Dashboard");
                    foreach (var socket in sockets) socket.Stop(); sockets.Clear();
                }
                var phaseClock = Stopwatch.StartNew();
                while (phaseClock.Elapsed.TotalSeconds < phaseSeconds)
                {
                    var allocated = GC.GetTotalAllocatedBytes(false); var refresh = Stopwatch.StartNew();
                    await vm.RefreshAsync(); refresh.Stop();
                    if (phase == "selection-churn")
                    {
                        for (var i = 0; i < 8; i++)
                        {
                            vm.CloseDetailsCommand.Execute(null);
                            await vm.InspectCommand.ExecuteAsync(vm.Processes.First(p => p.Pid == Environment.ProcessId));
                        }
                        window.NetworkTab.IsSelected = true;
                    }
                    await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ContextIdle);
                    self.Refresh();
                    samples.Add(new
                    {
                        Phase = phase, Seconds = Math.Round(started.Elapsed.TotalSeconds, 2),
                        WorkingSetMiB = Math.Round(self.WorkingSet64 / 1048576d, 2), PrivateMiB = Math.Round(self.PrivateMemorySize64 / 1048576d, 2),
                        ManagedMiB = Math.Round(GC.GetTotalMemory(false) / 1048576d, 2), AllocatedMiB = Math.Round((GC.GetTotalAllocatedBytes(false) - allocated) / 1048576d, 3),
                        RefreshMs = Math.Round(refresh.Elapsed.TotalMilliseconds, 2), Handles = self.HandleCount, Rows = vm.Ports.Count, Processes = vm.Processes.Count,
                        Gen0 = GC.CollectionCount(0), Gen1 = GC.CollectionCount(1), Gen2 = GC.CollectionCount(2)
                    });
                    await Task.Delay(2000);
                }
            }
            stop.Cancel(); await heartbeat;
            var ordered = latencies.Order().ToArray();
            var report = new
            {
                Version = typeof(App).Assembly.GetName().Version?.ToString(), Date = DateTimeOffset.Now, PhaseSeconds = phaseSeconds, FixtureEndpoints = fixtureCount,
                CpuPercent = Math.Round((self.TotalProcessorTime - cpuStart).TotalSeconds / started.Elapsed.TotalSeconds / Environment.ProcessorCount * 100, 2),
                DispatcherP95Ms = ordered[(int)((ordered.Length - 1) * .95)], DispatcherMaxMs = ordered[^1], Samples = samples
            };
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, "performance.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { stop.Cancel(); foreach (var socket in sockets) socket.Stop(); await heartbeat; }
    }
}
