using MotionDesk.Appearance;
using Microsoft.Win32;

var registry = new CurrentUserRegistry();
var engine = new AppearanceEngine(registry, () => { });
var before = engine.Snapshot();
try
{
    foreach (bool enabled in new[] { false, true })
    {
        engine.Apply(new Look {
            TaskbarCentered = enabled, ShowSearch = enabled, ShowTaskView = enabled,
            ShowWidgets = enabled, ClockSeconds = enabled, Transparency = enabled, AccentOnSurfaces = enabled
        });
        var actual = engine.ReadCurrent();
        Check(actual.TaskbarCentered == enabled, nameof(actual.TaskbarCentered));
        Check(actual.ShowSearch == enabled, nameof(actual.ShowSearch));
        Check(actual.ShowTaskView == enabled, nameof(actual.ShowTaskView));
        Check(actual.ShowWidgets == enabled, nameof(actual.ShowWidgets));
        Check(actual.ClockSeconds == enabled, nameof(actual.ClockSeconds));
        Check(actual.Transparency == enabled, nameof(actual.Transparency));
        Check(actual.AccentOnSurfaces == enabled, nameof(actual.AccentOnSurfaces));
    }

    for (int group = 0; group < 3; group++)
    {
        engine.Apply(new Look { TaskbarCombine = group });
        Check(engine.ReadCurrent().TaskbarCombine == group, "Taskbar grouping " + group);
    }

    string outside = @"Software\Opsis-TaskbarAudit-" + Guid.NewGuid().ToString("N");
    try
    {
        engine.Restore([new() { Key = outside, Name = "Blocked", Kind = "DWord", Text = "1" }]);
        Check(registry.Get(outside, "Blocked") is null, "Unexpected key rejected during restore");
    }
    finally { Registry.CurrentUser.DeleteSubKeyTree(outside, throwOnMissingSubKey: false); }
}
finally { engine.Restore(before); }
Check(before.Select(x => (x.Key, x.Name, x.Kind, x.Text)).SequenceEqual(
    engine.Snapshot().Select(x => (x.Key, x.Name, x.Kind, x.Text))), "Full HKCU restore");
Console.WriteLine("All taskbar registry assertions passed.");

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    Console.WriteLine("PASS " + message);
}
