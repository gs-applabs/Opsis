using System.Globalization;

namespace Opsis.Lockscreen;

/// <summary>
/// Video to GIF with FFmpeg only (two passes: palettegen, then paletteuse). FFmpeg is NOT shipped with Opsis: the user picks an existing ffmpeg.exe.
/// Arguments are built as a list (never a command line string) from validated numbers and paths.
/// </summary>
public static class FfmpegGif
{
    public const int MaxSeconds = 30;

    public sealed record Options(string Input, double StartSeconds, double DurationSeconds, int Width, int Fps);

    public static void Validate(Options o)
    {
        if (string.IsNullOrWhiteSpace(o.Input) || o.Input.StartsWith("-")) throw new ArgumentException("Input path");
        if (o.StartSeconds < 0 || o.StartSeconds > 86400) throw new ArgumentException("Start");
        if (o.DurationSeconds <= 0 || o.DurationSeconds > MaxSeconds) throw new ArgumentException("Duration must be 0.1 .. " + MaxSeconds + " seconds");
        if (o.Width < 64 || o.Width > 3840) throw new ArgumentException("Width");
        if (o.Fps < 1 || o.Fps > 30) throw new ArgumentException("Fps");
    }

    private static string N(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Filter(Options o) => "fps=" + o.Fps + ",scale=" + o.Width + ":-2:flags=lanczos";

    /// <summary>Pass 1: the optimal 256-colour palette of the clip.</summary>
    public static List<string> PaletteArgs(Options o, string palettePng)
    {
        Validate(o);
        return new List<string> { "-hide_banner", "-loglevel", "error", "-y", "-ss", N(o.StartSeconds), "-t", N(o.DurationSeconds), "-i", o.Input, "-an", "-sn", "-vf", Filter(o) + ",palettegen=stats_mode=diff", palettePng };
    }

    /// <summary>Pass 2: the GIF itself, using that palette; loops forever.</summary>
    public static List<string> GifArgs(Options o, string palettePng, string outGif)
    {
        Validate(o);
        return new List<string> { "-hide_banner", "-loglevel", "error", "-y", "-ss", N(o.StartSeconds), "-t", N(o.DurationSeconds), "-i", o.Input, "-i", palettePng, "-an", "-sn",
            "-lavfi", Filter(o) + " [x]; [x][1:v] paletteuse=dither=bayer:bayer_scale=5:diff_mode=rectangle", "-loop", "0", outGif };
    }

    /// <summary>The picked file must be called ffmpeg.exe and exist (it is then asked for its version before the first use).</summary>
    public static bool LooksLikeFfmpeg(string? path) =>
        !string.IsNullOrWhiteSpace(path) && string.Equals(Path.GetFileName(path), "ffmpeg.exe", StringComparison.OrdinalIgnoreCase) && File.Exists(path);

    public static bool VersionOutputIsFfmpeg(string output) => output.TrimStart().StartsWith("ffmpeg version", StringComparison.OrdinalIgnoreCase);
}
