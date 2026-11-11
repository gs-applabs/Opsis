namespace Opsis.Containers;

/// <summary>What a drop onto (or drag out of) a container does. Items in a container are REFERENCES to files: dropping never copies or moves the file itself.</summary>
public enum DropEffect { None, AddReference, Reorder, RemoveFromContainer }

public sealed record DropPlan(DropEffect Effect, List<ContainerItem> Add, int InsertAt, List<string> Skipped);

/// <summary>Decides a drop as pure logic (phase 3): which paths become items, where, which are skipped (duplicates, empty, the container's own backing folder).</summary>
public static class DropRules
{
    private static readonly char[] Seps = { '\\', '/' };

    public static DropPlan Plan(IEnumerable<string> droppedPaths, IReadOnlyList<ContainerItem> existing, int insertIndex, Func<string, bool>? isFolder = null, string? forbiddenRoot = null)
    {
        var add = new List<ContainerItem>(); var skipped = new List<string>();
        var known = new HashSet<string>(existing.Select(i => Normalize(i.Path)), StringComparer.OrdinalIgnoreCase);
        foreach (var raw in droppedPaths)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var p = Normalize(raw);
            if (forbiddenRoot != null && p.StartsWith(Normalize(forbiddenRoot), StringComparison.OrdinalIgnoreCase)) { skipped.Add(raw + " (inside the container's own folder)"); continue; }
            if (!known.Add(p)) { skipped.Add(raw + " (already in the container)"); continue; }
            add.Add(new ContainerItem { Path = raw, DisplayName = System.IO.Path.GetFileNameWithoutExtension(raw.TrimEnd(Seps)), IsFolder = isFolder?.Invoke(raw) ?? false });
        }
        var at = Math.Clamp(insertIndex, 0, existing.Count);
        return new DropPlan(add.Count == 0 ? DropEffect.None : DropEffect.AddReference, add, at, skipped);
    }

    /// <summary>Moves item <paramref name="from"/> so that it ends up before the original item at <paramref name="insertBefore"/> (drag to reorder inside one container).</summary>
    public static List<ContainerItem> Reorder(IReadOnlyList<ContainerItem> items, int from, int insertBefore)
    {
        var list = items.ToList();
        if (from < 0 || from >= list.Count) return list;
        var item = list[from]; list.RemoveAt(from);
        var at = insertBefore > from ? insertBefore - 1 : insertBefore;
        list.Insert(Math.Clamp(at, 0, list.Count), item);
        return list;
    }

    private static string Normalize(string p)
    {
        try { return System.IO.Path.GetFullPath(p.Trim().Trim('"')).TrimEnd(Seps); }
        catch (Exception) { return p.Trim().TrimEnd(Seps); }
    }
}
