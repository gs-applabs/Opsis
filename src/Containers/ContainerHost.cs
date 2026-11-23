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
    internal sealed partial class ContainerHost : IDisposable
    {
        private readonly ContainersService _service;
        private readonly Control _ui;                                   // a control created on the UI thread, used to marshal pipe-thread events
        private readonly Dictionary<string, ContainerWindow> _windows = new();
        private readonly Dictionary<string, Container> _dirty = new();
        private readonly ContainerLayoutHistory _layoutHistory = new();
        private readonly System.Windows.Forms.Timer _layoutHistorySave = new() { Interval = 4000 };
        private readonly System.Windows.Forms.Timer _save = new() { Interval = 400 };
        // z-order: directly above the desktop, below normal windows; re-seated every second (Win+D, Explorer restart and other topmost changes move windows around)
        private readonly System.Windows.Forms.Timer _z = new() { Interval = 1000 };
        private readonly System.Windows.Forms.Timer _rules = new() { Interval = 3000 };
        private HashSet<string> _activeRules = new();
        private PipeServer? _pipe;
        private bool _syncing, _quickHidden;
        internal event Action<string, string>? ContainerRenamed;
        internal event Action<string>? ContainerDeleted;
        internal event Action? WorkspacePreviewChanged;

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
            _layoutHistorySave.Tick += (_, _) => { _layoutHistorySave.Stop(); _layoutHistory.Save(); };
            _rules.Tick += (_, _) => EvaluateRules(); _rules.Start();
            _z.Tick += (_, _) => ReseatZOrder(); _z.Start();
            if (pipeName != null) { _pipe = new PipeServer(pipeName, new HostApi(this)); _pipe.Start(); }
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
            // live settings: a change of the global look (theme / tint / glass / performance tier / language) repaints the open windows, debounced into one repaint
            _look.Tick += (_, _) => { _look.Stop(); foreach (var w in _windows.Values) w.Restyle(); };
            MotionDesk.UI.ThemeManager.Changed += OnLookChanged; MotionDesk.UI.ThemeManager.Repainted += OnLookChanged;
            MotionDesk.Services.HardwareProfileService.ProfileChanged += OnTierChanged; MotionDesk.Services.LocalizationManager.Changed += OnLookChanged;
            _peekTimer.Tick += (_, _) => UpdatePeek();
            _monitorTimer.Tick += (_, _) => ApplyMonitorLayout();
            Current = this;
            try { Backup(false); } catch (Exception) { }
            try { _selection = new DesktopSelectionWatcher(this); } catch (Exception) { }
            Sync();
            StartOrganization();
            ApplyMonitorLayout();
        }

        /// <summary>Daily backup of the containers file (at most one per day, the newest 7 are kept); <paramref name="force"/> makes one more now ("Back up now" in the tray).</summary>
        public string? Backup(bool force)
        {
            var src = _service.StorePath; if (!File.Exists(src)) return null;
            var dir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(src)) ?? ".", "container-backups"); Directory.CreateDirectory(dir);
            var name = Path.Combine(dir, "containers-" + DateTime.Now.ToString(force ? "yyyyMMdd-HHmmss" : "yyyyMMdd") + ".json");
            if (!force && File.Exists(name)) return null;
            Flush(); File.Copy(src, name, true);
            foreach (var old in Directory.GetFiles(dir, "containers-*.json").OrderByDescending(f => f).Skip(7)) try { File.Delete(old); } catch (Exception) { }
            return name;
        }

        private DesktopSelectionWatcher? _selection;
        /// <summary>Rubber-band to container: makes a container at <paramref name="at"/> from desktop items (the originals are hidden like after a drop, always restorable).</summary>
        public string CreateFromDesktopSelection(IReadOnlyList<string> paths, Point at)
        {
            var id = NewContainerAtCursor("Data", null);
            var items = paths.Select(p => new ContainerItem { Path = p, DisplayName = Path.GetFileNameWithoutExtension(p), IsFolder = Directory.Exists(p) }).ToList();
            Apply(id, c => { c.Items.AddRange(items); c.Geometry.X = at.X; c.Geometry.Y = at.Y; return true; });
            if (MotionDesk.Services.AppSettings.Load().HideOriginalsOnDrop) MotionDesk.Widgets.DesktopIconVisibilityEngine.HideOriginals(paths);
            return id;
        }

        private readonly System.Windows.Forms.Timer _look = new() { Interval = 150 };
        private void OnTierChanged(MotionDesk.Services.HardwareTier _) => OnLookChanged();
        private void OnLookChanged() { if (_ui.IsHandleCreated && !_ui.IsDisposed) try { _ui.BeginInvoke(() => { _look.Stop(); _look.Start(); }); } catch (Exception) { } }

        /// <summary>Changes one container through the service and shows it at once (flushes pending window changes first so nothing is overwritten).</summary>
        public void Apply(string id, Func<Container, bool> change)
        {
            Flush();
            _service.Update(doc =>
            {
                var c = doc.Containers.FirstOrDefault(x => x.Id == id);
                if (c == null) return false;
                _layoutHistory.Remember(c);
                if (!_layoutHistorySave.Enabled) _layoutHistorySave.Start();
                return change(c);
            });
            Sync();
        }

        internal void Rename(string id, string title)
        {
            title = title.Trim();
            if (title.Length == 0) return;
            bool renamed = false;
            Apply(id, container =>
            {
                if (container.Title == title) return false;
                container.Title = title;
                renamed = true;
                return true;
            });
            if (renamed) ContainerRenamed?.Invoke(id, title);
        }

        internal void MoveFromPreview(string id, Point position)
        {
            var monitors = Monitors();
            Apply(id, model =>
            {
                if (model.Locked) return false;
                var bounds = Place(model, monitors);
                model.Geometry = LegacyImporter.Normalize(position.X, position.Y, bounds.Width, bounds.Height, monitors);
                return true;
            });
        }

        internal void ConfirmDelete(IWin32Window owner, string id)
        {
            var container = Containers().FirstOrDefault(c => c.Id == id);
            if (container == null) return;
            string title = string.IsNullOrWhiteSpace(container.Title) ? container.Id : container.Title;
            string message = string.Format(MotionDesk.Services.LocalizationManager.T("DCX.DeleteConfirm"), title);
            string heading = MotionDesk.Services.LocalizationManager.T("DCX.DeleteTitle");
            if (MessageBox.Show(owner, message, heading, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes)
                Delete(id);
        }

        internal bool Delete(string id)
        {
            Flush();
            var container = Containers().FirstOrDefault(c => c.Id == id);
            if (container == null) return false;
            _layoutHistory.Remember(container);
            _layoutHistorySave.Start();
            _service.Handle("delete", new System.Text.Json.Nodes.JsonObject { ["id"] = id });
            var remaining = Containers();
            if (remaining.Any(c => c.Id == id)) return false;
            Sync();

            static IEnumerable<string> References(Container c) =>
                c.Items.Concat(c.Tabs.SelectMany(tab => tab.Items)).Select(item => item.Path);
            var released = References(container).Except(remaining.SelectMany(References), StringComparer.OrdinalIgnoreCase);
            if (MotionDesk.Widgets.DesktopIconVisibilityEngine.RestoreOriginalsForPaths(released) > 0)
                MotionDesk.Widgets.DesktopIconVisibilityEngine.Instance.ApplyKeepSettings();
            ContainerDeleted?.Invoke(id);
            return true;
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
                        bool hidden = _h._ui.Invoke(_h.ToggleAll);
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

        /// <summary>Toggles the temporary quick-hide state while preserving individual visibility settings.</summary>
        public bool ToggleAll()
        {
            SetAllHidden(!_quickHidden);
            return _quickHidden;
        }

        public void SetAllHidden(bool hidden)
        {
            if (_quickHidden == hidden) return;
            Flush();
            _quickHidden = hidden;
            Sync();
        }

        public void ShowSpotSearch(string initialQuery = "")
        {
            var f = new SpotSearchForm(() => _service.Snapshot(), initialQuery);
            f.FormClosed += (_, _) => f.Dispose();
            f.Show();
        }

        /// <summary>Profile hotkeys: next / previous layout snapshot (the saved layouts act as profiles). Returns the name that was loaded, or null when there is none.</summary>
        public string? CycleSnapshot(int direction)
        {
            var names = SnapshotNames(); if (names.Count == 0) return null;
            _snapIndex = ((_snapIndex + direction) % names.Count + names.Count) % names.Count;
            LoadSnapshot(names[_snapIndex]); return names[_snapIndex];
        }
        private int _snapIndex = -1;

        public void SetHidden(string id, bool hidden) { Flush(); _service.Handle(hidden ? "hide" : "show", new System.Text.Json.Nodes.JsonObject { ["id"] = id }); Sync(); }
        private int _focusIndex = -1;
        /// <summary>Focus-container hotkey: gives the keyboard focus to the next visible container (Tab-like cycling), so the arrows / Enter work in it.</summary>
        public void FocusNext()
        {
            var ids = _windows.Where(p => p.Value.Visible).OrderBy(p => p.Value.Top).ThenBy(p => p.Value.Left).Select(p => p.Key).ToList(); if (ids.Count == 0) return;
            _focusIndex = (_focusIndex + 1) % ids.Count; Focus(ids[_focusIndex]);
        }
        public void Focus(string id) { if (_windows.TryGetValue(id, out var w)) w.FocusForKeyboard(); }
        public string SaveSnapshot(string name) { Flush(); var r = _service.Handle("snapshot-save", new System.Text.Json.Nodes.JsonObject { ["name"] = name }); return r?["saved"]?.ToString() ?? ""; }
        public void LoadSnapshot(string name)
        {
            Flush();
            if (!SnapshotNames().Contains(name)) return;
            _service.Handle("snapshot-load", new System.Text.Json.Nodes.JsonObject { ["name"] = name });
            ReplaceOrganizationWindows(_ => true);
        }
        public IReadOnlyList<string> SnapshotNames() => ((System.Text.Json.Nodes.JsonArray)_service.Handle("snapshot-list", new System.Text.Json.Nodes.JsonObject())!).Select(n => n!.ToString()).ToList();
        public IReadOnlyList<Container> Containers() => _service.Snapshot().Containers;
        internal bool HasLayoutHistory(string id) => _layoutHistory.Has(id);
        internal void RestorePreviousLayout(string id, IWin32Window owner)
        {
            if (!_layoutHistory.Has(id)) return;
            if (MessageBox.Show(owner,
                    "Restore the previous saved container layout? The actual files will not be changed.",
                    "Opsis · Layout history", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            Flush(); Backup(force: true);
            var previous = _layoutHistory.Pop(id);
            if (previous == null) return;
            _service.Update(doc =>
            {
                int index = doc.Containers.FindIndex(c => c.Id == id);
                if (index >= 0) doc.Containers[index] = previous;
                else doc.Containers.Add(previous);
                return true;
            });
            _layoutHistory.Save();
            ReplaceOrganizationWindows(c => c.Id == id);
        }
        internal async Task InspectBrokenShortcutsAsync(string id, IWin32Window owner)
        {
            var c = Containers().FirstOrDefault(x => x.Id == id);
            if (c == null) return;
            var paths = c.Items.Concat(c.Tabs.SelectMany(tab => tab.Items)).Select(i => i.Path).ToArray();
            await BrokenShortcutTools.InspectAndRepairAsync(owner, paths);
            if (_windows.TryGetValue(id, out var window) && !window.IsDisposed) window.Restyle();
        }

        /// <summary>Makes the windows match the stored data (UI thread).</summary>
        public void Sync()
        {
            if (_syncing) return; _syncing = true;
            try
            {
                var doc = _service.Snapshot(); var want = doc.Containers.Where(c => !c.Hidden && (!_quickHidden || c.ExcludeFromQuickHide)).ToDictionary(c => c.Id);
                foreach (var id in _windows.Keys.Where(k => !want.ContainsKey(k)).ToList()) { _windows[id].Close(); _windows[id].Dispose(); _windows.Remove(id); _dirty.Remove(id); }
                var mons = Monitors();
                bool added = false;
                foreach (var (id, model) in want)
                {
                    if (_windows.TryGetValue(id, out var existing))
                    {
                        if (!_dirty.ContainsKey(id))
                        {
                            existing.ApplyStyle(model);
                            existing.SetPlacement(Place(model, mons));
                        }
                        continue;
                    }
                    var w = new ContainerWindow(model, () => _windows.Where(p => p.Key != id).Select(p => p.Value.Bounds));
                    w.SetPlacement(Place(model, mons));
                    w.Changed += c => { _dirty[c.Id] = c; if (!_save.Enabled) _save.Start(); };
                    w.Renamed += Rename;
                    w.PreviewContentChanged += () => WorkspacePreviewChanged?.Invoke();
                    w.DeleteRequested += () => ConfirmDelete(w, w.Model.Id);
                    w.SettingsRequested += cid => ContainerSettingsForm.Open(this, cid);
                    w.ItemsAdded += paths => { if (MotionDesk.Services.AppSettings.Load().HideOriginalsOnDrop) MotionDesk.Widgets.DesktopIconVisibilityEngine.HideOriginals(paths); };
                    w.ItemRemoved += p => MotionDesk.Widgets.DesktopIconVisibilityEngine.RestoreOriginals(p);
                    ApplyOrganizationResults(w);
                    _windows[id] = w; w.Show();
                    added = true;
                }
                if (added) ReseatZOrder();
            }
            finally { _syncing = false; }
        }

        private static Rectangle Place(Container c, IReadOnlyList<MonitorInfo> mons)
        {
            var d = LegacyImporter.Denormalize(c.Geometry, mons);
            int height = d.Height;
            if (c.RolledUp && height <= c.TitleBar.Height + 12 && c.UnrolledHeight > height)
                height = c.UnrolledHeight; // older layouts stored the collapsed title height in Geometry
            var r = new Rectangle(d.X, d.Y, Math.Max(80, d.Width), Math.Max(60, height));
            var areas = Screen.AllScreens.Select(s => s.WorkingArea).ToList();
            return ContainerBehavior.KeepReachable(r, areas, c.TitleBar.Height);
        }

        /// <summary>Puts every container window back directly above the desktop (cheap: a no-op while they already are).</summary>
        /// <summary>Tests that drive the real mouse make the windows topmost (a normal window of the user could otherwise cover them) and stop the periodic re-seating.</summary>
        public static bool KeepOnTopForTests;
        public void ReseatZOrder()
        {
            if (KeepOnTopForTests || _peeking) { foreach (var w in _windows.Values) if (w.IsHandleCreated && w.Visible && !w.TopMost) w.TopMost = true; return; }
            DesktopZOrder.PlaceGroup(_windows.Values.Where(window => window.IsHandleCreated && window.Visible)
                .Select(window => window.Handle).ToHashSet());
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
                foreach (var c in changed)
                {
                    var i = doc.Containers.FindIndex(x => x.Id == c.Id);
                    if (i < 0) continue;
                    _layoutHistory.Remember(doc.Containers[i]);
                    doc.Containers[i] = c; any = true;
                }
                if (any && !_layoutHistorySave.Enabled) _layoutHistorySave.Start();
                return any;
            });
        }

        private void OnDisplayChanged(object? sender, EventArgs args)
        {
            if (!_ui.IsHandleCreated || _ui.IsDisposed) return;
            try { _ui.BeginInvoke(() => { _monitorTimer.Stop(); _monitorTimer.Start(); }); }
            catch (InvalidOperationException) { }
        }

        public void Dispose()
        {
            StopPeek();
            _peekTimer.Dispose();
            _monitorTimer.Dispose();
            StopOrganization();
            if (ReferenceEquals(Current, this)) Current = null;
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
            MotionDesk.UI.ThemeManager.Changed -= OnLookChanged; MotionDesk.UI.ThemeManager.Repainted -= OnLookChanged;
            MotionDesk.Services.HardwareProfileService.ProfileChanged -= OnTierChanged; MotionDesk.Services.LocalizationManager.Changed -= OnLookChanged; _look.Dispose(); _selection?.Dispose();
            try { MotionDesk.Widgets.DesktopIconVisibilityEngine.RestoreOriginals(); } catch (Exception) { }          // every exit gives the hidden originals back
            Flush(); _layoutHistorySave.Stop(); _layoutHistory.Save(); _layoutHistorySave.Dispose();
            _save.Dispose(); _rules.Dispose(); _z.Dispose(); _pipe?.Dispose();
            foreach (var w in _windows.Values) { w.Close(); w.Dispose(); }
            _windows.Clear(); _ui.Dispose();
        }
    }
}
