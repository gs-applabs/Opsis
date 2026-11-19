using System;
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
    /// every switch / check box / combo box on it (change, check, change back), recording exceptions, pages that take more than 3 s, controls that did not react or froze the UI.
    /// Report: ui-audit.md + ui-audit.json + one PNG per page. Exit 0 = no problem found.
    /// </summary>
    public static class UiAudit
    {
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] private static extern IntPtr FindWindow(string? cls, string? title);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int GetWindowThreadProcessId(IntPtr h, out int pid);
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] private static extern int GetWindowText(IntPtr h, System.Text.StringBuilder sb, int max);
        private delegate bool EnumProc(IntPtr h, IntPtr l);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr l);
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] private static extern int GetClassName(IntPtr h, System.Text.StringBuilder sb, int max);

        // A modal message box opened by some control would wait for ever on a runner nobody watches: close every standard dialog (#32770) of this process and note it in the report.
        private static void StartDialogGuard(List<string> problems, Func<string> where)
        {
            int me = Environment.ProcessId;
            var seen = new HashSet<IntPtr>();
            var t = new System.Threading.Thread(() =>
            {
                while (true)
                {
                    System.Threading.Thread.Sleep(1500);
                    try
                    {
                        EnumWindows((h, _) =>
                        {
                            GetWindowThreadProcessId(h, out int pid);
                            if (pid != me || !IsWindowVisible(h)) return true;
                            var cls = new System.Text.StringBuilder(64); GetClassName(h, cls, 64);
                            if (cls.ToString() != "#32770") return true;
                            var title = new System.Text.StringBuilder(256); GetWindowText(h, title, 256);
                            if (seen.Add(h)) lock (problems) problems.Add($"modal dialog '{title}' opened near {where()} (answered by the audit)");
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
        }

        private static long _beat = Environment.TickCount64;
        // watchdog: the UI thread pumps (Pump / DoEvents) regularly; if it does not for 10 s the app is hung (deadlock / blocking wait on the UI thread): write where, and exit 3 so a CI run fails instead of waiting for ever
        private static void StartWatchdog(string folder, Func<string> where)
        {
            var t = new System.Threading.Thread(() =>
            {
                while (true)
                {
                    System.Threading.Thread.Sleep(2000);
                    if (Environment.TickCount64 - System.Threading.Interlocked.Read(ref _beat) < 10000) continue;
                    try
                    {
                        var p = System.Diagnostics.Process.GetCurrentProcess();
                        File.WriteAllText(Path.Combine(folder, "HANG.txt"), "UI thread did not pump for 10 s near: " + where() + Environment.NewLine + "threads=" + p.Threads.Count + " cpu=" + p.TotalProcessorTime + Environment.NewLine
                            + "Take a dump with: createdump " + p.Id + "   (in the .NET runtime folder) and open it with dotnet-dump analyze -> clrstack" + Environment.NewLine);
                    }
                    catch (Exception) { }
                    Environment.Exit(3);
                }
            }) { IsBackground = true };
            t.Start();
        }

        private static void Pump(int ms)
        {
            System.Threading.Interlocked.Exchange(ref _beat, Environment.TickCount64);
            var end = DateTime.UtcNow.AddMilliseconds(ms);
            while (DateTime.UtcNow < end) { Application.DoEvents(); System.Threading.Interlocked.Exchange(ref _beat, Environment.TickCount64); System.Threading.Thread.Sleep(10); }
        }

        private static MotionDesk.Containers.ContainerHost? auditHost;

        public static int Run(string folder)
        {
            Directory.CreateDirectory(folder);
            string nl = Environment.NewLine;
            if (Environment.GetEnvironmentVariable("OPSIS_E2E") != "1" || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OPSIS_APPDATA")))
            { File.WriteAllText(Path.Combine(folder, "ui-audit.md"), "refused: needs OPSIS_E2E=1 and OPSIS_APPDATA"); return 2; }

            var problems = new List<string>();
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
            StartDialogGuard(problems, () => current);
            StartWatchdog(folder, () => current);
            var win = new MotionDesk.UI.MainWindow { StartPosition = FormStartPosition.Manual, Location = new Point(40, 40) };
            win.Show();
            Pump(2500);
            int totalClicked = 0;
            var allSkipped = new List<string>();
            var pages = new List<object>();

            // OPSIS_AUDIT_PAGES=Wallpaper,Widgets limits the pages; OPSIS_AUDIT_NOCLICK=1 only opens them (safe to run on a personal machine with an isolated OPSIS_APPDATA)
            var only = (Environment.GetEnvironmentVariable("OPSIS_AUDIT_PAGES") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            bool noClick = Environment.GetEnvironmentVariable("OPSIS_AUDIT_NOCLICK") == "1";
            try
            {
                // the Help window (F1): every tab is screenshotted
                win.NavigateTo("Wallpaper"); win.ShowHelpForAudit(); Pump(800);
                if (MotionDesk.UI.HelpForm.Current is { } help)
                {
                    for (int t = 0; t < 5; t++)
                    {
                        help.SelectTabForAudit(t); Pump(400);
                        using var hb = new Bitmap(help.Width, help.Height);
                        help.DrawToBitmap(hb, new Rectangle(0, 0, help.Width, help.Height));
                        hb.Save(Path.Combine(folder, "help-tab" + t + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                    }
                    help.Close();
                }
                else problems.Add("the Help window did not open");
            }
            catch (Exception ex) { problems.Add("Help window: " + ex.Message); }
            try
            {
                MotionDesk.Widgets.FamilyWindows.ShowFamily(); Pump(800);
                var fam = Application.OpenForms.OfType<Form>().FirstOrDefault(f => f.Tag as string == "opsis-family");
                if (fam != null)
                {
                    using var fb = new Bitmap(fam.Width, fam.Height);
                    fam.DrawToBitmap(fb, new Rectangle(0, 0, fam.Width, fam.Height));
                    fb.Save(Path.Combine(folder, "family-window.png"), System.Drawing.Imaging.ImageFormat.Png);
                    fam.Close();
                }
                else problems.Add("the family window did not open");
            }
            catch (Exception ex) { problems.Add("Family window: " + ex.Message); }
            // OPSIS_AUDIT_SEARCH=<text>: types into the global search of the header and screenshots the window with the results list open
            var gs = Environment.GetEnvironmentVariable("OPSIS_AUDIT_SEARCH");
            if (!string.IsNullOrEmpty(gs))
            {
                try
                {
                    win.NavigateTo("Wallpaper"); Pump(800);
                    win.GlobalSearchBox.RebuildIndex(win.PageKeys);
                    win.GlobalSearchBox.SetQueryForTest(gs); Pump(500);
                    using var sb2 = new Bitmap(win.Width, win.Height);
                    win.Activate(); win.GlobalSearchBox.Refresh(); win.GlobalSearchBox.ResultsList.Refresh(); Pump(400); using (var sg2 = Graphics.FromImage(sb2)) sg2.CopyFromScreen(win.Location, Point.Empty, win.Size);
                    sb2.Save(Path.Combine(folder, "global-search.png"), System.Drawing.Imaging.ImageFormat.Png);
                    problems.Add($"[search] INFO box={win.GlobalSearchBox.Bounds} vis={win.GlobalSearchBox.Visible} parent={win.GlobalSearchBox.Parent?.GetType().Name} list={win.GlobalSearchBox.ResultsList.Bounds} lvis={win.GlobalSearchBox.ResultsList.Visible} results for '{gs}': {win.GlobalSearchBox.ResultsList.Items.Count}");
                    win.GlobalSearchBox.SetQueryForTest("");
                }
                catch (Exception ex) { problems.Add("global search: " + ex.Message); }
            }
            // OPSIS_AUDIT_WIDGETS=1: opens a few native widgets (NOT through SpawnWidget: that one would register Opsis for Windows start-up), screenshots each one and closes it
            if (Environment.GetEnvironmentVariable("OPSIS_AUDIT_WIDGETS") == "1")
            {
                foreach (var wid in new[] { "sysmon", "clock", "disk", "network" })
                {
                    try
                    {
                        using var w = new MotionDesk.Widgets.WidgetWindow(wid, 80, 80, 340, 240);
                        w.Show(); Pump(1500);
                        using var wb = new Bitmap(w.Width, w.Height);
                        w.DrawToBitmap(wb, new Rectangle(0, 0, w.Width, w.Height));
                        wb.Save(Path.Combine(folder, "widget-" + wid + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                        w.Close();
                    }
                    catch (Exception ex) { problems.Add($"widget {wid}: {ex.Message}"); }
                }
            }
            foreach (var key in win.PageKeys.Where(k => only.Length == 0 || only.Contains(k)).ToList())
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

                // OPSIS_AUDIT_LOC=<city>: types the city into the weather location box, presses "Set location" and screenshots the card (the choices must appear UNDER the button)
                var locCity = Environment.GetEnvironmentVariable("OPSIS_AUDIT_LOC");
                if (!string.IsNullOrEmpty(locCity) && key == "Wallpaper")
                {
                    try
                    {
                        Control? Find(Control c, string name) { foreach (Control ch in c.Controls) { if (ch.Name == name) return ch; var r = Find(ch, name); if (r != null) return r; } return null; }
                        var box = Find(win, "weather-city") as TextBox; var btn = Find(win, "weather-set") as Button;
                        if (box != null && btn != null)
                        {
                            Mark("loc: set text"); box.Text = locCity; Mark("loc: click"); btn.PerformClick(); Mark("loc: pump"); Pump(6000); Mark("loc: pumped");
                            if (Find(win, "weather-choices") is Control ch0)
                                problems.Add($"[Wallpaper] INFO choices panel: visible={ch0.Visible} bounds={ch0.Bounds} parent={ch0.Parent?.GetType().Name} children={string.Join(";", ch0.Controls.Cast<Control>().Select(c => c.Bounds + (c.Visible ? "v" : "h")))}");
                            Opsis.UI.StableScrollPanel? sp2 = null;
                            void FindSp2(Control c) { foreach (Control ch in c.Controls) { if (sp2 == null && ch is Opsis.UI.StableScrollPanel s && s.Visible) sp2 = s; FindSp2(ch); } }
                            FindSp2(win);
                            if (sp2 != null) { var pt = box.PointToScreen(Point.Empty); var pp = sp2.PointToScreen(Point.Empty); sp2.ScrollTo(0, Math.Max(0, sp2.ScrollOffset.Y + (pt.Y - pp.Y) - 40)); Pump(500); }
                            Mark("loc: shot"); using var lb = new Bitmap(win.Width, win.Height);
                            /* DrawToBitmap (WM_PRINT) of the freshly shown choice buttons hung the UI thread in 4 of 5 runs: only the screen copy below is used here */
                            Mark("loc: drawn");
                            
                            Mark("loc: saved");
                            try { win.Activate(); Pump(300); using var sb = new Bitmap(win.Width, win.Height); using (var sg = Graphics.FromImage(sb)) sg.CopyFromScreen(win.Location, Point.Empty, win.Size); sb.Save(Path.Combine(folder, "page-Wallpaper-location-screen.png"), System.Drawing.Imaging.ImageFormat.Png); } catch (Exception) { }
                        }
                        else problems.Add("[Wallpaper] location box not found (needs a mode that shows the weather section)");
                    }
                    catch (Exception ex) { problems.Add("[Wallpaper] location test failed: " + ex.Message); }
                }

                // OPSIS_AUDIT_ADV=1: open every "Advanced" group of the page and screenshot the page from the top down (page-<key>-adv<n>.png)
                if (Environment.GetEnvironmentVariable("OPSIS_AUDIT_ADV") == "1")
                {
                    try
                    {
                        var advs = new List<Opsis.UI.AdvancedSection>();
                        void FindAdv(Control c) { foreach (Control ch in c.Controls) { if (ch is Opsis.UI.AdvancedSection a) advs.Add(a); FindAdv(ch); } }
                        FindAdv(win);
                        foreach (var a in advs) a.Expanded = true;
                        Pump(900);
                        Opsis.UI.StableScrollPanel? sp = null;
                        void FindSp(Control c) { foreach (Control ch in c.Controls) { if (sp == null && ch is Opsis.UI.StableScrollPanel s && s.Visible) sp = s; FindSp(ch); } }
                        FindSp(win);
                        for (int n = 0; n < 6; n++)
                        {
                            if (n > 0) { if (sp == null) break; sp.ScrollTo(0, sp.ScrollOffset.Y + Math.Max(200, sp.ClientSize.Height - 40)); Pump(500); }
                            using var ab = new Bitmap(win.Width, win.Height);
                            win.DrawToBitmap(ab, new Rectangle(0, 0, win.Width, win.Height));
                            ab.Save(Path.Combine(folder, "page-" + key.Replace(':', '_') + "-adv" + n + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                        }
                        if (sp != null)
                        {
                            var swp = System.Diagnostics.Stopwatch.StartNew();
                            int steps = 40;
                            for (int n = 0; n < steps; n++) { sp.ScrollTo(0, (n * 60) % Math.Max(100, sp.VerticalScroll.Maximum)); win.Update(); Application.DoEvents(); }
                            problems.Add($"[{key}] INFO scroll perf: {swp.ElapsedMilliseconds / steps} ms per 60px step ({sp.Controls.Count} cards, max {sp.VerticalScroll.Maximum})");
                        }
                        if (sp != null)
                        {
                            // the wheel over an ordinary control (a button / label inside a card) must scroll THE PAGE (user test: "the page sticks")
                            sp.ScrollTo(0, 0);
                            Control? target = null;
                            void FindBtn(Control c) { foreach (Control ch in c.Controls) { if (target == null && ch.Visible && ch is Opsis.UI.FlatButton && ch.Parent is not Opsis.UI.AdvancedSection) target = ch; FindBtn(ch); } }
                            FindBtn(sp);
                            if (target != null)
                            {
                                int before = sp.ScrollOffset.Y;
                                typeof(Control).GetMethod("OnMouseWheel", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(target, new object[] { new MouseEventArgs(MouseButtons.None, 0, 5, 5, -120) });
                                Pump(200);
                                int after = sp.ScrollOffset.Y;
                                if (after <= before) problems.Add($"[{key}] wheel over '{target.GetType().Name}' did not scroll the page ({before} -> {after})");
                                else problems.Add($"[{key}] INFO wheel over '{target.GetType().Name}' scrolled the page {before} -> {after}");
                            }
                        }
                        sp?.ScrollTo(0, 0);
                    }
                    catch (Exception ex) { problems.Add($"[{key}] advanced screenshot failed: {ex.Message}"); }
                }

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
                    // switches that change Windows itself (system transparency, fast start-up, start with Windows, the animated lock screen that asks before touching system files) or talk to other programs are listed but not flipped
                    if (c.Text.Contains("(Windows)") || c.Text.Contains("asks for confirmation", StringComparison.OrdinalIgnoreCase) || c.Text.Contains("with Windows", StringComparison.OrdinalIgnoreCase) || c.Text.Contains("Discord", StringComparison.OrdinalIgnoreCase) || c.Text.Contains("OpenStreetMap", StringComparison.OrdinalIgnoreCase)) { skipped.Add($"[{key}] '{c.Text}'"); continue; }
                    var t = System.Diagnostics.Stopwatch.StartNew();
                    Mark($"  [{key}] {c.GetType().Name} '{c.Text}'");
                    try
                    {
                        if (c is Opsis.UI.FlatCheckBox cb && !cb.IsDisposed)
                        {
                            bool before = cb.Checked;
                            cb.Checked = !before; Pump(80);
                            if (cb.Checked == before) problems.Add($"[{key}] '{cb.Text}' did not change");
                            cb.Checked = before; Pump(80);
                            clicked++;
                        }
                        else if (c is Opsis.UI.FlatComboBox combo && combo.Items.Count > 1 && !combo.IsDisposed)
                        {
                            int i = combo.SelectedIndex;
                            combo.SelectedIndex = (i + 1) % combo.Items.Count; Pump(80);
                            combo.SelectedIndex = i < 0 ? 0 : i; Pump(80);
                            clicked++;
                        }
                    }
                    catch (Exception ex) { problems.Add($"[{key}] control '{c.Name}' '{c.Text}' threw {ex.GetType().Name}: {ex.Message} @ {string.Join(" <- ", (ex.StackTrace ?? "").Split('\n').Take(3).Select(l => l.Trim()))} [{c.GetType().Name} #{clicked}]"); }
                    slowest = Math.Max(slowest, t.ElapsedMilliseconds);
                    if (t.ElapsedMilliseconds > 3000) problems.Add($"[{key}] a control took {t.ElapsedMilliseconds} ms (UI freeze)");
                }
                totalClicked += clicked;
                report.Append($"| {key} | {total} | {clicked} | {slowest} | {load} |" + nl);
                pages.Add(new { page = key, controls = total, clicked, slowestMs = slowest, loadMs = load });
            }

            report.Append(nl + $"**Total controls clicked: {totalClicked}**" + nl + nl);
            if (allSkipped.Count > 0) report.Append("Listed but not flipped on purpose (they change Windows itself or other programs):" + nl + string.Join(nl, allSkipped.Select(s => "- " + s)) + nl + nl);
            report.Append(problems.Count == 0 ? "No problems found." + nl : "## Problems" + nl + nl + string.Join(nl, problems.Select(p => "- " + p)) + nl);
            File.WriteAllText(Path.Combine(folder, "ui-audit.md"), report.ToString());
            File.WriteAllText(Path.Combine(folder, "ui-audit.json"), System.Text.Json.JsonSerializer.Serialize(new { clicked = totalClicked, problems, pages }));
            try { win.Close(); } catch (Exception) { }
            return problems.All(p => p.Contains(" INFO ")) ? 0 : 1;
        }
    }
}

namespace MotionDesk.Services
{
    /// <summary>Hang hunting: with OPSIS_TRACE=&lt;file&gt; steps of suspicious code paths are appended to that file (the last line shows where the UI thread stopped).</summary>
    internal static class HangTrace
    {
        private static readonly string? File_ = Environment.GetEnvironmentVariable("OPSIS_TRACE");
        public static void Mark(string s) { if (File_ == null) return; try { System.IO.File.AppendAllText(File_, DateTime.UtcNow.ToString("HH:mm:ss.fff") + " [" + Environment.CurrentManagedThreadId + "] " + s + "\n"); } catch (Exception) { } }
    }
}
