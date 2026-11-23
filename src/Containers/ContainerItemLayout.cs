using System.Text.Json;
using Opsis.Containers;

namespace MotionDesk.Containers;

internal readonly record struct ContainerItemLayout(Size Client, ContainerMetrics Grid, bool Rows)
{
    internal const string ViewKey = "opsisItemView";
    internal static string ViewOf(Container model) =>
        model.Extra?.TryGetValue(ViewKey, out var view) == true && view.ValueKind == JsonValueKind.String
            ? view.GetString() ?? "grid" : "grid";

    internal static int LeftInset(ContainerMetrics metrics) => Math.Max(1, metrics.Margin / 2);

    internal Rectangle Viewport => ContainerLayout.Viewport(Client, Grid);
    private int GridLeftShift => ContainerLayout.CellRect(0, Client, Grid, 0).Left - LeftInset(Grid);
    internal int Columns => Rows ? 1 : ContainerLayout.Columns(Client.Width, Grid);
    internal int RowHeight => Rows ? Math.Max(Grid.IconSize, Grid.LabelHeight / 2) + Grid.Spacing : Grid.CellHeight;
    internal int ContentHeight(int count) => Rows ? count * RowHeight + 2 * Grid.Margin : ContainerLayout.ContentHeight(Client.Width, count, Grid);
    internal int MaxScroll(int count) => Rows ? Math.Max(0, ContentHeight(count) - Viewport.Height) : ContainerLayout.MaxScroll(Client, count, Grid);
    internal int ClampScroll(int offset, int count) => Rows ? Math.Clamp(offset, 0, MaxScroll(count)) : ContainerLayout.ClampScroll(offset, Client, count, Grid);

    internal Rectangle CellRect(int index, int scroll)
    {
        if (!Rows)
        {
            var cell = ContainerLayout.CellRect(index, Client, Grid, scroll);
            cell.X -= GridLeftShift;
            return cell;
        }
        int left = LeftInset(Grid);
        return new Rectangle(left, Viewport.Top + Grid.Margin + index * RowHeight - scroll,
            Math.Max(1, Client.Width - left - Grid.Margin), RowHeight);
    }

    internal (int First, int Last) VisibleRange(int count, int scroll)
    {
        if (!Rows) return ContainerLayout.VisibleRange(Client, count, Grid, scroll);
        if (count == 0 || Viewport.Height == 0) return (0, -1);
        int first = Math.Max(0, (scroll - Grid.Margin) / RowHeight);
        int last = Math.Min(count - 1, (scroll + Viewport.Height - Grid.Margin - 1) / RowHeight);
        return (first, last);
    }

    internal int HitTest(Point point, int count, int scroll)
    {
        if (!Viewport.Contains(point)) return -1;
        if (!Rows)
        {
            point.X += GridLeftShift;
            return ContainerLayout.HitTest(point, Client, count, Grid, scroll);
        }
        int rowY = point.Y - CellRect(0, scroll).Top;
        int index = rowY / RowHeight;
        return rowY >= 0 && index < count && CellRect(index, scroll).Contains(point) ? index : -1;
    }

    internal int DropIndex(Point point, int count, int scroll)
    {
        if (!Rows)
        {
            point.X += GridLeftShift;
            return ContainerLayout.DropIndex(point, Client, count, Grid, scroll);
        }
        return Math.Clamp((point.Y - CellRect(0, scroll).Top + RowHeight / 2) / RowHeight, 0, count);
    }

    internal int ScrollIntoView(int index, int count, int scroll)
    {
        if (!Rows) return ContainerLayout.ScrollIntoView(index, Client, count, Grid, scroll);
        var row = CellRect(index, scroll);
        if (row.Top < Viewport.Top) scroll += row.Top - Viewport.Top;
        else if (row.Bottom > Viewport.Bottom) scroll += row.Bottom - Viewport.Bottom;
        return ClampScroll(scroll, count);
    }
}
