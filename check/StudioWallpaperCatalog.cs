namespace MotionDesk.Widgets;

internal static class StudioWallpaperCatalog
{
    internal sealed record Scene(string Mode, string Asset, string English, string Greek, string EnglishDetail, string GreekDetail)
    {
        internal string Title(bool greek) => greek ? Greek : English;
        internal string Detail(bool greek) => greek ? GreekDetail : EnglishDetail;
    }

    internal static IReadOnlyList<Scene> Scenes { get; } =
    [
        new("Studio Aurora", "aurora", "Nordic Aurora", "Βόρειο Σέλας", "Curtains of light over a glacial lake", "Κουρτίνες φωτός πάνω σε παγετωνική λίμνη"),
        new("Studio Nebula", "nebula", "Rose Nebula", "Ρόδινο Νεφέλωμα", "Stardust, violet clouds and luminous filaments", "Αστρόσκονη, ιώδη νέφη και φωτεινά νήματα"),
        new("Studio Tide", "tide", "Moonlit Tide", "Παλίρροια στο Φεγγαρόφωτο", "Moon reflections and bioluminescent waves", "Αντανακλάσεις σε κύματα που φωσφορίζουν"),
        new("Studio Dunes", "dunes", "Amber Dunes", "Κεχριμπαρένιοι Αμμόλοφοι", "Moving ridges and warm desert haze", "Κινούμενες ράχες άμμου και θερμή καταχνιά"),
        new("Studio Prism", "prism", "Spectral Glass", "Πρισματικό Γυαλί", "Chromatic refractions through suspended glass", "Χρωματικές διαθλάσεις σε αιωρούμενο γυαλί"),
        new("Studio Ink", "ink", "Liquid Marble", "Ρευστό Μάρμαρο", "Ink currents with copper veins", "Ρεύματα μελανιού με χάλκινες φλέβες"),
        new("Studio Neon", "neon", "Violet Terminal", "Ιώδης Μητρόπολη", "A luminous city and a moving perspective grid", "Φωτισμένη πόλη και κινούμενο πλέγμα προοπτικής"),
        new("Studio Forest", "forest", "Mistwood", "Δάσος στην Ομίχλη", "Layered evergreens and drifting mist", "Επάλληλα κωνοφόρα και αργή ομίχλη"),
        new("Studio Eclipse", "eclipse", "Solar Eclipse", "Ηλιακή Έκλειψη", "A low-lit planet and a moving solar corona", "Σκοτεινός δίσκος και κινούμενο ηλιακό στέμμα"),
        new("Studio Abyss", "abyss", "Into the Abyss", "Στον Βυθό", "Underwater light rays, caustics and fish", "Υποβρύχιες ακτίνες, φωτοσκιές και ψάρια")
    ];

    internal static Scene? Find(string? mode) => Scenes.FirstOrDefault(scene => scene.Mode == mode);
}
