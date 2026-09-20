using PortPilot.Core.Infrastructure;
using PortPilot.Core.Models;

namespace PortPilot.Core.Services;

public sealed class UserDataService
{
    private readonly PortableStore store;
    private readonly object gate = new();
    public AppSettings Settings { get; }
    public List<Favorite> Favorites { get; }
    public List<HistoryEntry> History { get; }
    public Dictionary<int, string> Notes { get; }
    public UserDataService(PortableStore store)
    {
        this.store = store;
        Settings = store.Load("config.json", () => new AppSettings()); Settings.Normalize();
        Favorites = store.Load("favorites.json", () => new List<Favorite>());
        History = store.Load("history.json", () => new List<HistoryEntry>());
        Notes = store.Load("ports.json", () => new Dictionary<int, string>());
        History.RemoveRange(Math.Min(History.Count, Settings.HistoryLimit), Math.Max(0, History.Count - Settings.HistoryLimit));
    }
    public void SaveSettings() { lock (gate) { Settings.Normalize(); store.Save("config.json", Settings); } }
    public void SaveFavorites() { lock (gate) store.Save("favorites.json", Favorites); }
    public void SetNote(int port, string note) { lock (gate) { if (string.IsNullOrWhiteSpace(note)) Notes.Remove(port); else Notes[port] = note; store.Save("ports.json", Notes); } }
    public void Record(string action, string detail)
    {
        lock (gate)
        {
            History.Insert(0, new(DateTimeOffset.Now, action, detail));
            if (History.Count > Settings.HistoryLimit) History.RemoveRange(Settings.HistoryLimit, History.Count - Settings.HistoryLimit);
            store.Save("history.json", History);
        }
    }
    public void ClearHistory() { lock (gate) { History.Clear(); store.Save("history.json", History); } }
}
