using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace PortPilot.Core.Infrastructure;

public sealed class PortableStore
{
    private readonly object gate = new();
    public string DataDirectory { get; }
    public List<string> Notices { get; } = [];
    public static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };
    public PortableStore(string? baseDirectory = null)
    {
        DataDirectory = Path.Combine(baseDirectory ?? AppContext.BaseDirectory, "data");
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(Path.Combine(DataDirectory, "logs"));
        Directory.CreateDirectory(Path.Combine(DataDirectory, "cache"));
    }
    public T Load<T>(string file, Func<T> factory)
    {
        lock (gate)
        {
            var path = Path.Combine(DataDirectory, file);
            if (File.Exists(path))
            {
                try { return JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions) ?? throw new JsonException("JSON 根节点为空"); }
                catch (JsonException)
                {
                    var backup = Path.Combine(DataDirectory, $"{Path.GetFileNameWithoutExtension(file)}.corrupted.{DateTime.Now:yyyyMMddHHmmssfff}.{Guid.NewGuid():N}.json");
                    File.Move(path, backup);
                    Notices.Add($"{file} 损坏，已备份并恢复默认值。");
                }
            }
            var value = factory(); Save(file, value); return value;
        }
    }
    public void Save<T>(string file, T value)
    {
        lock (gate)
        {
            var path = Path.Combine(DataDirectory, file);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                { JsonSerializer.Serialize(stream, value, JsonOptions); stream.Flush(true); }
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}

public sealed class PortableLoggerProvider(PortableStore store) : ILoggerProvider
{
    private readonly object gate = new();
    private DateOnly day;
    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);
    public void Dispose() { }
    private void Write(string message)
    {
        lock (gate)
        {
            var folder = Path.Combine(store.DataDirectory, "logs");
            var path = Path.Combine(folder, "app.log");
            var today = DateOnly.FromDateTime(DateTime.Now);
            try
            {
                if (day != today)
                {
                    if (File.Exists(path) && File.GetLastWriteTime(path).Date != DateTime.Today)
                        File.Move(path, Path.Combine(folder, $"app-{File.GetLastWriteTime(path):yyyy-MM-dd}-{Guid.NewGuid():N}.log"));
                    foreach (var file in Directory.EnumerateFiles(folder, "app-*.log"))
                        if (File.GetLastWriteTime(file) < DateTime.Now.AddDays(-7)) File.Delete(file);
                    day = today;
                }
                if (File.Exists(path) && new FileInfo(path).Length > 2_000_000)
                    File.Move(path, Path.Combine(folder, $"app-{DateTime.Now:yyyy-MM-dd-HHmmssfff}-{Guid.NewGuid():N}.log"));
                File.AppendAllText(path, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
            }
            catch (IOException ex) { System.Diagnostics.Trace.TraceError("PortPilot log unavailable: " + ex.Message); }
            catch (UnauthorizedAccessException ex) { System.Diagnostics.Trace.TraceError("PortPilot log access denied: " + ex.Message); }
        }
    }
    private sealed class FileLogger(PortableLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => level >= LogLevel.Information;
        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        { if (IsEnabled(level)) provider.Write($"[{level}] {category}: {formatter(state, exception)}{(exception is null ? "" : " | " + exception.GetType().Name + ": " + exception.Message)}"); }
    }
}
