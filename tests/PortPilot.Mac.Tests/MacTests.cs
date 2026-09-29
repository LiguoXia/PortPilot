using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using PortPilot.Core.Mac;
using PortPilot.Core.Models;
using Xunit;

namespace PortPilot.Mac.Tests;

public sealed class MacFactAttribute : FactAttribute
{ public MacFactAttribute() { if (!OperatingSystem.IsMacOS()) Skip = "Requires a real macOS host"; } }

public sealed class MacTests
{
    private static ProcessSnapshot P(int pid, long ticks, string name = "fixture", int parent = 1, string user = "tester")
        => new(pid, ticks, name, "/tmp/" + name, parent, user, 0, 0, 0, 0, "Available");
    [Fact]
    public void NulFieldsHandleMultipleProcessesFilesStatesAndDuplicates()
    {
        var rows = LsofParser.Parse("p123\0cname with spaces\0\nf1\0tIPv4\0PTCP\0n*:8080\0TST=LISTEN\0\nf2\0tIPv6\0PTCP\0n[::1]:12345->[::1]:443\0TST=ESTABLISHED\0\nf3\0tIPv4\0PUDP\0n127.0.0.1:5353\0\np456\0\nf1\0tIPv6\0PUDP\0n*:9000\0\nf2\0tIPv6\0PUDP\0n*:9000\0\n");
        Assert.Equal(4, rows.Count);
        Assert.Contains(rows, c => c.Pid == 123 && c.LocalAddress == "0.0.0.0" && c.LocalPort == 8080 && c.Listening);
        Assert.Contains(rows, c => c.Protocol == "TCP6" && c.LocalAddress == "::1" && c.RemotePort == 443 && c.State == "ESTABLISHED");
        Assert.Contains(rows, c => c.Protocol == "UDP" && c.State == "BOUND" && c.LocalPort == 5353);
        Assert.Contains(rows, c => c.Pid == 456 && c.Protocol == "UDP6" && c.LocalAddress == "::");
    }
    [Fact]
    public void MalformedUnsupportedAndIncompleteRecordsAreNotEndpoints()
    {
        Assert.Empty(LsofParser.Parse("p1\0\nf1\0tunix\0n/tmp/sock\0\nf2\0PTCP\0tIPv4\0n127.0.0.1:notaport\0\nf3\0tIPv4\0PTCP\0n127.0.0.1:999999\0"));
        Assert.Empty(LsofParser.Parse(""));
    }
    [Fact]
    public void TreeDeduplicatesAndRejectsOlderUnrelatedChildren()
    {
        var root = P(10001, 10); var child = P(10002, 11, parent: root.Pid); var grandchild = P(10003, 12, parent: child.Pid);
        var unrelated = P(10004, 9, parent: root.Pid);
        var snapshot = new ScanSnapshot([new("TCP", "::", 8080, "", 0, "LISTENING", root.Pid)], [grandchild, child, root, unrelated], DateTimeOffset.Now, []);
        var plan = new MacKillService().CreatePlan([root, root], snapshot, true, false);
        Assert.Equal(new[] { grandchild.Pid, child.Pid, root.Pid }, plan.Targets.Select(t => t.Identity.Pid));
        Assert.Equal(8080, Assert.Single(plan.Targets.Last().Ports));
    }
    [Theory]
    [InlineData(1, 1, "launchd", "root")]
    [InlineData(123, 1, "WindowServer", "_windowserver")]
    [InlineData(456, 1, "custom", "root")]
    [InlineData(456, 0, "custom", "tester")]
    public void CriticalRootAndUnverifiedProcessesAreProtected(int pid, long ticks, string name, string user)
    {
        var p = P(pid, ticks, name, user: user);
        Assert.Throws<InvalidOperationException>(() => new MacKillService().CreatePlan([p], new([], [p], DateTimeOffset.Now, []), false, true));
    }
    [MacFact]
    public async Task NativeTcpUdpIpv4Ipv6AndEstablishedEndpoints()
    {
        foreach (var ip in new[] { IPAddress.Loopback, IPAddress.IPv6Loopback })
        {
            using var listener = new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            listener.Bind(new IPEndPoint(ip, 0)); listener.Listen();
            using var udp = new Socket(ip.AddressFamily, SocketType.Dgram, ProtocolType.Udp); udp.Bind(new IPEndPoint(ip, 0));
            using var client = new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            await client.ConnectAsync(listener.LocalEndPoint!); using var accepted = await listener.AcceptAsync();
            var snapshot = await new MacNetworkService(new()).ScanAsync(default);
            var port = ((IPEndPoint)listener.LocalEndPoint!).Port; var udpPort = ((IPEndPoint)udp.LocalEndPoint!).Port;
            Assert.Contains(snapshot.Connections, c => c.Pid == Environment.ProcessId && c.LocalPort == port && c.LocalAddress == ip.ToString() && c.Listening);
            Assert.Contains(snapshot.Connections, c => c.Pid == Environment.ProcessId && c.LocalPort == udpPort && c.Protocol.StartsWith("UDP"));
            Assert.Contains(snapshot.Connections, c => c.Pid == Environment.ProcessId && c.RemotePort == port && c.State == "ESTABLISHED");
            var own = Assert.Single(snapshot.Processes, p => p.Pid == Environment.ProcessId);
            Assert.True(own.StartTicks > 0); Assert.True(own.Memory > 0); Assert.True(File.Exists(own.Path));
            var details = await new MacProcessService().DetailsAsync(own, default);
            Assert.NotEqual("Unavailable", details.CommandLine); Assert.NotEmpty(details.CommandLine);
        }
    }
    [MacFact]
    public async Task ReusedPidRejectedAndOwnedChildCanReceiveTermAndKill()
    {
        foreach (var force in new[] { false, true })
        {
            using var child = Process.Start(new ProcessStartInfo("/bin/sleep", "60") { UseShellExecute = false })!;
            try
            {
                var snapshot = await new MacNetworkService(new()).ScanAsync(default);
                var target = Assert.Single(snapshot.Processes, p => p.Pid == child.Id);
                var svc = new MacKillService(); var plan = svc.CreatePlan([target], snapshot, false, force);
                var stale = plan with { Targets = [plan.Targets[0] with { Identity = target.Identity with { StartTicks = 1 } }] };
                Assert.False(Assert.Single(await svc.ExecuteAsync(stale, default)).Success); Assert.False(child.HasExited);
                Assert.True(Assert.Single(await svc.ExecuteAsync(plan, default)).Success);
                await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                Assert.False(Assert.Single(await svc.ExecuteAsync(plan, default)).Success);
            }
            finally { if (!child.HasExited) child.Kill(); }
        }
    }
    [MacFact]
    public async Task CommandCancellationStopsOwnedHelper()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => MacCommand.RunAsync("/bin/sleep", ["30"], timeout.Token));
    }
}
