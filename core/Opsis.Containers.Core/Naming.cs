using System.Text.Json;

namespace Opsis.Containers;

/// <summary>Rules for renaming a container (inline editor: Enter saves, Esc cancels).</summary>
public static class ContainerNaming
{
    public const int MaxLength = 60;

    /// <summary>
    /// Returns the name to store, or null when the edit must be rejected (empty / only blanks / unchanged).
    /// Control characters are removed, the text is trimmed and cut to <see cref="MaxLength"/>. A name another container already uses (case-insensitive)
    /// is accepted but made unique with " (2)", " (3)", ... so the list and the title bars stay distinguishable.
    /// </summary>
    public static string? Normalize(string? typed, string current, IEnumerable<string> otherTitles)
    {
        if (typed == null) return null;
        var clean = new string(typed.Where(ch => !char.IsControl(ch)).ToArray()).Trim();
        if (clean.Length == 0) return null;
        if (clean.Length > MaxLength) clean = clean[..MaxLength].TrimEnd();
        if (string.Equals(clean, current, StringComparison.Ordinal)) return null;
        var used = new HashSet<string>(otherTitles, StringComparer.OrdinalIgnoreCase);
        if (!used.Contains(clean)) return clean;
        for (int n = 2; n < 1000; n++) { var cand = clean + " (" + n + ")"; if (!used.Contains(cand)) return cand; }
        return clean;
    }
}

/// <summary>Document-level rename used by the host (and tests): true when the stored title changed.</summary>
public static class ContainerRename
{
    public static bool Apply(ContainersDocument doc, string id, string? typed)
    {
        var c = doc.Containers.FirstOrDefault(x => x.Id == id); if (c == null) return false;
        var name = ContainerNaming.Normalize(typed, c.Title, doc.Containers.Where(x => x.Id != id).Select(x => x.Title));
        if (name == null) return false;
        c.Title = name; return true;
    }
}

/// <summary>A stable key for everything a window paints from the stored container (title, lock, title bar, appearance). Equal keys mean a repaint would change nothing.</summary>
public static class ContainerStyleKey
{
    public static string Of(Container c) => JsonSerializer.Serialize(new { c.Title, c.Locked, c.TitleBar, c.Appearance }, ContainersStore.Json);
}
