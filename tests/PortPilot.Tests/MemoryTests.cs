using PortPilot.Core.Models;
using PortPilot.Core.Services;
using PortPilot.ViewModels;
using Xunit;

namespace PortPilot.Tests;

public class MemoryTests
{
    private static ProcessSnapshot Process(int pid = 123) => new(pid, 100, "fixture.exe", "fixture.exe", 1, "test", 0, 1048576, 1, 1, "Available");
    private static ProcessDetails Detail(int pid) => new(Process(pid), "command", "directory", "x64", "no", "none", "company", "description", "1", "fixture", ["module.dll"], ["PATH"]);

    [Fact] public void DetailsEvictLeastRecentlyReadEntryAtCapacity()
    {
        var cache = new ProcessDetailsCache(2);
        cache.Store(Detail(1)); cache.Store(Detail(2)); Assert.True(cache.TryGet(Process(1).Identity, out _)); cache.Store(Detail(3));
        Assert.Equal(2, cache.Count); Assert.False(cache.TryGet(Process(2).Identity, out _)); Assert.True(cache.TryGet(Process(1).Identity, out _));
    }
    [Fact] public void DetailsRespectByteBudgetAndDoNotRetainOversizedEntries()
    {
        var budget = ProcessDetailsCache.EstimateBytes(Detail(1)) * 2;
        var cache = new ProcessDetailsCache(32, budget);
        for (var i = 0; i < 100; i++) cache.Store(Detail(i));
        Assert.InRange(cache.RetainedBytes, 1, budget); Assert.Equal(2, cache.Count);
        cache.Store(Detail(200) with { CommandLine = new string('x', (int)budget) });
        Assert.False(cache.TryGet(Process(200).Identity, out _)); Assert.Equal(2, cache.Count);
    }
    [Fact] public void DetailCacheExpiresAndUsesStartTimeToDistinguishPidReuse()
    {
        var clock = new Clock(); var cache = new ProcessDetailsCache(clock: clock);
        cache.Store(Detail(1));
        Assert.False(cache.TryGet(new ProcessIdentity(1, 101), out _));
        clock.Now = clock.Now.AddMinutes(6);
        Assert.False(cache.TryGet(Process(1).Identity, out _)); Assert.Equal(0, cache.RetainedBytes);
    }
    [Fact] public void DetailCachePrunesExitedProcessesAndClearsAccounting()
    {
        var cache = new ProcessDetailsCache(); cache.Store(Detail(1)); cache.Store(Detail(2));
        cache.RemoveExpiredOrExited([Process(2).Identity]); Assert.Equal(1, cache.Count);
        cache.Clear(); Assert.Equal(0, cache.Count); Assert.Equal(0, cache.RetainedBytes);
    }
    [Fact] public void ConcurrentDetailsRemainBounded()
    {
        var cache = new ProcessDetailsCache(8);
        Parallel.For(0, 200, i => { cache.Store(Detail(i)); cache.TryGet(Process(i).Identity, out _); });
        Assert.InRange(cache.Count, 1, 8); Assert.InRange(cache.RetainedBytes, 1, 4 * 1024 * 1024);
    }
    [Fact] public void ResourceUpdatesDoNotInvalidateEndpointOrIdentityBindings()
    {
        var p = Process(); var c = new ConnectionSnapshot("TCP", "127.0.0.1", 8080, "127.0.0.1", 9000, "ESTABLISHED", p.Pid);
        var row = new InspectorRow("fixture", p, c, DateTimeOffset.Now); var names = new List<string?>();
        row.PropertyChanged += (_, e) => names.Add(e.PropertyName);
        row.Update(p with { Cpu = 5, Memory = 4 * 1048576, Handles = 2 }, c, "", 0, "—");
        Assert.Contains(nameof(row.Cpu), names); Assert.Contains(nameof(row.Memory), names); Assert.Contains(nameof(row.Handles), names);
        Assert.DoesNotContain("", names); Assert.DoesNotContain(nameof(row.State), names); Assert.DoesNotContain(nameof(row.Name), names);
        Assert.DoesNotContain(nameof(row.Local), names); Assert.DoesNotContain(nameof(row.Remote), names);
    }
    [Fact] public void EndpointChangesNotifyStateAndRemoteBindings()
    {
        var p = Process(); var c = new ConnectionSnapshot("TCP", "127.0.0.1", 8080, "127.0.0.1", 9000, "ESTABLISHED", p.Pid);
        var row = new InspectorRow("fixture", p, c, DateTimeOffset.Now); var names = new List<string?>();
        row.PropertyChanged += (_, e) => names.Add(e.PropertyName);
        row.Update(p, c with { State = "CLOSE_WAIT", RemotePort = 9001 }, "", 0, "—");
        Assert.Contains(nameof(row.State), names); Assert.Contains(nameof(row.RemotePort), names); Assert.Contains(nameof(row.Remote), names);
        Assert.DoesNotContain(nameof(row.Local), names); Assert.DoesNotContain(nameof(row.Name), names);
    }
    private sealed class Clock : TimeProvider { public DateTimeOffset Now = DateTimeOffset.UtcNow; public override DateTimeOffset GetUtcNow() => Now; }
}
