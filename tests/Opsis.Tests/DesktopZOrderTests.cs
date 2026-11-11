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
