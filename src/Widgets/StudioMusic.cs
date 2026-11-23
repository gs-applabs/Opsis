using OpsisMusicOverlay;

namespace MotionDesk.Widgets;

internal static class StudioMusic
{
    private static DesktopMusicController? controller;
    internal static void Start() => controller ??= new DesktopMusicController();

    internal static void Stop()
    {
        controller?.Dispose();
        controller = null;
    }

    internal static Control CreateSettings(bool greek, Color background, Color surface, Color text)
    {
        var installed = FontFamily.Families.Select(family => family.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string[] candidates = ["Potra", "Potra Light", "Pavelt", "MODERN WARFARE"];
        return new MusicOverlayPage(() => controller, greek, candidates.Where(installed.Contains).ToArray(), background, surface, text)
        {
            Width = 545
        };
    }
}
