using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using PortPilot.Core.Infrastructure;
using PortPilot.Core.Models;
using PortPilot.Core.Services;
using Xunit;

namespace PortPilot.Tests;

public sealed class DataAndSafetyTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "PortPilot-tests-中文 空格-" + Guid.NewGuid().ToString("N"));
    [Fact]
    public void PortableStoreRoundTripsSpecialPathsAndConfiguration()
    {
        var store = new PortableStore(root);
        var settings = store.Load("config.json", () => new AppSettings()); settings.Theme = "dark";
        store.Save("config.json", settings);
        Assert.Equal("dark", store.Load("config.json", () => new AppSettings()).Theme);
        Assert.Equal(Path.Combine(root, "data"), store.DataDirectory);
        Assert.Empty(Directory.GetFiles(store.DataDirectory, "*.tmp"));
    }
    [Fact]
    public void CorruptedJsonBackedUpAndRecovered()
    {
        var store = new PortableStore(root); File.WriteAllText(Path.Combine(store.DataDirectory, "config.json"), "{broken");
        var settings = store.Load("config.json", () => new AppSettings());
        Assert.Equal(2000, settings.RefreshInterval); Assert.Single(Directory.GetFiles(store.DataDirectory, "config.corrupted.*.json"));
        Assert.Single(store.Notices); Assert.NotNull(JsonDocument.Parse(File.ReadAllText(Path.Combine(store.DataDirectory, "config.json"))));
    }
    [Fact]
    public void FavoritesNotesAndBoundedHistoryPersist()
    {
        var store = new PortableStore(root); var data = new UserDataService(store); data.Settings.HistoryLimit = 3;
        data.Favorites.Add(new() { Value = "8080", Name = "用户服务" }); data.SaveFavorites(); data.SetNote(8080, "中文 note");
        for (var i = 0; i < 5; i++) data.Record("Test", i.ToString());
        var reload = new UserDataService(store);
        Assert.Equal("用户服务", Assert.Single(reload.Favorites).Name); Assert.Equal("中文 note", reload.Notes[8080]); Assert.Equal(3, reload.History.Count);
        Assert.DoesNotContain("status", File.ReadAllText(Path.Combine(store.DataDirectory, "favorites.json")));
    }
    [Theory]
    [InlineData("8080", true)] [InlineData("PID:1234", true)] [InlineData("PID:8080", false)]
    [InlineData("java", true)] [InlineData("localhost:8080", true)] [InlineData("127.0.0.1:8080", true)]
    [InlineData("C:\\中文 空格", true)] [InlineData("3306", false)]
    public void SearchesPortsAndPidWithoutAmbiguity(string query, bool expected)
    {
        var p = P(1234, 1, "java.exe") with { Path = @"C:\中文 空格\java.exe" };
        var c = new ConnectionSnapshot("TCP", "127.0.0.1", 8080, "0.0.0.0", 0, "LISTENING", 1234);
        Assert.Equal(expected, SearchService.Match(query, c, p));
    }
    [Fact]
    public void PidReuseAndCriticalProcessAreRejected()
    {
        Assert.Throws<InvalidOperationException>(() => ProcessKillService.ValidateIdentity(new(999, 123), 124));
        Assert.Throws<InvalidOperationException>(() => ProcessKillService.ValidateIdentity(new(999, 0), 0));
        var p = P(800, 1, "lsass.exe"); var svc = new ProcessKillService(NullLogger<ProcessKillService>.Instance);
        Assert.Throws<InvalidOperationException>(() => svc.CreatePlan([p], new([], [p], DateTimeOffset.Now, []), false, true));
    }
    [Fact]
    public void BatchPlanDeduplicatesPidsAndIncludesAllPorts()
    {
        var p = P(10000, 10, "fixture.exe"); var child = P(10001, 11, "child.exe") with { ParentPid = 10000 };
        var stale = P(10002, 9, "unrelated.exe") with { ParentPid = 10000 };
        var snapshot = new ScanSnapshot([new("TCP", "0.0.0.0", 8080, "", 0, "LISTENING", p.Pid), new("TCP", "0.0.0.0", 8081, "", 0, "LISTENING", p.Pid)], [p, child, stale], DateTimeOffset.Now, []);
        var svc = new ProcessKillService(NullLogger<ProcessKillService>.Instance);
        var plan = svc.CreatePlan([p, p], snapshot, false, true);
        Assert.Equal(new[] {8080, 8081}, Assert.Single(plan.Targets).Ports);
        var tree = svc.CreatePlan([p], snapshot, true, true); Assert.Equal(2, tree.Targets.Count); Assert.DoesNotContain(tree.Targets, t => t.Identity.Pid == stale.Pid);
    }
    [Fact]
    public async Task ExportQuotesAndNeutralizesFormulas()
    {
        Directory.CreateDirectory(root); var path = Path.Combine(root, "测试 export.csv");
        await ExportService.WriteAsync(path, [new() { ["Process"] = "=EVIL()", ["Path"] = "C:\\中文, \"test\"\\app.exe" }], default);
        var csv = File.ReadAllText(path); Assert.Contains("'=EVIL()", csv); Assert.Contains("\"\"test\"\"", csv);
        var json = Path.ChangeExtension(path, ".json"); await ExportService.WriteAsync(json, [new() { ["A"] = "测试" }], default); Assert.NotNull(JsonDocument.Parse(File.ReadAllText(json)));
    }
    [Fact]
    public void InvalidSettingsNormalizeToSafeDefaults()
    {
        var settings = new AppSettings { Theme = "unknown", RefreshInterval = -1, HistoryLimit = 100000, ConfirmBeforeKill = false };
        settings.Normalize(); Assert.Equal("system", settings.Theme); Assert.Equal(2000, settings.RefreshInterval); Assert.Equal(1000, settings.HistoryLimit); Assert.True(settings.ConfirmBeforeKill);
    }
    private static ProcessSnapshot P(int pid, long ticks, string name) => new(pid, ticks, name, "", 0, "user", 0, 0, 0, 0, "Available");
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
