using Opsis.Containers;
using Xunit;

namespace Opsis.Containers.Tests;

public sealed class NavigationTests
{
    [Fact]
    public void ArrowsMoveByCellAndRow()
    {
        Assert.Equal(1, ContainerNavigation.Move(0, NavKey.Right, 4, 10));
        Assert.Equal(4, ContainerNavigation.Move(0, NavKey.Down, 4, 10));
        Assert.Equal(0, ContainerNavigation.Move(4, NavKey.Up, 4, 10));
        Assert.Equal(0, ContainerNavigation.Move(0, NavKey.Left, 4, 10));      // clamped
        Assert.Equal(9, ContainerNavigation.Move(9, NavKey.Down, 4, 10));      // already on the last row
    }

    [Fact]
    public void HomeEndPagesAndNoSelectionYet()
    {
        Assert.Equal(0, ContainerNavigation.Move(7, NavKey.Home, 4, 10));
        Assert.Equal(9, ContainerNavigation.Move(2, NavKey.End, 4, 10));
        Assert.Equal(9, ContainerNavigation.Move(2, NavKey.PageDown, 4, 10, 5));
        Assert.Equal(0, ContainerNavigation.Move(-1, NavKey.Right, 4, 10));
        Assert.Equal(9, ContainerNavigation.Move(-1, NavKey.End, 4, 10));
        Assert.Equal(-1, ContainerNavigation.Move(0, NavKey.Right, 4, 0));
    }
}
