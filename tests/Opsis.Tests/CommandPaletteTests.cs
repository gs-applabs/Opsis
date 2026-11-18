using MotionDesk.UI;
using Xunit;

namespace Opsis.Tests;

public class CommandPaletteTests
{
    [Fact]
    public void ExactPrefixAndSubstringMatchesPrecedeFuzzyMatches()
    {
        CommandPaletteEntry[] entries =
        [
            new("fuzzy", "Previewer"),
            new("contains", "System Power"),
            new("prefix", "Power settings"),
            new("exact", "Power")
        ];

        Assert.Equal(new[] { "exact", "prefix", "contains" },
            CommandPaletteEntry.Find(entries, "power").Select(entry => entry.Id));
        Assert.Equal(new[] { "fuzzy", "contains", "prefix", "exact" },
            CommandPaletteEntry.Find(entries, "pwr").Select(entry => entry.Id));
    }

    [Theory]
    [InlineData("vs", "Visual Studio")]
    [InlineData("  εικονα  ", "Εικόνα κλειδώματος")]
    [InlineData("βιντεο\tστούντιο", "Στούντιο βίντεο")]
    [InlineData("wm", "Wallpaper-Media")]
    public void SearchFindsAbbreviationsAndLocalizedLabels(string query, string label)
    {
        Assert.Single(CommandPaletteEntry.Find([new("command", label)], query));
    }

    [Fact]
    public void CommandsWithSameLabelKeepTheirOwnIdentities()
    {
        CommandPaletteEntry[] entries =
        [
            new("Settings", "Tools"),
            new("plugin:Tools", "Tools")
        ];

        Assert.Equal(entries, CommandPaletteEntry.Find(entries, "tools"));
        Assert.Equal(entries, CommandPaletteEntry.Find(entries, " \t"));
    }
}
