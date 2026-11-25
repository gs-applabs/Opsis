namespace MotionDesk.UI;

internal enum WindowSkin { Classic, Studio, Atlas, Inspector, Index, Split }

internal static class WindowSkins
{
    internal static WindowSkin FromSettings(string? name, int revision) =>
        Enum.TryParse<WindowSkin>(name, true, out var skin) && Enum.IsDefined(skin) &&
        (skin != WindowSkin.Classic || revision >= 1) ? skin : WindowSkin.Studio;

    internal static bool HasTopNavigation(this WindowSkin skin) => skin is WindowSkin.Atlas or WindowSkin.Split;
    internal static bool HasContainerWorkspace(this WindowSkin skin) =>
        skin is WindowSkin.Atlas or WindowSkin.Inspector or WindowSkin.Index or WindowSkin.Split;
}
