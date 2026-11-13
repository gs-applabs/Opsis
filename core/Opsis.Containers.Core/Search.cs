namespace Opsis.Containers;

public sealed record SearchHit(string ContainerId, string ContainerTitle, ContainerItem Item);

/// <summary>Spot Search (the old Ctrl+` search): finds items of every container by name, folders and files alike. Pure and tested; the popup only shows the hits.</summary>
public static class ContainerSearch
{
    public static List<SearchHit> Find(ContainersDocument doc, string text, int max = 12)
    {
        var result = new List<SearchHit>();
        var q = (text ?? "").Trim();
        if (q.Length == 0) return result;
        foreach (var c in doc.Containers.Where(c => c.Kind == ContainerKind.Data))
        {
            foreach (var it in c.Items.Concat(c.Tabs.SelectMany(t => t.Items)))
            {
                var name = string.IsNullOrEmpty(it.DisplayName) ? System.IO.Path.GetFileNameWithoutExtension(it.Path) : it.DisplayName;
                if (name.Contains(q, StringComparison.CurrentCultureIgnoreCase) || it.Path.Contains(q, StringComparison.CurrentCultureIgnoreCase)) result.Add(new SearchHit(c.Id, c.Title, it));
            }
        }
        // names that START with the text first, then the rest, each group alphabetical
        return result.OrderBy(h => !NameOf(h).StartsWith(q, StringComparison.CurrentCultureIgnoreCase)).ThenBy(h => NameOf(h), StringComparer.CurrentCultureIgnoreCase).Take(max).ToList();
    }

    private static string NameOf(SearchHit h) => string.IsNullOrEmpty(h.Item.DisplayName) ? System.IO.Path.GetFileNameWithoutExtension(h.Item.Path) : h.Item.DisplayName;
}
