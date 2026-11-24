using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MotionDesk.Widgets;

namespace MotionDesk.Services;

/// <summary>
/// Scene Composer: author/persist safe combinations of the existing Weather Wallpaper
/// renderer options without duplicating its weather, astronomical or WebView2 engines.
/// Scenes can come from core or approved contributions using this same declarative schema.
/// </summary>
public static class WeatherSceneComposer
{
    public sealed class Scene
    {
        public int Schema { get; set; } = 1;
        public string Name { get; set; } = "";
        public string Weather { get; set; } = "Auto";
        public string Time { get; set; } = "Auto";
        public string Wind { get; set; } = "Auto";
        public string Landmark { get; set; } = "Auto";
        public int PhotoFocus { get; set; } = 50;
        public bool Glass { get; set; } = true;
        public int RainLandscape { get; set; } = 100;
        public int RainGlass { get; set; } = 100;
        public bool ShowInfo { get; set; } = true;
    }
    private static readonly object Gate = new();
    private static string Root => Path.Combine(AppPaths.AppDataFolder, "weather-scenes");
    private static readonly HashSet<string> Weather = new(StringComparer.Ordinal)
        { "Auto","Clear","PartlyCloudy","Cloudy","Drizzle","Rain","HeavyRain","Thunderstorm","Snow","Fog" };
    private static readonly HashSet<string> Time = new(StringComparer.Ordinal)
        { "Auto","Dawn","Day","Dusk","Night" };
    private static readonly HashSet<string> Wind = new(StringComparer.Ordinal)
        { "Auto","Calm","LightBreeze","FreshBreeze","Strong","Gale" };
    private static string PathFor(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 60 ||
            name.Any(x => !char.IsLetterOrDigit(x) && x is not (' ' or '_' or '-')))
            throw new ArgumentException("Invalid Weather scene name.", nameof(name));
        return Path.Combine(Root, name.Trim() + ".json");
    }

    public static Scene Capture(string name)
    {
        var s = WallpaperSettings.Load();
        return new Scene
        {
            Name = name.Trim(), Weather = s.WeatherSimulation, Time = s.TimeSimulation,
            Wind = s.WindSimulation, Landmark = s.WeatherLandmarkId,
            PhotoFocus = s.WeatherPhotoFocusX, Glass = s.WeatherGlass,
            RainLandscape = s.RainLandscapeIntensity, RainGlass = s.RainGlassIntensity,
            ShowInfo = s.WeatherShowInfo
        };
    }

    public static void Validate(Scene s)
    {
        PathFor(s.Name);
        if (s.Schema != 1 || !Weather.Contains(s.Weather) || !Time.Contains(s.Time) ||
            !Wind.Contains(s.Wind) || !LandmarkCatalog.IsValid(s.Landmark) ||
            s.PhotoFocus is < 0 or > 100 || s.RainLandscape is < 0 or > 200 ||
            s.RainGlass is < 0 or > 200)
            throw new InvalidDataException("Invalid or unsupported Weather scene setting.");
    }

    public static IReadOnlyList<string> List() => Directory.Exists(Root)
        ? Directory.EnumerateFiles(Root, "*.json", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileNameWithoutExtension).Where(x => !string.IsNullOrWhiteSpace(x))
            .Cast<string>().OrderBy(x => x).Take(250).ToArray()
        : Array.Empty<string>();

    public static void Save(Scene scene)
    {
        Validate(scene);
        lock (Gate)
        {
            Directory.CreateDirectory(Root);
            var path = PathFor(scene.Name);
            if (File.Exists(path)) throw new IOException("Scene already exists.");
            AtomicFile.WriteAllText(path, JsonSerializer.Serialize(scene, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    public static Scene Read(string name)
    {
        var path = PathFor(name);
        if (new FileInfo(path).Length > 32 * 1024) throw new InvalidDataException("Scene too large.");
        var scene = JsonSerializer.Deserialize<Scene>(AtomicFile.ReadAllText(path))
            ?? throw new InvalidDataException("Missing scene.");
        if (!string.Equals(scene.Name, name.Trim(), StringComparison.Ordinal))
            throw new InvalidDataException("Scene name mismatch.");
        Validate(scene);
        return scene;
    }

    public static void Delete(string name) => File.Delete(PathFor(name));

    public static void Apply(string name)
    {
        var scene = Read(name);
        var s = WallpaperSettings.Load();
        // Capture original settings once before any user-directed change.
        OpsisRecoveryHub.Snapshot("Before applying Weather scene " + scene.Name);
        s.WeatherSimulation = scene.Weather;
        s.TimeSimulation = scene.Time;
        s.WindSimulation = scene.Wind;
        s.WeatherLandmarkId = scene.Landmark;
        s.WeatherLandmarks = scene.Landmark != LandmarkCatalog.Off;
        s.WeatherPhotoFocusX = scene.PhotoFocus;
        s.WeatherGlass = scene.Glass;
        s.RainLandscapeIntensity = scene.RainLandscape;
        s.RainGlassIntensity = scene.RainGlass;
        s.WeatherShowInfo = scene.ShowInfo;
        s.Save();
        WallpaperHostEngine.Instance.RefreshAll();
    }
}
