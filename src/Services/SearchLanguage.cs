using System.Globalization;
using System.Text;

namespace MotionDesk.Services;

/// <summary>
/// Shared, culture-neutral search for Greek and Latin labels. Recognises Greeklish
/// spellings without sending search terms to a network service or storing history.
/// This helper is intentionally pure: navigation, launcher and help reuse one policy.
/// </summary>
internal static class SearchLanguage
{
    private static readonly (string Greek, string Latin)[] Digraphs =
    {
        ("ου", "ou"), ("αι", "ai"), ("ει", "ei"), ("οι", "oi"),
        ("αυ", "av"), ("ευ", "ev"), ("ηυ", "iv"),
        ("μπ", "mp"), ("ντ", "nt"), ("γκ", "gk"), ("γγ", "ng"),
        ("τσ", "ts"), ("τζ", "tz")
    };

    private static readonly Dictionary<char, string> GreekChars = new()
    {
        ['α']="a",['β']="v",['γ']="g",['δ']="d",['ε']="e",['ζ']="z",
        ['η']="i",['θ']="th",['ι']="i",['κ']="k",['λ']="l",['μ']="m",
        ['ν']="n",['ξ']="x",['ο']="o",['π']="p",['ρ']="r",['σ']="s",
        ['ς']="s",['τ']="t",['υ']="y",['φ']="f",['χ']="ch",['ψ']="ps",['ω']="o"
    };

    // Search-only aliases, not user-visible translations. Additions must not change
    // existing UI copy and never trigger a network request.
    private static readonly string[][] Aliases =
    {
        ["ρυθμισεις", "settings", "config", "preferences"],
        ["καιρος", "weather", "meteo"],
        ["βοηθεια", "help", "manual", "οδηγιες"],
        ["εφαρμογες", "apps", "applications"],
        ["ιστορικο", "history", "timeline", "χρονολογιο"],
        ["ειδησεις", "news", "crawler"],
        ["προσθετα", "plugins", "extensions"],
        ["οθονη", "screen", "display", "monitor"],
        ["φοντο", "wallpaper", "background"],
        ["μουσικη", "music", "audio"],
        ["ζωνες", "zones", "snap"],
        ["εμφανιση", "appearance", "theme", "skin"],
        ["αναζητηση", "search", "find"],
        ["συντομευσεις", "shortcuts", "hotkeys"],
        ["προφιλ", "profile", "workspace"]
    };

    public static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        string decomposed = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (char c in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(char.ToLowerInvariant(c));
        return sb.ToString().Replace("&", "");
    }

    public static string ToGreeklish(string? text)
    {
        string normalized = Normalize(text);
        var sb = new StringBuilder(normalized.Length * 2);
        for (int i = 0; i < normalized.Length;)
        {
            bool matched = false;
            foreach (var (greek, latin) in Digraphs)
            {
                if (i + greek.Length <= normalized.Length &&
                    string.CompareOrdinal(normalized, i, greek, 0, greek.Length) == 0)
                {
                    sb.Append(latin); i += greek.Length; matched = true; break;
                }
            }
            if (matched) continue;
            char c = normalized[i++];
            sb.Append(GreekChars.TryGetValue(c, out string? latinChar) ? latinChar : c.ToString());
        }
        return sb.ToString();
    }

    private static IEnumerable<string> Alternatives(string word)
    {
        yield return word;
        foreach (string[] group in Aliases)
            if (group.Any(x => Normalize(x) == word || ToGreeklish(x) == word))
                foreach (string value in group) yield return Normalize(value);
    }

    public static bool Matches(string? content, string? query)
    {
        string text = Normalize(content);
        string roman = ToGreeklish(text);
        foreach (string word in Normalize(query).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!Alternatives(word).Any(w => text.Contains(w, StringComparison.Ordinal) ||
                roman.Contains(ToGreeklish(w), StringComparison.Ordinal))) return false;
        }
        return true;
    }

    public static int Score(string? content, string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return 0;
        string text = Normalize(content), term = Normalize(query);
        int exact = text.IndexOf(term, StringComparison.Ordinal);
        if (exact >= 0) return Math.Max(40, 110 - exact);
        string roman = ToGreeklish(text);
        int transliterated = roman.IndexOf(ToGreeklish(term), StringComparison.Ordinal);
        if (transliterated >= 0) return Math.Max(20, 85 - transliterated);
        return Matches(content, query) ? 15 : -100_000;
    }
}
