using System.Text.Json;
using MotionDesk.Services;

var folder = Path.Combine(Path.GetTempPath(), "opsis-experience-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
Environment.SetEnvironmentVariable("OPSIS_APPDATA", folder);
int passed = 0;
void Check(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException("FAILED " + description);
    Console.WriteLine("PASS " + description);
    passed++;
}
try
{
    Check(OpsisModCore.Catalog.Count >= 10, "unified module catalog available");
    Check(OpsisModCore.Read().Enabled.Count == 0, "module flags disabled by default");
    OpsisModCore.SetModule("windows.hot-corners", true, 22631, false);
    Check(OpsisModCore.Read().Enabled["windows.hot-corners"], "module enabled with compatible build");
    OpsisModCore.SetModule("windows.hot-corners", false, 22631, false);
    Check(!OpsisModCore.Read().Enabled["windows.hot-corners"], "module independently disabled");
    bool rejected = false;
    try { OpsisModCore.SetModule("wallpaper.scene-composer", true, 22631, false); }
    catch (InvalidOperationException) { rejected = true; }
    Check(rejected, "planned modules cannot be activated as implemented");
    OpsisModCore.SetOption("widgets.opsis", "category", "Weather");
    Check(OpsisModCore.Read().Options["widgets.opsis"]["category"] == "Weather", "typed module options persisted");

    var scene = new WeatherSceneComposer.Scene {
        Name = "Athens Dawn", Weather = "Rain", Time = "Dawn",
        Wind = "FreshBreeze", Landmark = "Auto"
    };
    WeatherSceneComposer.Validate(scene);
    WeatherSceneComposer.Save(scene);
    Check(WeatherSceneComposer.List().Contains("Athens Dawn"), "scene saved");
    Check(WeatherSceneComposer.Read("Athens Dawn").Time == "Dawn", "scene round trip");
    rejected = false;
    try { WeatherSceneComposer.Validate(new WeatherSceneComposer.Scene { Name = "../escape" }); }
    catch (ArgumentException) { rejected = true; }
    Check(rejected, "unsafe scene path rejected");
    scene.Weather = "Unsafe";
    rejected = false;
    try { WeatherSceneComposer.Validate(scene); }
    catch (InvalidDataException) { rejected = true; }
    Check(rejected, "unknown renderer settings rejected");

    var conf = new OpsisAutomationStudio.Configuration();
    conf.Rules.Add(new OpsisAutomationStudio.Rule {
        Name = "Morning theme", Id = "morning", Enabled = true, Trigger = "DailyAt",
        Hour = 9, Minute = 15, Action = "ThemeMode", Target = "Light"
    });
    conf.Rules.Add(new OpsisAutomationStudio.Rule {
        Name = "Startup theme", Id = "boot", Enabled = true, Trigger = "Startup",
        Action = "ThemeMode", Target = "Dark"
    });
    OpsisAutomationStudio.Save(conf);
    Check(OpsisAutomationStudio.Read().Rules.Count == 2, "automation persisted");
    Check(OpsisAutomationStudio.Plan(conf, new DateTime(2026, 11, 24, 9, 15, 0), false).Count == 1,
        "time rule triggers at requested minute");
    Check(OpsisAutomationStudio.Plan(conf, new DateTime(2026, 11, 24, 9, 16, 0), false).Count == 0,
        "time rule does not trigger outside requested minute");
    Check(OpsisAutomationStudio.Plan(conf, new DateTime(2026, 11, 24, 9, 15, 0), true).Count == 1,
        "startup rule isolated from clock rules");
    rejected = false;
    conf.Rules[0].Target = "NotAWindowsTheme";
    try { OpsisAutomationStudio.Validate(conf); }
    catch (InvalidDataException) { rejected = true; }
    Check(rejected, "automation rejects unsupported actions");
    conf.Rules[0].Target = "Light";

    Check(!WindowsUpdateGuard.EvaluateStartup(), "fresh Windows baseline requires no review");
    Check(!WindowsUpdateGuard.RequiresReview, "baseline approved");
    var stateFile = Path.Combine(OpsisRecoveryHub.Root, "update-guard.json");
    File.WriteAllText(stateFile, JsonSerializer.Serialize(new WindowsUpdateGuard.State
    {
        LastApprovedFingerprint = "Build=OLD;UBR=0"
    }));
    Check(WindowsUpdateGuard.EvaluateStartup(), "changed Windows build triggers review");
    Check(WindowsUpdateGuard.RequiresReview, "hold persists after change");
    WindowsUpdateGuard.ApproveCurrentBuild();
    Check(!WindowsUpdateGuard.RequiresReview, "user approval clears hold");

    var plugin = Path.Combine(folder, "plugins", "example-plugin");
    Directory.CreateDirectory(plugin);
    File.WriteAllText(Path.Combine(plugin, "cover.webp"), "fixture");
    File.WriteAllText(Path.Combine(plugin, "opsis-contributions.json"), JsonSerializer.Serialize(new
    {
        Schema = 1, PluginId = "example.plugin", Name = "Example",
        Contributions = new[] { new {
            Id = "city-wallpaper", Type = "wallpaper", Category = "Greece",
            Title = "Athens", Asset = "cover.webp", Author = "Test", Description = "Fixture"
        } }
    }));
    Check(OpsisEcosystemGallery.Discover().Any(x => x.PluginId == "example.plugin"), "plugin wallpaper contribution discovered");

    var before = File.ReadAllText(Path.Combine(folder, "module-core.json"));
    var snapshot = OpsisRecoveryHub.Snapshot("Integration test");
    Check(snapshot.Files.Any(x => x.RelativePath == "module-core.json"), "recovery saved modules");
    Check(snapshot.Files.Any(x => x.RelativePath == "automations.json"), "recovery saved automations");
    Check(snapshot.Files.Any(x => x.RelativePath.StartsWith("weather-scenes/", StringComparison.Ordinal)), "recovery saved scenes");
    File.WriteAllText(Path.Combine(folder, "module-core.json"), "{}");
    OpsisRecoveryHub.RestoreOwnedSettings(snapshot.Id);
    Check(File.ReadAllText(Path.Combine(folder, "module-core.json")) == before, "recovery restored original settings");
    Check(OpsisRecoveryHub.List().Count >= 2, "restore creates pre-restore safety snapshot");

    Console.WriteLine($"COMPLETE {passed} validated assertions");
}
finally
{
    try { Directory.Delete(folder, true); } catch { }
}
