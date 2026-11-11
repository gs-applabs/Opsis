namespace Opsis.Containers;

public enum NavKey { Left, Right, Up, Down, Home, End, PageUp, PageDown }

/// <summary>Keyboard navigation over the icon grid as a pure function (phase 7/8, accessibility). No selection yet (-1) starts at the first item.</summary>
public static class ContainerNavigation
{
    public static int Move(int current, NavKey key, int columns, int count, int rowsPerPage = 3)
    {
        if (count <= 0) return -1;
        columns = Math.Max(1, columns);
        if (current < 0 || current >= count) return key == NavKey.End ? count - 1 : 0;
        var target = key switch
        {
            NavKey.Left => current - 1,
            NavKey.Right => current + 1,
            NavKey.Up => current - columns,
            NavKey.Down => current + columns,
            NavKey.PageUp => current - columns * Math.Max(1, rowsPerPage),
            NavKey.PageDown => current + columns * Math.Max(1, rowsPerPage),
            NavKey.Home => 0,
            NavKey.End => count - 1,
            _ => current
        };
        if (key == NavKey.Down && target >= count) target = current;                 // last row: Down stays (does not jump to the end)
        if (key == NavKey.PageDown && target >= count) target = count - 1;
        return Math.Clamp(target, 0, count - 1);
    }
}
