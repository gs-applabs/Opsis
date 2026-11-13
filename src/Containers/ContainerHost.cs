using System.Drawing;
using System.Windows.Forms;
using Opsis.Containers;

namespace MotionDesk.Containers
{
    /// <summary>
    /// Phase 5/9: the live set of native container windows. Owns one <see cref="ContainerWindow"/> per visible container of the stored document, keeps them in step with the data
    /// (pipe commands change the file -> <see cref="ContainersService.Mutated"/> -> windows are created / closed / moved on the UI thread) and writes a window's changes back
    /// (debounced, under the service's lock). Windows are re-seated into the visible area after a monitor change. Not wired into the running product yet (the WPF fork is still the live engine).
    /// </summary>
    internal sealed class ContainerHost : IDisposable
    {
        private readonly ContainersService _service;
        private readonly Control _ui;                                   // a control created on the UI thread, used to marshal pipe-thread events
        private readonly Dictionary<string, ContainerWindow> _windows = new();
        private readonly Dictionary<string, Container> _dirty = new();
        private readonly System.Windows.Forms.Timer _save = new() { Interval = 400 };
        // z-order: directly above the desktop, below normal windows; re-seated every second (Win+D, Explorer restart and other topmost changes move windows around)
        private readonly System.Windows.Forms.Timer _z = new() { Interval = 1000 };
        private readonly System.Windows.Forms.Timer _rules = new() { Interval = 3000 };
        private HashSet<string> _activeRules = new();
        private PipeServer? _pipe;
        private bool _syncing;

        /// <summary>The running host (null when the native containers are off); used by the desktop menu actions.</summary>
        public static ContainerHost? Current { get; private set; }

        public IReadOnlyCollection<ContainerWindow> Windows => _windows.Values;
        public ContainersService Service => _service;

        public ContainerHost(ContainersStore store, string? pipeName = null)
        {
            _ui = new Control(); _ = _ui.Handle;                        // force the handle on the UI thread
            _service = new ContainersService(store, Monitors);
            _service.Mutated += () => { if (_ui.IsHandleCreated && !_ui.IsDisposed) try { _ui.BeginInvoke(Sync); } catch (Exception) { } };
            _save.Tick += (_, _) => Flush();
            _rules.Tick += (_, _) => EvaluateRules(); _rules.Start();
            _z.Tick += (_, _) => ReseatZOrder(); _z.Start();
            if (pipeName != null) { _pipe = new PipeServer(pipeName, new HostApi(this)); _pipe.Start(); }
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
            Current = this;
            Sync();
        }

        /// <summary>Host-level commands on top of the data commands: "focus" (id) gives a container the keyboard focus, "toggle-all" hides every container or shows them again (all of them, like the old quick-hide).</summary>
        private sealed class HostApi : IContainersApi
        {
            private readonly ContainerHost _h; public HostApi(ContainerHost h) { _h = h; }
            public System.Text.Json.Nodes.JsonNode? Handle(string cmd, System.Text.Json.Nodes.JsonObject args)
            {
                switch (cmd)
                {
                    case "focus":
                    {
                        var id = args["id"]?.ToString() ?? "";
                        _h._ui.Invoke(() => { if (_h._windows.TryGetValue(id, out var w)) w.FocusForKeyboard(); });
                        return new System.Text.Json.Nodes.JsonObject { ["focused"] = _h._windows.ContainsKey(id) };
                    }
                    case "toggle-all":
                    {
                        var hidden = false;
                        _h._service.Update(doc => { hidden = doc.Containers.Any(c => !c.Hidden); foreach (var c in doc.Containers) c.Hidden = hidden; return doc.Containers.Count > 0; });
                        _h._ui.BeginInvoke(_h.Sync);
                        return new System.Text.Json.Nodes.JsonObject { ["hidden"] = hidden };
                    }
                    default: return _h._service.Handle(cmd, args);
                }
            }
        }

        private static IReadOnlyList<MonitorInfo> Monitors() => Screen.AllScreens.Select(s => new MonitorInfo(s.DeviceName, s.Bounds, s.Primary)).ToList();

        // ---- commands used by the tray menu and the global hotkeys (UI thread) ----
        public string NewContainerAtCursor(string kind = "Data", string? title = null)
        {
            var p = Cursor.Position;
            var r = _service.Handle("create", new System.Text.Json.Nodes.JsonObject { ["title"] = title ?? (kind == "Note" ? "Note" : kind == "Portal" ? "Portal" : "New container"), ["kind"] = kind, ["x"] = p.X, ["y"] = p.Y, ["width"] = 320, ["height"] = 240 });
            Sync();
            return r?["id"]?.ToString() ?? "";
        }

        /// <summary>Hides every container, or shows them again when none is visible (the quick-hide gesture). Returns true when they are hidden now.</summary>
        public bool ToggleAll()
        {
            var hidden = false;
            _service.Update(doc => { hidden = doc.Containers.Any(c => !c.Hidden); foreach (var c in doc.Containers) c.Hidden = hidden; return doc.Containers.Count > 0; });
            Sync(); return hidden;
        }

        /// <summary>Quick-hide / quick-show of the desktop gesture: every container follows the desktop icons.</summary>
        public void SetAllHidden(bool hidden)
        {
            _service.Update(doc => { var any = false; foreach (var c in doc.Containers) if (c.Hidden != hidden) { c.Hidden = hidden; any = true; } return any; });
            Sync();
        }

