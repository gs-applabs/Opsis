using MotionDesk.UI;
using Opsis.UI;
using Xunit;

namespace Opsis.Tests;

// 2.6.0: the bridge between the shared Glass state and Opsis' own palette.
[Collection("glass")]
public sealed class GlassHostTests
{
    [Fact]
    public void SolidReturnsTheRawPaletteAndGlassReturnsAReadableTintedCopy()
    {
        GlassEnvironment.TransparencyEnabledOverride = () => true; GlassEnvironment.HighContrastOverride = () => false;
        GlassEnvironment.Windows11Override = () => true; GlassEnvironment.MachineTierOverride = () => GlassTier.High;
        var raw = UiPalette.Dark(0.5);
        try
        {
            GlassManager.ManageTheme = false;
            GlassManager.Apply(new GlassSettings { Mode = "Solid" });
            Assert.Same(raw, GlassHost.Apply(raw));

            GlassManager.Apply(new GlassSettings { Mode = "Glass", TintColor = "#FF2020", Intensity = 100 });
            var tinted = GlassHost.Apply(raw);
            Assert.NotSame(raw, tinted);
            Assert.NotEqual(raw.Surface, tinted.Surface);
            Assert.True(tinted.Surface.R > raw.Surface.R, "the surface moved towards the red tint");
            Assert.True(Contrast.Ratio(tinted.TextPrimary, tinted.Surface) >= 4.5);
            Assert.True(Contrast.Ratio(tinted.TextSecondary, tinted.Surface) >= 4.5);
            Assert.Same(tinted, GlassHost.Apply(raw)); // cached: no new palette per call
        }
        finally
        {
            GlassManager.Apply(new GlassSettings()); GlassManager.ManageTheme = true;
            GlassEnvironment.TransparencyEnabledOverride = null; GlassEnvironment.HighContrastOverride = null;
            GlassEnvironment.Windows11Override = null; GlassEnvironment.MachineTierOverride = null;
        }
    }

    [Fact]
    public void WindowsTransparencyOffKeepsTheSolidPalette()
    {
        GlassEnvironment.TransparencyEnabledOverride = () => false; GlassEnvironment.HighContrastOverride = () => false;
        var raw = UiPalette.Dark(0.5);
        try
        {
            GlassManager.ManageTheme = false;
            GlassManager.Apply(new GlassSettings { Mode = "Glass" });
            Assert.Same(raw, GlassHost.Apply(raw));
        }
        finally { GlassManager.Apply(new GlassSettings()); GlassManager.ManageTheme = true; GlassEnvironment.TransparencyEnabledOverride = null; GlassEnvironment.HighContrastOverride = null; }
    }

    [Fact]
    public void UiScaleNeverCompounds()
    {
        var baseSize = UiTheme.FontBody.Size / (UiTheme.ScalePercent / 100f);
        UiTheme.ApplyScale(150);
        Assert.Equal(baseSize * 1.5f, UiTheme.FontBody.Size, 0.1f);
        UiTheme.ApplyScale(125);
        Assert.Equal(baseSize * 1.25f, UiTheme.FontBody.Size, 0.1f);
        UiTheme.ApplyScale(100);
        Assert.Equal(baseSize, UiTheme.FontBody.Size, 0.1f);
    }
}
