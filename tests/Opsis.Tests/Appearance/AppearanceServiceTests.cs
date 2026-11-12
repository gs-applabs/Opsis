using System.Runtime.CompilerServices;
using MotionDesk.Appearance;
using Xunit;

namespace Opsis.Tests.AppearanceEngineTests;

/// <summary>The test run must never write into the real %AppData%\Opsis (settings, themes, backups of the person running the tests).</summary>
internal static class TestDataIsolation
{
    [ModuleInitializer]
    internal static void Init()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPSIS_APPDATA")))
            Environment.SetEnvironmentVariable("OPSIS_APPDATA", Path.Combine(Path.GetTempPath(), "opsis-tests-" + Environment.ProcessId));
    }
}

public sealed class AppearanceServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "opsis-appearance-svc-" + Guid.NewGuid().ToString("N"));
    public AppearanceServiceTests() { Directory.CreateDirectory(_dir); }
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (Exception) { } }

    private (AppearanceService Svc, FakeRegistry Reg, FakeWallpaper Wall) Make()
    {
        var reg = new FakeRegistry(); var wall = new FakeWallpaper();
        var svc = new AppearanceService(reg, new FakeDwm(), new FakeTaskbar(false), wall, Path.Combine(_dir, "themes"), new FakeSurface(), new FakeRestorePoints(), Path.Combine(_dir, "vault"));
        return (svc, reg, wall);
    }

    [Fact]
    public void TheScheduleFollowsTheFixedHoursAndBacksUpTheOriginalValuesFirst()
    {
        var (svc, reg, _) = Make();
        var now = DateTime.Now;
        svc.Settings.Mode = ThemeMode.AutoFixed;
        svc.Settings.LightAt = now.AddHours(1).ToString("HH:mm"); svc.Settings.DarkAt = now.AddHours(2).ToString("HH:mm");
        svc.ApplySchedule();
        Assert.Equal(AppearanceEngine.ShouldBeDark(svc.Settings, DateTime.Now), svc.Engine.IsDark);
        if (svc.Engine.IsDark) Assert.True(svc.Store.HasBackup);   // the first change saved the user's own values
    }

    [Fact]
    public void ManualModeNeverTouchesTheTheme()
    {
        var (svc, reg, _) = Make();
        svc.Settings.Mode = ThemeMode.Manual;
        svc.ApplySchedule();
        Assert.Empty(reg.Data);
    }

    [Fact]
    public void SlideshowChangesTheWallpaperAndStoppingPutsTheOriginalBack()
    {
        var (svc, _, wall) = Make();
        string pics = Path.Combine(_dir, "pics"); Directory.CreateDirectory(pics);
        File.WriteAllBytes(Path.Combine(pics, "a.jpg"), new byte[] { 1 }); File.WriteAllBytes(Path.Combine(pics, "b.jpg"), new byte[] { 2 });
        string original = Path.Combine(_dir, "orig.jpg"); File.WriteAllBytes(original, new byte[] { 3 });
        wall.Path = original;
        svc.Settings.Slideshow.Enabled = true; svc.Settings.Slideshow.Folder = pics; svc.Settings.Slideshow.Shuffle = false;
        Assert.True(svc.SlideshowTick(DateTime.UtcNow, force: true));
        Assert.StartsWith(pics, wall.Path!);
        Assert.Equal(original, svc.Settings.Slideshow.OriginalPath);
        svc.StopSlideshow(restoreWallpaper: true);
        Assert.Equal(original, wall.Path);
        Assert.False(svc.Settings.Slideshow.Enabled);
        svc.Stop();
    }

    [Fact]
    public void ResettingACategoryOnlyResetsThatCategory()
    {
        var (svc, _, _) = Make();
        svc.Settings.Effects.ActiveBorder = true; svc.Settings.Slideshow.IntervalMinutes = 5; svc.Settings.Mode = ThemeMode.AutoSun;
        svc.ResetCategory(2);
        Assert.False(svc.Settings.Effects.ActiveBorder);
        Assert.Equal(5, svc.Settings.Slideshow.IntervalMinutes);
        Assert.Equal(ThemeMode.AutoSun, svc.Settings.Mode);
        svc.Stop();
    }

    [Fact]
    public void ARestorePointIsRequestedOnlyWhenAskedAndSomethingInvasiveIsOn()
    {
        var (svc, _, _) = Make();
        svc.Settings.TaskbarLook.Enabled = true;
        Assert.Null(svc.ApplyTaskbar(askForRestorePoint: false));
        var r = svc.ApplyTaskbar(askForRestorePoint: true);
        Assert.NotNull(r);
        svc.Stop();
    }

    [Fact]
    public void AnIsolatedRunNeverTouchesTheRealWindows()
    {
        Assert.True(AppearanceService.IsolatedRun);   // the module initializer above sets OPSIS_APPDATA for the whole test run
        object? Real() => Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", null);
        var before = Real();
        var svc = new AppearanceService();
        svc.Engine.Apply(new Look { Dark = before is int b && b != 0, Transparency = true });   // the opposite of "no change" is irrelevant: it must go to memory
        svc.Engine.Apply(new Look { Dark = !(before is int b2 && b2 != 0) });
        Assert.Equal(before, Real());
        Assert.Equal(!(before is int b3 && b3 != 0), svc.Engine.IsDark);   // the in-memory store took the change
        svc.Stop();
    }

    [Fact]
    public void StopIsSafeWithoutStartAndRepeatable()
    {
        var (svc, _, _) = Make();
        svc.Stop(); svc.Stop();
        Assert.False(svc.Started);
    }
}

