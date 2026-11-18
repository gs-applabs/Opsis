namespace MotionDesk.Services;

internal static class LauncherShortcuts
{
    public static IReadOnlyList<string> Discover(CancellationToken cancellationToken)
    {
        var shortcuts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        void Add(IEnumerable<string> paths)
        {
            foreach (var path in paths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name = Path.GetFileNameWithoutExtension(path);
                if (!name.Contains("uninstall", StringComparison.OrdinalIgnoreCase))
                    shortcuts.TryAdd(name, path);
            }
        }

        Add(TaskbarPinsReader.GetPinnedAppPaths());
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };
        foreach (var folder in new[] { Environment.SpecialFolder.Programs, Environment.SpecialFolder.CommonPrograms })
        {
            cancellationToken.ThrowIfCancellationRequested();
            var root = Environment.GetFolderPath(folder);
            if (!Directory.Exists(root)) continue;
            try { Add(Directory.EnumerateFiles(root, "*.lnk", options)); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return shortcuts.Values.ToArray();
    }
}
