using System;
using System.IO;
using System.Text.Json;

namespace MotionDesk.Services;

/// <summary>
/// Data-only local screensaver scene presets. Launches a native fullscreen Opsis
/// preview only when explicitly requested. Does not register an untrusted .scr
/// executable or replace Windows' built-in screen saver settings.
/// </summary>
public static class OpsisScreensaverStudio
{
    public sealed class Profile
    {
        public int Schema { get; set; } = 1;
        public string Mode { get; set; } = "Clock"; // Clock | StaticImage | ClockAndImage
        public string ImagePath { get; set; } = "";
        public int DimPercent { get; set; } = 20;
        public bool ShowSeconds { get; set; } = true;
    }
    private static string FilePath => Path.Combine(AppPaths.AppDataFolder, "screensaver-studio.json");
    private static readonly object Sync = new();

    public static void Validate(Profile? p)
    {
        if (p == null || p.Schema != 1 ||
            p.Mode is not ("Clock" or "StaticImage" or "ClockAndImage") ||
            p.DimPercent is < 0 or > 75 || p.ImagePath == null || p.ImagePath.Length > 2048)
            throw new InvalidDataException("Invalid screensaver scene.");
        if ((p.Mode is "StaticImage" or "ClockAndImage") &&
            (!Path.IsPathFullyQualified(p.ImagePath) ||
             Path.GetExtension(p.ImagePath).ToLowerInvariant() is not (".jpg" or ".jpeg" or ".png" or ".webp" or ".bmp")))
            throw new InvalidDataException("Select a local, supported image format.");
    }

    public static Profile Read()
    {
        lock (Sync)
        {
            if (!File.Exists(FilePath)) return new Profile();
            if (new FileInfo(FilePath).Length > 32 * 1024)
                throw new InvalidDataException("Screensaver profile is oversized.");
            var p = JsonSerializer.Deserialize<Profile>(AtomicFile.ReadAllText(FilePath))
                ?? throw new InvalidDataException("Screensaver settings are unreadable.");
            Validate(p);
            return p;
        }
    }

    public static void Save(Profile profile)
    {
        Validate(profile);
        if (profile.Mode != "Clock" &&
            (!File.Exists(profile.ImagePath) || new FileInfo(profile.ImagePath).Length > 64L * 1024 * 1024))
            throw new InvalidDataException("Screensaver image is missing or too large.");
        lock (Sync)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            OpsisRecoveryHub.Snapshot("Before screensaver scene changes");
            AtomicFile.WriteAllText(FilePath, JsonSerializer.Serialize(profile,
                new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
