namespace MotionDesk.UI;

internal static class AppWindowIcon
{
    internal static Icon Value { get; } = Load();

    private static Icon Load()
    {
        try { return new Icon(Path.Combine(AppContext.BaseDirectory, "assets", "Opsis.ico")); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return SystemIcons.Application;
        }
    }
}

internal class AppDialog : Form
{
    public AppDialog() => Icon = AppWindowIcon.Value;
}
