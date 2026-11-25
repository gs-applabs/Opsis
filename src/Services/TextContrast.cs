using System.Drawing;

namespace MotionDesk.Services;

/// <summary>WCAG relative luminance and contrast for colours used as text/accent.</summary>
internal static class TextContrast
{
    private static double Linear(byte channel)
    {
        double v = channel / 255.0;
        return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
    }

    public static double Luminance(Color color) =>
        0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);

    public static double Ratio(Color a, Color b)
    {
        double one = Luminance(a), two = Luminance(b);
        return (Math.Max(one, two) + 0.05) / (Math.Min(one, two) + 0.05);
    }

    /// <summary>Find the nearest blend towards black/white that yields at least 4.5:1.</summary>
    public static Color Readable(Color accent, Color background, double required = 4.5)
    {
        if (Ratio(accent, background) >= required) return accent;
        Color best = accent;
        double bestDistance = double.PositiveInfinity;
        foreach (Color endpoint in new[] { Color.Black, Color.White })
        {
            for (int step = 1; step <= 100; step++)
            {
                double t = step / 100.0;
                var candidate = Color.FromArgb(
                    (int)Math.Round(accent.R * (1 - t) + endpoint.R * t),
                    (int)Math.Round(accent.G * (1 - t) + endpoint.G * t),
                    (int)Math.Round(accent.B * (1 - t) + endpoint.B * t));
                if (Ratio(candidate, background) < required) continue;
                double delta = Math.Pow(candidate.R - accent.R, 2) + Math.Pow(candidate.G - accent.G, 2) + Math.Pow(candidate.B - accent.B, 2);
                if (delta < bestDistance) { best = candidate; bestDistance = delta; }
                break;
            }
        }
        return bestDistance < double.PositiveInfinity ? best :
            Ratio(Color.Black, background) >= Ratio(Color.White, background) ? Color.Black : Color.White;
    }
}
