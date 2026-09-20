using PortPilot.Core.Models;
using PortPilot.Core.Services;
using Xunit;

namespace PortPilot.Tests;

public sealed class SearchTests
{
    private static readonly ProcessSnapshot Process = new(8080, 1, "java.exe", @"C:\中文 空格\Java\java.exe", 1, @"DEV\java-user", 0, 0, 0, 0, "Available");
    private static readonly ConnectionSnapshot Connection = new("TCP", "127.0.0.1", 9000, "192.0.2.10", 443, "ESTABLISHED", 8080);

    [Theory]
    [InlineData(SearchField.LocalPort, "9000", true)]
    [InlineData(SearchField.LocalPort, " 09000 ", true)]
    [InlineData(SearchField.LocalPort, "8080", false)]
    [InlineData(SearchField.LocalPort, "443", false)]
    [InlineData(SearchField.LocalPort, "90", false)]
    [InlineData(SearchField.RemotePort, "443", true)]
    [InlineData(SearchField.RemotePort, "9000", false)]
    [InlineData(SearchField.Pid, "8080", true)]
    [InlineData(SearchField.Pid, "9000", false)]
    [InlineData(SearchField.Pid, "80", false)]
    [InlineData(SearchField.Pid, "java", false)]
    public void NumericFieldsDoNotCrossMatch(SearchField field, string query, bool expected)
        => Assert.Equal(expected, SearchService.Match(query, Connection, Process, field));

    [Theory]
    [InlineData(SearchField.ProcessName, "java", false, true)]
    [InlineData(SearchField.ProcessName, "java", true, false)]
    [InlineData(SearchField.ProcessName, " JAVA.EXE ", true, true)]
    [InlineData(SearchField.ProcessName, "中文 空格", false, false)]
    [InlineData(SearchField.Path, "中文 空格", false, true)]
    [InlineData(SearchField.Path, "java.exe", true, false)]
    [InlineData(SearchField.Path, "C:\\中文 空格\\JAVA\\JAVA.EXE", true, true)]
    [InlineData(SearchField.All, "java", true, false)]
    [InlineData(SearchField.All, "java.exe", true, true)]
    [InlineData(SearchField.IpAddress, "192.0.2.1", true, false)]
    [InlineData(SearchField.IpAddress, "192.0.2.1", false, true)]
    [InlineData(SearchField.IpAddress, "192.0.2.10", true, true)]
    [InlineData(SearchField.IpAddress, "localhost", true, true)]
    public void ExactTextMatchesWholeSelectedField(SearchField field, string query, bool exact, bool expected)
        => Assert.Equal(expected, SearchService.Match(query, Connection, Process, field, exact));

    [Theory]
    [InlineData("port:9000", true)]
    [InlineData("port:8080", false)]
    [InlineData("pid:8080", true)]
    [InlineData("pid:9000", false)]
    [InlineData("remoteport:443", true)]
    [InlineData("PROCESS: java.exe", true)]
    [InlineData("process:中文 空格", false)]
    [InlineData("path:C:\\中文 空格\\Java\\java.exe", true)]
    [InlineData("ip:192.0.2.10", true)]
    [InlineData("pid:", false)]
    [InlineData("port:", false)]
    public void PrefixShortcutsAreScoped(string query, bool expected)
        => Assert.Equal(expected, SearchService.Match(query, Connection, Process));

    [Fact]
    public void ExplicitFieldDoesNotGetSilentlyOverriddenByPrefix()
        => Assert.False(SearchService.Match("pid:8080", Connection, Process, SearchField.ProcessName));

    [Theory]
    [InlineData("-1")]
    [InlineData("65536")]
    [InlineData("2147483648")]
    [InlineData("8O80")]
    [InlineData("0")]
    public void InvalidPortsNeverMatchOrOfferPortFavorite(string query)
    {
        Assert.False(SearchService.Match(query, Connection, Process, SearchField.LocalPort));
        Assert.Null(SearchQuery.Parse(query, SearchField.LocalPort).LocalPort);
    }

    [Fact]
    public void EmptyQueryStillShowsAllRowsAndPathsKeepTheirDrivePrefix()
    {
        Assert.True(SearchService.Match("  ", Connection, Process, SearchField.Pid, true));
        Assert.Equal(SearchField.All, SearchQuery.Parse(@"C:\中文 空格\Java\java.exe").Field);
        Assert.True(SearchService.Match(@"C:\中文 空格\Java\java.exe", Connection, Process, exact: true));
        Assert.True(SearchService.Match("9000", Connection, Process));
        Assert.True(SearchService.Match("8080", Connection, Process));
        Assert.Null(SearchQuery.Parse("8080", SearchField.Pid).LocalPort);
        Assert.True(SearchService.Match("localhost:443", Connection with { RemoteAddress = "127.0.0.1" }, Process));
        Assert.True(SearchService.Match("192.0.2.10", Connection, Process, exact: true));
        Assert.False(SearchService.Match("192.0.2.1", Connection, Process, exact: true));
    }

    [Fact]
    public void IPv6ExactSearchAcceptsEquivalentAddressNotation()
    {
        var ipv6 = Connection with { Protocol = "TCP6", LocalAddress = "::1" };
        Assert.True(SearchService.Match("0:0:0:0:0:0:0:1", ipv6, Process, SearchField.IpAddress, true));
        Assert.False(SearchService.Match("::2", ipv6, Process, SearchField.IpAddress, true));
    }

    [Fact]
    public void FavoritesFollowSelectedFieldInsteadOfMatchingUnrelatedNames()
    {
        var port = new Favorite { Type = "port", Value = "8080", Name = "java.exe" };
        var pid = new Favorite { Type = "pid", Value = "8080", Name = "服务端口 8080" };
        var process = new Favorite { Type = "process", Value = "java.exe", Name = "Java 服务" };
        Assert.True(SearchService.MatchFavorite(SearchQuery.Parse("port:8080"), port));
        Assert.False(SearchService.MatchFavorite(SearchQuery.Parse("port:8080"), pid));
        Assert.True(SearchService.MatchFavorite(SearchQuery.Parse("pid:8080"), pid));
        Assert.False(SearchService.MatchFavorite(SearchQuery.Parse("pid:8080"), port));
        Assert.False(SearchService.MatchFavorite(SearchQuery.Parse("java.exe", SearchField.ProcessName, true), port));
        Assert.True(SearchService.MatchFavorite(SearchQuery.Parse("JAVA.EXE", SearchField.ProcessName, true), process));
    }
}
