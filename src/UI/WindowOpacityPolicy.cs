namespace MotionDesk.UI;

internal static class WindowOpacityPolicy
{
    internal const double Minimum = .60;

    internal static double Normalize(double value) => double.IsFinite(value) ? Math.Clamp(value, Minimum, 1) : 1;

    internal static bool UseMica(double opacity, bool effectsEnabled, bool highContrast) =>
        effectsEnabled && !highContrast && Normalize(opacity) == 1;
}
