using System.Globalization;
using System.Net;
using PortPilot.Core.Models;

namespace PortPilot.Core.Services;

public enum SearchField { All, LocalPort, RemotePort, Pid, ProcessName, Path, IpAddress }

public sealed record SearchQuery(SearchField Field, string Text, bool Exact, bool IsEmpty)
{
    public static SearchQuery Parse(string text, SearchField field = SearchField.All, bool exact = false)
    {
        text = text.Trim();
        if (text.Length == 0) return new(field, "", exact, true);
        // The picker is authoritative; prefixes are shortcuts in All mode only.
        var separator = text.IndexOf(':');
        if (field == SearchField.All && separator > 0)
        {
            var scoped = text[..separator].ToLowerInvariant() switch
            {
                "port" or "localport" => SearchField.LocalPort, "remoteport" => SearchField.RemotePort,
                "pid" => SearchField.Pid, "process" or "name" => SearchField.ProcessName,
                "path" => SearchField.Path, "ip" => SearchField.IpAddress, _ => SearchField.All
            };
            if (scoped != SearchField.All) { field = scoped; text = text[(separator + 1)..].Trim(); }
        }
        return new(field, text, exact, false);
    }

    public int? LocalPort => Field is SearchField.All or SearchField.LocalPort && TryNumber(Text, out var n) && n is > 0 and <= 65535 ? n : null;
    internal static bool TryNumber(string text, out int value) => int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
}

public static class SearchService
{
    public static bool Match(string query, ConnectionSnapshot? c, ProcessSnapshot? p, SearchField field = SearchField.All, bool exact = false)
        => Match(SearchQuery.Parse(query, field, exact), c, p);

    public static bool Match(SearchQuery query, ConnectionSnapshot? c, ProcessSnapshot? p)
    {
        if (query.IsEmpty) return true;
        if (query.Text.Length == 0) return false;
        bool Text(string? value) => MatchesText(query, value);
        var numeric = SearchQuery.TryNumber(query.Text, out var number);
        var port = numeric && number is > 0 and <= 65535;
        return query.Field switch
        {
            SearchField.LocalPort => port && c?.LocalPort == number,
            SearchField.RemotePort => port && c?.RemotePort == number,
            SearchField.Pid => numeric && (c?.Pid ?? p?.Pid) == number,
            SearchField.ProcessName => p != null && Text(p.Name),
            SearchField.Path => p != null && Text(p.Path),
            SearchField.IpAddress => c != null && (MatchesIp(query, c.LocalAddress) || MatchesIp(query, c.RemoteAddress)),
            _ => numeric
                ? port && (c?.LocalPort == number || c?.RemotePort == number) || (c?.Pid ?? p?.Pid) == number
                : c != null && (Text(c.Local) || Text(c.Remote) || Text(c.LocalAddress) || Text(c.RemoteAddress) || Text(c.State)
                    || MatchesEndpoint(query, c.Local) || MatchesEndpoint(query, c.Remote))
                    || p != null && (Text(p.Name) || Text(p.Path) || Text(p.User))
        };
    }

    public static bool MatchFavorite(SearchQuery query, Favorite favorite)
    {
        if (query.IsEmpty) return true;
        if (query.Text.Length == 0) return false;
        return query.Field switch
        {
            SearchField.LocalPort => favorite.Type == "port" && query.LocalPort is { } port && SearchQuery.TryNumber(favorite.Value, out var value) && port == value,
            SearchField.Pid => favorite.Type == "pid" && SearchQuery.TryNumber(query.Text, out var pid) && SearchQuery.TryNumber(favorite.Value, out var value) && pid == value,
            SearchField.ProcessName => favorite.Type == "process" && MatchesText(query, favorite.Value),
            SearchField.All => MatchesText(query, favorite.Name) || MatchesText(query, favorite.Value) || MatchesText(query, favorite.Note),
            _ => false // Favorites do not store executable paths or remote endpoints.
        };
    }

    private static bool MatchesText(SearchQuery query, string? value) => value != null && (query.Exact
        ? value.Equals(query.Text, StringComparison.OrdinalIgnoreCase)
        : value.Contains(query.Text, StringComparison.OrdinalIgnoreCase));

    private static bool MatchesIp(SearchQuery query, string address)
    {
        if (query.Text.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            return IPAddress.TryParse(address, out var loopback) && IPAddress.IsLoopback(loopback);
        if (query.Exact && IPAddress.TryParse(query.Text, out var wanted) && IPAddress.TryParse(address, out var actual)) return wanted.Equals(actual);
        return MatchesText(query, address);
    }

    private static bool MatchesEndpoint(SearchQuery query, string endpoint)
        => query.Text.StartsWith("localhost:", StringComparison.OrdinalIgnoreCase)
            && MatchesText(query with { Text = "127.0.0.1:" + query.Text[10..] }, endpoint);
}
