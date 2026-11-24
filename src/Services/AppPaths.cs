namespace MotionDesk.Services;

public static class AppPaths
{
    private static readonly Lazy<string> DataFolder = new(() =>
    {
        var folder = Environment.GetEnvironmentVariable("OPSIS_APPDATA");
        if (string.IsNullOrWhiteSpace(folder))
        {
            // the application is "Opsis - Desktop Customization": its data lives in %AppData%\Opsis. A 2.8.0.2500 preview kept it in "Opsis Studio": it is moved once (only when the new folder does not exist yet)
            string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            folder = Path.Combine(roaming, "Opsis");
            string previewFolder = Path.Combine(roaming, "Opsis Studio");
            try { if (!Directory.Exists(folder) && Directory.Exists(previewFolder)) Directory.Move(previewFolder, folder); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        Directory.CreateDirectory(folder);
        return folder;
    });

    public static string AppDataFolder => DataFolder.Value;
}
