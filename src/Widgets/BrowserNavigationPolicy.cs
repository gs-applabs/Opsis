namespace MotionDesk.Widgets;

internal static class BrowserNavigationPolicy
{
    internal static bool IsBundledDocument(string document, string address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || !uri.IsFile || uri.IsUnc ||
            !string.IsNullOrEmpty(uri.Host) || !string.IsNullOrEmpty(uri.Query)) return false;
        try
        {
            return string.Equals(Path.GetFullPath(document), Path.GetFullPath(uri.LocalPath),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }

    internal static bool IsLocalFile(string address) => Uri.TryCreate(address, UriKind.Absolute, out var uri) &&
        uri.IsFile && !uri.IsUnc && string.IsNullOrEmpty(uri.Host);

    internal static bool IsWallpaperFrame(string address, bool localPage)
    {
        if (address == "about:blank") return true;
        return localPage ? IsLocalFile(address)
            : Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http";
    }
}
