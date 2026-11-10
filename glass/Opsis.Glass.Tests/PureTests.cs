using System.Drawing;
using Opsis.Glass;
using Xunit;

namespace Opsis.Glass.Tests;

public sealed class PureTests
{
    private static GlassConditions C(Func<GlassConditions, GlassConditions>? f = null) { var c = new GlassConditions(); return f is null ? c : f(c); }

    [Fact]
    public void HighTierWithLiveBackdropRefractsAt60()
    {
        var d = GlassPolicy.Evaluate(C(c => c with { LiveBackdrop = true }));
        Assert.True(d.CaptureNow); Assert.Equal(60, d.MaxFps); Assert.True(d.Refraction); Assert.True(d.Blur); Assert.False(d.Static);
    }

    [Fact]
    public void BalancedIsBlurOnlyAt30AndLowIsStaticWithoutCapture()
    {
        var b = GlassPolicy.Evaluate(C(c => c with { Tier = GlassTier.Balanced, LiveBackdrop = true }));
        Assert.Equal(30, b.MaxFps); Assert.False(b.Refraction); Assert.True(b.Blur);
        var l = GlassPolicy.Evaluate(C(c => c with { Tier = GlassTier.Low, LiveBackdrop = true }));
        Assert.False(l.CaptureNow); Assert.True(l.Static); Assert.Equal(0, l.MaxFps);
    }

    [Fact]
    public void StaticBackgroundStillWatchesForMovingWindowsButOnlyAtALowRate()
    {
        var d = GlassPolicy.Evaluate(C());                 // static wallpaper, nothing animating
        Assert.True(d.CaptureNow); Assert.Equal(15, d.MaxFps); Assert.False(d.Static);   // change-driven: a still background costs nothing, but a moving window behind is seen
        Assert.Equal(4, GlassPolicy.Evaluate(C(c => c with { Idle = true })).MaxFps);
        Assert.Equal(60, GlassPolicy.Evaluate(C(c => c with { WindowMovingBehind = true })).MaxFps);
    }

    [Fact]
    public void FrozenKeepsThePanesAsTheyAreWithoutAnyWork()
    {
        var d = GlassPolicy.Evaluate(C(c => c with { Frozen = true, LiveBackdrop = true }));
        Assert.False(d.Suspended); Assert.False(d.CaptureNow); Assert.Equal(0, d.MaxFps);
    }

    [Theory]
    [InlineData(SuspendReason.Locked)] [InlineData(SuspendReason.RemoteSession)] [InlineData(SuspendReason.FullscreenApp)] [InlineData(SuspendReason.AllHidden)] [InlineData(SuspendReason.SafeMode)]
    public void SuspendsOnLockRemoteFullscreenHiddenAndSafeMode(SuspendReason reason)
    {
        var d = GlassPolicy.Evaluate(C(c => (c with { LiveBackdrop = true }) with
        {
            Locked = reason == SuspendReason.Locked, RemoteSession = reason == SuspendReason.RemoteSession, FullscreenApp = reason == SuspendReason.FullscreenApp,
            AllMinimizedOrHidden = reason == SuspendReason.AllHidden, SafeMode = reason == SuspendReason.SafeMode
        }));
        Assert.True(d.Suspended); Assert.Equal(reason, d.Reason); Assert.False(d.CaptureNow); Assert.Equal(0, d.MaxFps);
    }

    [Fact]
    public void BatterySaverLowersTheRateAndMissingDuplicationFallsBackToStatic()
    {
        Assert.Equal(20, GlassPolicy.Evaluate(C(c => c with { LiveBackdrop = true, BatterySaver = true })).MaxFps);
        var d = GlassPolicy.Evaluate(C(c => c with { LiveBackdrop = true, DuplicationAvailable = false }));
        Assert.True(d.Static); Assert.False(d.CaptureNow);
    }

    [Fact]
    public void UnionIsOneRectangleOverAllPanesAndCropOffsetsAreRelative()
    {
        var a = new Rectangle(100, 200, 320, 240); var b = new Rectangle(430, 210, 320, 240); var off = new Rectangle(-50, 5000, 0, 0);
        var u = RectMath.Union(new[] { a, b, off });
        Assert.Equal(new Rectangle(100, 200, 650, 250), u);
        Assert.Equal(new Point(330, 10), RectMath.OffsetIn(u, b));
        Assert.Equal(Rectangle.Empty, RectMath.Union(Array.Empty<Rectangle>()));
        Assert.Equal(new Rectangle(0, 0, 10, 10), RectMath.ClipTo(new Rectangle(0, 0, 100, 100), new Rectangle(-10, -10, 20, 20)));
    }

    [Fact]
    public void TextureIsOnlyRecreatedWhenItMustGrowOrShrinksALot()
    {
        Assert.False(RectMath.NeedsNewTexture(new Size(1000, 500), new Size(900, 450)));    // a small shrink: keep (no churn while dragging)
        Assert.True(RectMath.NeedsNewTexture(new Size(1000, 500), new Size(1001, 500)));
        Assert.True(RectMath.NeedsNewTexture(new Size(1000, 500), new Size(400, 200)));
    }

    private sealed class Res : IDisposable { public bool Disposed; public void Dispose() => Disposed = true; }

    [Fact]
    public void LruNeverExceedsItsLimitAndDisposesEvicted()
    {
        using var c = new BoundedLru<int, Res>(2);
        var r1 = c.GetOrCreate(1, _ => new Res()); var r2 = c.GetOrCreate(2, _ => new Res());
        c.TryGet(1, out _);
        c.GetOrCreate(3, _ => new Res());
        Assert.Equal(2, c.Count); Assert.True(r2.Disposed); Assert.False(r1.Disposed);
        Parallel.For(0, 500, i => c.GetOrCreate(i % 40, _ => new Res()));
        Assert.True(c.Count <= 2);
    }

    [Fact]
    public void DisplacementMapIsFlatInTheMiddleAndInwardAtTheBezel()
    {
        const int w = 200, h = 120;
        var m = DisplacementMapBuilder.Build(w, h, 16, 26);
        byte R(int x, int y) => m[(y * w + x) * 4 + 2];
        byte G(int x, int y) => m[(y * w + x) * 4 + 1];
        Assert.Equal(128, R(w / 2, h / 2)); Assert.Equal(128, G(w / 2, h / 2));
        Assert.True(R(2, h / 2) > 128, "left edge pushes right (inward)");
        Assert.True(R(w - 3, h / 2) < 128, "right edge pushes left");
        Assert.True(G(w / 2, 2) > 128, "top edge pushes down");
        Assert.True(G(w / 2, h - 3) < 128, "bottom edge pushes up");
        Assert.Equal(w * h * 4, m.Length);
    }

    [Fact]
    public void DistanceIsNegativeInTheTransparentCorners()
    {
        Assert.True(DisplacementMapBuilder.DistanceInside(0, 0, 100, 100, 16) < 0);
        Assert.True(DisplacementMapBuilder.DistanceInside(50, 50, 100, 100, 16) > 40);
    }

    [Fact]
    public void StyleDerivesRefractionAndCacheKeyFromGloss()
    {
        var calm = new GlassStyle { Gloss = 0.1f }; var very = new GlassStyle { Gloss = 0.9f };
        Assert.True(very.RefractionPx > calm.RefractionPx);
        Assert.NotEqual(calm.MapKey(320, 240, 96), very.MapKey(320, 240, 96));
        Assert.NotEqual(calm.MapKey(320, 240, 96), calm.MapKey(320, 240, 192));
    }
}
