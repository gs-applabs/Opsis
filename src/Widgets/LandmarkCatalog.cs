namespace MotionDesk.Widgets
{
    /// <summary>The landmarks that have a photograph (landmarks/&lt;id&gt;.webp), for the choice list of the Wallpaper page. Must match SPRITE_IDS in wallpaper/landmarks.js (a test checks it).</summary>
    public static class LandmarkCatalog
    {
        public sealed record Entry(string Id, string Name);

        public static readonly Entry[] Items =
        {
            new("parthenon", "Parthenon, Athens"),
            new("patras", "Lighthouse of Patras"),
            new("rioantirrio", "Rio-Antirrio Bridge"),
            new("nafplio", "Bourtzi, Nafplio"),
            new("eiffel", "Eiffel Tower, Paris"),
            new("bigben", "Big Ben, London"),
            new("taj", "Taj Mahal, Agra"),
        };

        public const string Auto = "Auto", Off = "Off";

        public static bool IsValid(string? id) => id is Auto or Off || Items.Any(e => e.Id == id);
    }
}
