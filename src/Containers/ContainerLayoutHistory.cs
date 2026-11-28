using System.Text.Json;
using MotionDesk.Services;
using Opsis.Containers;

namespace MotionDesk.Containers;

/// <summary>Bounded local layout history. Does not move, modify or duplicate user files.</summary>
internal sealed class ContainerLayoutHistory
{
    private const int EntriesPerContainer = 8;
    private const int MaxFileBytes = 2_000_000;
    private readonly string path = Path.Combine(AppPaths.AppDataFolder, "containers-layout-history.json");
    private Dictionary<string, List<string>> entries = new(StringComparer.OrdinalIgnoreCase);
    private bool dirty;

    public ContainerLayoutHistory()
    {
        try
        {
            if (File.Exists(path) && new FileInfo(path).Length <= MaxFileBytes)
            {
                var loaded = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(AtomicFile.ReadAllText(path));
                if (loaded != null)
                    foreach (var pair in loaded)
                        if (pair.Value is { Count: > 0 }) entries.TryAdd(pair.Key, pair.Value.TakeLast(EntriesPerContainer).ToList());
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { }
    }

    public bool Has(string id) => entries.TryGetValue(id, out var states) && states is { Count: > 0 };

    public void Remember(Container model)
    {
        var json = JsonSerializer.Serialize(model);
        if (json.Length > 64_000) return;
        if (!entries.TryGetValue(model.Id, out var states) || states == null) states = [];
        if (states.Count > 0 && states[^1] == json) return;
        // Rebuild rather than remove/reinsert: Dictionary can reuse the old slot.
        // Serialized enumeration order stays oldest-to-newest with a fresh map.
        var ordered = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in entries)
            if (!string.Equals(pair.Key, model.Id, StringComparison.OrdinalIgnoreCase)) ordered.Add(pair.Key, pair.Value);
        ordered.Add(model.Id, states);
        entries = ordered;
        states.Add(json);
        while (states.Count > EntriesPerContainer) states.RemoveAt(0);
        dirty = true;
    }

    public Container? Peek(string id)
    {
        if (!Has(id)) return null;
        var states = entries[id];
        while (states.Count > 0)
        {
            var value = states[^1];
            try
            {
                var model = JsonSerializer.Deserialize<Container>(value);
                if (model != null && string.Equals(model.Id, id, StringComparison.OrdinalIgnoreCase)) return model;
            }
            catch (Exception ex) when (ex is JsonException or ArgumentNullException) { }
            states.RemoveAt(states.Count - 1); dirty = true;
        }
        return null;
    }

    public void Consume(string id)
    {
        if (!Has(id)) return;
        var states = entries[id];
        states.RemoveAt(states.Count - 1);
        dirty = true;
    }

    public void Save()
    {
        if (!dirty) return;
        // Keep newest history records within a hard cap to reduce SSD writes.
        foreach (var key in entries.Keys.ToList())
        {
            var items = entries[key];
            if (items == null || items.Count == 0) entries.Remove(key);
        }
        foreach (var key in entries.Keys.Take(Math.Max(0, entries.Count - 50)).ToArray()) entries.Remove(key);
        var json = JsonSerializer.Serialize(entries);
        while (System.Text.Encoding.UTF8.GetByteCount(json) > MaxFileBytes && entries.Count > 0)
        {
            entries.Remove(entries.Keys.First());
            json = JsonSerializer.Serialize(entries);
        }
        try { AtomicFile.WriteAllText(path, json); dirty = false; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
