using MotionDesk.Widgets;
using Xunit;

namespace Opsis.Tests;

public sealed class FamilyAutoPinTests
{
    private static FamilyApp A(string id) => new(id, id, id, "", Array.Empty<string>(), "me");

    [Fact]
    public void InstalledFamilyAppsArePinnedOnceAndOpsisItselfIsSkipped()
    {
        var apps = new[] { A("opsis"), A("player"), A("crawler"), A("missing") };
        var installed = new Dictionary<string, string> { ["opsis"] = @"C:\o\Opsis.exe", ["player"] = @"C:\p\Player.exe", ["crawler"] = @"C:\c\Crawler.exe" };
        var offered = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "crawler" };       // the user already unpinned it earlier
        var found = FamilyApp.AutoPinCandidates(apps, offered, a => installed.GetValueOrDefault(a.Id));
        Assert.Single(found);
        Assert.Equal(("player", @"C:\p\Player.exe"), found[0]);
    }
}
