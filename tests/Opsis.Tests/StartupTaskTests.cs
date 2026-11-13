using MotionDesk.Services;
using Xunit;

namespace Opsis.Tests;

public sealed class StartupTaskTests
{
    [Fact]
    public void TheTaskIsPerUserLimitedAndStartsAtLogonWithoutDelay()
    {
        string xml = StartupTask.BuildXml(@"C:\Program Files\Opsis & Co\Opsis.exe", @"PC\Ana <x>");
        var doc = System.Xml.Linq.XDocument.Parse(xml);               // well-formed even with & and < in the names
        Assert.Contains("<LogonTrigger>", xml);
        Assert.DoesNotContain("<Delay>", xml);
        Assert.Contains("<RunLevel>LeastPrivilege</RunLevel>", xml);
        Assert.Contains("<Arguments>--background</Arguments>", xml);
        Assert.Contains("Opsis &amp; Co", xml);
        Assert.Contains("Ana &lt;x&gt;", xml);
        Assert.NotNull(doc.Root);
    }

    [Fact]
    public void AnIsolatedRunNeverTouchesTheTaskScheduler()
    {
        Assert.True(StartupTask.Isolated);
        Assert.True(StartupTask.Set(true));
        Assert.False(StartupTask.IsEnabled());
        Assert.StartsWith("/Create /TN", StartupTask.CreateArguments("x.xml"));
        Assert.StartsWith("/Delete /TN", StartupTask.DeleteArguments);
    }
}
