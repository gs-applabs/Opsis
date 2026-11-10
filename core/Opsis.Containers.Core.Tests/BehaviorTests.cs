using System.Drawing;
using Opsis.Containers;
using Xunit;

namespace Opsis.Containers.Tests;

public sealed class BehaviorTests
{
    private static readonly Rectangle Work = new(0, 0, 2560, 1392);

    [Fact]
    public void SnapsToTheEdgesOfTheWorkArea()
    {
        var r = ContainerBehavior.Snap(new Rectangle(6, 9, 300, 200), Array.Empty<Rectangle>(), Work, 12);
        Assert.Equal(new Point(0, 0), r.Location);
        var br = ContainerBehavior.Snap(new Rectangle(2560 - 300 - 5, 1392 - 200 - 7, 300, 200), Array.Empty<Rectangle>(), Work, 12);
        Assert.Equal(new Rectangle(2260, 1192, 300, 200), br);
    }

    [Fact]
    public void SnapsToOtherContainersEdgeToEdgeAndAlignedAndNeverWhenFar()
    {
        var other = new Rectangle(400, 300, 200, 200);
        var r = ContainerBehavior.Snap(new Rectangle(606, 310, 150, 100), new[] { other }, Work, 12);      // left edge near the right edge of the other (600)
        Assert.Equal(600, r.X); Assert.Equal(300, r.Y);                                                       // and the top aligns with the other's top
        var far = new Rectangle(1000, 800, 150, 100);
        Assert.Equal(far, ContainerBehavior.Snap(far, new[] { other }, Work, 12));
        Assert.Equal(far, ContainerBehavior.Snap(far, new[] { other }, Work, 0));                              // threshold 0 = snapping off
    }

    [Fact]
    public void EachAxisSnapsToItsNearestCandidate()
    {
        var a = new Rectangle(100, 0, 100, 100); var b = new Rectangle(0, 0, 100, 100);
        var r = ContainerBehavior.Snap(new Rectangle(205, 40, 50, 50), new[] { a, b }, Work, 20);              // 205 -> 200 (edge of a), not 100
        Assert.Equal(200, r.X);
    }

    [Fact]
    public void ResizeKeepsTheMinimumAndStaysInsideTheWorkArea()
    {
        var start = new Rectangle(2400, 1300, 100, 80);
        var r = ContainerBehavior.Resize(start, new Size(10, 10), Work);
        Assert.Equal(new Size(ContainerBehavior.MinWidth, ContainerBehavior.MinHeight), r.Size);
        var big = ContainerBehavior.Resize(start, new Size(5000, 5000), Work);
        Assert.Equal(2560 - 2400, big.Width); Assert.Equal(1392 - 1300, big.Height);
        Assert.Equal(start.Location, big.Location);
    }

    [Fact]
    public void RollUpShrinksToTheOwnTitleBarHeightAndUnrollRestoresIt()
    {
        var full = new Rectangle(500, 400, 320, 240);
        var (rolled, h) = ContainerBehavior.RollUp(full, 44);                                                    // a tall custom title bar
        Assert.Equal(44, rolled.Height); Assert.Equal(240, h); Assert.Equal(full.X, rolled.X);
        Assert.Equal(full, ContainerBehavior.Unroll(rolled, h, Work));
        var low = new Rectangle(500, 1380, 320, 28);
        var up = ContainerBehavior.Unroll(low, 240, Work);
        Assert.True(Work.Contains(up));                                                                          // opening at the bottom edge does not leave the screen
        Assert.True(ContainerBehavior.Unroll(low, 0, Work).Height >= ContainerBehavior.MinHeight);               // unknown saved height: a sane default
    }

    [Fact]
    public void AContainerFromAnUnpluggedMonitorComesBackReachable()
    {
        var gone = new Rectangle(3000, 200, 320, 240);
        var r = ContainerBehavior.KeepReachable(gone, new[] { Work });
        Assert.True(Work.Contains(new Point(r.X + 10, r.Y + 10)));
        var ok = new Rectangle(100, 100, 320, 240);
        Assert.Equal(ok, ContainerBehavior.KeepReachable(ok, new[] { Work }));
        var onSecond = new Rectangle(2700, 50, 320, 240);
        Assert.Equal(onSecond, ContainerBehavior.KeepReachable(onSecond, new[] { Work, new Rectangle(2560, 0, 1920, 1032) }));
    }

    [Fact]
    public void LockIgnoresMoveAndResize()
    {
        var cur = new Rectangle(10, 10, 100, 100); var wanted = new Rectangle(50, 50, 200, 200);
        Assert.Equal(cur, ContainerBehavior.ApplyLock(true, cur, wanted));
        Assert.Equal(wanted, ContainerBehavior.ApplyLock(false, cur, wanted));
    }

    [Fact]
    public void HitTestReturnsTheTopmostVisibleContainer()
    {
        var w = new List<(Rectangle, bool, bool)> { (new Rectangle(0, 0, 200, 200), false, false), (new Rectangle(100, 100, 200, 200), false, false), (new Rectangle(150, 150, 50, 50), false, true) };
        Assert.Equal(1, ContainerBehavior.HitTest(w, new Point(160, 160)));      // the hidden one on top is skipped
        Assert.Equal(0, ContainerBehavior.HitTest(w, new Point(20, 20)));
        Assert.Equal(-1, ContainerBehavior.HitTest(w, new Point(900, 900)));
    }
}
