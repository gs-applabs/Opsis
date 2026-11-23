namespace MotionDesk.Services;

internal static class AutoTileLayout
{
    internal static Rectangle[] Arrange(Rectangle area, int count, string mode, int padding = 8)
    {
        if (count <= 0 || area.Width < 80 || area.Height < 80) return [];
        int gap = Math.Clamp(padding, 0, Math.Min(area.Width, area.Height) / Math.Max(4, count * 4));
        Rectangle Slot(int x1, int y1, int x2, int y2) => Rectangle.FromLTRB(x1 + gap, y1 + gap, x2 - gap, y2 - gap);
        if (mode == "Split")
        {
            var result = new List<Rectangle>(count);
            void Divide(Rectangle part, int remaining)
            {
                if (remaining == 1) { result.Add(Slot(part.Left, part.Top, part.Right, part.Bottom)); return; }
                int first = (remaining + 1) / 2;
                if (part.Width >= part.Height)
                {
                    int mid = part.Left + part.Width * first / remaining;
                    Divide(Rectangle.FromLTRB(part.Left, part.Top, mid, part.Bottom), first);
                    Divide(Rectangle.FromLTRB(mid, part.Top, part.Right, part.Bottom), remaining - first);
                }
                else
                {
                    int mid = part.Top + part.Height * first / remaining;
                    Divide(Rectangle.FromLTRB(part.Left, part.Top, part.Right, mid), first);
                    Divide(Rectangle.FromLTRB(part.Left, mid, part.Right, part.Bottom), remaining - first);
                }
            }
            Divide(area, count);
            return result.ToArray();
        }
        return Enumerable.Range(0, count).Select(index =>
        {
            if (mode == "Stack" && count > 1)
            {
                int split = area.Left + area.Width * 3 / 5;
                if (index == 0) return Slot(area.Left, area.Top, split, area.Bottom);
                int row = index - 1, rows = count - 1;
                return Slot(split, area.Top + area.Height * row / rows, area.Right, area.Top + area.Height * (row + 1) / rows);
            }
            int columns = mode == "Grid" ? (int)Math.Ceiling(Math.Sqrt(count)) : count;
            int rowCount = (count + columns - 1) / columns;
            int y = index / columns, x = index % columns;
            int rowColumns = Math.Min(columns, count - y * columns);
            return Slot(area.Left + area.Width * x / rowColumns, area.Top + area.Height * y / rowCount,
                area.Left + area.Width * (x + 1) / rowColumns, area.Top + area.Height * (y + 1) / rowCount);
        }).ToArray();
    }
}
