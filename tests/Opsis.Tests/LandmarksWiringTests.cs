using Xunit;

namespace Opsis.Tests;

public sealed class LandmarksWiringTests
{
    private static string Wall()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "MotionDeskStudio.csproj"))) d = d.Parent;
        return Path.Combine(d!.FullName, "src", "Widgets", "wallpaper");
    }

    [Fact]
    public void LandmarksScriptIsLoadedBeforeTheWeatherScriptAndUsedByIt()
    {
        var html = File.ReadAllText(Path.Combine(Wall(), "index.html"));
        Assert.True(html.IndexOf("landmarks.js", StringComparison.Ordinal) is var a && a > 0 && a < html.IndexOf("weather.js", StringComparison.Ordinal));
        Assert.Contains("OpsisLandmarks.pickLandmark", File.ReadAllText(Path.Combine(Wall(), "weather.js")));
        Assert.Contains("weatherLandmarks", html);
    }

    [Fact]
    public void EveryLandmarkHasAKnownPlaceAndPolygons()
    {
        var js = File.ReadAllText(Path.Combine(Wall(), "landmarks.js"));
        var count = System.Text.RegularExpressions.Regex.Matches(js, @"L\('[a-z]+', '[^']+', -?\d+\.\d+, -?\d+\.\d+, '[a-z]+', \[").Count;
        Assert.True(count >= 20, "landmarks found: " + count);
    }

    [Fact]
    public void EverySpriteHasAPhotoAndACreditLine()
    {
        var js = File.ReadAllText(Path.Combine(Wall(), "landmarks.js"));
        var ids = System.Text.RegularExpressions.Regex.Match(js, @"SPRITE_IDS = \[([^\]]+)\]").Groups[1].Value.Split(',').Select(x => x.Trim().Trim('\'')).ToList();
        Assert.True(ids.Count >= 4);
        var credits = File.ReadAllText(Path.Combine(Wall(), "landmarks", "ATTRIBUTION.md"));
        foreach (var id in ids)
        {
            var png = Path.Combine(Wall(), "landmarks", id + ".png");
            Assert.True(File.Exists(png), id + ".png missing");
            Assert.True(new FileInfo(png).Length < 1_000_000, id + ".png is too big for a wallpaper asset");
            Assert.Contains(id + ".png", credits);
        }
        Assert.DoesNotContain("-SA", credits.Replace("CC BY 2.0", ""), StringComparison.Ordinal);   // no share-alike material
    }}

