using MotionDesk.Appearance;

var explorer = (IntPtr)1;
var other = (IntPtr)2;
var dwm = new FakeDwm(explorer, other);
var engine = new WindowEffectsEngine(dwm, new FakeTaskbar(), () => false);
var settings = new EffectsSettings { ExplorerGlass = true };
void Check(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine("PASS " + name); }

engine.Apply(settings);
Check(dwm.Backdrops.GetValueOrDefault(explorer) == 3 && !dwm.Backdrops.ContainsKey(other), "Explorer only");
settings.ExplorerGlass = false;
engine.Apply(settings);
Check(dwm.Backdrops[explorer] == 0, "restore after in-place settings mutation");

settings.Backdrop = BackdropStyle.Mica;
settings.ExplorerGlass = true;
engine.Apply(settings);
Check(dwm.Backdrops[explorer] == 3 && dwm.Backdrops[other] == 2, "override global backdrop");
settings.ExplorerGlass = false;
engine.Apply(settings);
Check(dwm.Backdrops[explorer] == 2, "restore global backdrop");

settings.Backdrop = BackdropStyle.Default;
settings.ExplorerGlass = true;
settings.ExcludedProcesses.Add("explorer");
engine.Apply(settings);
Check(dwm.Backdrops[explorer] == 0, "exclusions respected");
settings.ExcludedProcesses.Clear();
dwm.SupportsExplorerGlass = false;
engine.Apply(settings);
Check(dwm.Backdrops[explorer] == 0, "unsupported platform");
dwm.SupportsExplorerGlass = true;
engine.Apply(settings);
Check(dwm.Backdrops[explorer] == 3, "enable on supported platform");
engine.RestoreAll();
Check(dwm.Backdrops[explorer] == 0, "restore on exit");

sealed class FakeDwm(IntPtr explorer, IntPtr other) : IDwmWindows
{
    public Dictionary<IntPtr,int> Backdrops { get; } = new();
    public bool SupportsExplorerGlass { get; set; } = true;
    public bool IsExplorerWindow(IntPtr h) => h == explorer;
    public string? ProcessName(IntPtr h) => h == explorer ? "explorer.exe" : "other.exe";
    public IReadOnlyList<IntPtr> TopLevelWindows() => new[] { explorer, other };
    public bool IsAlive(IntPtr h) => h == explorer || h == other;
    public bool IsEligible(IntPtr h) => IsAlive(h);
    public IntPtr Foreground => explorer;
    public void SetBackdrop(IntPtr h, int value) => Backdrops[h] = value;
    public void SetCorner(IntPtr h, int preference) { }
    public void SetBorderColor(IntPtr h, int colorRef) { }
    public void SetDarkTitle(IntPtr h, bool dark) { }
}
sealed class FakeTaskbar : ITaskbarControl
{
    public bool IsAutoHide { get; private set; }
    public void SetAutoHide(bool on) => IsAutoHide = on;
}