        public void SetHidden(string id, bool hidden) { _service.Handle(hidden ? "hide" : "show", new System.Text.Json.Nodes.JsonObject { ["id"] = id }); Sync(); }
        public void Focus(string id) { if (_windows.TryGetValue(id, out var w)) w.FocusForKeyboard(); }
        public string SaveSnapshot(string name) { Flush(); var r = _service.Handle("snapshot-save", new System.Text.Json.Nodes.JsonObject { ["name"] = name }); return r?["saved"]?.ToString() ?? ""; }
        public void LoadSnapshot(string name) { Flush(); _service.Handle("snapshot-load", new System.Text.Json.Nodes.JsonObject { ["name"] = name }); Sync(); }
        public IReadOnlyList<string> SnapshotNames() => ((System.Text.Json.Nodes.JsonArray)_service.Handle("snapshot-list", new System.Text.Json.Nodes.JsonObject())!).Select(n => n!.ToString()).ToList();
        public IReadOnlyList<Container> Containers() => _service.Snapshot().Containers;

        /// <summary>Makes the windows match the stored data (UI thread).</summary>
        public void Sync()
        {
            if (_syncing) return; _syncing = true;
            try
            {
                var doc = _service.Snapshot(); var want = doc.Containers.Where(c => !c.Hidden).ToDictionary(c => c.Id);
                foreach (var id in _windows.Keys.Where(k => !want.ContainsKey(k)).ToList()) { _windows[id].Close(); _windows[id].Dispose(); _windows.Remove(id); _dirty.Remove(id); }
                var mons = Monitors();
                foreach (var (id, model) in want)
                {
                    if (_windows.TryGetValue(id, out var existing))
                    {
                        var b = Place(model, mons); if (existing.Bounds != b && !_dirty.ContainsKey(id)) existing.Bounds = b;
                        continue;
                    }
                    var w = new ContainerWindow(model, () => _windows.Where(p => p.Key != id).Select(p => p.Value.Bounds)) { Bounds = Place(model, mons) };
                    w.Changed += c => { _dirty[c.Id] = c; if (!_save.Enabled) _save.Start(); };
                    _windows[id] = w; w.Show(); DesktopZOrder.Place(w.Handle);
                }
            }
            finally { _syncing = false; }
        }

        private static Rectangle Place(Container c, IReadOnlyList<MonitorInfo> mons)
        {
            var d = LegacyImporter.Denormalize(c.Geometry, mons);
            var r = new Rectangle(d.X, d.Y, Math.Max(80, d.Width), Math.Max(c.RolledUp ? c.TitleBar.Height : 60, d.Height));
            var areas = Screen.AllScreens.Select(s => s.WorkingArea).ToList();
            return ContainerBehavior.KeepReachable(r, areas, c.TitleBar.Height);
        }

        /// <summary>Puts every container window back directly above the desktop (cheap: a no-op while they already are).</summary>
        public void ReseatZOrder()
        {
            foreach (var w in _windows.Values) if (w.IsHandleCreated && w.Visible) DesktopZOrder.Place(w.Handle);
        }

        /// <summary>Phase 5: rules (monitor count, process running, time of day, fullscreen app) are evaluated every 3 s, edge-triggered; cheap when there are none.</summary>
        public void EvaluateRules()
        {
            var rules = _service.Snapshot().Rules.Where(r => r.Enabled).ToList();
            if (rules.Count == 0) { _activeRules.Clear(); return; }
            var procs = rules.Any(r => r.When == RuleWhen.ProcessRunning)
                ? System.Diagnostics.Process.GetProcesses().Select(p => { try { return p.ProcessName.ToLowerInvariant() + ".exe"; } finally { p.Dispose(); } }).ToHashSet()
                : new HashSet<string>();
            var ctx = new RuleContext(Screen.AllScreens.Length, procs, DateTime.Now.TimeOfDay, MotionDesk.Services.AppActivity.IsFullscreenAppActive);
            var (actions, active) = RuleEngine.Evaluate(rules, ctx, _activeRules); _activeRules = active;
            foreach (var a in actions)
            {
                if (a.Do == RuleDo.LoadSnapshot) { try { _service.Handle("snapshot-load", new System.Text.Json.Nodes.JsonObject { ["name"] = a.Targets.FirstOrDefault() ?? "" }); } catch (PipeException) { } continue; }
                _service.Update(doc => RuleEngine.Apply(doc, a)); Sync();
            }
        }

        /// <summary>Writes the changed containers back through the service (one load/save for all of them).</summary>
        public void Flush()
        {
            _save.Stop();
            if (_dirty.Count == 0) return;
            var changed = _dirty.Values.ToList(); _dirty.Clear();
            _service.Update(doc =>
            {
                var any = false;
                foreach (var c in changed) { var i = doc.Containers.FindIndex(x => x.Id == c.Id); if (i >= 0) { doc.Containers[i] = c; any = true; } }
                return any;
            });
        }

        private void OnDisplayChanged(object? s, EventArgs e) { if (_ui.IsHandleCreated && !_ui.IsDisposed) try { _ui.BeginInvoke(() => { foreach (var w in _windows.Values) w.Bounds = Place(w.Model, Monitors()); }); } catch (Exception) { } }

        public void Dispose()
        {
            if (ReferenceEquals(Current, this)) Current = null;
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
            Flush(); _save.Dispose(); _rules.Dispose(); _z.Dispose(); _pipe?.Dispose();
            foreach (var w in _windows.Values) { w.Close(); w.Dispose(); }
            _windows.Clear(); _ui.Dispose();
        }
    }
}
