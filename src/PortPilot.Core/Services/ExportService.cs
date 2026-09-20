using System.Text;
using System.Text.Json;
using PortPilot.Core.Infrastructure;

namespace PortPilot.Core.Services;

public static class ExportService
{
    public static string CsvCell(string value)
    {
        // Prevent spreadsheet formula injection when opening exported executable paths/names.
        if (value.TrimStart().StartsWith('=') || value.TrimStart().StartsWith('+') || value.TrimStart().StartsWith('-') || value.TrimStart().StartsWith('@')) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
    public static async Task WriteAsync(string path, IReadOnlyList<Dictionary<string, string>> rows, CancellationToken token)
    {
        if (Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase))
        { await File.WriteAllTextAsync(path, JsonSerializer.Serialize(rows, PortableStore.JsonOptions), Encoding.UTF8, token); return; }
        var csv = Path.GetExtension(path).Equals(".csv", StringComparison.OrdinalIgnoreCase);
        var builder = new StringBuilder();
        if (rows.Count > 0)
        {
            builder.AppendLine(string.Join(csv ? "," : "\t", rows[0].Keys.Select(x => csv ? CsvCell(x) : x)));
            foreach (var row in rows) builder.AppendLine(string.Join(csv ? "," : "\t", row.Values.Select(x => csv ? CsvCell(x) : x.Replace("\r", " ").Replace("\n", " ").Replace("\t", " "))));
        }
        await File.WriteAllTextAsync(path, builder.ToString(), new UTF8Encoding(true), token);
    }
}
