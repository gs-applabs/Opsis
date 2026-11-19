using MotionDesk.Containers;
using Xunit;

namespace Opsis.Tests;

public sealed class DesktopZOrderTests
{
    private static IntPtr H(int n) => new(n);

    [Fact]
    public void StaysDirectlyAboveTheHighestDesktopWindow()
    {
        // top ... app1(1) app2(2) WorkerW(10) Progman(11) ... self(99) at the bottom
        var chain = new List<IntPtr> { H(1), H(2), H(10), H(11), H(99) };
        var after = DesktopZOrder.InsertAfter(chain, new HashSet<IntPtr> { H(10), H(11) }, H(99));
        Assert.Equal(H(2), after);                    // inserted after app2 = right above WorkerW, below every normal window
    }

    [Fact]
    public void GoesToTheTopWhenTheDesktopIsTheTopWindow()
    {
        var chain = new List<IntPtr> { H(10), H(11), H(99) };
        Assert.Null(DesktopZOrder.InsertAfter(chain, new HashSet<IntPtr> { H(10), H(11) }, H(99)));
    }

    [Fact]
    public void OurOwnWindowIsIgnoredInTheChain()
    {
        var chain = new List<IntPtr> { H(1), H(99), H(10) };
        Assert.Equal(H(1), DesktopZOrder.InsertAfter(chain, new HashSet<IntPtr> { H(10) }, H(99)));
    }

    [Fact]
    public void NoDesktopWindowMeansNoDecision() =>
        Assert.Null(DesktopZOrder.InsertAfter(new List<IntPtr> { H(1), H(2) }, new HashSet<IntPtr>(), H(99)));
}

public sealed class DesktopZOrderInPlaceTests
{
    private static IntPtr H(int n) => new(n);
    private static readonly HashSet<IntPtr> Desktop = new() { H(10), H(11) };

    [Fact]
    public void OtherContainersAndGlassPanesBetweenUsAndTheDesktopCountAsInPlace()
    {
        // top ... app(1) C1(50) pane1(51) C2(52) pane2(53) WorkerW(10)
        var chain = new List<IntPtr> { H(1), H(50), H(51), H(52), H(53), H(10), H(11) };
        var ours = new HashSet<IntPtr> { H(50), H(51), H(52), H(53) };
        Assert.True(MotionDesk.Containers.DesktopZOrder.IsInPlace(chain, Desktop, H(50), ours.Contains));
        Assert.True(MotionDesk.Containers.DesktopZOrder.IsInPlace(chain, Desktop, H(52), ours.Contains));
    }

    [Fact]
    public void AForeignWindowBetweenUsAndTheDesktopIsNotInPlace()
    {
        var chain = new List<IntPtr> { H(50), H(7), H(10) };
        Assert.False(MotionDesk.Containers.DesktopZOrder.IsInPlace(chain, Desktop, H(50), h => h == H(50)));
        Assert.False(MotionDesk.Containers.DesktopZOrder.IsInPlace(new List<IntPtr> { H(1) }, Desktop, H(50), _ => true));
    }
}

public sealed class DesktopZOrderTopmostTests
{
    private static IntPtr H(int n) => new(n);

    [Fact]
    public void NeverInsertedAfterATopmostWindow()
    {
        // taskbar(5, topmost) app(1) WorkerW(10) self(99)
        var chain = new List<IntPtr> { H(5), H(1), H(10), H(99) };
        var after = MotionDesk.Containers.DesktopZOrder.InsertAfter(chain, new HashSet<IntPtr> { H(10) }, H(99), h => h == H(5));
        Assert.Equal(H(1), after);
        // only a topmost window above the desktop: go to the top of the normal band (null), never after the topmost one
        var only = new List<IntPtr> { H(5), H(10), H(99) };
        Assert.Null(MotionDesk.Containers.DesktopZOrder.InsertAfter(only, new HashSet<IntPtr> { H(10) }, H(99), h => h == H(5)));
    }
}
