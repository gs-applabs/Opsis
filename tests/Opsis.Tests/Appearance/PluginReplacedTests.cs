using MotionDesk.Services;
using Xunit;

namespace Opsis.Tests.AppearanceEngineTests;

public sealed class PluginReplacedTests
{
    [Fact]
    public void AFolderWithTheOldAppearancePluginIsNotLoadedByTheHost()
    {
        string dir = Path.Combine(Path.GetTempPath(), "opsis-replaced-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            Assert.False(PluginHost.IsReplacedByBuiltIn(dir));
            File.WriteAllBytes(Path.Combine(dir, "OpsisAppearancePlugin.dll"), new byte[] { 1 });
            Assert.True(PluginHost.IsReplacedByBuiltIn(dir));
            Assert.True(File.Exists(Path.Combine(dir, "OpsisAppearancePlugin.dll")));   // never deleted
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void TheInstallerRemovesTheOldAppearanceInsteadOfOfferingIt()
    {
        var d = AppContext.BaseDirectory;
        while (d != null && !File.Exists(Path.Combine(d, "installer.iss"))) d = Path.GetDirectoryName(d);
        Assert.NotNull(d);
        string iss = File.ReadAllText(Path.Combine(d!, "installer.iss"));
        Assert.Contains("5D2F7B1C-8E43-4A9B-B6C0-71A3E9D4F528", iss);        // the Inno AppId of the old Opsis Appearance
        Assert.Contains("RemoveOldAppearance", iss);
        Assert.DoesNotContain("installappearance", iss);                      // no more "download and install Opsis Appearance"
        Assert.DoesNotContain("AppearanceSetupUrl", iss);
        foreach (var lang in new[] { "greek", "english", "german", "french", "spanish", "korean", "chinese", "italian", "russian", "japanese", "portuguese", "turkish", "arabic", "hindi" })
            Assert.Contains(lang + ".RemoveOldAppearance=", iss);
    }

    [Fact]
    public void TheInstallerFollowsTheWindowsDarkTheme()
    {
        var d = AppContext.BaseDirectory;
        while (d != null && !File.Exists(Path.Combine(d, "installer.iss"))) d = Path.GetDirectoryName(d);
        string iss = File.ReadAllText(Path.Combine(d!, "installer.iss"));
        Assert.Contains("WizardStyle=modern dynamic", iss);
        Assert.Contains("WizardImageFileDynamicDark=", iss);
        Assert.Contains("WizardSmallImageFileDynamicDark=", iss);
        foreach (var f in new[] { "dark-large-100.bmp", "dark-large-125.bmp", "dark-large-150.bmp", "dark-large-200.bmp", "dark-small-100.png", "dark-small-125.png", "dark-small-150.png", "dark-small-200.png" })
            Assert.True(File.Exists(Path.Combine(d!, "installer", "wizard", f)), f);
    }
}
