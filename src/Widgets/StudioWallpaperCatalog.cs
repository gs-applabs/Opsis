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
        new("Studio Abyss", "abyss", "Into the Abyss", "Στον Βυθό", "Underwater light rays, caustics and fish", "Υποβρύχιες ακτίνες, φωτοσκιές και ψάρια"),
        new("Studio Alpine", "alpine", "Alpine Mirror", "Αλπικός Καθρέφτης", "Snowy peaks, lake reflections and a passing flock", "Χιονισμένες κορυφές, αντανακλάσεις και περάσματα πουλιών"),
        new("Studio Cedar", "cedar", "Cedar Valley", "Κοιλάδα των Κέδρων", "Layered conifers with drifting morning fog", "Κωνοφόρα με πρωινή ομίχλη"),
        new("Studio Caldera", "caldera", "Aegean Caldera", "Αιγαιοπελαγίτικη Καλντέρα", "Sunset over volcanic cliffs and moving water", "Ηλιοβασίλεμα πάνω στους βράχους και τη θάλασσα"),
        new("Studio Fjord", "fjord", "Glacial Fjord", "Παγετωνικό Φιόρδ", "Frosted slopes and moving bands of mist", "Παγωμένες πλαγιές και κινούμενη ομίχλη"),
        new("Studio Lightning", "lightning", "Offshore Storm", "Καταιγίδα στο Πέλαγος", "Lightning, wind-driven rain and dark swell", "Αστραπές, βροχή και σκοτεινά κύματα"),
        new("Studio Kelp", "kelp", "Kelp Forest", "Δάσος Φυκιών", "Swaying kelp, light rays and schooling fish", "Φύκια που λικνίζονται, ακτίνες και κοπάδια ψαριών"),
        new("Studio Atoll", "atoll", "Turquoise Atoll", "Τυρκουάζ Ατόλη", "Aerial shoreline with moving surf and caustics", "Ακτογραμμή από ψηλά, αφρός και διαθλάσεις"),
        new("Studio Medusa", "medusa", "Midnight Jellyfish", "Μέδουσες του Μεσονυχτίου", "Luminous bells and trailing tentacles", "Φωτεινές μέδουσες με κινούμενα πλοκάμια"),
        new("Studio Reef", "reef", "Coral Passage", "Πέρασμα στον Ύφαλο", "Schools of fish above layered coral", "Κοπάδια ψαριών πάνω από τα κοράλλια"),
        new("Studio Lunar", "lunar", "Lunar Highlands", "Σεληνιακά Υψίπεδα", "A cratered Moon above a dark mountain range", "Σελήνη με κρατήρες πάνω από σκοτεινά βουνά"),
        new("Studio Saturn", "saturn", "Rings of Saturn", "Δακτύλιοι του Κρόνου", "Orbital rings and drifting bands of atmosphere", "Δακτύλιοι και κινούμενες ατμοσφαιρικές ζώνες"),
        new("Studio Meteor", "meteor", "Meteor Ridge", "Βροχή Μετεώρων", "Passing meteors above a night ridge", "Διάττοντες αστέρες πάνω από νυχτερινή κορυφογραμμή"),
        new("Studio Corona", "corona", "Solar Granules", "Κόκκοι του Ήλιου", "Granular photosphere and moving plasma", "Κοκκώδης φωτόσφαιρα και κινούμενο πλάσμα"),
        new("Studio Opal", "opal", "Opal Bloom", "Οπαλίτης", "Shifting iridescence across polished opal", "Μεταβαλλόμενοι ιριδισμοί σε γυαλισμένο οπάλιο"),
        new("Studio Obsidian", "obsidian", "Gold Obsidian", "Χρυσός Οψιδιανός", "Dark stone crossed by luminous mineral seams", "Σκούρα πέτρα με φωτεινές μεταλλικές φλέβες"),
        new("Studio Silk", "silk", "Silk Current", "Ρεύμα Μεταξιού", "Folding silk with shifting highlights", "Πτυχές μεταξιού με κινούμενες αντανακλάσεις"),
        new("Studio Petals", "petals", "Petal Drift", "Ροή Πετάλων", "Falling petals turning in the wind", "Πέταλα που περιστρέφονται στον άνεμο"),
        new("Studio Canyon", "canyon", "Red Canyon", "Κόκκινο Φαράγγι", "Layered sandstone with a moving river", "Στρώσεις ψαμμίτη και κινούμενο ποτάμι"),
        new("Studio Snowfall", "snowfall", "Winter Pines", "Χειμωνιάτικα Πεύκα", "Snow drifting through evergreen forest", "Χιόνι που πέφτει ανάμεσα στα κωνοφόρα"),
        new("Studio Fireflies", "fireflies", "Firefly Hollow", "Κοιλάδα με Πυγολαμπίδες", "Fireflies and moonlight in a night woodland", "Πυγολαμπίδες και φεγγαρόφωτο σε νυχτερινό δάσος")
    ];

    internal static Scene? Find(string? mode) => Scenes.FirstOrDefault(scene => scene.Mode == mode);
}
