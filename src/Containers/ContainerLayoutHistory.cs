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
                entries = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(AtomicFile.ReadAllText(path))
                    ?? new(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { }
    }

    public bool Has(string id) => entries.TryGetValue(id, out var states) && states.Count > 0;

    public void Remember(Container model)
    {
        var json = JsonSerializer.Serialize(model);
        if (json.Length > 64_000) return;
        if (!entries.TryGetValue(model.Id, out var states)) entries[model.Id] = states = [];
        if (states.Count > 0 && states[^1] == json) return;
        states.Add(json);
        while (states.Count > EntriesPerContainer) states.RemoveAt(0);
        dirty = true;
    }

    public Container? Pop(string id)
    {
        if (!Has(id)) return null;
        var states = entries[id];
        while (states.Count > 0)
        {
            var value = states[^1]; states.RemoveAt(states.Count - 1); dirty = true;
            try { return JsonSerializer.Deserialize<Container>(value); }
            catch (JsonException) { }
        }
        return null;
    }

    public void Save()
    {
        if (!dirty) return;
        // Keep newest history records within a hard cap to reduce SSD writes.
        foreach (var key in entries.Keys.ToList())
        {
            var items = entries[key];
            if (items.Count == 0) entries.Remove(key);
        }
        foreach (var key in entries.Keys.Skip(50).ToArray()) entries.Remove(key);
        var json = JsonSerializer.Serialize(entries);
        if (json.Length > MaxFileBytes) return;
        try { AtomicFile.WriteAllText(path, json); dirty = false; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