public sealed class InstanceGuardTests
{
    [Fact]
    public void ASecondGuardWithTheSameNameIsRefusedUntilReleased()
    {
        string name = "OpsisAppearance.Test." + Guid.NewGuid().ToString("N");
        using var a = new InstanceGuard(); using var b = new InstanceGuard();
        Assert.True(a.TryAcquire(name)); Assert.True(a.IsOwner);
        bool second = true; var t = new Thread(() => second = b.TryAcquire(name)); t.Start(); t.Join();
        Assert.False(second); Assert.False(b.IsOwner);
        Assert.Equal("OpsisAppearance.Engine", InstanceGuard.DefaultName);   // the name the old stand-alone app / installer use
    }
}

public sealed class AppearanceCachesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "opsis-appearance-cache-" + Guid.NewGuid().ToString("N"));
    public AppearanceCachesTests() { Directory.CreateDirectory(_dir); }
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (Exception) { } }

    [Fact]
    public void TheSlideshowListIsRebuiltOnlyWhenTheFolderChanges()
    {
        File.WriteAllBytes(Path.Combine(_dir, "a.jpg"), new byte[] { 1 });
        var first = WallpaperSlideshow.Images(_dir);
        Assert.Single(first);
        first.Add("tamper");                                                    // callers get a copy, the cache is safe
        Assert.Single(WallpaperSlideshow.Images(_dir));
        File.WriteAllBytes(Path.Combine(_dir, "b.png"), new byte[] { 2 });
        Directory.SetLastWriteTimeUtc(_dir, DateTime.UtcNow.AddMinutes(1));          // make the change visible even on a coarse clock
        Assert.Equal(2, WallpaperSlideshow.Images(_dir).Count);
    }

    [Fact]
    public void TheWallpaperColourIsReadFromASmallSampleAndRemembered()
    {
        string pic = Path.Combine(_dir, "red.png");
        using (var bmp = new System.Drawing.Bitmap(200, 200)) { using var g = System.Drawing.Graphics.FromImage(bmp); g.Clear(System.Drawing.Color.FromArgb(220, 30, 30)); bmp.Save(pic, System.Drawing.Imaging.ImageFormat.Png); }
        var reg = new FakeRegistry(); reg.Set(@"Control Panel\Desktop", "WallPaper", pic, Microsoft.Win32.RegistryValueKind.String);
        var engine = new AppearanceEngine(reg, () => { });
        int? a = engine.WallpaperAccent();
        Assert.NotNull(a);
        Assert.True(((a!.Value >> 16) & 0xFF) > 150);                           // red dominates
        File.Delete(pic);                                                       // remembered: no second decode needed while the file is unchanged... the file is gone now
        Assert.Null(engine.WallpaperAccent());                                  // a missing file gives null, never an exception
    }
}

public sealed class SettingsPeekTests
{
    [Fact]
    public void PeekIsSharedForAShortTimeAndASaveRefreshesIt()
    {
        Assert.True(MotionDesk.Services.AppSettings.IsIsolatedForTests());
        var a = MotionDesk.Services.AppSettings.Peek();
        Assert.Same(a, MotionDesk.Services.AppSettings.Peek());
        var edit = MotionDesk.Services.AppSettings.Load();
        bool before = edit.UseGpuGlass;
        edit.UseGpuGlass = !before; edit.Save();
        Assert.Equal(!before, MotionDesk.Services.AppSettings.Peek().UseGpuGlass);   // the Save dropped the snapshot
        edit.UseGpuGlass = before; edit.Save();
    }
}
