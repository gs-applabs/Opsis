using MotionDesk.Services;
using MotionDesk.Widgets;
using Xunit;

namespace Opsis.Tests;

// Music overlay options (row 20): auto-hide, click-through, position setting; the old audio-spectrum wallpaper element stays retired (the Music visualizer element covers it).
public sealed class MusicOverlayOptionsTests
{
    [Fact]
    public void AutoHideZeroKeepsTheCardWhilePlaying()
    {
        Assert.True(OverlayVisibility.ShouldShow(false, 0, TimeSpan.FromHours(3)));
    }

    [Fact]
    public void AutoHideHidesAfterTheConfiguredSecondsAndAClickDismisses()
    {
        Assert.True(OverlayVisibility.ShouldShow(false, 8, TimeSpan.FromSeconds(7.9)));
        Assert.False(OverlayVisibility.ShouldShow(false, 8, TimeSpan.FromSeconds(8)));
        Assert.False(OverlayVisibility.ShouldShow(true, 0, TimeSpan.Zero));
    }

    [Fact]
    public void DefaultsKeepTheOldBehaviour()
    {
        var s = new AppSettings();
        Assert.True(s.MusicOverlayClickThrough);
        Assert.Equal(0, s.MusicOverlayAutoHideSeconds);
        Assert.False(s.MusicOverlayEnabled);
        Assert.Equal("BottomRight", s.MusicOverlayAnchor);
    }

    [Fact]
    public void NoSpectrumElementInTheWallpaperCatalog()
    {
        Assert.DoesNotContain(MotionDesk.Widgets.WallpaperElement.Catalog, e => e.Id.Contains("spectrum", StringComparison.OrdinalIgnoreCase));
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "MotionDeskStudio.csproj"))) d = d.Parent;
        Assert.True(File.Exists(Path.Combine(d!.FullName, "src", "Widgets", "wallpaper", "visualizer.js")));   // the Music visualizer element that replaced it
        Assert.True(new WallpaperSettings().Visualizer != null);
    }
}
