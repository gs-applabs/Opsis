using System;
using System.Collections.Generic;
using MotionDesk.Widgets;

namespace MotionDesk.Services;

/// <summary>
/// Executable adapters for selected standalone Opsis modules. This is the bridge
/// from the capability registry to the real services, never a pretend toggle.
/// Only modules with an explicit reversible adapter are switchable here.
/// </summary>
public static class OpsisFeatureSwitcher
{
    public static IReadOnlyList<string> Switchable { get; } = new[]
    {
        "windows.hot-corners", "windows.window-rules", "automation.studio",
        "explorer.context-menu", "theme.dynamic-accent", "cursor.effects"
    };

    public static bool CanSwitch(string id) =>
        id is "windows.hot-corners" or "windows.window-rules" or "automation.studio" or
            "explorer.context-menu" or "theme.dynamic-accent" or "cursor.effects";

    public static bool IsEnabled(string id)
    {
        var s = AppSettings.Load();
        return id switch
        {
            "windows.hot-corners" => s.HotCornersEnabled,
            "windows.window-rules" => s.WindowRulesEnabled,
            "automation.studio" => s.OpsisAutomationsEnabled,
            "explorer.context-menu" => OpsisContextMenuStudio.IsEnabled(),
            "theme.dynamic-accent" => s.WallpaperAccentEnabled,
            "cursor.effects" => s.CursorFxEnabled,
            _ => false
        };
    }

    public static void Set(string id, bool enabled)
    {
        if (!CanSwitch(id)) throw new NotSupportedException("No operational adapter is registered for: " + id);
        if (enabled && (SafeModeService.IsSafeMode || WindowsUpdateGuard.RequiresReview))
            throw new InvalidOperationException("This Windows session requires a Safe Mode/compatibility review.");
        bool previous = IsEnabled(id);
        if (previous == enabled) return;

        // Snapshot before user changes; all adapters use their existing safe
        // settings and never patch protected Windows Shell binaries.
        OpsisRecoveryHub.Snapshot("Before Opsis feature toggle " + id);
        OpsisModCore.SetModule(id, enabled, Environment.OSVersion.Version.Build, false);
        try
        {
            var s = AppSettings.Load();
            switch (id)
            {
                case "windows.hot-corners":
                    s.HotCornersEnabled = enabled; s.Save(); break;
                case "windows.window-rules":
                    WindowRulesEngine.Instance.SetEnabled(enabled);
                    break;
                case "automation.studio":
                    s.OpsisAutomationsEnabled = enabled; s.Save(); break;
                case "explorer.context-menu":
                    if (enabled) OpsisContextMenuStudio.Enable();
                    else OpsisContextMenuStudio.Disable();
                    break;
                case "theme.dynamic-accent":
                    if (enabled) WallpaperAccentEngine.ApplyCurrent();
                    else WallpaperAccentEngine.Disable();
                    break;
                case "cursor.effects":
                    s.CursorFxEnabled = enabled;
                    s.Save();
                    if (enabled) MotionDesk.UI.OpsisCursorFx.Start();
                    else MotionDesk.UI.OpsisCursorFx.Stop();
                    break;
            }
        }
        catch
        {
            // Return the module registry to its original state if the adapter
            // could not be activated (e.g. unknown Start Menu ownership).
            OpsisModCore.SetModule(id, previous, Environment.OSVersion.Version.Build, false);
            throw;
        }
    }
}
