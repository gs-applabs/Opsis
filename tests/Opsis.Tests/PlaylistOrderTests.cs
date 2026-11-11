using MotionDesk.Widgets;
using Xunit;

namespace Opsis.Tests;

public sealed class PlaylistOrderTests
{
    [Theory]
    [InlineData(0, 3, "b,c,a,d")]
    [InlineData(0, 4, "b,c,d,a")]
    [InlineData(3, 0, "d,a,b,c")]
    [InlineData(2, 1, "a,c,b,d")]
    public void MovesBeforeTheOriginalTarget(int from, int before, string expected)
    {
        var l = new List<string> { "a", "b", "c", "d" };
        Assert.True(PlaylistOrder.Move(l, from, before));
        Assert.Equal(expected, string.Join(",", l));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 2)]
    [InlineData(-1, 0)]
    [InlineData(9, 0)]
    public void NoChangeWhenDroppedInPlaceOrOutOfRange(int from, int before)
    {
        var l = new List<string> { "a", "b", "c" };
        Assert.False(PlaylistOrder.Move(l, from, before));
        Assert.Equal("a,b,c", string.Join(",", l));
    }
}
