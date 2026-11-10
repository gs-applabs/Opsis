using System.Drawing;
using Opsis.Containers;
using Xunit;

namespace Opsis.Containers.Tests;

public sealed class LayoutTests
{
    private static readonly ContainerMetrics M = ContainerMetrics.Default();   // 100 % DPI: title 28, icon 48
    private static readonly Size Client = new(420, 300);

    [Fact]
    public void ColumnsFollowTheWidthAndNeverDropBelowOne()
    {
        Assert.True(ContainerLayout.Columns(420, M) >= 4);
        Assert.Equal(1, ContainerLayout.Columns(10, M));
        Assert.True(ContainerLayout.Columns(900, M) > ContainerLayout.Columns(420, M));
    }

    [Fact]
    public void ScaleChangesEveryMeasurementTogether()
    {
        var m2 = ContainerMetrics.Default(2f);
        Assert.Equal(M.TitleBarHeight * 2, m2.TitleBarHeight); Assert.Equal(M.IconSize * 2, m2.IconSize); Assert.Equal(M.CellHeight * 2, m2.CellHeight);
    }

    [Fact]
    public void ContentHeightGrowsByRowAndScrollIsClamped()
    {
        Assert.Equal(0, ContainerLayout.ContentHeight(420, 0, M));
        var one = ContainerLayout.ContentHeight(420, 1, M);
        var many = ContainerLayout.ContentHeight(420, 200, M);
        Assert.True(many > one);
        Assert.Equal(0, ContainerLayout.MaxScroll(Client, 3, M));                              // three icons fit: nothing to scroll
        var max = ContainerLayout.MaxScroll(Client, 200, M);
        Assert.True(max > 0);
        Assert.Equal(max, ContainerLayout.ClampScroll(99999, Client, 200, M)); Assert.Equal(0, ContainerLayout.ClampScroll(-50, Client, 200, M));
    }

    [Fact]
    public void CellsDoNotOverlapAndStayInsideTheViewportWidth()
    {
        var cells = Enumerable.Range(0, 40).Select(i => ContainerLayout.CellRect(i, Client, M, 0)).ToList();
        for (var a = 0; a < cells.Count; a++)
        {
            Assert.True(cells[a].Left >= 0 && cells[a].Right <= Client.Width, $"cell {a} inside the width");
            for (var b = a + 1; b < cells.Count; b++) Assert.False(cells[a].IntersectsWith(cells[b]), $"{a} overlaps {b}");
        }
        Assert.Equal(M.TitleBarHeight + M.Margin, cells[0].Top);                              // the first row starts below the title bar
    }

    [Fact]
    public void OnlyTheVisibleRowsAreAskedToBeDrawn()
    {
        var all = ContainerLayout.VisibleRange(Client, 1000, M, 0);
        Assert.Equal(0, all.First); Assert.True(all.Last < 100, "a screenful, not 1000 items");
        var scrolled = ContainerLayout.VisibleRange(Client, 1000, M, 5000);
        Assert.True(scrolled.First > 0);
        Assert.Equal((0, -1), ContainerLayout.VisibleRange(Client, 0, M, 0));
        // every visible index really intersects the viewport and no hidden one is needed
        var view = ContainerLayout.Viewport(Client, M);
        for (var i = scrolled.First; i <= scrolled.Last; i++) Assert.True(ContainerLayout.CellRect(i, Client, M, 5000).IntersectsWith(view));
        if (scrolled.First > 0) Assert.False(ContainerLayout.CellRect(scrolled.First - 1, Client, M, 5000).IntersectsWith(view));
    }

    [Fact]
    public void HitTestMatchesWhatIsDrawnAndIgnoresTheTitleBarAndGaps()
    {
        var r = ContainerLayout.CellRect(5, Client, M, 0);
        Assert.Equal(5, ContainerLayout.HitTest(new Point(r.X + 4, r.Y + 4), Client, 40, M, 0));
        Assert.Equal(-1, ContainerLayout.HitTest(new Point(r.X + 4, 5), Client, 40, M, 0));          // inside the title bar
        Assert.Equal(-1, ContainerLayout.HitTest(new Point(1, M.TitleBarHeight + 1), Client, 40, M, 0)); // the margin
        var scrolled = ContainerLayout.CellRect(30, Client, M, 400);
        Assert.Equal(30, ContainerLayout.HitTest(scrolled.Location + new Size(3, 3), Client, 40, M, 400));
    }

    [Fact]
    public void ScrollIntoViewBringsAnItemFullyIntoTheViewport()
    {
        var s = ContainerLayout.ScrollIntoView(150, Client, 200, M, 0);
        var r = ContainerLayout.CellRect(150, Client, M, s); var view = ContainerLayout.Viewport(Client, M);
        Assert.True(view.Contains(r));
        Assert.Equal(0, ContainerLayout.ScrollIntoView(0, Client, 200, M, s));
    }

    [Fact]
    public void DropIndexIsTheInsertionPointBeforeTheCellUnderTheCursor()
    {
        var c0 = ContainerLayout.CellRect(0, Client, M, 0); var c2 = ContainerLayout.CellRect(2, Client, M, 0);
        Assert.Equal(0, ContainerLayout.DropIndex(new Point(c0.X + 2, c0.Y + 5), Client, 10, M, 0));
        Assert.Equal(2, ContainerLayout.DropIndex(new Point(c2.X + 2, c2.Y + 5), Client, 10, M, 0));
        Assert.Equal(10, ContainerLayout.DropIndex(new Point(5, 5000), Client, 10, M, 0));
        Assert.Equal(0, ContainerLayout.DropIndex(new Point(5, 5), Client, 0, M, 0));
    }
}
