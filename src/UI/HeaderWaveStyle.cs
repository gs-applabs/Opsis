using System.Drawing;

namespace MotionDesk.UI
{
    /// <summary>
    /// Alpha of the moving wave lines behind the page title. The old fixed alpha (38) gave a contrast of only ~30 levels on the dark theme (and less on the light one),
    /// so on many screens the animation looked absent. The alpha now follows the real colour distance between line and background so the lines always differ from the
    /// background by about <see cref="TargetDelta"/> levels (clamped), whatever the theme.
    /// </summary>
    public static class HeaderWaveStyle
    {
        public const int TargetDelta = 80;

        public static int Alpha(Color background, Color accent, int lineIndex)
        {
            int d = Math.Max(Math.Abs(accent.R - background.R), Math.Max(Math.Abs(accent.G - background.G), Math.Abs(accent.B - background.B)));
            if (d < 8) d = 8;
            double a = Math.Clamp(TargetDelta / (double)d * 255.0, 60, 150);
            return (int)Math.Round(a * (1.0 - lineIndex * 0.18));
        }

        /// <summary>The level difference a line of that alpha makes over the background (what the eye sees).</summary>
        public static double Contrast(Color background, Color accent, int alpha)
        {
            double f = alpha / 255.0;
            return Math.Max(Math.Abs(accent.R - background.R), Math.Max(Math.Abs(accent.G - background.G), Math.Abs(accent.B - background.B))) * f;
        }
    }
}
