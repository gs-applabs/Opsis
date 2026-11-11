using System.Drawing;
using MotionDesk.Services;
using Xunit;

namespace Opsis.Tests;

public sealed class OverlayPlacementTests
{
    private static readonly Rectangle Wa = new(0, 0, 1920, 1040);
    private static readonly Size Card = new(340, 88);

    [Theory]
    [InlineData(OverlayAnchor.TopLeft, 24, 24)]
    [InlineData(OverlayAnchor.TopCenter, 790, 24)]
    [InlineData(OverlayAnchor.TopRight, 1556, 24)]
    [InlineData(OverlayAnchor.Center, 790, 476)]
    [InlineData(OverlayAnchor.BottomLeft, 24, 928)]
    [InlineData(OverlayAnchor.BottomRight, 1556, 928)]
    public void NineAnchors(OverlayAnchor a, int x, int y) => Assert.Equal(new Point(x, y), OverlayPlacement.Compute(Wa, Card, a, 24));

    [Fact]
    public void NeverLeavesTheWorkAreaOnATinyScreen() =>
        Assert.Equal(new Point(0, 0), OverlayPlacement.Compute(new Rectangle(0, 0, 200, 50), Card, OverlayAnchor.BottomRight, 24));
}
