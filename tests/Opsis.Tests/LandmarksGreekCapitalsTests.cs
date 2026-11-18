using System.Diagnostics;
using System.Text.RegularExpressions;
using MotionDesk.Widgets;
using Xunit;

namespace Opsis.Tests;

/// <summary>One landmark for EVERY Greek prefecture capital (user order 2026-11-18): photo where an allowed licence exists, otherwise our own silhouette.</summary>
public sealed class LandmarksGreekCapitalsTests
{
    // the classic prefecture (nomos) capitals; Athens and Piraeus count separately
    private static readonly string[] Capitals =
    {
        "Athens", "Piraeus", "Chalkida", "Karpenisi", "Lamia", "Amfissa", "Livadeia", "Agrinio", "Patras", "Pyrgos", "Nafplio", "Tripoli", "Corinth", "Sparta", "Kalamata",
        "Arta", "Igoumenitsa", "Ioannina", "Preveza", "Karditsa", "Larissa", "Volos", "Trikala", "Grevena", "Kastoria", "Kozani", "Florina", "Veria", "Thessaloniki", "Kilkis",
        "Edessa", "Katerini", "Serres", "Polygyros", "Drama", "Kavala", "Alexandroupoli", "Xanthi", "Komotini", "Zakynthos", "Corfu", "Argostoli", "Lefkada",
        "Heraklion", "Agios Nikolaos", "Rethymno", "Chania", "Rhodes", "Ermoupoli", "Mytilene", "Samos", "Chios",
    };

