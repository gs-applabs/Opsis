using MotionDesk.Services;
using Xunit;

namespace Opsis.Tests;

public sealed class StartupPhasesTests
{
    [Fact]
    public void DeferredStepsRunOneAtATimeInQueueOrder()
    {
        using var q = new DeferredStartup(); var order = new List<string>();
        q.Add("wallpaper", () => order.Add("wallpaper")); q.Add("containers", () => order.Add("containers")); q.Add("widgets", () => order.Add("widgets"));
        Assert.Equal(3, q.Pending);
        Assert.True(q.Step()); Assert.Equal(new[] { "wallpaper" }, order);              // nothing else ran in the same step
        Assert.True(q.Step()); Assert.False(q.Step());
        Assert.Equal(new[] { "wallpaper", "containers", "widgets" }, order);
        Assert.False(q.Step());
    }

    [Fact]
    public void AFailingStepIsRecordedAndDoesNotStopTheOthers()
    {
        using var q = new DeferredStartup(); bool second = false;
        q.Add("bad-step", () => throw new InvalidOperationException("boom")); q.Add("good-step", () => second = true);
        q.Step(); q.Step();
        Assert.True(second);
        Assert.Contains(StartupPhases.Snapshot(), p => p.Name == "deferred: bad-step");
    }

    [Fact]
    public void MarkRecordsPhasesWithTheirTimeAndThread()
    {
        StartupPhases.Mark("test-phase-a"); Thread.Sleep(30); StartupPhases.Mark("test-phase-b");
        var b = StartupPhases.Snapshot().Last(p => p.Name == "test-phase-b");
        Assert.InRange(b.Ms, 25, 2000); Assert.Equal(Environment.CurrentManagedThreadId, b.Thread);
    }

    [Fact]
    public void TheHeavyStartupPhasesAreQueuedNotRunInTheConstructor()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "src", "Widgets"))) d = d.Parent;
        var src = File.ReadAllText(Path.Combine(d!.FullName, "src", "Widgets", "TrayApplicationContext.cs"));
        Assert.Contains("StartupPhases.Queue.Add(\"wallpaper\"", src);
        Assert.Contains("StartupPhases.Queue.Add(\"native containers\"", src);
        Assert.Contains("StartupPhases.Queue.Add(\"desktop icon engine", src);
        Assert.Equal(1, src.Split("StartupPhases.Queue.Start(").Length - 1);    // one start, one mechanism
        var main = File.ReadAllText(Path.Combine(d.FullName, "src", "UI", "MainWindow.cs"));
        Assert.Contains("StartupPhases.Queue.Add(\"last session", main);
    }
}
