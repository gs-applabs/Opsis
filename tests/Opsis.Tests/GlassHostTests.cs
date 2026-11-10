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

public sealed class DesktopCoverageTests
{
    [Fact]
    public void AMaximizedWindowCoversTheWorkAreaButATiledOneDoesNot()
    {
        var work = new System.Drawing.Rectangle(0, 0, 2560, 1392);                       // screen minus the taskbar
        Assert.True(MotionDesk.Services.DesktopCoverageTestAccess.Covers(new System.Drawing.Rectangle(-8, -8, 2576, 1408), work)); // maximized (invisible borders)
        Assert.True(MotionDesk.Services.DesktopCoverageTestAccess.Covers(new System.Drawing.Rectangle(0, 0, 2560, 1440), work));   // fullscreen
        Assert.False(MotionDesk.Services.DesktopCoverageTestAccess.Covers(new System.Drawing.Rectangle(0, 0, 1280, 1392), work)); // half of the screen
        Assert.False(MotionDesk.Services.DesktopCoverageTestAccess.Covers(new System.Drawing.Rectangle(100, 100, 2400, 1200), work));
        Assert.False(MotionDesk.Services.DesktopCoverageTestAccess.Covers(default, work));
    }
}

public sealed class DesktopCoverageDecisionTests
{
    private static readonly Dictionary<string, System.Drawing.Rectangle> Work = new()
    {
        ["A"] = new System.Drawing.Rectangle(0, 0, 2560, 1392),
        ["B"] = new System.Drawing.Rectangle(2560, 0, 1920, 1032)
    };
    private static readonly System.Drawing.Rectangle MaxA = new(-8, -8, 2576, 1408);
    private static (System.Drawing.Rectangle, uint, bool, bool, long, string) W(System.Drawing.Rectangle r, uint pid = 10, bool min = false, bool cloaked = false, long ex = 0, string cls = "Chrome_WidgetWin_1") => (r, pid, min, cloaked, ex, cls);
    private static string[] Run(params (System.Drawing.Rectangle, uint, bool, bool, long, string)[] w) => MotionDesk.Services.DesktopCoverageTestAccess.Evaluate(w, Work, 99);

    [Fact] public void MaximizedWindowCoversOnlyItsMonitor() => Assert.Equal(new[] { "A" }, Run(W(MaxA)));
    [Fact] public void NoWindowsMeansNothingCovered() => Assert.Empty(Run());
    [Fact] public void MinimizedCloakedAndOwnWindowsDoNotCover()
    {
        Assert.Empty(Run(W(MaxA, min: true)));
        Assert.Empty(Run(W(MaxA, cloaked: true)));
        Assert.Empty(Run(W(MaxA, pid: 99)));              // Opsis' own window (own pid) never pauses its wallpaper
    }
    [Fact] public void ToolLayeredAndClickThroughWindowsDoNotCover()
    {
        Assert.Empty(Run(W(MaxA, ex: 0x80)));              // tool window
        Assert.Empty(Run(W(MaxA, ex: 0x80000)));           // layered (may be translucent)
        Assert.Empty(Run(W(MaxA, ex: 0x20)));              // click-through overlay
    }
    [Fact] public void ShellWindowsDoNotCover()
    {
        Assert.Empty(Run(W(MaxA, cls: "Progman")));
        Assert.Empty(Run(W(MaxA, cls: "WorkerW")));
        Assert.Empty(Run(W(MaxA, cls: "Shell_TrayWnd")));
    }
    [Fact] public void TiledWindowsNeverCoverEvenIfTheyTogetherFillTheScreen() =>
        Assert.Empty(Run(W(new System.Drawing.Rectangle(0, 0, 1280, 1392)), W(new System.Drawing.Rectangle(1280, 0, 1280, 1392))));
    [Fact] public void TwoMaximizedWindowsOnTwoMonitorsCoverBoth() =>
        Assert.Equal(new[] { "A", "B" }, Run(W(MaxA), W(new System.Drawing.Rectangle(2552, -8, 1936, 1048))));
    [Fact] public void ACoveringWindowBelowAnotherCoveringWindowStillCountsOnce() =>
        Assert.Equal(new[] { "A" }, Run(W(MaxA), W(MaxA, pid: 11)));
}
