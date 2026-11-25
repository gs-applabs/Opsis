using System.Text.Json;

namespace MotionDesk.Services;

internal static class WidgetCatalog
{
    private static readonly string BuiltIn = Path.Combine(AppContext.BaseDirectory, "widget-sdk", "catalog");
    private static readonly string Installed = Path.Combine(AppPaths.AppDataFolder, "widget-sdk");
    internal sealed record Package(WidgetManifest Manifest, string Directory, bool BuiltIn)
    {
        internal string Key => "sdk-" + Manifest.Id;
        public override string ToString() => Manifest.Name;
    }

    internal static Package[] List() => ReadDirectory(BuiltIn, true).Concat(ReadDirectory(Installed, false))
        .DistinctBy(pkg => pkg.Manifest.Id, StringComparer.Ordinal).OrderBy(pkg => pkg.Manifest.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();

    private static IEnumerable<Package> ReadDirectory(string root, bool builtIn)
    {
        if (!Directory.Exists(root)) yield break;
        foreach (var dir in Directory.EnumerateDirectories(root).Where(path => !Path.GetFileName(path).StartsWith('.')))
        {
            WidgetManifest? manifest = null;
            try { if (PackagePaths.IsExistingUnlinkedPath(root, dir)) manifest = WidgetPackageFormat.Read(dir); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException) { }
            if (manifest != null && Path.GetFileName(dir) == manifest.Id) yield return new Package(manifest, dir, builtIn);
        }
    }

    internal static Package? Find(string key) => List().FirstOrDefault(pkg => pkg.Key == key);

    internal static void Install(string archive)
    {
        Directory.CreateDirectory(Installed);
        var temp = Path.Combine(Installed, ".install-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(temp);
            var manifest = WidgetPackageFormat.Extract(archive, temp);
            if (ReadDirectory(BuiltIn, true).Any(pkg => pkg.Manifest.Id == manifest.Id))
                throw new InvalidDataException("This id belongs to a bundled widget. Choose a different id in widget.json.");
            var destination = Path.Combine(Installed, manifest.Id);
            if (Directory.Exists(destination)) throw new InvalidDataException("This widget is already installed. Remove it before installing a different version.");
            Directory.Move(temp, destination);
        }
        finally { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
    }

    internal static void Remove(Package package)
    {
        var current = Find(package.Key);
        if (current is { BuiltIn: false }) Directory.Delete(current.Directory, true);
    }
}
