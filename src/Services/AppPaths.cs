namespace MotionDesk.Services;

public static class AppPaths
{
    private static readonly Lazy<string> DataFolder = new(() =>
    {
        var folder = Environment.GetEnvironmentVariable("OPSIS_APPDATA");
        if (string.IsNullOrWhiteSpace(folder))
            folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Opsis Studio");
        Directory.CreateDirectory(folder);
        return folder;
    });

    public static string AppDataFolder => DataFolder.Value;
}
