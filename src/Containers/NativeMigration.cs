using Opsis.Containers;

namespace MotionDesk.Containers
{
    /// <summary>
    /// First start of the native containers on a machine that ran the 2.4.x engine: the legacy data is imported READ-ONLY (the old folder stays untouched, so going back to 2.4.x
    /// loses nothing). The Default profile becomes the live layout, every other profile becomes a named layout snapshot. Runs only when the native data file does not exist yet.
    /// </summary>
    internal static class NativeMigration
    {
        public static string? ImportIfNeeded(ContainersStore store, string legacyRoot, IReadOnlyList<MonitorInfo> monitors)
        {
            if (File.Exists(store.Path)) return null;                         // never overwrites what the user already has
            if (!Directory.Exists(Path.Combine(legacyRoot, "Profiles"))) return null;
            var (docs, report) = LegacyImporter.Import(legacyRoot, monitors);
            if (docs.Count == 0) return "legacy import: nothing importable (" + string.Join("; ", report.Warnings) + ")";
            store.Save(docs[0]);
            var snapDir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(store.Path)) ?? ".", "container-snapshots");
            for (int i = 1; i < docs.Count; i++) ContainerSnapshots.Save(docs[i], snapDir, docs[i].Profile);
            return $"legacy import: {report.Containers} containers, {report.Items} items, {report.Profiles} profile(s) ({docs.Count - 1} as snapshots), {report.NotImported.Count} not carried";
        }
    }
}
