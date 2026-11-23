namespace MotionDesk.Containers;

internal static class ContainerTabStrip
{
    internal static Rectangle[] Arrange(int width, int top, int height, int margin, int current, int[] idealWidths)
    {
        var result = new Rectangle[idealWidths.Length];
        int usable = width - 2 * margin;
        if (idealWidths.Length == 0 || usable <= 0 || height <= 0) return result;
        int count = Math.Min(idealWidths.Length, Math.Max(1, usable / Math.Max(28, 4 * margin)));
        int first = Math.Clamp(current - (count - 1) / 2, 0, idealWidths.Length - count);
        int total = idealWidths.Skip(first).Take(count).Sum() + (count - 1) * 2;
        int x = margin;
        int equal = Math.Max(1, (usable - (count - 1) * 2) / count);
        for (int i = first; i < first + count; i++)
        {
            int size = Math.Min(usable - (x - margin), total <= usable ? idealWidths[i] : equal);
            if (size <= 0) break;
            result[i] = new Rectangle(x, top, size, height);
            x += size + 2;
        }
        return result;
    }

    internal static int Insertion(IReadOnlyList<Rectangle> bounds, int x)
    {
        int last = -1;
        for (int i = 0; i < bounds.Count; i++)
        {
            if (bounds[i].IsEmpty) continue;
            if (x < bounds[i].Left + bounds[i].Width / 2) return i;
            last = i;
        }
        return last < 0 ? 0 : last + 1;
    }
}
