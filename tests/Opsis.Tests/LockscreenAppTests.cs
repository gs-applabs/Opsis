using System.Text.RegularExpressions;
using MotionDesk.Services;
using Opsis.Lockscreen;
using Xunit;

namespace Opsis.Tests;

public sealed class LockscreenAppTests
{
    private static string Root()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !(Directory.Exists(Path.Combine(d.FullName, "core")) && Directory.Exists(Path.Combine(d.FullName, "src")))) d = d.Parent;
        return d?.FullName ?? @"C:\w\Opsis";
    }
    private static string Read(params string[] p) => File.ReadAllText(Path.Combine(new[] { Root() }.Concat(p).ToArray()));

    [Fact]
    public void OptInIsOffByDefault() => Assert.False(new AppSettings().LockscreenGifConsent);

    [Fact]
    public async Task IsolatedRunsNeverStartTheElevatedHelper()
    {
        var r = await LockscreenGifService.RunHelperAsync("apply", isolatedOverride: true);     // no environment variable is touched: tests run in parallel
        Assert.False(r.Ok); Assert.Equal("Isolated", r.Code);
    }

    [Fact]
    public async Task OnlyKnownHelperCommandsAreAccepted()
    {
        var r = await LockscreenGifService.RunHelperAsync("apply C:\\x", isolatedOverride: false);   // refused before anything is started
        Assert.Equal("BadCommand", r.Code);
    }

    [Theory]
    [InlineData(1, null, true)]   // slideshow off, spotlight unset is NOT enough
    [InlineData(0, 0, true)]
    [InlineData(1, 0, false)]     // slideshow on
    [InlineData(0, 1, false)]     // spotlight on
    public void PictureModeInference(int slideshow, int? spotlight, bool pictureExpected)
    {
        var mode = Prerequisites.InferMode(slideshow == 1 && spotlight == null ? null : slideshow, spotlight, slideshowValueMissing: false);
        if (slideshow == 1 && spotlight == null) { Assert.Equal(LockScreenMode.Unknown, mode); return; }
        Assert.Equal(pictureExpected, mode == LockScreenMode.PictureOrOther);
    }

    [Fact]
    public void SlideshowSpotlightAndConflictsAreRecognised()
    {
        Assert.Equal(LockScreenMode.Slideshow, Prerequisites.InferMode(1, 0));
        Assert.Equal(LockScreenMode.Spotlight, Prerequisites.InferMode(0, 1));
        Assert.Equal(LockScreenMode.Unknown, Prerequisites.InferMode(1, 1));
        Assert.Equal(LockScreenMode.PictureOrOther, Prerequisites.InferMode(null, 0, slideshowValueMissing: true));
        Assert.Equal(LockScreenMode.PictureOrOther, Prerequisites.InferMode(new byte[] { 0 }, 0));
        Assert.Equal(LockScreenMode.Unknown, Prerequisites.InferMode(null, null));
    }

    [Fact]
    public void FeatureOnlyAppliesFromBuild26200()
    {
        Assert.False(Prerequisites.FeatureApplies(26100)); Assert.True(Prerequisites.FeatureApplies(26200)); Assert.True(Prerequisites.FeatureApplies(26300));
    }

    [Fact]
    public void CachePresentNeedsALockScreenFolder()
    {
        string d = Path.Combine(Path.GetTempPath(), "opsis-cp-" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.False(Prerequisites.CachePresent(d));
            Directory.CreateDirectory(Path.Combine(d, "Other")); Assert.False(Prerequisites.CachePresent(d));
            Directory.CreateDirectory(Path.Combine(d, "LockScreen_x")); Assert.True(Prerequisites.CachePresent(d));
        }
        finally { try { Directory.Delete(d, true); } catch (Exception) { } }
    }

    // ---- video to GIF: FFmpeg only, arguments as a list ------------------------------------------------------------------------------------------
    [Fact]
    public void FfmpegArgsUsePalettegenThenPaletteuseAndKeepPathsAsSingleArguments()
    {
        var o = new FfmpegGif.Options(@"C:\My Videos\clip one.mp4", 1.5, 4, 1280, 15);
        var p1 = FfmpegGif.PaletteArgs(o, @"C:\t\palette.png"); var p2 = FfmpegGif.GifArgs(o, @"C:\t\palette.png", @"C:\t\o.gif");
        Assert.Contains(p1, a => a.Contains("palettegen")); Assert.Contains(p2, a => a.Contains("paletteuse"));
        Assert.Contains(@"C:\My Videos\clip one.mp4", p1); Assert.Contains(@"C:\My Videos\clip one.mp4", p2);     // one element: no shell splitting
        Assert.Equal("1.5", p1[p1.IndexOf("-ss") + 1]); Assert.Equal("4", p1[p1.IndexOf("-t") + 1]);              // invariant culture
        Assert.Equal("0", p2[p2.IndexOf("-loop") + 1]);
        Assert.DoesNotContain(p1.Concat(p2), a => a.Contains("gifski", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("-version", 0, 5, 640, 15)] [InlineData("", 0, 5, 640, 15)] [InlineData("a.mp4", -1, 5, 640, 15)] [InlineData("a.mp4", 0, 0, 640, 15)]
    [InlineData("a.mp4", 0, 31, 640, 15)] [InlineData("a.mp4", 0, 5, 10, 15)] [InlineData("a.mp4", 0, 5, 640, 0)] [InlineData("a.mp4", 0, 5, 640, 99)]
    public void FfmpegOptionsAreValidated(string input, double start, double dur, int width, int fps) =>
        Assert.Throws<ArgumentException>(() => FfmpegGif.PaletteArgs(new FfmpegGif.Options(input, start, dur, width, fps), "p.png"));

    [Fact]
    public void OnlyAnExistingFfmpegExeIsAccepted()
    {
        string d = Path.Combine(Path.GetTempPath(), "opsis-ff-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(d);
        try
        {
            string good = Path.Combine(d, "ffmpeg.exe"), other = Path.Combine(d, "notffmpeg.exe");
            File.WriteAllText(good, "x"); File.WriteAllText(other, "x");
            Assert.True(FfmpegGif.LooksLikeFfmpeg(good)); Assert.False(FfmpegGif.LooksLikeFfmpeg(other)); Assert.False(FfmpegGif.LooksLikeFfmpeg(Path.Combine(d, "missing", "ffmpeg.exe")));
            Assert.True(FfmpegGif.VersionOutputIsFfmpeg("ffmpeg version 7.1 Copyright")); Assert.False(FfmpegGif.VersionOutputIsFfmpeg("malware 1.0"));
        }
        finally { try { Directory.Delete(d, true); } catch (Exception) { } }
    }

    // ---- wiring and safety rules --------------------------------------------------------------------------------------------------------------
    [Fact]
    public void CommandLineRestoreInstallerPromptAndHelperAreWired()
    {
        Assert.Contains("--restore-lockscreen", Read("src", "Program.cs"));
        string iss = Read("installer.iss");
        Assert.Contains("OpsisLockHelper.exe", iss);
        int languages = Regex.Matches(iss, @"^Name: ""[a-z]+""; MessagesFile", RegexOptions.Multiline).Count;
        Assert.Equal(languages, Regex.Matches(iss, @"^[a-z]+\.RestoreLockscreenPrompt=", RegexOptions.Multiline).Count);   // offered in every installer language
        Assert.Contains("OpsisLockHelper.csproj", Read("MotionDeskStudio.csproj"));
        Assert.Contains("requireAdministrator", Read("tools", "OpsisLockHelper", "app.manifest"));
    }

    [Fact]
    public void HelperTakesOnlyTheCommandWordAndOpsisItselfNeverElevates()
    {
        string helper = Read("tools", "OpsisLockHelper", "Program.cs");
        Assert.Contains("HelperCommand.TryParse(args", helper);
        Assert.DoesNotContain("args[1]", helper); Assert.DoesNotContain("Process.Start", helper); Assert.DoesNotContain("GetEnvironmentVariable(\"OPSIS_LOCKSCREEN", helper);
        string svc = Read("src", "Services", "LockscreenGifService.cs");
        Assert.Contains("Verb = \"runas\"", svc); Assert.Contains("OpsisLockHelper.exe", svc);
        Assert.DoesNotContain("--set-lockscreen", svc);
    }

    [Fact]
    public void NoTelemetryOrNetworkCodeInTheLockScreenFeature()
    {
        var files = Directory.GetFiles(Path.Combine(Root(), "core", "Opsis.Lockscreen.Core"), "*.cs").Concat(Directory.GetFiles(Path.Combine(Root(), "tools", "OpsisLockHelper"), "*.cs"))
            .Concat(new[] { Path.Combine(Root(), "src", "Services", "LockscreenGifService.cs"), Path.Combine(Root(), "src", "UI", "MainWindow.LockscreenGif.cs") });
        foreach (var f in files)
        {
            string t = File.ReadAllText(f);
            foreach (var bad in new[] { "HttpClient", "WebClient", "posthog", "PostHog", "Socket", "http://", "https://", "gifski.exe" })
                Assert.False(t.Contains(bad), f + " contains " + bad);
        }
    }

    [Fact]
    public void LockscreenGifLicenceNoticeIsShipped()
    {
        string n = Read("THIRD_PARTY_NOTICES.md");
        Assert.Contains("Leapward-Koex", n); Assert.Contains("Copyright (c) 2026 Leapward-Koex", n); Assert.Contains("Permission is hereby granted", n);
    }

    [Fact]
    public void LogsGoToTheOpsisLogFolderOnly()
    {
        string svc = Read("src", "Services", "LockscreenGifService.cs"), ui = Read("src", "UI", "MainWindow.LockscreenGif.cs");
        Assert.Contains("RotatingLogger.Write", svc);
        Assert.Contains("Path.Combine(AppPaths.AppDataFolder, \"logs\")", ui);
        Assert.DoesNotContain("File.AppendAllText", svc);
    }
}
