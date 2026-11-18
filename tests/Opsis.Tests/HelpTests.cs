using System.Text.Json;
using System.Text.RegularExpressions;
using MotionDesk.Services;
using MotionDesk.UI;
using Xunit;

namespace Opsis.Tests;

// Δ10: the in-app Help (F1): every text exists in all 14 languages, the version history is newest-first and agrees with the CHANGELOG and the csproj.
public sealed class HelpTests
{
    private static string Root()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "MotionDeskStudio.csproj"))) d = d.Parent;
        return d!.FullName;
    }
    private static Dictionary<string, Dictionary<string, string>> Locales() =>
        Directory.GetFiles(Path.Combine(Root(), "locales"), "*.json").ToDictionary(f => Path.GetFileNameWithoutExtension(f), f => JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(f))!);

    [Fact]
    public void EveryHelpTextExistsInAll14Languages()
    {
        var keys = new List<string> { "Help.Title", "Help.Tooltip", "Help.TabGuide", "Help.TabPages", "Help.TabShortcuts", "Help.TabRestore", "Help.TabHistory", "Help.RestoreIntro", "Help.RestoreDesktopIcons", "Help.RestoreLockscreen", "Help.CurrentPage", "About.InstructionsList", "About.ShortcutsList" };
        foreach (var (_, t, i) in HelpForm.Pages) { keys.Add(t); keys.Add(i); }
        var all = Locales();
        Assert.Equal(14, all.Count);
        var missing = new List<string>();
        foreach (var (lang, map) in all) foreach (var k in keys) if (!map.TryGetValue(k, out var v) || string.IsNullOrWhiteSpace(v)) missing.Add(lang + ":" + k);
        Assert.True(missing.Count == 0, string.Join(", ", missing.Take(20)));
    }

    [Fact]
    public void RestoreCommandsAreListedInEveryLanguageAndExistInProgram()
    {
        var program = File.ReadAllText(Path.Combine(Root(), "src", "Program.cs"));
        foreach (var cmd in HelpForm.RestoreCommands) Assert.Contains("\"" + cmd + "\"", program);
        foreach (var (lang, map) in Locales())
        {
            Assert.Contains("--restore-desktop-icons", map["Help.RestoreDesktopIcons"]);
            Assert.Contains("--restore-lockscreen", map["Help.RestoreLockscreen"]);
            Assert.Contains('\n', map["Help.RestoreDesktopIcons"]);   // command line first, explanation after it
        }
    }

    [Fact]
    public void VersionHistoryIsNewestFirstAndMatchesTheProject()
    {
        Assert.True(VersionCatalog.IsNewestFirst(VersionCatalog.All));
        Assert.False(VersionCatalog.IsNewestFirst(new[] { ("1.9.9", "x"), ("1.9.12", "x") }));   // 1.9.12 is newer than 1.9.9 (not a text sort)
        Assert.False(VersionCatalog.IsNewestFirst(new[] { ("2.4.6", "x"), ("2.4.6.1", "x") }));
        Assert.Equal(AppVersion.Short, VersionCatalog.All[0].Version);
        var el = Locales()["el-GR"];
        foreach (var (v, _) in VersionCatalog.All) Assert.True(el.ContainsKey("VersionNotes." + v), "no notes for " + v);
    }

    [Fact]
    public void ChangelogParsesAndIsNewestFirst()
    {
        var text = File.ReadAllText(Path.Combine(Root(), "CHANGELOG.md"));
        var versions = Regex.Matches(text, @"(?m)^## (\d+(?:\.\d+){2,3})").Select(m => Version.Parse(m.Groups[1].Value)).ToList();
        Assert.True(versions.Count > 5);
        for (int i = 1; i < versions.Count; i++) Assert.True(versions[i - 1] > versions[i], versions[i - 1] + " is not above " + versions[i]);
        Assert.Equal(AppVersion.Number.ToString(3), versions[0].ToString(3));
    }
}
