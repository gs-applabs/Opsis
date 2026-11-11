using MotionDesk.UI;
using Xunit;

namespace Opsis.Tests;

public sealed class PluginIconChooserTests
{
    [Theory]
    [InlineData("plugin:opsis.newscrawler", PluginIconKind.News)]
    [InlineData("plugin:opsis.appearance", PluginIconKind.Palette)]
    [InlineData("plugin:mediasuite.audio", PluginIconKind.Audio)]
    [InlineData("plugin:waveframe", PluginIconKind.Media)]
    [InlineData("plugin:gearwin", PluginIconKind.Letter)]
    public void KnownThemesGetTheirGlyphOthersGetALetter(string key, PluginIconKind expected) => Assert.Equal(expected, PluginIconChooser.Choose(key).Kind);

    [Fact]
    public void FutureDifferentPluginsGetDifferentLetters()
    {
        Assert.Equal('G', PluginIconChooser.Choose("plugin:gearwin").Letter);
        Assert.Equal('K', PluginIconChooser.Choose("plugin:kombos").Letter);
        Assert.Equal('+', PluginIconChooser.Choose("plugin:---").Letter);
    }
}
