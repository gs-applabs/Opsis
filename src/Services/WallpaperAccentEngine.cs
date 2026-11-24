using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using MotionDesk.Widgets;

namespace MotionDesk.Services;

/// <summary>
/// Opt-in palette extraction from local wallpaper pixels. Never changes Windows
/// personalization registry, never downloads content, never runs plugin scripts.
/// </summary>
public static class WallpaperAccentEngine
{
    public static Color Extract(string path)
    {
        if (!File.Exists(path) || new FileInfo(path).Length > 64L * 1024 * 1024)
            throw new InvalidDataException("Choose a local wallpaper image under 64 MiB.");
        using var image = Image.FromFile(path);
        if ((long)image.Width * image.Height > 45_000_000 || image.Width < 2 || image.Height < 2)
            throw new InvalidDataException("Image dimensions are unsupported.");
        using var thumbnail = new Bitmap(24, 24);
        using (var g = Graphics.FromImage(thumbnail))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.DrawImage(image, 0, 0, thumbnail.Width, thumbnail.Height);
        }

        // Weighted toward the more saturated non-extreme portions so the accent
        // follows the landscape without becoming white from cloud highlights.
        double r = 0, green = 0, b = 0, total = 0;
        for (int y = 0; y < thumbnail.Height; y++)
            for (int x = 0; x < thumbnail.Width; x++)
            {
                var c = thumbnail.GetPixel(x, y);
                var max = Math.Max(c.R, Math.Max(c.G, c.B));
                var min = Math.Min(c.R, Math.Min(c.G, c.B));
                var luminosity = (max + min) / 2.0;
                if (luminosity < 26 || luminosity > 232) continue;
                double saturation = (max - min) / 255.0;
                var weight = 0.2 + saturation * saturation * 3;
                r += c.R * weight; green += c.G * weight; b += c.B * weight; total += weight;
            }
        if (total < 0.1) return Color.FromArgb(0, 160, 205);
        var candidate = Color.FromArgb((int)Math.Round(r / total),
            (int)Math.Round(green / total), (int)Math.Round(b / total));
        return EnsureVisibility(candidate);
    }

    public static Color EnsureVisibility(Color c)
    {
        var brightness = (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B);
        double shift = brightness < 70 ? 90.0 / Math.Max(1, brightness)
            : brightness > 205 ? 175.0 / brightness : 1.0;
        return Color.FromArgb(Math.Clamp((int)(c.R * shift), 0, 255),
            Math.Clamp((int)(c.G * shift), 0, 255),
            Math.Clamp((int)(c.B * shift), 0, 255));
    }

    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    public static string PreviewCurrent()
    {
        var settings = WallpaperSettings.Load();
        if (string.IsNullOrWhiteSpace(settings.ImagePath) || !File.Exists(settings.ImagePath))
            throw new FileNotFoundException("Select an installed local image wallpaper first.");
        return ToHex(Extract(settings.ImagePath));
    }

    public static void ApplyCurrent()
    {
        var accent = PreviewCurrent();
        OpsisRecoveryHub.Snapshot("Before changing Opsis wallpaper accent");
        var settings = AppSettings.Load();
        settings.WallpaperAccentHex = accent;
        settings.WallpaperAccentEnabled = true;
        settings.FollowWindowsAccent = false;
        settings.Save();
        MotionDesk.UI.ThemeManager.RefreshOpsisAccent();
    }

    public static void Disable()
    {
        var settings = AppSettings.Load();
        settings.WallpaperAccentEnabled = false;
        settings.Save();
        MotionDesk.UI.ThemeManager.RefreshOpsisAccent();
    }
}
