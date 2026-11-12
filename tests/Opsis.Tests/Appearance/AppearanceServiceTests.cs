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
