using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace MotionDesk.Services;

/// <summary>
/// Inspect plugin package metadata without loading DLL code or overriding PluginHost.
/// PluginHost remains the sole runtime loader/approval authority.
/// </summary>
public static class OpsisPluginPackageAudit
{
    public sealed record Package(string FolderName, string Dll, string AssemblyVersion,
        string Status, string Explanation);

    private static readonly HashSet<string> PinnedEntrypoints = new(StringComparer.OrdinalIgnoreCase)
    {
        "OpsisNewsCrawler.OpsisPlugin.dll",
        "Chronikologio.OpsisPlugin.dll"
    };

    public static string Classify(string fileName, Version? version)
    {
        if (version == null) return "InvalidMetadata";
        if (!PinnedEntrypoints.Contains(fileName)) return "RequiresTrustReview";
        return version == new Version(1, 0, 0, 0) ? "CompatibleABI" : "IncompatibleABI";
    }

    public static IReadOnlyList<Package> Scan()
    {
        var results = new List<Package>();
        foreach (var (folder, _) in PluginHost.EnumerateCandidateFolders().Take(250))
        {
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(folder, "*Plugin.dll").Take(80).ToArray(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { continue; }
            foreach (var file in files)
            {
                string status;
                Version? version = null;
                try
                {
                    if (!PackagePaths.IsExistingUnlinkedPath(folder, file) ||
                        new FileInfo(file).Length > 100 * 1024 * 1024)
                        throw new InvalidDataException("Unsafe plugin DLL file path/size");
                    version = AssemblyName.GetAssemblyName(file).Version;
                    PluginCapabilityManifest.RequireCompatibleApi(folder);
                    status = Classify(Path.GetFileName(file), version);
                }
                catch (Exception ex) when (ex is IOException or BadImageFormatException or
                    UnauthorizedAccessException or InvalidDataException or ArgumentException or System.Text.Json.JsonException)
                { status = "InvalidMetadata"; }
                results.Add(new Package(Path.GetFileName(folder), Path.GetFileName(file),
                    version?.ToString() ?? "unknown", status,
                    status switch
                    {
                        "CompatibleABI" => "Version 1.0.0.0 matches Opsis's pinned host ABI; separate SHA trust approval still required.",
                        "IncompatibleABI" => "This known plugin will be skipped by PluginHost. Update its plugin assembly to 1.0.0.0.",
                        "RequiresTrustReview" => "Third-party plugin ABI cannot be certified from filename alone; PluginHost trust/contract checks apply.",
                        _ => "Unreadable or unsafe plugin assembly; do not load."
                    }));
            }
        }
        return results.OrderBy(x => x.FolderName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Dll, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
