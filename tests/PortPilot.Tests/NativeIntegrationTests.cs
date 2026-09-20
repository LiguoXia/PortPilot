using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using PortPilot.Core.Models;
using PortPilot.Core.Native;
using PortPilot.Core.Services;
using Xunit;

namespace PortPilot.Tests;

public sealed class NativeIntegrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TcpListenerHasCorrectPortPidAndAddress(bool ipv6)
    {
        var ip = ipv6 ? IPAddress.IPv6Loopback : IPAddress.Loopback;
        using var listener = new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(new IPEndPoint(ip, 0)); listener.Listen();
        var port = ((IPEndPoint)listener.LocalEndPoint!).Port;
        var found = Assert.Single(IpHelperApi.Read(true, ipv6), c => c.LocalPort == port && c.Pid == Environment.ProcessId);
        Assert.True(found.Listening); Assert.Equal(ip.ToString(), found.LocalAddress);
        Assert.Equal(ipv6 ? "TCP6" : "TCP", found.Protocol);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UdpBindingHasCorrectPid(bool ipv6)
    {
        var ip = ipv6 ? IPAddress.IPv6Loopback : IPAddress.Loopback;
        using var socket = new Socket(ip.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(ip, 0)); var port = ((IPEndPoint)socket.LocalEndPoint!).Port;
        var found = Assert.Single(IpHelperApi.Read(false, ipv6), c => c.LocalPort == port && c.Pid == Environment.ProcessId);
        Assert.Equal(ip.ToString(), found.LocalAddress); Assert.Equal("BOUND", found.State);
    }
    [Fact]
    public async Task EstablishedConnectionContainsRemoteEndpoint()
    {
        using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(new IPEndPoint(IPAddress.Loopback, 0)); listener.Listen();
        using var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        await client.ConnectAsync(listener.LocalEndPoint!); using var accepted = await listener.AcceptAsync();
        var local = ((IPEndPoint)client.LocalEndPoint!).Port;
        var row = Assert.Single(IpHelperApi.Read(true, false), c => c.LocalPort == local && c.State == "ESTABLISHED");
        Assert.Equal(((IPEndPoint)listener.LocalEndPoint!).Port, row.RemotePort);
        Assert.Equal(Environment.ProcessId, row.Pid);
    }
    [Fact]
    public void PidQueryReturnsOwnMetadataAndCommand()
    {
        var service = new ProcessService(NullLogger<ProcessService>.Instance);
        var process = Assert.Single(service.Scan(default), p => p.Pid == Environment.ProcessId);
        Assert.True(process.StartTicks > 0); Assert.True(File.Exists(process.Path)); Assert.True(process.Memory > 0);
        var details = service.Details(process, default);
        Assert.False(string.IsNullOrWhiteSpace(details.CommandLine));
        Assert.DoesNotContain("Unavailable", details.CommandLine);
        Assert.Equal("x64", details.Architecture);
        Assert.Contains(details.Elevated, new[] {"Standard", "Administrator"});
    }
    [Fact]
    public void MissingPidFailsExplicitly()
    {
        using var handle = ProcessNativeApi.OpenProcess(ProcessNativeApi.Query, false, int.MaxValue);
        Assert.True(handle.IsInvalid);
        var service = new ProcessService(NullLogger<ProcessService>.Instance);
        var snapshot = new ProcessSnapshot(int.MaxValue, 1, "missing", "", 0, "", 0, 0, 0, 0, "");
        Assert.Throws<System.ComponentModel.Win32Exception>(() => service.Details(snapshot, default));
    }
    [Fact]
    public void SystemAccessDenialIsSafe()
    {
        using var handle = ProcessNativeApi.OpenProcess(ProcessNativeApi.Query, false, 0);
        Assert.True(handle.IsInvalid);
        var snapshot = new ProcessService(NullLogger<ProcessService>.Instance).Scan(default);
        var system = snapshot.FirstOrDefault(p => p.Pid == 4);
        Assert.NotNull(system); Assert.True(system.IsSystem);
        Assert.True(ProcessProtection.IsProtected(4, "System"));
    }
    [Fact]
    public async Task KillOnlyOwnedFixtureAndHandleExitedPid()
    {
        // The only real termination test target is a process explicitly created here.
        using var child = Process.Start(new ProcessStartInfo("cmd.exe", "/d /c pause") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true })!;
        try
        {
            var processService = new ProcessService(NullLogger<ProcessService>.Instance);
            var process = processService.Scan(default).Single(p => p.Pid == child.Id);
            var service = new ProcessKillService(NullLogger<ProcessKillService>.Instance);
            var plan = new KillPlan([new(process.Identity, process.Name, process.Path, [])], false, true);
            var results = await service.ExecuteAsync(plan, default);
            Assert.True(Assert.Single(results).Success);
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.ThrowsAny<Exception>(() => processService.Details(process, default));
            Assert.False(Assert.Single(await service.ExecuteAsync(plan, default)).Success);
        }
        finally { if (!child.HasExited) child.Kill(true); }
    }
    [Fact]
    public async Task ReusedPidIsNeverTerminated()
    {
        using var child = Process.Start(new ProcessStartInfo("cmd.exe", "/d /c pause") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true })!;
        try
        {
            var service = new ProcessKillService(NullLogger<ProcessKillService>.Instance);
            var plan = new KillPlan([new(new(child.Id, 1), "cmd.exe", "", [])], false, true);
            var result = Assert.Single(await service.ExecuteAsync(plan, default));
            Assert.False(result.Success); Assert.False(child.HasExited);
        }
        finally { if (!child.HasExited) child.Kill(true); }
    }
    [Fact]
    public void AdministratorDetectionMatchesToken()
    {
        using var handle = ProcessNativeApi.OpenProcess(ProcessNativeApi.Query, false, Environment.ProcessId);
        var token = ProcessNativeApi.Token(handle);
        Assert.Equal(ProcessNativeApi.IsAdministrator(), token.Elevated == "Administrator");
    }
    [Fact]
    public void ProtectedProcessReturnsAccessDeniedWithoutAnyTermination()
    {
        using var handle = ProcessNativeApi.OpenProcess(ProcessNativeApi.Terminate, false, 4);
        Assert.True(handle.IsInvalid);
        Assert.Equal(5, System.Runtime.InteropServices.Marshal.GetLastWin32Error());
    }
    [Fact]
    public void ChineseWorkingDirectoryAndCommandLineAreReadable()
    {
        var folder = Path.Combine(Path.GetTempPath(), "PortPilot 中文 空格 " + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        using var child = Process.Start(new ProcessStartInfo("cmd.exe", "/d /c \"rem 中文 空格 & pause\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, WorkingDirectory = folder })!;
        try
        {
            using var handle = ProcessNativeApi.OpenProcess(ProcessNativeApi.Query, false, child.Id);
            Assert.Contains("中文 空格", ProcessNativeApi.CommandLine(handle));
            Assert.Equal(folder.TrimEnd('\\'), ProcessNativeApi.Parameters(child.Id).Directory.TrimEnd('\\'));
        }
        finally { if (!child.HasExited) child.Kill(true); Directory.Delete(folder); }
    }
}
