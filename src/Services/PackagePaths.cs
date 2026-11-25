namespace MotionDesk.Services;

internal static class PackagePaths
{
    internal static bool IsExistingUnlinkedPath(string root, string path)
    {
        try
        {
            string relative = Path.GetRelativePath(root, path);
            if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                return false;

            string current = Path.GetFullPath(root);
            if (IsLink(current)) return false;
            if (relative == ".") return true;
            foreach (string part in relative.Split(Path.DirectorySeparatorChar))
            {
                current = Path.Combine(current, part);
                if (IsLink(current)) return false;
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    internal static bool IsLink(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
}
