using System.Text.Json;

namespace MotionDesk.Widgets;

public static class LandmarkCatalog
{
    public sealed record CityScene(string Id, string Name, string NameEn, double Lat, double Lon,
        string Terrain, string Motif, string? File, string? Lights, bool ManualOnly = false, string? SkyMask = null);

    public sealed record Entry(string Id, string Name, string NameEl);

    public const string Auto = "Auto", Off = "Off", MountainLake = "mountain-lake";

    private static CityScene[] ReadScenes(string resource)
    {
        using var stream = typeof(LandmarkCatalog).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Weather scene catalog {resource} is missing.");
        return JsonSerializer.Deserialize<CityScene[]>(stream, JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException($"Weather scene catalog {resource} is empty.");
    }

    private static readonly Lazy<CityScene[]> CityCatalog = new(() => ReadScenes("opsis-capitals.json"));
    private static readonly Lazy<CityScene[]> SceneCatalog = new(() => [.. Cities, .. ReadScenes("opsis-islands.json")]);
    public static IReadOnlyList<CityScene> Cities => CityCatalog.Value;
    public static IReadOnlyList<CityScene> Scenes => [.. SceneCatalog.Value, .. EuropeanPhotoCatalog.Discover().Select(e => e.Scene)];

    public static readonly Entry[] Landmarks =
    [
        new("patras-lighthouse", "Lighthouse of Patras", "Φάρος Πάτρας"),
        new("rioantirrio", "Rio–Antirrio Bridge", "Γέφυρα Ρίου–Αντιρρίου"),
        new("nafplio-bourtzi", "Bourtzi, Nafplio", "Μπούρτζι Ναυπλίου")
    ];

    public static bool IsValid(string? id) => id is Auto or Off or MountainLake || Scenes.Any(city => city.Id == id)
        || Landmarks.Any(landmark => landmark.Id == id);
}
