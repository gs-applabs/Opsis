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
        Assert.Contains("window.OpsisLandmarks", File.ReadAllText(Path.Combine(Wall(), "weather.js")));
        Assert.Contains("weatherLandmarks", html);
    }

    [Fact]
    public void EveryLandmarkHasAKnownPlaceAndPolygons()
    {
        var js = File.ReadAllText(Path.Combine(Wall(), "landmarks.js"));
        var count = System.Text.RegularExpressions.Regex.Matches(js, @"L\('[a-z]+', '[^']+', -?\d+\.\d+, -?\d+\.\d+, '[a-z]+', \[").Count;
        Assert.True(count >= 2, "vector landmarks found: " + count);     // Greek landmarks only (2.6.0): Parthenon and the White Tower keep a vector fallback
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
            var png = Path.Combine(Wall(), "landmarks", id + ".webp");
            Assert.True(File.Exists(png), id + ".webp missing");
            Assert.True(new FileInfo(png).Length <= 400 * 1024, id + ".webp is too big for a wallpaper asset (max 400 KB)");
            Assert.Contains(id + ".webp", credits);
        }
        // licence manifest (plan 0C.2 #9): per image licence in the allowed list, a Commons URL, an author and a check date
        var manifest = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(Wall(), "landmarks", "manifest.json"))).RootElement;
        Assert.Equal(ids.Count, manifest.GetArrayLength());
        long total = 0;
        foreach (var e in manifest.EnumerateArray())
        {
            var lic = e.GetProperty("license").GetString()!;
            Assert.Matches(@"^(Public domain|CC0|CC BY(-SA)? [0-9.]+)$", lic);                        // never -NC, -ND (user decision 2026-11-18: CC BY-SA allowed with the attribution recorded)
            Assert.StartsWith("https://commons.wikimedia.org/wiki/File:", e.GetProperty("commonsUrl").GetString());
            Assert.False(string.IsNullOrWhiteSpace(e.GetProperty("author").GetString()));
            Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", e.GetProperty("checkedOn").GetString()!);
            if (lic.StartsWith("CC BY")) Assert.True(e.GetProperty("attributionRequired").GetBoolean());
            total += new FileInfo(Path.Combine(Wall(), "landmarks", e.GetProperty("file").GetString()!)).Length;
        }
        Assert.True(total < 2_000_000,"all landmark sprites together must stay small: " + total);
        Assert.True(File.Exists(Path.Combine(Wall(), "..", "..", "..", "docs", "LANDMARK-IMAGES.json")));
    }

    [Fact]
    public void TheChoiceListMatchesTheSpritesOfTheScript()
    {
        var js = File.ReadAllText(Path.Combine(Wall(), "landmarks.js"));
        var ids = System.Text.RegularExpressions.Regex.Match(js, @"SPRITE_IDS = \[([^\]]+)\]").Groups[1].Value.Split(',').Select(x => x.Trim().Trim('\'')).OrderBy(x => x).ToList();
        Assert.All(ids, id => Assert.Contains(id, MotionDesk.Widgets.LandmarkCatalog.Items.Select(e => e.Id)));          // the photographs are a subset of the catalog (the rest are silhouettes, see LandmarksGreekCapitalsTests)
        Assert.All(ids, id => Assert.Contains("'" + id + "': {", js.Substring(js.IndexOf("const SCENE", StringComparison.Ordinal)).Replace(id + ": {", "'" + id + "': {")));   // every landmark has a scene (land or sea)
        Assert.True(MotionDesk.Widgets.LandmarkCatalog.IsValid("Auto") && MotionDesk.Widgets.LandmarkCatalog.IsValid("Off") && !MotionDesk.Widgets.LandmarkCatalog.IsValid("../evil"));
    }

    [Fact]
    public void WallpaperElementsFollowTheBackgroundColourWhenThereIsNoCard()
    {
        var js = File.ReadAllText(Path.Combine(Wall(), "elements.js"));
        Assert.Contains("function autoContrast", js);
        Assert.Contains("c.elAuto !== false", js);                   // on by default, only without the frosted card (!c.elCard)
        Assert.Contains("!c.elCard", js);
        Assert.Contains("var(--fg,#fff)", js);
        var cs = File.ReadAllText(Path.Combine(Wall(), "..", "WallpaperWindow.cs"));
        Assert.Contains("elAuto = settings.ElementsAutoColor", cs);
    }

    [Fact]
    public void ScenePhotosHaveAnAllowedLicenceAndStaySmall()
    {
        var dir = Path.Combine(Wall(), "scenes");
        var manifest = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "manifest.json"))).RootElement;
        long total = 0;
        foreach (var e in manifest.EnumerateArray())
        {
            Assert.Matches(@"^(Public domain|CC0|CC BY [0-9.]+)$", e.GetProperty("license").GetString()!);
            Assert.StartsWith("https://commons.wikimedia.org/wiki/File:", e.GetProperty("commonsUrl").GetString());
            total += new FileInfo(Path.Combine(dir, e.GetProperty("file").GetString()!)).Length + new FileInfo(Path.Combine(dir, e.GetProperty("lights").GetString()!)).Length;
        }
        Assert.True(total < 1_000_000, "scene photos together: " + total);
        Assert.Contains("scenes/acropolis.webp", File.ReadAllText(Path.Combine(Wall(), "landmarks.js")));
    }
}

