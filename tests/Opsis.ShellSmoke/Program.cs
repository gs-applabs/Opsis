using MotionDesk.Services;

string root = Path.Combine(Path.GetTempPath(), "opsis-p1p2-smoke-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
Environment.SetEnvironmentVariable("OPSIS_APPDATA", root);
Console.WriteLine("Opsis isolated shell smoke: " + root);
int assertions = 0;
void Check(bool ok, string message)
{
    if (!ok) throw new Exception("ASSERT: " + message);
    Console.WriteLine("PASS " + message);
    assertions++;
}
int originalExtensions = ExplorerViewProfiles.ReadOption("HideFileExt");
int originalHidden = ExplorerViewProfiles.ReadOption("Hidden");
int originalCompact = ExplorerViewProfiles.ReadOption("UseCompactMode");
int originalLaunch = ExplorerViewProfiles.ReadOption("LaunchTo");
int originalAlignment = TaskbarViewProfiles.IsWindows11 ? TaskbarViewProfiles.ReadOption("TaskbarAl") : 1;
try
{
    // Read-only validation / overwrite protection.
    Check(!AppearancePresets.List().Any(), "fresh preset store empty");
    Check(ExplorerViewProfiles.ReadOption("HideFileExt") is 0 or 1, "Explorer extensions read");
    bool rejected = false;
    try { ExplorerViewProfiles.ApplyOption("NonWhitelisted", 1); }
    catch (ArgumentOutOfRangeException) { rejected = true; }
    Check(rejected, "Explorer whitelist");
    rejected = false;
    try { AppearancePresets.SaveCurrent("../bad"); }
    catch (ArgumentException) { rejected = true; }
    Check(rejected, "preset name traversal rejected");
    AppearancePresets.SaveCurrent("CI Smoke");
    Check(AppearancePresets.List().Contains("CI Smoke"), "preset stored");
    var saved = AppearancePresets.Read("CI Smoke");
    Check(saved.Name == "CI Smoke" && saved.Schema == 1, "preset roundtrip");
    Check(AppearancePresets.Preview("CI Smoke").Contains("Preview only"), "dry-run preview");
    rejected = false;
    try { AppearancePresets.SaveCurrent("CI Smoke"); }
    catch (IOException) { rejected = true; }
    Check(rejected, "no silent preset overwrite");
    AppearancePresets.Apply("CI Smoke");
    Check(ExplorerViewProfiles.ReadOption("HideFileExt") == originalExtensions, "no-op preset apply");
    AppearancePresets.Delete("CI Smoke");
    Check(!AppearancePresets.List().Any(), "preset delete");

    ExplorerViewProfiles.ApplyOption("HideFileExt", originalExtensions == 0 ? 1 : 0);
    Check(ExplorerViewProfiles.ReadOption("HideFileExt") != originalExtensions, "Explorer individual option");
    ExplorerViewProfiles.Restore();
    Check(ExplorerViewProfiles.ReadOption("HideFileExt") == originalExtensions, "Explorer restore");
    if (TaskbarViewProfiles.IsWindows11)
    {
        TaskbarViewProfiles.ApplyOption("TaskbarAl", originalAlignment == 0 ? 1 : 0);
        Check(TaskbarViewProfiles.ReadOption("TaskbarAl") != originalAlignment, "Taskbar individual option");
        TaskbarViewProfiles.Restore();
        Check(TaskbarViewProfiles.ReadOption("TaskbarAl") == originalAlignment, "Taskbar restore");
    }
}
finally
{
    // Explicitly restore all original Explorer values even after a failure.
    try { ExplorerViewProfiles.ApplyOption("HideFileExt", originalExtensions); } catch { }
    try { ExplorerViewProfiles.ApplyOption("Hidden", originalHidden); } catch { }
    try { ExplorerViewProfiles.ApplyOption("UseCompactMode", originalCompact); } catch { }
    try { ExplorerViewProfiles.ApplyOption("LaunchTo", originalLaunch); } catch { }
    if (TaskbarViewProfiles.IsWindows11)
        try { TaskbarViewProfiles.ApplyOption("TaskbarAl", originalAlignment); } catch { }
    try { Directory.Delete(root, true); } catch { }
}
Console.WriteLine($"COMPLETE {assertions} assertions");
