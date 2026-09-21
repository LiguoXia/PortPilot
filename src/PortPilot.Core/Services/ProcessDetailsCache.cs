using PortPilot.Core.Models;

namespace PortPilot.Core.Services;

internal sealed class ProcessDetailsCache(int capacity = 32, long byteBudget = 4 * 1024 * 1024, TimeProvider? clock = null)
{
    private sealed record Entry(ProcessDetails Value, long Bytes, DateTimeOffset Expires);
    private readonly Dictionary<ProcessIdentity, LinkedListNode<Entry>> entries = [];
    private readonly LinkedList<Entry> recent = [];
    private readonly object gate = new();
    private long bytes;
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    internal int Count { get { lock (gate) return entries.Count; } }
    internal long RetainedBytes { get { lock (gate) return bytes; } }

    public bool TryGet(ProcessIdentity identity, out ProcessDetails value)
    {
        lock (gate)
        {
            if (entries.TryGetValue(identity, out var node))
            {
                if (node.Value.Expires > time.GetUtcNow())
                { recent.Remove(node); recent.AddFirst(node); value = node.Value.Value; return true; }
                Remove(node);
            }
            value = null!; return false;
        }
    }
    public void Store(ProcessDetails value)
    {
        lock (gate)
        {
            var identity = value.Process.Identity;
            if (entries.TryGetValue(identity, out var previous)) Remove(previous);
            var size = EstimateBytes(value);
            if (capacity <= 0 || size > byteBudget) return; // Still display large details, but do not retain them.
            while (recent.Last is { } last && (entries.Count >= capacity || bytes + size > byteBudget)) Remove(last);
            entries.Add(identity, recent.AddFirst(new Entry(value, size, time.GetUtcNow().AddMinutes(5)))); bytes += size;
        }
    }
    public void RemoveExpiredOrExited(HashSet<ProcessIdentity> live)
    {
        lock (gate)
            foreach (var node in entries.Values.Where(n => !live.Contains(n.Value.Value.Process.Identity) || n.Value.Expires <= time.GetUtcNow()).ToArray()) Remove(node);
    }
    public void Clear() { lock (gate) { entries.Clear(); recent.Clear(); bytes = 0; } }
    private void Remove(LinkedListNode<Entry> node) { entries.Remove(node.Value.Value.Process.Identity); recent.Remove(node); bytes -= node.Value.Bytes; }
    internal static long EstimateBytes(ProcessDetails value)
    {
        string[] fields = [value.CommandLine, value.WorkingDirectory, value.Architecture, value.Elevated, value.Signature, value.Company, value.Description,
            value.Version, value.Product, value.Process.Name, value.Process.Path, value.Process.User, value.Process.Status];
        return 512 + fields.Concat(value.Modules).Concat(value.EnvironmentNames).Sum(s => 32L + s.Length * 2L);
    }
}
