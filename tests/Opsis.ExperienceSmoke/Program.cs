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
    var themeFile = Path.Combine(folder, "Exported.opsis-theme");
    OpsisThemeBundle.ExportCurrent("Portable Theme", themeFile);
    Check(File.Exists(themeFile), "theme exported");
    var imported = OpsisThemeBundle.Import(themeFile);
    Check(imported == "Portable Theme", "theme imported with matching name");
    Check(AppearancePresets.List().Contains("Portable Theme"), "appearance contribution installed");
    Check(WeatherSceneComposer.List().Contains("Portable Theme"), "weather scene contribution installed");
    rejected = false;
    try { OpsisThemeBundle.Import(themeFile); }
    catch (IOException) { rejected = true; }
    Check(rejected, "theme import refuses overwrite");
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

    Check(OpsisFeatureSwitcher.CanSwitch("windows.hot-corners"), "core module has operational hot corner adapter");
    Check(!OpsisFeatureSwitcher.CanSwitch("start.shell-replacement"), "planned Start replacement not switchable");
    OpsisFeatureSwitcher.Set("windows.hot-corners", true);
    Check(AppSettings.Load().HotCornersEnabled && OpsisFeatureSwitcher.IsEnabled("windows.hot-corners"),
        "core enabled a real Hot Corners service preference");
    OpsisFeatureSwitcher.Set("windows.hot-corners", false);
    Check(!AppSettings.Load().HotCornersEnabled, "core restored Hot Corners opt-out");
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
    // A second contributor may supply a non-executable native widget layout.
    File.WriteAllText(Path.Combine(plugin, "clock.json"), JsonSerializer.Serialize(new OpsisWidgetDesigner.Layout
    {
        Name = "Plugin Clock", Binding = "Clock", AccentHex = "#44BBDD"
    }));
    File.WriteAllText(Path.Combine(plugin, "opsis-contributions.json"), JsonSerializer.Serialize(new
    {
        Schema = 1, PluginId = "example.plugin", Name = "Example",
        Contributions = new[] {
            new { Id = "city-wallpaper", Type = "wallpaper", Category = "Greece",
                Title = "Athens", Asset = "cover.webp", Author = "Test", Description = "Fixture" },
            new { Id = "clock-preview", Type = "widget", Category = "Clock",
                Title = "Plugin Clock", Asset = "clock.json", Author = "Test", Description = "Native widget" }
        }
    }));
    var pluginWidget = OpsisEcosystemGallery.Discover().Single(x => x.Type == "widget" && x.PluginId == "example.plugin");
    Check(OpsisEcosystemGallery.ReadWidgetContribution(pluginWidget).Binding == "Clock",
        "safe plugin widget contribution instantiated as native layout");


    var widget = new OpsisWidgetDesigner.Layout {
        Name = "Clock Demo", Binding = "Clock", AccentHex = "#00D2FF", FontSize = 30
    };
    OpsisWidgetDesigner.Save(widget);
    Check(OpsisWidgetDesigner.List().Contains("Clock Demo"), "native widget layout appears in Gallery storage");
    Check(OpsisWidgetDesigner.Read("Clock Demo").Binding == "Clock", "native widget layout roundtrip");
    rejected = false;
    try { OpsisWidgetDesigner.Validate(new OpsisWidgetDesigner.Layout { Name = "../escape", Binding = "Clock" }); }
    catch (ArgumentException) { rejected = true; }
    Check(rejected, "widget layout traversal rejected");
    rejected = false;
    try { OpsisWidgetDesigner.Validate(new OpsisWidgetDesigner.Layout { Name = "Bad Widget", Binding = "Script" }); }
    catch (InvalidDataException) { rejected = true; }
    Check(rejected, "widget designer refuses arbitrary script bindings");

    // Image-to-accent extraction must be deterministic and never modify Windows colors.
    var imagePath = Path.Combine(folder, "test-accent.png");
    using (var bmp = new System.Drawing.Bitmap(30, 30))
    {
        using var graphics = System.Drawing.Graphics.FromImage(bmp);
        graphics.Clear(System.Drawing.Color.FromArgb(25, 170, 205));
        bmp.Save(imagePath, System.Drawing.Imaging.ImageFormat.Png);
    }
    var computed = WallpaperAccentEngine.Extract(imagePath);
    Check(computed.B > computed.R, "wallpaper accent extracts representative blue color");
    Check(WallpaperAccentEngine.ToHex(computed).Length == 7, "accent encoded as validated hex");
    var appSettings = AppSettings.Load();
    appSettings.WallpaperAccentEnabled = true;
    appSettings.WallpaperAccentHex = WallpaperAccentEngine.ToHex(computed);
    appSettings.Save();
    MotionDesk.UI.ThemeManager.RefreshOpsisAccent();
    Check(MotionDesk.UI.ThemeManager.Current.AccentCyan.ToArgb() == computed.ToArgb(),
        "unified Opsis palette uses wallpaper-derived accent");
    WallpaperAccentEngine.Disable();
    Check(!AppSettings.Load().WallpaperAccentEnabled, "dynamic accent reversible");

    // Registry context menu writes only to HKCU-owned keys; CI runner uses an ephemeral account.
    var fakeOpsis = Path.Combine(folder, "Opsis.exe");
    File.WriteAllBytes(fakeOpsis, new byte[] {77, 90}); // file name check; never execute
    try
    {
        OpsisContextMenuStudio.Enable(fakeOpsis);
        Check(OpsisContextMenuStudio.IsEnabled(), "Opsis owned context menu installed");
    }
    finally { OpsisContextMenuStudio.Disable(); }
    Check(!OpsisContextMenuStudio.IsEnabled(), "Opsis context menu removed safely");
    var before = File.ReadAllText(Path.Combine(folder, "module-core.json"));
    var snapshot = OpsisRecoveryHub.Snapshot("Integration test");
    Check(snapshot.Files.Any(x => x.RelativePath == "module-core.json"), "recovery saved modules");
    Check(snapshot.Files.Any(x => x.RelativePath == "automations.json"), "recovery saved automations");
    Check(snapshot.Files.Any(x => x.RelativePath.StartsWith("weather-scenes/", StringComparison.Ordinal)), "recovery saved scenes");
    Check(snapshot.Files.Any(x => x.RelativePath.StartsWith("widget-designs/", StringComparison.Ordinal)), "recovery saved widget designs");
    File.WriteAllText(Path.Combine(folder, "module-core.json"), "{}");
    OpsisRecoveryHub.RestoreOwnedSettings(snapshot.Id);
    Check(File.ReadAllText(Path.Combine(folder, "module-core.json")) == before, "recovery restored original settings");
    Check(OpsisRecoveryHub.List().Count >= 2, "restore creates pre-restore safety snapshot");
    var tamper = OpsisRecoveryHub.Snapshot("Tamper-detection test");
    var tamperFile = Path.Combine(OpsisRecoveryHub.Root, "snapshots", tamper.Id, "files", "module-core.json");
    File.AppendAllText(tamperFile, "tampered");
    var untouched = File.ReadAllText(Path.Combine(folder, "module-core.json"));
    rejected = false;
    try { OpsisRecoveryHub.RestoreOwnedSettings(tamper.Id); }
    catch (InvalidDataException) { rejected = true; }
    Check(rejected, "recovery detects altered backup hash");
    Check(File.ReadAllText(Path.Combine(folder, "module-core.json")) == untouched,
        "failed recovery cannot modify live settings");

    // High repetition of the pure rules planner does not spawn work or touch
    // disk. Clock-bound and startup actions must remain mutually exclusive.
    var planned = OpsisAutomationStudio.Read();
    var dueCount = 0;
    for (var i = 0; i < 10_000; i++)
        dueCount += OpsisAutomationStudio.Plan(planned,
            new DateTime(2026, 11, 24, 9, i % 60, 0), false).Count;
    Check(dueCount > 0 && dueCount < 10_000, "ten thousand in-memory automation evaluations stable");


    Console.WriteLine($"COMPLETE {passed} validated assertions");
}
finally
{
    try { Directory.Delete(folder, true); } catch { }
}
