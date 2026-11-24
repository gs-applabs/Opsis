using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace MotionDesk.Services
{
    /// <summary>
    /// `Opsis.exe --ui-audit &lt;folder&gt;` (CI: workflow ui-click-audit; needs OPSIS_E2E=1 and an isolated OPSIS_APPDATA): opens the main window, visits EVERY page, screenshots it, and "clicks"
    /// supported check boxes and combo boxes (change, verify, restore), plus explicit checks for risky commands and dynamic panels.
    /// Report: ui-audit.md + ui-audit.json + one PNG per page. Exit 0 means the executed checks passed.
    /// </summary>
    public static class UiAudit
    {
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] private static extern IntPtr FindWindow(string? cls, string? title);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int GetWindowThreadProcessId(IntPtr h, out int pid);
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] private static extern int GetWindowText(IntPtr h, System.Text.StringBuilder sb, int max);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool GetLayeredWindowAttributes(IntPtr window, out uint key, out byte alpha, out uint flags);

        private delegate bool EnumProc(IntPtr h, IntPtr l);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr l);
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] private static extern int GetClassName(IntPtr h, System.Text.StringBuilder sb, int max);

        // A modal message box opened by some control would wait for ever on a runner nobody watches: close every standard dialog (#32770) of this process and note it in the report.
        private static Thread StartDialogGuard(ConcurrentQueue<string> problems, Func<string> where, CancellationToken cancellation)
        {
            int me = Environment.ProcessId;
            var seen = new HashSet<IntPtr>();
            var t = new System.Threading.Thread(() =>
            {
                while (!cancellation.WaitHandle.WaitOne(1500))
                {
                    try
                    {
                        EnumWindows((h, _) =>
                        {
                            GetWindowThreadProcessId(h, out int pid);
                            if (pid != me || !IsWindowVisible(h)) return true;
                            var cls = new System.Text.StringBuilder(64); GetClassName(h, cls, 64);
                            if (cls.ToString() != "#32770") return true;
                            var title = new System.Text.StringBuilder(256); GetWindowText(h, title, 256);
                            if (seen.Add(h)) problems.Enqueue($"modal dialog '{title}' opened near {where()} (answered by the audit)");
                            // a Yes/No box has no close button (WM_CLOSE is ignored): press No, then Cancel, then OK (IDNO=7, IDCANCEL=2, IDOK=1) - whichever button the box has
                            foreach (int id in new[] { 7, 2, 1 }) PostMessage(h, 0x0111 /* WM_COMMAND */, (IntPtr)id, IntPtr.Zero);
                            PostMessage(h, 0x0010 /* WM_CLOSE */, IntPtr.Zero, IntPtr.Zero);
                            return true;
                        }, IntPtr.Zero);
                    }
                    catch (Exception) { }
                }
            }) { IsBackground = true };
            t.Start();
            return t;
        }

        private static void Pump(int ms)
        {
            var end = DateTime.UtcNow.AddMilliseconds(ms);
            while (DateTime.UtcNow < end) { Application.DoEvents(); System.Threading.Thread.Sleep(10); }
        }

        // hang hunting: every message the UI thread dispatches while the taskbar toggles are checked (the last one written is the one whose handler did not return)
        private sealed class TraceFilter : IMessageFilter
        {
            private int _n;
            public bool PreFilterMessage(ref Message m)
            {
                if (_n++ < 600) HangTrace.Mark($"msg 0x{m.Msg:X4} hwnd={(Control.FromHandle(m.HWnd)?.GetType().Name ?? m.HWnd.ToString())}");
                return false;
            }
        }

        private static void VerifyTaskbarControls(Form window, List<string> problems, Action<string> mark)
        {
            var traceFilter = new TraceFilter(); Application.AddMessageFilter(traceFilter);
            var engine = MotionDesk.Appearance.AppearanceService.Instance.Engine;
            var snapshot = engine.Snapshot();
            var toggles = new (string Name, Func<MotionDesk.Appearance.Look, bool?> Read)[]
            {
                ("Settings.TransparencyEffects", x => x.Transparency),
                ("Win.AccentSurfaces", x => x.AccentOnSurfaces),
                ("Win.Centered", x => x.TaskbarCentered),
                ("Win.TbSearch", x => x.ShowSearch),
                ("Win.TbTaskView", x => x.ShowTaskView),
                ("Win.TbWidgets", x => x.ShowWidgets),
                ("Win.ClockSecs", x => x.ClockSeconds)
            };
            try
            {
                foreach (var (name, read) in toggles)
                {
                    mark("[Taskbar] " + name);
                    var control = window.Controls.Find(name, true).OfType<Opsis.UI.FlatToggle>().FirstOrDefault();
                    if (control == null) { problems.Add("[Taskbar] Missing " + name); continue; }
                    bool original = control.Checked;
                    try
                    {
                        HangTrace.Mark("audit: toggling " + name);
                        control.Checked = !original;
                        HangTrace.Mark("audit: toggled, pumping");
                        Pump(250);
                        HangTrace.Mark("audit: pumped, reading");
                        var afterToggle = engine.ReadCurrent();
                        HangTrace.Mark("audit: read");
                        if (read(afterToggle) != !original) problems.Add("[Taskbar] Windows did not apply " + name + " (wanted " + !original + ", read " + (read(afterToggle)?.ToString() ?? "null") + ", EnableTransparency=" + (Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USERSoftwareMicrosoftWindowsCurrentVersionThemesPersonalize", "EnableTransparency", "missing")?.ToString() ?? "null") + ")");
                    }
                    finally
                    {
                        var current = window.Controls.Find(name, true).OfType<Opsis.UI.FlatToggle>().FirstOrDefault();
                        HangTrace.Mark("audit: restoring " + name);
                        if (current != null) current.Checked = original;
                        HangTrace.Mark("audit: restored, pumping");
                        Pump(250);
                        HangTrace.Mark("audit: pumped after restore");
                        if (read(engine.ReadCurrent()) != original) problems.Add("[Taskbar] Windows did not restore " + name);
                    }
                }

                mark("[Taskbar] Win.Combine");
                if (window.Controls.Find("Win.Combine", true).FirstOrDefault() is Opsis.UI.FlatComboBox combine)
                {
                    int original = combine.SelectedIndex;
                    try
                    {
                        for (int mode = 0; mode < 3; mode++)
                        {
                            combine.SelectedIndex = mode;
                            Pump(120);
                            if (engine.ReadCurrent().TaskbarCombine != mode) problems.Add("[Taskbar] Grouping mode did not apply: " + mode);
                        }
                    }
                    finally { combine.SelectedIndex = original; Pump(120); }
                }
                else problems.Add("[Taskbar] Missing Win.Combine");
            }
            finally { Application.RemoveMessageFilter(traceFilter); engine.Restore(snapshot); }
        }

        private static IEnumerable<MotionDesk.UI.StudioAdvancedSection> AdvancedSections(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                if (child is MotionDesk.UI.StudioAdvancedSection section) yield return section;
                foreach (var nested in AdvancedSections(child)) yield return nested;
            }
        }

        private static MotionDesk.Containers.ContainerHost? auditHost;

        private static void VerifyHelp(Form owner, List<string> problems)
        {
            if (owner.Controls.Find("open-opsis-help", true).FirstOrDefault() is not Button open)
            {
                problems.Add("[Help] Cannot find the Help button");
                return;
            }
            open.PerformClick();
            Pump(100);
            using var help = Application.OpenForms.OfType<MotionDesk.UI.HelpForm>().SingleOrDefault();
            if (help == null)
            {
                problems.Add("[Help] The Help button did not open the window");
                return;
            }
            var tabs = help.Controls.Find("HelpTabs", true).OfType<TabControl>().Single();
            var picker = help.Controls.Find("HistoryVersionPicker", true).OfType<ListBox>().Single();
            var notes = help.Controls.Find("HistoryNotes", true).OfType<RichTextBox>().Single();
            tabs.SelectedIndex = (int)MotionDesk.UI.HelpSection.History;
            Pump(100);
            if (!picker.Visible || !notes.Visible || picker.Items.Count < 2 || picker.SelectedIndex != 0 ||
                !notes.Text.Contains(AppVersion.Label, StringComparison.Ordinal))
            {
                problems.Add("[Help] Release history is empty or does not start with the current release");
                return;
            }
            picker.SelectedIndex = 1;
            string previous = notes.Text;
            tabs.SelectedIndex = (int)MotionDesk.UI.HelpSection.License;
            tabs.SelectedIndex = (int)MotionDesk.UI.HelpSection.History;
            Pump(100);
            if (!notes.Visible || picker.SelectedIndex != 1 || notes.Text != previous)
                problems.Add("[Help] History did not keep the selected version after switching tabs");
            help.Close();
        }

        public static int Run(string folder)
        {
            Directory.CreateDirectory(folder);
            string nl = Environment.NewLine;
            if (Environment.GetEnvironmentVariable("OPSIS_E2E") != "1" || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OPSIS_APPDATA")))
            { File.WriteAllText(Path.Combine(folder, "ui-audit.md"), "refused: needs OPSIS_E2E=1 and OPSIS_APPDATA"); return 2; }

            var problems = new List<string>();
            var dialogProblems = new ConcurrentQueue<string>();
            string? requestedLanguage = Environment.GetEnvironmentVariable("OPSIS_AUDIT_LANGUAGE");
            string? requestedTheme = Environment.GetEnvironmentVariable("OPSIS_AUDIT_THEME");
            if (!string.IsNullOrWhiteSpace(requestedLanguage)) LocalizationManager.SetLanguage(requestedLanguage);
            if (requestedTheme is "Light" or "Dark" or "Contrast") MotionDesk.UI.ThemeManager.SetMode(requestedTheme);
            if (Enum.TryParse<MotionDesk.UI.WindowSkin>(Environment.GetEnvironmentVariable("OPSIS_AUDIT_SKIN"), true, out var skin)
                && Enum.IsDefined(skin))
            {
                var settings = AppSettings.Load();
                settings.MainWindowSkin = skin.ToString();
                settings.WindowSkinRevision = 1;
                settings.Save();
            }
            var report = new StringBuilder("# Opsis UI click audit" + nl + nl + "| page | controls | clicked | slowest click ms | page load ms |" + nl + "|---|---|---|---|---|" + nl);
            Application.ThreadException += (_, e) => problems.Add("ThreadException: " + e.Exception);

            // the "where are you? (weather location)" question is asked once on the first visit of the Wallpaper page by design: answer it beforehand so it does not count as a freeze
            try { var ws = MotionDesk.Widgets.WallpaperSettings.Load(); ws.WeatherLocationPrompted = true; ws.Save(); } catch (Exception) { }
            // the DeskContainers page needs a running native host: start one with a HIDDEN sample container (no window appears) so the audit exercises the real controls
            try
            {
                var store = new Opsis.Containers.ContainersStore(Path.Combine(AppPaths.AppDataFolder, "containers-native.json"));
                var doc = new Opsis.Containers.ContainersDocument(); doc.Containers.Add(new Opsis.Containers.Container { Title = "Audit", Hidden = true });
                store.Save(doc); auditHost = new MotionDesk.Containers.ContainerHost(store);
            }
            catch (Exception ex) { problems.Add("could not start the native host for the audit: " + ex.Message); }
            string progress = Path.Combine(folder, "progress.txt"); string current = "start";
            void Mark(string s) { current = s; try { File.AppendAllText(progress, DateTime.UtcNow.ToString("HH:mm:ss.fff") + " " + s + nl); } catch (Exception) { } }
            using var stopGuard = new CancellationTokenSource();
            var dialogGuard = StartDialogGuard(dialogProblems, () => current, stopGuard.Token);
            using var win = new MotionDesk.UI.MainWindow { StartPosition = FormStartPosition.Manual, Location = new Point(40, 40) };
            MotionDesk.UI.SecondaryWindowStyler.Install();
            // OPSIS_AUDIT_DIALOG=1: a plain WinForms dialog with the standard controls must come out in the Opsis design (dark title, skin colours, flat controls)
            if (Environment.GetEnvironmentVariable("OPSIS_AUDIT_DIALOG") == "1")
            {
                try
                {
                    using var dlg = new Form { Text = "Sample dialog", Size = new Size(420, 300), StartPosition = FormStartPosition.Manual, Location = new Point(200, 200) };
                    var combo = new ComboBox { Left = 16, Top = 16, Width = 200, DropDownStyle = ComboBoxStyle.DropDownList }; combo.Items.AddRange(new object[] { "One", "Two" }); combo.SelectedIndex = 0;
                    dlg.Controls.AddRange(new Control[] { new Label { Text = "A label", Left = 16, Top = 52 }, combo, new TextBox { Left = 16, Top = 80, Width = 200, Text = "text" },
                        new CheckBox { Text = "A check box", Left = 16, Top = 112, Checked = true }, new ListBox { Left = 230, Top = 16, Width = 150, Height = 90 }, new Button { Text = "Button", Left = 16, Top = 150 } });
                    dlg.Show(); Pump(1200);
                    using var db = new Bitmap(dlg.Width, dlg.Height); dlg.DrawToBitmap(db, new Rectangle(0, 0, dlg.Width, dlg.Height));
                    db.Save(Path.Combine(folder, "sample-dialog.png"), System.Drawing.Imaging.ImageFormat.Png);
                    dlg.Close();
                }
                catch (Exception ex) { problems.Add("sample dialog: " + ex.Message); }
            }
            win.Show();
            int requestedWidth = int.TryParse(Environment.GetEnvironmentVariable("OPSIS_AUDIT_WIDTH"), out var width) ? width : 0;
            if (requestedWidth > 0)
            {
                int minimum = Math.Max(320, win.MinimumSize.Width);
                win.Width = Math.Clamp(requestedWidth, minimum, Math.Max(4096, minimum));
            }
            Pump(2500);
            int totalClicked = 0;
            var allSkipped = new List<string>();
            var pages = new List<object>();
            var layoutReview = new HashSet<string>();

            // OPSIS_AUDIT_PAGES=Wallpaper,Widgets limits the pages; OPSIS_AUDIT_NOCLICK=1 only opens them (safe to run on a personal machine with an isolated OPSIS_APPDATA)
            var only = (Environment.GetEnvironmentVariable("OPSIS_AUDIT_PAGES") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            bool noClick = Environment.GetEnvironmentVariable("OPSIS_AUDIT_NOCLICK") == "1";
            var selectedPages = win.PageKeys.Where(k => only.Length == 0 || only.Contains(k)).ToArray();
            foreach (var key in selectedPages)
            {
                Mark("page " + key);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                try { win.NavigateTo(key); Pump(1200); }
                catch (Exception ex) { problems.Add($"[{key}] navigation failed: {ex.Message}"); }
                long load = sw.ElapsedMilliseconds - 1200;
                if (load > 3000) problems.Add($"[{key}] page took {load} ms to open");

                try
                {
                    using var bmp = new Bitmap(win.Width, win.Height);
                    win.DrawToBitmap(bmp, new Rectangle(0, 0, win.Width, win.Height));
                    bmp.Save(Path.Combine(folder, "page-" + key.Replace(':', '_') + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                }
                catch (Exception ex) { problems.Add($"[{key}] screenshot failed: {ex.Message}"); }

                foreach (var section in AdvancedSections(win))
                {
                    if (section.Visible && !section.VerifyHeaderButton())
                        problems.Add($"[{key}] an Advanced button failed to toggle its content and height");
                }

                if (!noClick && key == "Settings" && win.Controls.Find("settings-window-opacity", true).FirstOrDefault() is Opsis.UI.FlatSlider opacity)
                {
                    int previous = opacity.Value;
                    int expected = previous == 72 ? 78 : 72;
                    try
                    {
                        opacity.Value = expected;
                        Pump(350);
                        if (Math.Abs(win.Opacity - expected / 100d) > .005 ||
                            !GetLayeredWindowAttributes(win.Handle, out _, out byte alpha, out uint flags) ||
                            (flags & 2) == 0 || Math.Abs(alpha - expected * 255d / 100) > 1 ||
                            Math.Abs(AppSettings.Load().WindowOpacity - expected / 100d) > .005)
                            problems.Add("[Settings] opacity slider did not update native window alpha and saved settings");
                    }
                    finally { opacity.Value = previous; Pump(300); }
                }

                if (!noClick && key == "Personalization" && Environment.GetEnvironmentVariable("OPSIS_AUDIT_TASKBAR") == "1")
                {
                    try { VerifyTaskbarControls(win, problems, Mark); }
                    catch (Exception ex) { problems.Add("[Taskbar] Audit failed: " + ex.Message); }
                }

                if (!noClick && key == "About")
                {
                    try { VerifyHelp(win, problems); }
                    catch (Exception ex) { problems.Add("[Help] Audit failed: " + ex.Message); }
                }

                var inventory = new List<object>();
                void Inventory(Control parent)
                {
                    foreach (Control child in parent.Controls)
                    {
                        if (child is ButtonBase or Opsis.UI.FlatComboBox or Opsis.UI.FlatSlider or Opsis.UI.FlatToggle or ComboBox or UpDownBase or TextBox)
                        {
                            inventory.Add(new
                            {
                                type = child.GetType().Name, child.Name,
                                label = child.AccessibleName ?? (child is TextBox ? null : child.Text),
                                child.Enabled, child.Visible, child.Width, child.Height,
                                x = child.Left, y = child.Top, dpi = child.DeviceDpi
                            });
                            if (child is Button { Visible: true, AutoEllipsis: false } button && button.Text.Length > 8)
                            {
                                int inset = Math.Max(button.Padding.Horizontal, button.LogicalToDeviceUnits(button.GetType().Name == "IconButton" ? 62 : 28));
                                int available = Math.Max(1, button.ClientSize.Width - inset);
                                var flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak;
                                int textHeight = TextRenderer.MeasureText(button.Text, button.Font, new Size(available, int.MaxValue), flags).Height;
                                if (textHeight > button.ClientSize.Height)
                                    layoutReview.Add($"[{key}] {button.Text} ({button.Width}×{button.Height}, {button.DeviceDpi} DPI)");
                            }
                        }
                        if (child is Button { Visible: true, Width: >= 8, Height: >= 8 } action
                            && action.GetType().Name is ("IconButton" or "HoverButton" or "PillButton"))
                        {
                            var region = action.Region;
                            if (region == null || region.IsVisible(0, 0) ||
                                !region.IsVisible(action.ClientSize.Width / 2, action.ClientSize.Height / 2))
                                problems.Add($"[{key}] incorrect rounded bounds for {action.Text}");
                        }
                        if (child is FlowLayoutPanel { Name: "action-grid", Visible: true } grid)
                        {
                            foreach (var row in grid.Controls.OfType<Button>().Where(button => button.Visible).GroupBy(button => button.Top))
                            {
                                if (row.Select(button => button.Height).Distinct().Count() > 1 ||
                                    row.Any(button => button.Right > grid.ClientSize.Width))
                                    layoutReview.Add($"[{key}] misaligned action row ({string.Join(", ", row.Select(button => button.Text))})");
                            }
                        }
                        Inventory(child);
                    }
                }
                Inventory(win);

                var controls = new List<Control>();
                void Collect(Control c)
                {
                    foreach (Control child in c.Controls)
                    {
                        if (child.Visible && child.Enabled && (child is Opsis.UI.FlatCheckBox || child is Opsis.UI.FlatComboBox)) controls.Add(child);
                        Collect(child);
                    }
                }
                Collect(win);
                int total = controls.Count;
                var skipped = allSkipped;
                var done = new HashSet<string>();
                string Sig(Control c, List<Control> list) => c.GetType().Name + "|" + c.Text + "|" + list.Where(x => x.GetType() == c.GetType() && x.Text == c.Text).ToList().IndexOf(c);

                int clicked = 0; long slowest = 0;
                for (int guard = 0; guard < 150; guard++)
                {
                    if (win.IsDisposed || noClick) break;
                    controls.Clear(); Collect(win);          // a click can rebuild the whole page (the old controls are then disposed): look again after every click
                    Control? c = controls.FirstOrDefault(x => !done.Contains(Sig(x, controls)));
                    if (c == null) break;
                    done.Add(Sig(c, controls));
                    // switches that change Windows itself (system transparency, fast start-up, start with Windows) or talk to other programs are listed but not flipped
                    if (c.Text.Contains("(Windows)") || c.Text.Contains("with Windows", StringComparison.OrdinalIgnoreCase) || c.Text.Contains("Discord", StringComparison.OrdinalIgnoreCase) || c.Text.Contains("OpenStreetMap", StringComparison.OrdinalIgnoreCase)) { skipped.Add($"[{key}] '{c.Text}'"); continue; }
                    var t = System.Diagnostics.Stopwatch.StartNew();
                    Mark($"  [{key}] {c.GetType().Name} '{c.Text}'");
                    try
                    {
                        if (c is Opsis.UI.FlatCheckBox cb && !cb.IsDisposed)
                        {
                            bool before = cb.Checked;
                            cb.Checked = !before; Pump(80);
                            if (cb.Checked == before) problems.Add($"[{key}] '{cb.Text}' did not change");
                            var restored = cb.IsDisposed ? FindReplacement(c) as Opsis.UI.FlatCheckBox : cb;
                            if (restored != null) restored.Checked = before;
                            else problems.Add($"[{key}] '{c.Text}' no longer available to restore");
                            Pump(80);
                            clicked++;
                        }
                        else if (c is Opsis.UI.FlatComboBox combo && combo.Items.Count > 1 && !combo.IsDisposed)
                        {
                            int i = combo.SelectedIndex;
                            combo.SelectedIndex = (i + 1) % combo.Items.Count; Pump(80);
                            var restored = combo.IsDisposed ? FindReplacement(c) as Opsis.UI.FlatComboBox : combo;
                            if (restored != null) restored.SelectedIndex = i;
                            else problems.Add($"[{key}] '{c.Text}' no longer available to restore");
                            Pump(80);
                            clicked++;
                        }
                    }
                    catch (Exception ex) { problems.Add($"[{key}] control '{c.Name}' '{c.Text}' threw {ex.GetType().Name}: {ex.Message} @ {string.Join(" <- ", (ex.StackTrace ?? "").Split('\n').Take(3).Select(l => l.Trim()))} [{c.GetType().Name} #{clicked}]"); }
                    slowest = Math.Max(slowest, t.ElapsedMilliseconds);
                    if (t.ElapsedMilliseconds > 3000) problems.Add($"[{key}] a control took {t.ElapsedMilliseconds} ms (UI freeze)");

                    Control? FindReplacement(Control previous)
                    {
                        var candidates = new List<Control>();
                        void Visit(Control parent)
                        {
                            foreach (Control child in parent.Controls)
                            {
                                if (child.Visible && child.GetType() == previous.GetType() &&
                                    (previous.Name.Length > 0 ? child.Name == previous.Name : child.Text == previous.Text))
                                    candidates.Add(child);
                                Visit(child);
                            }
                        }
                        Visit(win);
                        return candidates.FirstOrDefault();
                    }
                }
                totalClicked += clicked;
                report.Append($"| {key} | {total} | {clicked} | {slowest} | {load} |" + nl);
                pages.Add(new { page = key, controls = total, clicked, slowestMs = slowest, loadMs = load, inventory });
            }

            string? resources = null;
            try { if (!win.IsDisposed) resources = UiResourceAudit.Run(win, selectedPages, folder, Pump); }
            catch (Exception ex) { problems.Add("resource sampling failed: " + ex.Message); }
            if (resources != null) report.Append(nl + $"Resource samples: {resources}" + nl);
            stopGuard.Cancel();
            dialogGuard.Join();
            problems.AddRange(dialogProblems);
            report.Append(nl + $"**Reversible controls exercised: {totalClicked}**" + nl + nl);
            report.Append($"Language {LocalizationManager.CurrentLanguage}, theme {AppSettings.Peek().ThemeMode}, window {win.Width}×{win.Height}, DPI {win.DeviceDpi}." + nl + nl);
            if (layoutReview.Count > 0)
                report.Append("## Button layout to review" + nl + string.Join(nl, layoutReview.Order().Select(text => "- " + text)) + nl + nl);
            if (allSkipped.Count > 0) report.Append("Listed but not flipped on purpose (they change Windows itself or other programs):" + nl + string.Join(nl, allSkipped.Select(s => "- " + s)) + nl + nl);
            report.Append(problems.Count == 0 ? "No problems found." + nl : "## Problems" + nl + nl + string.Join(nl, problems.Select(p => "- " + p)) + nl);
            File.WriteAllText(Path.Combine(folder, "ui-audit.md"), report.ToString());
            File.WriteAllText(Path.Combine(folder, "ui-audit.json"), System.Text.Json.JsonSerializer.Serialize(new { clicked = totalClicked, problems, captionReview = layoutReview.Order(), pages }));
            try { win.Close(); } catch (Exception) { }
            auditHost?.Dispose();
            auditHost = null;
            return problems.Count == 0 ? 0 : 1;
        }
    }
}
