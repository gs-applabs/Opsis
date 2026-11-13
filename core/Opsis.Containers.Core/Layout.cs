using System.Drawing;

namespace Opsis.Containers;

/// <summary>The measurements of one container view (everything in device pixels; the host multiplies DIPs by its DPI scale before asking).</summary>
public sealed record ContainerMetrics(int TitleBarHeight, int IconSize, int Spacing, int Margin, int LabelHeight, int TopInset = 0)
{
    /// <summary>Where the content starts: below the title bar and, when the container has tabs, below the tab strip.</summary>
    public int Top => TitleBarHeight + TopInset;
    public static ContainerMetrics Default(float scale = 1f, int titleBarDips = 28, int iconDips = 48) =>
        new((int)Math.Round(titleBarDips * scale), (int)Math.Round(iconDips * scale), (int)Math.Round(8 * scale), (int)Math.Round(10 * scale), (int)Math.Round(34 * scale));

    public int CellWidth => Math.Max(IconSize + 2 * Spacing, (int)(IconSize * 1.5));
    public int CellHeight => IconSize + LabelHeight + Spacing;
}

/// <summary>
/// The grid of a container as pure functions (phase 2, own rendering): cell rectangles, columns, content height, scrolling, which cells are visible and which item a point hits.
/// Rendering draws ONLY the visible range (virtualization), hit testing and scrolling use the same numbers, so what is drawn is what is clickable.
/// </summary>
public static class ContainerLayout
{
    public static int Columns(int clientWidth, ContainerMetrics m)
    {
        var usable = clientWidth - 2 * m.Margin;
        return Math.Max(1, (usable + m.Spacing) / (m.CellWidth + m.Spacing));
    }

    public static int Rows(int itemCount, int columns) => itemCount == 0 ? 0 : (itemCount + columns - 1) / columns;

    /// <summary>Height of all rows (without the title bar).</summary>
    public static int ContentHeight(int clientWidth, int itemCount, ContainerMetrics m)
    {
        var rows = Rows(itemCount, Columns(clientWidth, m));
        return rows == 0 ? 0 : 2 * m.Margin + rows * m.CellHeight + (rows - 1) * m.Spacing;
    }

    /// <summary>The area below the title bar that shows the items.</summary>
    public static Rectangle Viewport(Size client, ContainerMetrics m) => new(0, m.Top, client.Width, Math.Max(0, client.Height - m.Top));

    public static int MaxScroll(Size client, int itemCount, ContainerMetrics m) => Math.Max(0, ContentHeight(client.Width, itemCount, m) - Viewport(client, m).Height);

    public static int ClampScroll(int scroll, Size client, int itemCount, ContainerMetrics m) => Math.Clamp(scroll, 0, MaxScroll(client, itemCount, m));

    /// <summary>Rectangle of item <paramref name="index"/> in client coordinates, with the given vertical scroll applied.</summary>
    public static Rectangle CellRect(int index, Size client, ContainerMetrics m, int scroll)
    {
        var cols = Columns(client.Width, m);
        var row = index / cols; var col = index % cols;
        var totalW = cols * m.CellWidth + (cols - 1) * m.Spacing;
        var left = m.Margin + Math.Max(0, (client.Width - 2 * m.Margin - totalW) / 2);      // the grid is centred when there is spare width
        var x = left + col * (m.CellWidth + m.Spacing);
        var y = m.Top + m.Margin + row * (m.CellHeight + m.Spacing) - scroll;
        return new Rectangle(x, y, m.CellWidth, m.CellHeight);
    }

    /// <summary>The first and last item that can be seen (inclusive); (0, -1) when none: only these are drawn.</summary>
    public static (int First, int Last) VisibleRange(Size client, int itemCount, ContainerMetrics m, int scroll)
    {
        if (itemCount == 0) return (0, -1);
        var cols = Columns(client.Width, m);
        var rowH = m.CellHeight + m.Spacing;
        var top = scroll - m.Margin - m.CellHeight;                                  // a row is visible when its bottom is below the viewport top ...
        var firstRow = top < 0 ? 0 : top / Math.Max(1, rowH) + 1;
        var bottom = scroll + Viewport(client, m).Height - m.Margin - 1;               // ... and its top is above the viewport bottom
        var lastRow = Math.Min(Rows(itemCount, cols) - 1, bottom < 0 ? -1 : bottom / Math.Max(1, rowH));
        if (lastRow < firstRow) return (0, -1);
        return (firstRow * cols, Math.Min(itemCount - 1, (lastRow + 1) * cols - 1));
    }

    /// <summary>The item under a client point, or -1 (title bar, gaps, empty space).</summary>
    public static int HitTest(Point p, Size client, int itemCount, ContainerMetrics m, int scroll)
    {
        if (p.Y < m.Top || itemCount == 0) return -1;
        var (first, last) = VisibleRange(client, itemCount, m, scroll);
        for (var i = first; i <= last; i++)
            if (CellRect(i, client, m, scroll).Contains(p)) return i;
        return -1;
    }

    /// <summary>Scroll so that item <paramref name="index"/> is fully visible (keyboard navigation, drop target).</summary>
    public static int ScrollIntoView(int index, Size client, int itemCount, ContainerMetrics m, int scroll)
    {
        var r = CellRect(index, client, m, 0);
        var view = Viewport(client, m);
        if (r.Top - scroll < view.Top + m.Margin) scroll = r.Top - view.Top - m.Margin;
        else if (r.Bottom - scroll > view.Bottom - m.Margin) scroll = r.Bottom - view.Bottom + m.Margin;
        return ClampScroll(scroll, client, itemCount, m);
    }

    /// <summary>Where a dropped item would land (insertion index) for a point; the last position when the point is below all rows.</summary>
    public static int DropIndex(Point p, Size client, int itemCount, ContainerMetrics m, int scroll)
    {
        if (itemCount == 0) return 0;
        var cols = Columns(client.Width, m);
        for (var i = 0; i < itemCount; i++)
        {
            var r = CellRect(i, client, m, scroll);
            if (p.Y < r.Bottom && p.X < r.X + r.Width / 2) return i;
        }
        return itemCount;
    }
}
