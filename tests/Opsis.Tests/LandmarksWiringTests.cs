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
}
