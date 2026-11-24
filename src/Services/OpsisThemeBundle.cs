using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MotionDesk.Services;

/// <summary>
/// Portable .opsis-theme package of existing Opsis appearance + weather settings.
/// Pure JSON. Never extracts ZIP entries, imports DLLs, touches system binaries,
/// or activates a preset without explicit follow-up user action.
/// </summary>
public static class OpsisThemeBundle
{
    public sealed class PackageHeader
    {
        public int Schema { get; set; } = 1;
        public string Name { get; set; } = "";
        public string AppearanceSha256 { get; set; } = "";
        public string SceneSha256 { get; set; } = "";
        public DateTime ExportedUtc { get; set; } = DateTime.UtcNow;
    }
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static void ValidatePackagePath(string path)
    {
        if (!string.Equals(Path.GetExtension(path), ".opsis-theme", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Use a .opsis-theme file.");
    }
    private static string Hash(string input) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)));

    private static void Add(ZipArchive zip, string name, string text)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(text);
    }

    public static void ExportCurrent(string name, string destination)
    {
        ValidatePackagePath(destination);
        var appearance = AppearancePresets.Capture(name);
        var scene = WeatherSceneComposer.Capture(name);
        WeatherSceneComposer.Validate(scene);
        var a = JsonSerializer.Serialize(appearance, Json);
        var b = JsonSerializer.Serialize(scene, Json);
        var header = JsonSerializer.Serialize(new PackageHeader
        {
            Name = appearance.Name, AppearanceSha256 = Hash(a), SceneSha256 = Hash(b)
        }, Json);
        if (File.Exists(destination)) throw new IOException("Target bundle exists; refusing to overwrite.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
        var temp = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                Add(zip, "manifest.json", header);
                Add(zip, "appearance.json", a);
                Add(zip, "weather-scene.json", b);
            }
            File.Move(temp, destination, false);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private static string Read(ZipArchiveEntry entry)
    {
        if (entry.Length > 64 * 1024 || entry.CompressedLength > 128 * 1024)
            throw new InvalidDataException("Theme entry too large.");
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8, true);
        var text = reader.ReadToEnd();
        if (Encoding.UTF8.GetByteCount(text) > 64 * 1024)
            throw new InvalidDataException("Theme entry exceeds quota.");
        return text;
    }

    public static string Import(string file)
    {
        ValidatePackagePath(file);
        if (new FileInfo(file).Length > 1024 * 1024)
            throw new InvalidDataException("Theme package too large.");
        string manifestText, appearanceText, sceneText;
        using (var zip = ZipFile.OpenRead(file))
        {
            var names = new[] { "manifest.json", "appearance.json", "weather-scene.json" };
            if (zip.Entries.Count != names.Length ||
                names.Any(name => zip.Entries.Count(x => x.FullName == name) != 1))
                throw new InvalidDataException("Unknown or duplicate theme entries.");
            manifestText = Read(zip.GetEntry("manifest.json")!);
            appearanceText = Read(zip.GetEntry("appearance.json")!);
            sceneText = Read(zip.GetEntry("weather-scene.json")!);
        }
        var header = JsonSerializer.Deserialize<PackageHeader>(manifestText)
            ?? throw new InvalidDataException("Invalid theme manifest.");
        var appearance = JsonSerializer.Deserialize<AppearancePresets.Preset>(appearanceText)
            ?? throw new InvalidDataException("Missing appearance settings.");
        var scene = JsonSerializer.Deserialize<WeatherSceneComposer.Scene>(sceneText)
            ?? throw new InvalidDataException("Missing scene settings.");
        if (header.Schema != 1 || appearance.Schema != 1 ||
            !string.Equals(header.Name, appearance.Name, StringComparison.Ordinal) ||
            !string.Equals(header.Name, scene.Name, StringComparison.Ordinal) ||
            !Hash(appearanceText).Equals(header.AppearanceSha256, StringComparison.OrdinalIgnoreCase) ||
            !Hash(sceneText).Equals(header.SceneSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Theme integrity or schema validation failed.");
        WeatherSceneComposer.Validate(scene);
        // Neither target may be overwritten. No OS setting changes during import.
        if (AppearancePresets.List().Contains(appearance.Name) ||
            WeatherSceneComposer.List().Contains(scene.Name))
            throw new IOException("A local preset or scene already uses this name.");
        AppearancePresets.SavePortable(appearance);
        try { WeatherSceneComposer.Save(scene); }
        catch { AppearancePresets.Delete(appearance.Name); throw; }
        return header.Name;
    }
}
