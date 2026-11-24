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

public sealed class WebView2ProfileKeyTests
{
    [Fact]
    public void ProfileIsKeyedByTheBrowserArguments()
    {
        string a = WebView2Support.UserDataFolder("", @"C:\L", "--x");
        Assert.Equal(a, WebView2Support.UserDataFolder("", @"C:\L", "--x"));                 // same arguments, same folder (instances of one edition share)
        Assert.NotEqual(a, WebView2Support.UserDataFolder("", @"C:\L", "--y"));              // another edition with other arguments never fights over it
        Assert.StartsWith(Path.Combine(@"C:\L", "Opsis", "WebView2") + Path.DirectorySeparatorChar + "p", a);
    }
}
