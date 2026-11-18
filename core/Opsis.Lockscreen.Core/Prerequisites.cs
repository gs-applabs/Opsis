using Microsoft.Win32;

namespace Opsis.Lockscreen;

public enum LockScreenMode { Unknown, PictureOrOther, Slideshow, Spotlight }

/// <summary>Read-only checks shown in the prerequisites panel (never write, never elevate). The mode inference is adapted from LockscreenGif (MIT, (c) 2026 Leapward-Koex).</summary>
public static class Prerequisites
{
    /// <summary>The Windows build from which the animation-blocking feature matters (LockscreenGif documents 26200+).</summary>
    public const int FeatureBuild = 26200;

    public static LockScreenMode InferMode()
    {
        try
        {
            using var settings = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Lock Screen");
            using var delivery = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager");
            var slideshow = settings?.GetValue("SlideshowEnabled");
            var spotlight = delivery?.GetValue("RotatingLockScreenEnabled");
            bool missing = settings is not null && slideshow is null && !settings.GetValueNames().Contains("SlideshowEnabled", StringComparer.OrdinalIgnoreCase);
            return InferMode(slideshow, spotlight, missing);
        }
        catch (Exception) { return LockScreenMode.Unknown; }
    }

    public static LockScreenMode InferMode(object? slideshow, object? spotlight, bool slideshowValueMissing = false)
    {
        bool? slide = Switch(slideshow), rotate = Switch(spotlight);
        if (slide == true && rotate == true) return LockScreenMode.Unknown;
        if (slide == true) return LockScreenMode.Slideshow;
        if (rotate == true) return LockScreenMode.Spotlight;
        return (slide == false || (slideshow is null && slideshowValueMissing)) && rotate == false ? LockScreenMode.PictureOrOther : LockScreenMode.Unknown;
    }

    private static bool? Switch(object? v) => v switch
    {
        int n when n is 0 or 1 => n == 1,
        byte[] { Length: 1 } b when b[0] is 0 or 1 => b[0] == 1,
        _ => null,
    };

    /// <summary>true when at least one LockScreen* folder exists in the cache (listing is allowed for the user; false also when it cannot be read).</summary>
    public static bool CachePresent(string cacheRoot)
    {
        try { return Directory.Exists(cacheRoot) && Directory.GetDirectories(cacheRoot, "LockScreen*").Length > 0; }
        catch (Exception) { return false; }
    }

    public static bool FeatureApplies(int windowsBuild) => windowsBuild >= FeatureBuild;
}
