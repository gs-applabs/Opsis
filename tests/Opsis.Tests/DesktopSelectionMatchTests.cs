using MotionDesk.Widgets;
using Xunit;

namespace Opsis.Tests;

public sealed class DesktopSelectionMatchTests
{
    private static IReadOnlyList<string> Dir(params string[] n) => n.Select(x => @"C:\D\" + x).ToList();

    [Fact]
    public void NamesWithoutExtensionMatchShortcutsAndFiles()
    {
        var r = DesktopIconVisibilityEngine.MatchDisplayNames(new[] { "Notepad", "report.txt" }, new[] { Dir("Notepad.lnk", "report.txt", "other.docx") });
        Assert.Equal(new[] { @"C:\D\Notepad.lnk", @"C:\D\report.txt" }, r);
    }

    [Fact]
    public void ExactNameBeatsExtensionlessMatchAndUserDesktopBeatsPublic()
    {
        var user = Dir("a", "a.lnk"); var pub = new List<string> { @"C:\P\b.lnk", @"C:\P\a.lnk" };
        var r = DesktopIconVisibilityEngine.MatchDisplayNames(new[] { "a", "b" }, new IReadOnlyList<string>[] { user, pub });
        Assert.Equal(new[] { @"C:\D\a", @"C:\P\b.lnk" }, r);
    }

    [Fact]
    public void DuplicatesUnknownAndDesktopIniAreDropped()
    {
        var r = DesktopIconVisibilityEngine.MatchDisplayNames(new[] { "x", "x", "ghost", "", "desktop.ini", "desktop" }, new[] { Dir("x.lnk", "desktop.ini") });
        Assert.Equal(new[] { @"C:\D\x.lnk" }, r);
    }

    [Fact]
    public void FewerThanTwoSelectedIconsNeverMakeAnOffer()
    {
        Assert.Empty(DesktopIconVisibilityEngine.MatchDisplayNames(Array.Empty<string>(), new[] { Dir("a") }));
    }
}
