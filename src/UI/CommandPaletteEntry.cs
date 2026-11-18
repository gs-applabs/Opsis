using System.Text;

namespace MotionDesk.UI;

internal sealed class CommandPaletteEntry
{
    public string Id { get; }
    public string Label { get; }
    public string? ShortcutPath { get; }
    private string SearchText { get; }
    private string Initials { get; }

    public CommandPaletteEntry(string id, string label, string? shortcutPath = null)
    {
        Id = id;
        Label = label;
        ShortcutPath = shortcutPath;
        SearchText = PageSearch.Normalize(label);
        Initials = GetInitials(SearchText);
    }

    public override string ToString() => Label;

    public static IEnumerable<CommandPaletteEntry> Find(IEnumerable<CommandPaletteEntry> entries, string query)
    {
        // Normalize whitespace once, so pasted searches and queries with extra spaces rank the same.
        var words = PageSearch.Normalize(query).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return entries;

        var phrase = string.Join(' ', words);
        var compact = string.Concat(words);
        return entries.Select(entry => (Entry: entry, Rank: entry.GetRank(phrase, compact, words)))
            .Where(match => match.Rank >= 0)
            .OrderBy(match => match.Rank)
            .Select(match => match.Entry);
    }

    private int GetRank(string phrase, string compact, string[] words)
    {
        if (SearchText.Equals(phrase, StringComparison.Ordinal)) return 0;
        if (SearchText.StartsWith(phrase, StringComparison.Ordinal)) return 1;
        if (SearchText.Contains(phrase, StringComparison.Ordinal)) return 2;
        if (Initials.Equals(compact, StringComparison.Ordinal)) return 3;
        if (words.All(word => SearchText.Contains(word, StringComparison.Ordinal))) return 4;
        if (Initials.StartsWith(compact, StringComparison.Ordinal)) return 5;
        return IsSubsequence(compact, SearchText) ? 6 : -1;
    }

    private static string GetInitials(string text)
    {
        var initials = new StringBuilder();
        bool inWord = false;
        foreach (char c in text)
        {
            bool isWord = char.IsLetterOrDigit(c);
            if (isWord && !inWord) initials.Append(c);
            inWord = isWord;
        }
        return initials.ToString();
    }

    private static bool IsSubsequence(string query, string text)
    {
        int next = 0;
        foreach (char c in text)
        {
            if (c == query[next] && ++next == query.Length) return true;
        }
        return false;
    }
}
