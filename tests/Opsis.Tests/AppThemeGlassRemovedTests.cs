using System.Text.Json;
using MotionDesk.Services;
using Xunit;

namespace Opsis.Tests;

// Δ2: the APPLICATION Glass theme (Theme page / AppSettings.Glass / GlassHost) is gone; GPU glass of widgets / DeskStrip / containers stays.
public sealed class AppThemeGlassRemovedTests
{
    [Fact]
    public void OldSettingsWithAGlassThemeLoadWithoutErrorAndKeepTheThemeMode()
    {
        var dir = Path.Combine(Path.GetTempPath(), "opsis-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "settings.json");
            File.WriteAllText(path, "{\"schemaVersion\":2,\"ThemeMode\":\"Midnight\",\"GpuGlassMode\":\"Always\",\"Glass\":{\"Mode\":\"Glass\",\"BaseTheme\":\"midnight\",\"TintColor\":\"#FF2020\",\"Intensity\":80}}");
            var s = AppSettings.NewStore(path).Load();
            Assert.Equal("Midnight", s.ThemeMode);          // the dark/solid app theme choice survives
            Assert.Equal("Always", s.GpuGlassMode);         // widget / strip / container glass setting survives
            Assert.Null(typeof(AppSettings).GetProperty("Glass"));
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }

    [Fact]
    public void TheApplicationThemeHasNoGlassHostOrThemePage()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "installer.iss"))) d = d.Parent;
        Assert.NotNull(d);
        var src = Path.Combine(d!.FullName, "src");
        Assert.False(File.Exists(Path.Combine(src, "UI", "GlassHost.cs")));
        foreach (var f in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories))
        {
            var t = File.ReadAllText(f);
            Assert.DoesNotContain("GlassHost", t);
            Assert.DoesNotContain("GlassSettingsPage", t);
            Assert.DoesNotContain("ShowTheme", t);
        }
        // widgets / DeskStrip / containers keep the GPU glass
        Assert.Contains("GlassBlur.Apply", File.ReadAllText(Path.Combine(src, "Widgets", "WidgetEngine.cs")));
    }
}
