using System.Text.Json;
using MotionDesk.Widgets;
using Xunit;

namespace Opsis.Tests;

public sealed class VisualizerConfigTests
{
    private static string Root()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "src", "Widgets", "wallpaper"))) d = d.Parent;
        return d?.FullName ?? @"C:\w\Opsis";
    }

    [Fact]
    public void OffByDefaultAndDefaultsMatchDocumentedValues()
    {
        var c = new VisualizerConfig();
        Assert.False(c.Enabled);
        Assert.True(c.Rounded); Assert.Equal(60, c.Intensity); Assert.Equal(45, c.AddLevel); Assert.Equal(25, c.LineWidth);
        Assert.Equal(52, c.PosY); Assert.Equal(54, c.ClockY); Assert.Equal(34, c.DateY);
    }

    [Fact]
    public void PageObjectClampsEveryNumber()
    {
        var c = new VisualizerConfig { Intensity = 5000, MinLevel = -4, AddLevel = 99999, LineWidth = -1, RayAngle = 500, TrebleBoost = 101, PosX = -9, PosY = 300, ClockSize = 777, DateSize = -3, ShadowIntensity = 9999 };
        var o = c.ToPageObject();
        Assert.Equal(99, o["intensity"]); Assert.Equal(0, o["minLevel"]); Assert.Equal(500, o["addLevel"]); Assert.Equal(0, o["lineWidth"]);
        Assert.Equal(100, o["rayAngle"]); Assert.Equal(100, o["treble"]); Assert.Equal(0, o["x"]); Assert.Equal(100, o["y"]);
        Assert.Equal(100, o["clockSize"]); Assert.Equal(0, o["dateSize"]); Assert.Equal(500, o["shadowIntensity"]);
        JsonSerializer.Serialize(o); // must serialise
    }

    [Theory]
    [InlineData("red; background:url(x)", "#123456")]
    [InlineData("#12345G", "#123456")]
    [InlineData("#abc", "#abc")]
    [InlineData("#AABBCC", "#AABBCC")]
    public void ColoursAreValidatedSoNoCssCanBeInjected(string input, string expected) =>
        Assert.Equal(expected, VisualizerConfig.SafeColor(input, "#123456"));

    [Fact]
    public void FontNameIsStrippedOfQuotesAndMarkup() =>
        Assert.Equal("Evil bfont", VisualizerConfig.SafeFont("Evil\"; <b>font"));

    [Fact]
    public void RoundTripsThroughWallpaperSettingsJson()
    {
        var s = new WallpaperSettings(); s.Visualizer.Enabled = true; s.Visualizer.Color1 = "#112233";
        var back = JsonSerializer.Deserialize<WallpaperSettings>(JsonSerializer.Serialize(s))!;
        Assert.True(back.Visualizer.Enabled); Assert.Equal("#112233", back.Visualizer.Color1);
        Assert.NotNull(JsonSerializer.Deserialize<WallpaperSettings>("{}")!.Visualizer);   // old settings files have no Visualizer
    }

    [Fact]
    public void PageWiringAndLifecycleAreInPlace()
    {
        string w = Path.Combine(Root(), "src", "Widgets", "wallpaper");
        string html = File.ReadAllText(Path.Combine(w, "index.html")), el = File.ReadAllText(Path.Combine(w, "elements.js")), js = File.ReadAllText(Path.Combine(w, "visualizer.js"));
        Assert.Contains("visualizer.js", html);
        Assert.Contains("opsisApplyViz", el); Assert.Contains("opsisPauseViz", el);
        Assert.Contains("requestAnimationFrame", js); Assert.Contains("StopAudioBands", js);
        Assert.Contains("'Battery'", js);            // battery-saver stops the rays
        Assert.Contains("visibilitychange", js);     // hidden page stops the loop
        Assert.DoesNotContain("setInterval(frame", js);
    }
}
