using System.Runtime.InteropServices;
using MotionDesk.Widgets;
using Xunit;

namespace Opsis.Tests;

// Bug: after Explorer restarted the screen stayed black. The watcher that listens for the "TaskbarCreated" broadcast was a message-only window, which never receives broadcasts.
public sealed class ExplorerRestartWatcherTests
{
    private delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);

    private static void OnSta(Action a)
    {
        Exception? ex = null;
        var t = new Thread(() => { try { a(); } catch (Exception e) { ex = e; } }); t.SetApartmentState(ApartmentState.STA); t.Start(); t.Join();
        if (ex != null) throw ex;
    }

    [Fact]
    public void TheWatcherIsAHiddenTopLevelWindowSoItReceivesBroadcasts()
    {
        OnSta(() =>
        {
            using var w = new ExplorerRestartWatcher();
            bool found = false;
            EnumWindows((h, _) => { if (h == w.Handle) found = true; return true; }, IntPtr.Zero);      // EnumWindows lists top-level windows only, never message-only ones
            Assert.True(found, "the watcher must be a top-level window to receive the TaskbarCreated broadcast");
            Assert.False(IsWindowVisible(w.Handle));
        });
    }

    [Fact]
    public void TheTaskbarCreatedMessageRaisesTheRestartEvent()
    {
        OnSta(() =>
        {
            using var w = new ExplorerRestartWatcher();
            int raised = 0; w.ExplorerRestarted += () => raised++;
            SendMessage(w.Handle, w.TaskbarCreatedMessage, IntPtr.Zero, IntPtr.Zero);          // sent straight to the watcher, never as a machine-wide broadcast
            Assert.Equal(1, raised);
            SendMessage(w.Handle, 0x0001 /* WM_CREATE-like noise */ + 0x400, IntPtr.Zero, IntPtr.Zero);
            Assert.Equal(1, raised);
        });
    }
}

// Δ6: "CoreWebView2 members cannot be accessed after the WebView2 control is disposed" when Explorer restarts and the wallpaper windows are rebuilt.
public sealed class WebView2AfterDisposeTests
{
    private static void OnSta(Action a)
    {
        Exception? ex = null;
        var t = new Thread(() => { try { a(); } catch (Exception e) { ex = e; } }); t.SetApartmentState(ApartmentState.STA); t.Start(); t.Join();
        if (ex != null) throw ex;
    }

    [Fact]
    public void TryCoreIsNullForNullNotInitializedAndDisposedControls()
    {
        OnSta(() =>
        {
            Assert.Null(WebView2Support.TryCore(null));
            var v = new Microsoft.Web.WebView2.WinForms.WebView2();
            Assert.Null(WebView2Support.TryCore(v));
            v.Dispose();
            Assert.Null(WebView2Support.TryCore(v));       // used to throw InvalidOperationException on a disposed control
        });
    }

    [Fact]
    public void TwoRestartNotificationsInARowWithAHandlerThatTouchesADisposedControlDoNotThrow()
    {
        OnSta(() =>
        {
            using var w = new ExplorerRestartWatcher();
            var v = new Microsoft.Web.WebView2.WinForms.WebView2(); v.Dispose();
            int calls = 0;
            w.ExplorerRestarted += () => { calls++; _ = WebView2Support.TryCore(v)?.DocumentTitle; };
            w.RaiseForTest(); w.RaiseForTest();
            Assert.Equal(2, calls);
        });
    }

    [Fact]
    public void NoCodeReadsCoreWebView2OfAControlWithoutTheGuard()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "src", "Widgets"))) d = d.Parent;
        foreach (var f in new[] { "WallpaperWindow.cs", "WidgetEngine.cs" })
            foreach (var line in File.ReadAllLines(Path.Combine(d!.FullName, "src", "Widgets", f)))
                Assert.DoesNotContain("?.CoreWebView2", line);     // the null-conditional form skips only a null control, not a disposed one
    }
}

public sealed class ExplorerRestarterTests
{
    [Fact]
    public void WindowsBringsExplorerBackItIsNotStartedTwice()
    {
        int running = 1, started = 0;
        var ok = MotionDesk.Services.ExplorerRestarter.Restart(() => running = 0, () => running > 0, () => started++, ms => { if (running == 0) running = 1; });
        Assert.True(ok); Assert.Equal(0, started);
    }

    [Fact]
    public void WhenNothingComesBackItIsStartedExplicitly()
    {
        int running = 1, started = 0;
        var ok = MotionDesk.Services.ExplorerRestarter.Restart(() => running = 0, () => running > 0, () => { started++; running = 1; }, _ => { });
        Assert.True(ok); Assert.Equal(1, started);
    }

    [Fact]
    public void FailureToStartIsReportedNotThrown()
    {
        Assert.False(MotionDesk.Services.ExplorerRestarter.Restart(() => { }, () => false, () => throw new InvalidOperationException(), _ => { }));
        Assert.False(MotionDesk.Services.ExplorerRestarter.Restart(() => { }, () => false, () => { }, _ => { }));
    }
}
