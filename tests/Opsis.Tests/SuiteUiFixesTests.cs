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

public sealed class WallpaperPageFixesTests
{
    [Fact]
    public void WheelPassthroughScrollsTheLiveHostNotTheDiscardedPanel()
    {
        var oldPanel = new System.Windows.Forms.Panel { AutoScroll = true };
        var live = new System.Windows.Forms.Panel { AutoScroll = true };
        var card = new System.Windows.Forms.Panel();
        var button = new System.Windows.Forms.Button();
        live.Controls.Add(card); card.Controls.Add(button);
        Assert.Same(live, MotionDesk.UI.MainWindow.LiveScrollHost(button, oldPanel));
        Assert.Same(oldPanel, MotionDesk.UI.MainWindow.LiveScrollHost(new System.Windows.Forms.Button(), oldPanel));
    }

    [Fact]
    public void MusicOverlayLivesOnTheWallpaperPageAndVisualizerIsSplitPerFunction()
    {
        string root = SuiteUiFixesTests_Root();
        string main = File.ReadAllText(Path.Combine(root, "src", "UI", "MainWindow.cs"));
        Assert.Contains("BuildMusicOverlaySection(panel);", main);
        Assert.DoesNotContain("music.AddControl", main);
        string viz = File.ReadAllText(Path.Combine(root, "src", "UI", "MainWindow.Visualizer.cs"));
        foreach (var hdr in new[] { "HdrColors", "HdrRays", "HdrTime", "HdrDate", "HdrShadow" }) Assert.Contains("Sub(\"" + hdr + "\")", viz);
    }

    private static string SuiteUiFixesTests_Root()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "MotionDeskStudio.csproj"))) d = d.Parent;
        return d!.FullName;
    }
}