    private static string Root()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "MotionDeskStudio.csproj"))) d = d.Parent;
        return d!.FullName;
    }
    private static string Js() => File.ReadAllText(Path.Combine(Root(), "src", "Widgets", "wallpaper", "landmarks.js"));

    [Fact]
    public void EveryPrefectureCapitalHasExactlyOneLandmark()
    {
        Assert.Equal(52, Capitals.Length);
        foreach (var c in Capitals) Assert.Single(LandmarkCatalog.Items, e => e.Capital == c);
        Assert.Equal(Capitals.Length + 1, LandmarkCatalog.Items.Length);                 // + the Rio-Antirrio bridge (extra of Patras)
        Assert.Equal(LandmarkCatalog.Items.Length, LandmarkCatalog.Items.Select(e => e.Id).Distinct().Count());
        Assert.All(LandmarkCatalog.Items, e => { Assert.Contains(e.Region, LandmarkCatalog.Regions); Assert.False(string.IsNullOrWhiteSpace(e.NameEl)); Assert.False(string.IsNullOrWhiteSpace(e.CapitalEl)); });
        Assert.All(LandmarkCatalog.Regions, r => Assert.Contains(LandmarkCatalog.Items, e => e.Region == r));
    }

    [Fact]
    public void TheScriptListsTheSameLandmarksAsTheCatalogEachWithAScene()
    {
        var js = Js();
        var info = Regex.Matches(js.Substring(js.IndexOf("const INFO = {", StringComparison.Ordinal)), @"^\s+([a-z]+): \{ city:", RegexOptions.Multiline).Select(m => m.Groups[1].Value).Take(LandmarkCatalog.Items.Length).ToList();
        Assert.Equal(LandmarkCatalog.Items.Select(e => e.Id).OrderBy(x => x), info.OrderBy(x => x));
        var scene = js.Substring(js.IndexOf("const SCENE = {", StringComparison.Ordinal)); scene = scene.Substring(0, scene.IndexOf("};", StringComparison.Ordinal));
        foreach (var e in LandmarkCatalog.Items) Assert.Matches(@"\b" + e.Id + @": \{ scene: '(sea|land|photo)'", scene);
    }

    [Fact]
    public void EveryLandmarkIsAPhotoOrHasItsOwnSilhouetteAndPhotosHaveAllowedLicences()
    {
        var js = Js();
        var sprites = Regex.Match(js, @"SPRITE_IDS = \[([^\]]+)\]").Groups[1].Value.Split(',').Select(x => x.Trim().Trim('\'')).ToHashSet();
        foreach (var e in LandmarkCatalog.Items)
        {
            if (sprites.Contains(e.Id))
            {
                var f = new FileInfo(Path.Combine(Root(), "src", "Widgets", "wallpaper", "landmarks", e.Id + ".webp"));
                Assert.True(f.Exists, e.Id);
                Assert.True(f.Length <= 400 * 1024, e.Id + ".webp is larger than 400 KB");
            }
            // every landmark (photo or not) keeps its own silhouette as the fallback, so a missing or removed photo never leaves a gap
            Assert.True(Regex.IsMatch(js, @"L\('" + e.Id + @"', '[^']+', -?[\d.]+, -?[\d.]+, '[a-z]+', "), "no silhouette for " + e.Id);
        }
        var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(Root(), "docs", "LANDMARK-IMAGES.json"))).RootElement;
        var md = File.ReadAllText(Path.Combine(Root(), "docs", "LANDMARK-IMAGES.md"));
        var listed = new HashSet<string>();
        foreach (var p in json.EnumerateArray())
        {
            var lic = p.GetProperty("license").GetString()!;
            listed.Add(p.GetProperty("file").GetString()!);
            Assert.Matches(@"^(Public domain|CC0|CC BY(-SA)? (2\.0|3\.0|4\.0))$", lic);                  // never -NC / -ND / unknown / fair use
            Assert.StartsWith("https://commons.wikimedia.org/wiki/File:", p.GetProperty("commonsUrl").GetString());   // file page = source
            Assert.False(string.IsNullOrWhiteSpace(p.GetProperty("author").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(p.GetProperty("changes").GetString()));
            Assert.Contains(p.GetProperty("file").GetString()!, md);                                   // every photo is in the docs table
            if (lic.StartsWith("CC BY"))
            {
                Assert.True(p.GetProperty("attributionRequired").GetBoolean());                          // CC BY and CC BY-SA need attribution
                Assert.StartsWith("https://creativecommons.org/licenses/by", p.GetProperty("licenseUrl").GetString());
                Assert.False(string.IsNullOrWhiteSpace(p.GetProperty("attribution").GetString()));      // the credit line to show
                Assert.Equal(lic.StartsWith("CC BY-SA"), p.GetProperty("shareAlike").GetBoolean());
                if (lic.StartsWith("CC BY-SA")) Assert.Contains("CC BY-SA", p.GetProperty("attribution").GetString());
            }
        }
        foreach (var id in sprites) Assert.Contains(id + ".webp", listed);                              // every sprite has a licence record
        Assert.Contains("released under the same CC BY-SA licence", md);
    }

    [Fact]
    public void EverySilhouetteParsesAndRenders()
    {
        // run the real script in node when it is installed: every drawn landmark must have finite polygons with at least 3 points and several parts
        string? node = new[] { @"C:\Program Files\nodejs\node.exe" }.FirstOrDefault(File.Exists);
        if (node == null) return;
        var script = "const L=require(process.argv[1]);let n=0,bad=[];for(const m of L.LANDMARKS){n++;if(m.parts.length<2)bad.push(m.id+':parts');for(const p of m.parts){if(p.p.length<3||p.p.some(q=>!isFinite(q[0])||!isFinite(q[1])||q[1]<-0.01||q[1]>1.7))bad.push(m.id+':pts');}}console.log(n+' '+bad.join(','));";
        var psi = new ProcessStartInfo(node) { RedirectStandardOutput = true, UseShellExecute = false };
        psi.ArgumentList.Add("-e"); psi.ArgumentList.Add(script); psi.ArgumentList.Add(Path.Combine(Root(), "src", "Widgets", "wallpaper", "landmarks.js"));
        using var pr = Process.Start(psi)!; var o = pr.StandardOutput.ReadToEnd().Trim(); pr.WaitForExit();
        var parts = o.Split(' ', 2);
        Assert.True(int.Parse(parts[0]) >= LandmarkCatalog.Items.Length, o);                            // photos included: all 53 keep a silhouette fallback
        Assert.True(parts.Length < 2 || parts[1].Length == 0, "bad silhouettes: " + o);
    }

    [Fact]
    public void NearestLandmarkAndChoiceValuesWork()
    {
        Assert.Equal("heraklion", LandmarkCatalog.Nearest(35.33, 25.13).Id);
        Assert.Equal("whitetower", LandmarkCatalog.Nearest(40.64, 22.94).Id);
        Assert.True(LandmarkCatalog.IsValid("Random") && LandmarkCatalog.IsValid("Auto") && LandmarkCatalog.IsValid("Off") && LandmarkCatalog.IsValid("rhodes") && !LandmarkCatalog.IsValid("../x"));
        var weather = File.ReadAllText(Path.Combine(Root(), "src", "Widgets", "wallpaper", "weather.js"));
        Assert.Contains("randomLandmark", weather);
    }

    [Fact]
    public void SelectorStringsExistInAll14Languages()
    {
        var keys = new[] { "Wallpaper.LandmarkRandom", "Wallpaper.LandmarkRegion", "Wallpaper.LandmarkRegionAll" }.Concat(LandmarkCatalog.Regions.Select(r => "Wallpaper.Region." + r)).ToList();
        var files = Directory.GetFiles(Path.Combine(Root(), "locales"), "*.json");
        Assert.Equal(14, files.Length);
        foreach (var f in files)
        {
            var d = System.Text.Json.JsonDocument.Parse(File.ReadAllText(f)).RootElement;
            foreach (var k in keys) Assert.True(d.TryGetProperty(k, out var v) && !string.IsNullOrWhiteSpace(v.GetString()), f + " " + k);
        }
    }
}
