using MotionDesk.Widgets;
using Xunit;

namespace Opsis.Tests;

// An isolated run (OPSIS_APPDATA) must not share the WebView2 browser profile of the real installation: sharing it made the wallpaper fail with 0x800700AA while the real Opsis was running.
public sealed class WebView2UserDataTests
{
    [Fact]
    public void IsolatedRunUsesItsOwnProfileInsideTheIsolatedFolder()
    {
        Assert.Equal(Path.Combine(@"C:\temp\iso", "WebView2"), WebView2Support.UserDataFolder(@"C:\temp\iso", @"C:\Users\x\AppData\Local"));
    }

    [Fact]
    public void NormalRunKeepsTheLocalAppDataProfile()
    {
        Assert.Equal(Path.Combine(@"C:\Users\x\AppData\Local", "Opsis", "WebView2"), WebView2Support.UserDataFolder("", @"C:\Users\x\AppData\Local"));
    }
}
