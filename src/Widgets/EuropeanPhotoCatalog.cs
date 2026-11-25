using System.Text.Json;
using System.Text.RegularExpressions;

namespace MotionDesk.Widgets;

/// <summary>Optional installer-owned photo scenes; never discovers review artifacts.</summary>
public static class EuropeanPhotoCatalog
{
    private sealed record Place(string Id, string Name, string NameEn, double Lat, double Lon);
    private static readonly Place[] Places =
    [
        new("eu-amsterdam", "Άμστερνταμ", "Amsterdam", 52.367, 4.9),
        new("eu-andorra-la-vella", "Ανδόρα λα Βέγια", "Andorra la Vella", 42.507, 1.522),
        new("eu-ankara", "Άγκυρα", "Ankara", 39.936, 32.838),
        new("eu-belgrade", "Βελιγράδι", "Belgrade", 44.817, 20.457),
        new("eu-berlin", "Βερολίνο", "Berlin", 52.517, 13.383),
        new("eu-bern", "Βέρνη", "Bern", 46.948, 7.447),
        new("eu-bratislava", "Μπρατισλάβα", "Bratislava", 48.152, 17.11),
        new("eu-brussels", "Βρυξέλλες", "Brussels", 50.847, 4.352),
        new("eu-bucharest", "Βουκουρέστι", "Bucharest", 44.413, 26.097),
        new("eu-budapest", "Βουδαπέστη", "Budapest", 47.498, 19.041),
        new("eu-chisinau", "Κισινάου", "Chișinău", 47.023, 28.835),
        new("eu-copenhagen", "Κοπεγχάγη", "Copenhagen", 55.676, 12.569),
        new("eu-dublin", "Δουβλίνο", "Dublin", 53.35, -6.26),
        new("eu-helsinki", "Ελσίνκι", "Helsinki", 60.171, 24.938),
        new("eu-kyiv", "Κίεβο", "Kyiv", 50.45, 30.524),
        new("eu-lisbon", "Λισαβόνα", "Lisbon", 38.708, -9.139),
        new("eu-ljubljana", "Λιουμπλιάνα", "Ljubljana", 46.051, 14.506),
        new("eu-london", "Λονδίνο", "London", 51.507, -0.128),
        new("eu-luxembourg", "Λουξεμβούργο", "Luxembourg", 49.611, 6.13),
        new("eu-madrid", "Μαδρίτη", "Madrid", 40.417, -3.703),
        new("eu-minsk", "Μινσκ", "Minsk", 53.902, 27.562),
        new("eu-monaco", "Μονακό", "Monaco", 43.731, 7.42),
        new("eu-moscow", "Μόσχα", "Moscow", 55.751, 37.618),
        new("eu-nicosia", "Λευκωσία", "Nicosia", 35.172, 33.365),
        new("eu-oslo", "Όσλο", "Oslo", 59.913, 10.739),
        new("eu-paris", "Παρίσι", "Paris", 48.857, 2.352),
        new("eu-podgorica", "Ποντγκόριτσα", "Podgorica", 42.441, 19.263),
        new("eu-prague", "Πράγα", "Prague", 50.087, 14.422),
        new("eu-reykjavik", "Ρέικιαβικ", "Reykjavík", 64.147, -21.935),
        new("eu-riga", "Ρίγα", "Riga", 56.947, 24.107),
        new("eu-rome", "Ρώμη", "Rome", 41.893, 12.483),
        new("eu-san-marino", "Άγιος Μαρίνος", "San Marino", 43.932, 12.448),
        new("eu-sarajevo", "Σαράγεβο", "Sarajevo", 43.856, 18.413),
        new("eu-skopje", "Σκόπια", "Skopje", 41.996, 21.432),
        new("eu-sofia", "Σόφια", "Sofia", 42.698, 23.322),
        new("eu-stockholm", "Στοκχόλμη", "Stockholm", 59.329, 18.069),
        new("eu-tallinn", "Ταλίν", "Tallinn", 59.437, 24.745),
        new("eu-tirana", "Τίρανα", "Tirana", 41.328, 19.818),
        new("eu-vaduz", "Βαντούζ", "Vaduz", 47.14, 9.522),
        new("eu-valletta", "Βαλέτα", "Valletta", 35.897, 14.513),
        new("eu-warsaw", "Βαρσοβία", "Warsaw", 52.23, 21.011),
        new("eu-vatican-city", "Βατικανό", "Vatican City", 41.904, 12.453),
        new("eu-vienna", "Βιέννη", "Vienna", 48.208, 16.372),
        new("eu-vilnius", "Βίλνιους", "Vilnius", 54.688, 25.28),
        new("eu-zagreb", "Ζάγκρεμπ", "Zagreb", 45.813, 15.977),
        new("eu-pristina", "Πρίστινα", "Pristina", 42.663, 21.163),
        new("eu-tbilisi", "Τιφλίδα", "Tbilisi", 41.716, 44.784),
        new("eu-yerevan", "Ερεβάν", "Yerevan", 40.18, 44.514),
        new("eu-baku", "Μπακού", "Baku", 40.377, 49.892),
    ];
    public sealed record Entry(LandmarkCatalog.CityScene Scene, string Poster);
    private sealed record Receipt(int SchemaVersion, string Id, string Status, string MasterSha256, string MaskSha256);
    private static bool Safe(string path, bool directory = false)
    {
        try { return (directory ? Directory.Exists(path) : File.Exists(path)) &&
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0; }
        catch (Exception) { return false; }
    }
    public static IReadOnlyList<Entry> Discover(string? baseDirectory = null)
    {
        var result = new List<Entry>();
        var root = baseDirectory ?? AppContext.BaseDirectory;
        foreach (var part in new[] { "widgets", "wallpaper", "scenes", "europe" })
        {
            root = Path.Combine(root, part);
            if (!Safe(root, true)) return result;
        }
        foreach (var p in Places)
        {
            try
            {
                var dir = Path.Combine(root, p.Id);
                if (!Safe(dir, true)) continue;
                var receipt = Path.Combine(dir, "release.json");
                if (!Safe(receipt) || new FileInfo(receipt).Length is <= 0 or > 16384) continue;
                var m = JsonSerializer.Deserialize<Receipt>(File.ReadAllText(receipt), JsonSerializerOptions.Web);
                if (m is null || m.SchemaVersion != 1 || m.Id != p.Id || m.Status != "approved" ||
                    !Regex.IsMatch(m.MasterSha256 ?? "", "^[a-f0-9]{64}$") ||
                    !Regex.IsMatch(m.MaskSha256 ?? "", "^[a-f0-9]{64}$")) continue;
                string[] required = ["master.webp", "sky-mask.png", "poster.jpg", "dawn.webp", "day.webp",
                    "golden.webp", "blue.webp", "night.webp", "attribution.json"];
                if (required.Any(name => !Safe(Path.Combine(dir, name)) || new FileInfo(Path.Combine(dir,name)).Length <= 0)) continue;
                string relative = "scenes/europe/" + p.Id + "/";
                result.Add(new Entry(new LandmarkCatalog.CityScene(p.Id, p.Name, p.NameEn, p.Lat, p.Lon,
                    "city", "photo", relative + "master.webp", null, true, relative + "sky-mask.png"), Path.Combine(dir, "poster.jpg")));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
            { /* Optional broken scene is omitted. */ }
        }
        return result;
    }
}
