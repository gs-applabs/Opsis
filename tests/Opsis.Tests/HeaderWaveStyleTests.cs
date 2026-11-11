using MotionDesk.UI;
using Xunit;

namespace Opsis.Tests;

// Regression: on the dark theme the animated waves behind the page title were too faint to see (fixed alpha 38 = ~30 levels of contrast).
public sealed class HeaderWaveStyleTests
{
    public static IEnumerable<object[]> Palettes()
    {
        yield return new object[] { "dark 0.5", UiPalette.Dark(0.5) };
        yield return new object[] { "dark 0.0", UiPalette.Dark(0.0) };
        yield return new object[] { "dark 1.0", UiPalette.Dark(1.0) };
        yield return new object[] { "light", UiPalette.Light() };
    }

    [Theory]
    [MemberData(nameof(Palettes))]
    public void EveryLineIsClearlyVisibleOnEveryTheme(string name, UiPalette p)
    {
        var colors = new[] { p.AccentCyan, p.AccentBlue, p.AccentCyan };
        for (int i = 0; i < 3; i++)
        {
            int a = HeaderWaveStyle.Alpha(p.Background, colors[i], i);
            Assert.InRange(a, 30, 150);
            Assert.True(HeaderWaveStyle.Contrast(p.Background, colors[i], a) >= 28, $"{name} line {i}: contrast {HeaderWaveStyle.Contrast(p.Background, colors[i], a):0.#}");
        }
        Assert.True(HeaderWaveStyle.Contrast(p.Background, p.AccentCyan, HeaderWaveStyle.Alpha(p.Background, p.AccentCyan, 0)) >= 40, name);
    }

    [Fact]
    public void TheOldFixedAlphaWasTooFaintOnTheDarkTheme()
    {
        var p = UiPalette.Dark(0.5);
        Assert.True(HeaderWaveStyle.Contrast(p.Background, p.AccentCyan, 38) < 40);
    }
}
