using System.Text.Json;
using MotionDesk.Widgets;
using Xunit;

namespace Opsis.Tests;

// 2.6.0 user-test fixes: real family icons, Help/About single sources.
public sealed class SuiteUiFixesTests
{
    private static string Root()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "MotionDeskStudio.csproj"))) d = d.Parent;
        return d!.FullName;
    }

    [Fact]
    public void EveryFamilyAppHasAnEmbeddedIcon()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root(), "suite", "apps.json")));
        foreach (var a in doc.RootElement.GetProperty("apps").EnumerateArray())
        {
            string id = a.GetProperty("id").GetString()!;
            Assert.True(FamilyIcons.Exists(id), "missing embedded icon for " + id);
        }
    }

    [Fact]
    public void HelpShortcutsUseTheAboutCardAndAboutHasNoHistoryButton()
    {
        string help = File.ReadAllText(Path.Combine(Root(), "src", "UI", "HelpForm.cs"));
        Assert.Contains("ShortcutsCard", help);
        string main = File.ReadAllText(Path.Combine(Root(), "src", "UI", "MainWindow.cs"));
        Assert.DoesNotContain("T(\"VersionHistory.Button\")", main);
    }
}
