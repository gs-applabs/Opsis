namespace MotionDesk.UI
{
    public enum PluginIconKind { Letter, News, Media, Audio, Palette }

    /// <summary>Pure choice of the side-menu glyph of a plugin page from its key ("plugin:..."), so every plugin tab is distinctive without per-plugin code (plan 2.1.1).</summary>
    public static class PluginIconChooser
    {
        public static (PluginIconKind Kind, char Letter) Choose(string navKey)
        {
            var k = navKey.StartsWith("plugin:", StringComparison.Ordinal) ? navKey[7..] : navKey;
            var low = k.ToLowerInvariant();
            if (low.Contains("news") || low.Contains("crawler") || low.Contains("feed") || low.Contains("ticker")) return (PluginIconKind.News, 'N');
            if (low.Contains("appearance") || low.Contains("theme")) return (PluginIconKind.Palette, 'A');
            if (low.Contains("audio") || low.Contains("sound")) return (PluginIconKind.Audio, 'A');
            if (low.Contains("media") || low.Contains("waveframe") || low.Contains("player")) return (PluginIconKind.Media, 'M');
            var letter = k.FirstOrDefault(char.IsLetterOrDigit);
            return (PluginIconKind.Letter, letter == default ? '+' : char.ToUpperInvariant(letter));
        }
    }
}
