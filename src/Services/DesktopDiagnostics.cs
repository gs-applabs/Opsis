using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using MotionDesk.Widgets;

namespace MotionDesk.Services
{
    // `Opsis.exe --diag` : headless Ξ΄ΞΉΞ±Ξ³Ξ½Ο‰ΟƒΟ„ΞΉΞΊΟ Ξ³ΞΉΞ± "Ο„ΞΏ wallpaper / ΞΏ ΞΊΞ±ΞΉΟΟΟ‚ Ξ΄ΞµΞ½ Ο†Ξ±Ξ―Ξ½ΞµΟ„Ξ±ΞΉ". Ξ“ΟΞ¬Ο†ΞµΞΉ Ξ±Ξ½Ξ±Ο†ΞΏΟΞ¬ ΟƒΟ„ΞΏ
    // %AppData%\Opsis\diagnostics.txt ΞΊΞ±ΞΉ Ο„Ξ·Ξ½ Ο„Ο…Ο€ΟΞ½ΞµΞΉ ΟƒΟ„Ξ·Ξ½ ΞΊΞΏΞ½ΟƒΟΞ»Ξ±. Ξ”ΞµΞ½ Ξ±Ξ³Ξ³Ξ―Ξ¶ΞµΞΉ ΟΟ…ΞΈΞΌΞ―ΟƒΞµΞΉΟ‚.
    //   - Ξ­ΞΊΞ΄ΞΏΟƒΞ· Windows, WebView2 Runtime, ΞΏΞΈΟΞ½ΞµΟ‚, hardware tier
    //   - Ξ΄ΞΏΞΌΞ® Progman/WorkerW/SHELLDLL_DefView (Ο„ΞΏ ΟƒΞ·ΞΌΞµΞ―ΞΏ Ο€ΞΏΟ… Ξ¬Ξ»Ξ»Ξ±ΞΎΞµ ΟƒΟ„Ξ± Windows 11 24H2/25H2+)
    //   - Ξ Ξ΅Ξ‘Ξ“ΞΞ‘Ξ¤Ξ™ΞΞ Ο„ΞµΟƒΟ„ attach: Ξ­Ξ½Ξ± magenta Ο€Ξ±ΟΞ¬ΞΈΟ…ΟΞΏ ΞΌΟ€Ξ±Ξ―Ξ½ΞµΞΉ Ο€Ξ―ΟƒΟ‰ Ξ±Ο€Ο Ο„Ξ± ΞµΞΉΞΊΞΏΞ½Ξ―Ξ΄ΞΉΞ± ΞΊΞ±ΞΉ Ξ΄ΞΉΞ±Ξ²Ξ¬Ξ¶ΞµΟ„Ξ±ΞΉ pixel Ο„Ξ·Ο‚ ΞΏΞΈΟΞ½Ξ·Ο‚
    //   - Ο„ΞµΟƒΟ„ Ο€Ξ±ΟΟΟ‡Ο‰Ξ½ ΞΊΞ±ΞΉΟΞΏΟ (Open-Meteo, wttr.in)
    public static class DesktopDiagnostics
    {
        /// <summary>
        /// GPU glass on the DeskStrip over a SYNTHETIC live background (a moving GDI+ picture in my own window, so nothing from the desktop of the user is captured or shown):
        /// enables UseGpuGlass in the (isolated) settings, shows the strip at the left edge over the stand-in, checks that the glass pane exists and saves a screenshot.
        /// </summary>
        public static int TestDeskStripGlass(string folder)
        {
            Directory.CreateDirectory(folder);
            var report = new System.Collections.Generic.List<string>();
            void Pump(int ms) { var end = DateTime.UtcNow.AddMilliseconds(ms); while (DateTime.UtcNow < end) { Application.DoEvents(); System.Threading.Thread.Sleep(10); } }
            var settings = AppSettings.Load(); settings.UseGpuGlass = true; settings.Save();
            var t0 = Stopwatch.StartNew();
            using var stage = new StageForm { FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.Manual, Bounds = new Rectangle(0, 0, 1100, 800), TopMost = true, ShowInTaskbar = false, BackColor = Color.FromArgb(5, 7, 13) };
            stage.Paint += (_, e) =>
            {
                var g = e.Graphics; g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias; var t = t0.Elapsed.TotalSeconds;
                for (int layer = 0; layer < 3; layer++)
                {
                    var pts = new System.Collections.Generic.List<PointF>();
                    for (int x = 0; x <= stage.Width; x += 8) pts.Add(new PointF(x, (float)(stage.Height * (0.45 + layer * 0.13) - 40 * Math.Sin(x * 0.004 + t * (0.8 + layer * 0.3) + layer))));
                    var poly = new System.Collections.Generic.List<PointF>(pts) { new PointF(stage.Width, stage.Height), new PointF(0, stage.Height) };
                    using var fill = new SolidBrush(Color.FromArgb(70, 30 + layer * 25, 80 + layer * 30, 160 + layer * 25)); g.FillPolygon(fill, poly.ToArray());
                    using var pen = new Pen(Color.FromArgb(230, 0, 230, 255), 2.2f); g.DrawLines(pen, pts.ToArray());
                }
            };
            var anim = new System.Windows.Forms.Timer { Interval = 33 }; anim.Tick += (_, _) => stage.Invalidate(); anim.Start();
            stage.Show(); Pump(800);
            try
            {
                var engine = DeskStripHostEngine.Instance;
                string sys = Environment.GetFolderPath(Environment.SpecialFolder.System);
                engine.UpdateSettings(s => { s.Paths = new System.Collections.Generic.List<string> { Path.Combine(sys, "notepad.exe"), Path.Combine(sys, "mspaint.exe"), Path.Combine(sys, "calc.exe") }; s.Position = DockPosition.Left; s.IconSize = 48; s.ShowRunning = false; s.Magnify = true; });
                engine.Enable();
                Pump(4000);
                var m = GlassService.Manager;
                report.Add("GlassService.Manager ready: " + (m != null));
                var win = Application.OpenForms.OfType<DeskStripWindow>().FirstOrDefault();
                report.Add("DeskStripWindow present: " + (win != null) + (win != null ? $" bounds={win.Bounds}" : ""));
                if (m != null && win != null)
                {
                    var st = m.Stats;
                    report.Add($"glass stats after 4 s live: panes={st.Panes} captures={st.Captures} renders={st.Renders} errors='{st.LastError}'");
                    GlassService.Hold = true;
                    m.SetConditions(new Opsis.Glass.GlassConditions { Frozen = true });
                    Pump(300);
                    m.SetVisibleToCaptureTools(true); Pump(500);
                    var r = Rectangle.Intersect(new Rectangle(0, 0, 1100, 800), Rectangle.Inflate(win.Bounds, 180, 180));
                    using var bmp = new Bitmap(r.Width, r.Height);
                    using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(r.Location, Point.Empty, r.Size);
                    bmp.Save(Path.Combine(folder, "deskstrip-gpu-glass.png"), System.Drawing.Imaging.ImageFormat.Png);
                    m.SetVisibleToCaptureTools(false); GlassService.Hold = false;
                    report.Add("screenshot deskstrip-gpu-glass.png saved (stand-in background only)");
                }
                engine.Disable(); Pump(500);
                report.Add("after Disable: panes=" + (GlassService.Manager?.Stats.Panes.ToString() ?? "n/a"));
            }
            catch (Exception ex) { report.Add("EXCEPTION: " + ex); }
            finally { anim.Stop(); GlassService.Shutdown(); }
            File.WriteAllLines(Path.Combine(folder, "deskstrip-glass.txt"), report);
            return report.Any(l => l.StartsWith("EXCEPTION")) ? 1 : 0;
        }

        /// <summary>
        /// GPU glass behind a see-through WIDGET (Transparent mode, no tint = colour key) over a synthetic moving background in my own window (nothing from the desktop of the user is captured).
        /// Settings are changed in the (isolated) data folder only. Saves a screenshot with and without the pane and a report.
        /// </summary>
        public static int TestWidgetGlass(string folder)
        {
            Directory.CreateDirectory(folder);
            var report = new System.Collections.Generic.List<string>();
            void Pump(int ms) { var end = DateTime.UtcNow.AddMilliseconds(ms); while (DateTime.UtcNow < end) { Application.DoEvents(); System.Threading.Thread.Sleep(10); } }
            var settings = AppSettings.Load(); settings.UseGpuGlass = true; settings.WidgetsThemeMode = "Transparent"; settings.WidgetsTintPercent = 0; settings.Save();
            var t0 = Stopwatch.StartNew();
            using var stage = new StageForm { FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.Manual, Bounds = new Rectangle(0, 0, 900, 600), TopMost = true, ShowInTaskbar = false, BackColor = Color.FromArgb(5, 7, 13) };
            stage.Paint += (_, e) =>
            {
                var g = e.Graphics; g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias; var t = t0.Elapsed.TotalSeconds;
                for (int layer = 0; layer < 3; layer++)
                {
                    var pts = new System.Collections.Generic.List<PointF>();
                    for (int x = 0; x <= stage.Width; x += 8) pts.Add(new PointF(x, (float)(stage.Height * (0.45 + layer * 0.13) - 40 * Math.Sin(x * 0.004 + t * (0.8 + layer * 0.3) + layer))));
                    var poly = new System.Collections.Generic.List<PointF>(pts) { new PointF(stage.Width, stage.Height), new PointF(0, stage.Height) };
                    using var fill = new SolidBrush(Color.FromArgb(70, 30 + layer * 25, 80 + layer * 30, 160 + layer * 25)); g.FillPolygon(fill, poly.ToArray());
                    using var pen = new Pen(Color.FromArgb(230, 0, 230, 255), 2.2f); g.DrawLines(pen, pts.ToArray());
                }
            };
            var anim = new System.Windows.Forms.Timer { Interval = 33 }; anim.Tick += (_, _) => stage.Invalidate(); anim.Start();
            stage.Show(); Pump(800);
            try
            {
                var w = WidgetHostEngine.Instance.SpawnWidget("Clock", 260, 250, 300, 200);
                w.TopMost = true; w.BringToFront();
                Pump(4000);
                var m = GlassService.Manager;
                report.Add("GlassService.Manager ready: " + (m != null));
                report.Add($"widget bounds={w.Bounds} visible={w.Visible} transparencyKey={w.TransparencyKey}");
                if (m != null)
                {
                    var st = m.Stats;
                    report.Add($"glass stats after 4 s live: panes={st.Panes} captures={st.Captures} renders={st.Renders} errors='{st.LastError}'");
                    GlassService.Hold = true;
                    m.SetConditions(new Opsis.Glass.GlassConditions { Frozen = true });
                    Pump(300);
                    m.SetVisibleToCaptureTools(true); Pump(500);
                    var r = Rectangle.Intersect(new Rectangle(0, 0, 900, 600), Rectangle.Inflate(w.Bounds, 120, 120));
                    using var bmp = new Bitmap(r.Width, r.Height);
                    using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(r.Location, Point.Empty, r.Size);
                    bmp.Save(Path.Combine(folder, "widget-gpu-glass.png"), System.Drawing.Imaging.ImageFormat.Png);
                    m.SetVisibleToCaptureTools(false); GlassService.Hold = false;
                    report.Add("screenshot widget-gpu-glass.png saved (stand-in background only)");
                }
                w.Close(); Pump(500);
                report.Add("after close: panes=" + (GlassService.Manager?.Stats.Panes.ToString() ?? "n/a"));
            }
            catch (Exception ex) { report.Add("EXCEPTION: " + ex); }
            finally { anim.Stop(); GlassService.Shutdown(); }
            File.WriteAllLines(Path.Combine(folder, "widget-glass.txt"), report);
            return report.Any(l => l.StartsWith("EXCEPTION")) ? 1 : 0;
        }

        /// <summary>
        /// A real CONTAINER window (layered host + glass pane, own title-bar colour / height / font) over a synthetic moving background in my own window. Saves screenshots (glass on,
        /// scrolled, rolled up) and checks that the title bar really has its own colour. Nothing from the desktop of the user is captured.
        /// </summary>
        public static int TestContainerWindow(string folder)
        {
            Directory.CreateDirectory(folder);
            var report = new System.Collections.Generic.List<string>();
            void Pump(int ms) { var end = DateTime.UtcNow.AddMilliseconds(ms); while (DateTime.UtcNow < end) { Application.DoEvents(); System.Threading.Thread.Sleep(10); } }
            var settings = AppSettings.Load(); settings.UseGpuGlass = true; settings.Save();
            var t0 = Stopwatch.StartNew();
            using var stage = new StageForm { FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.Manual, Bounds = new Rectangle(0, 0, 1000, 700), TopMost = true, ShowInTaskbar = false, BackColor = Color.FromArgb(5, 7, 13) };
            stage.Paint += (_, e) =>
            {
                var g = e.Graphics; g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias; var t = t0.Elapsed.TotalSeconds;
                for (int layer = 0; layer < 3; layer++)
                {
                    var pts = new System.Collections.Generic.List<PointF>();
                    for (int x = 0; x <= stage.Width; x += 8) pts.Add(new PointF(x, (float)(stage.Height * (0.45 + layer * 0.13) - 40 * Math.Sin(x * 0.004 + t * (0.8 + layer * 0.3) + layer))));
                    var poly = new System.Collections.Generic.List<PointF>(pts) { new PointF(stage.Width, stage.Height), new PointF(0, stage.Height) };
                    using var fill = new SolidBrush(Color.FromArgb(70, 30 + layer * 25, 80 + layer * 30, 160 + layer * 25)); g.FillPolygon(fill, poly.ToArray());
                    using var pen = new Pen(Color.FromArgb(230, 0, 230, 255), 2.2f); g.DrawLines(pen, pts.ToArray());
                }
            };
            var anim = new System.Windows.Forms.Timer { Interval = 33 }; anim.Tick += (_, _) => stage.Invalidate(); anim.Start();
            stage.Show(); Pump(800);
            stage.TopMost = false; stage.TopMost = true; stage.BringToFront(); Pump(300);     // make sure my stand-in really covers the area (so nothing of the desktop is captured)
            MotionDesk.Containers.ContainerWindow? win = null;
            try
            {
                string sys = Environment.GetFolderPath(Environment.SpecialFolder.System), win32 = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                var model = new Opsis.Containers.Container { Title = "Tools", Locked = false };
                model.TitleBar = new Opsis.Containers.TitleBarStyle { Color = "#C2561A", Opacity = 92, Height = 44, FontSize = 13, Bold = true };
                foreach (var f in new[] { "notepad.exe", "mspaint.exe", "calc.exe", "charmap.exe", "cmd.exe", "mstsc.exe", "taskmgr.exe", "regedit.exe", "control.exe", "msinfo32.exe", "dxdiag.exe", "magnify.exe", "osk.exe", "perfmon.exe" })
                {
                    var p = System.IO.File.Exists(Path.Combine(sys, f)) ? Path.Combine(sys, f) : Path.Combine(win32, f);
                    model.Items.Add(new Opsis.Containers.ContainerItem { Path = p, DisplayName = Path.GetFileNameWithoutExtension(f) });
                }
                win = new MotionDesk.Containers.ContainerWindow(model) { Bounds = new Rectangle(300, 200, 420, 330), TopMost = true };
                win.Show(); Pump(3500);
                report.Add($"container window: bounds={win.Bounds} glassOn={win.GlassOn} items={model.Items.Count}");
                var mgr = GlassService.Manager;
                if (mgr != null) report.Add($"glass: panes={mgr.Stats.Panes} captures={mgr.Stats.Captures} renders={mgr.Stats.Renders} errors='{mgr.Stats.LastError}'");

                // the title bar has its OWN colour: render to a bitmap and compare the title bar pixel with a frame pixel
                using (var bmp = new Bitmap(win.Width, win.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb))
                {
                    using (var g = Graphics.FromImage(bmp)) win.RenderTo(g);
                    var bar = bmp.GetPixel(win.Width / 4, 8); var body = bmp.GetPixel(2, win.Height - 40);
                    report.Add($"title bar pixel={bar} (expected orange, alpha~{92 * 255 / 100}); frame pixel={body}; own colour: {(bar.R > 150 && bar.B < 90 && bar.A > 200)}");
                }

                void Shot(string name)
                {
                    GlassService.Hold = true; mgr?.SetConditions(new Opsis.Glass.GlassConditions { Frozen = true }); Pump(300);
                    mgr?.SetVisibleToCaptureTools(true); Pump(500);
                    var r = Rectangle.Intersect(new Rectangle(0, 0, 1000, 700), Rectangle.Inflate(win!.Bounds, 60, 60));
                    using var bmp = new Bitmap(r.Width, r.Height);
                    using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(r.Location, Point.Empty, r.Size);
                    // privacy guard: the corner of the shot must be the colour of my stand-in (5,7,13); anything else means something from the real desktop is in the picture -> do not save it
                    var corner = bmp.GetPixel(2, 2);
                    if (Math.Abs(corner.R - 5) + Math.Abs(corner.G - 7) + Math.Abs(corner.B - 13) > 12) report.Add("screenshot " + name + " NOT saved: the stand-in does not cover the area (corner=" + corner + ")");
                    else { bmp.Save(Path.Combine(folder, name + ".png"), System.Drawing.Imaging.ImageFormat.Png); report.Add("screenshot " + name + ".png"); }
                    mgr?.SetVisibleToCaptureTools(false); GlassService.Hold = false; GlassService.Refresh(); Pump(200);
                }
                Shot("container-glass");
                win.ScrollBy(260); Pump(400); Shot("container-scrolled");
                win.ToggleRollUp(); Pump(600); report.Add($"rolled up: height={win.Height} (title bar 44)"); Shot("container-rolled");
                win.ToggleRollUp(); Pump(400); report.Add($"unrolled: height={win.Height}");
                // phase 3: drop adds references (no duplicates), reorder moves, the files themselves are never touched
                int before = model.Items.Count; var fired = 0; win.Changed += _ => fired++;
                var extra = Path.Combine(win32, "winhlp32.exe"); var dupe = model.Items[0].Path;
                int added = win.AcceptDrop(new[] { extra, dupe, "" }, new Point(60, 120));
                report.Add($"drop: added={added} (expected 1 or 0 if file missing, duplicate skipped) items {before}->{model.Items.Count} changedEvents={fired}; fileUntouched={File.Exists(dupe)}");
                var firstBefore = model.Items[0].Path; win.MoveItem(0, new Point(300, 300));
                // phase 7/8: keyboard, accessibility tree, high contrast
                win.HandleKey(Keys.Right); win.HandleKey(Keys.Right); win.HandleKey(Keys.Down);
                report.Add($"keyboard: selected={win.Selected} after Right,Right,Down (expected >= 2)");
                var acc = win.AccessibilityObject;
                report.Add($"accessibility: name='{acc.Name}' role={acc.Role} children={acc.GetChildCount()} (items={model.Items.Count}) child0='{acc.GetChild(0)?.Name}' action='{acc.GetChild(0)?.DefaultAction}'");
                using (var hcBmp = new Bitmap(win.Width, win.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb))
                {
                    using (var g = Graphics.FromImage(hcBmp)) win.RenderTo(g, true);
                    var px = hcBmp.GetPixel(win.Width / 4, 8); var hl = SystemColors.Highlight;
                    report.Add($"high contrast: title bar pixel={px} equals system Highlight={px.R == hl.R && px.G == hl.G && px.B == hl.B && px.A == 255}");
                }
                // tabs: enabling tabs moves the icons into the first tab, a new tab is empty, removing a tab moves its icons to the neighbour; nothing is lost
                int allBefore = win.Items.Count;
                win.AddTab("Second"); bool tabsOk = model.Tabs.Count == 2 && win.Items.Count == 0 && model.Tabs[0].Items.Count == allBefore;
                win.SelectTab(0); tabsOk &= win.Items.Count == allBefore;
                win.RemoveTab(1); tabsOk &= model.Tabs.Count == 1 && win.Items.Count == allBefore;
                win.RemoveTab(0); tabsOk &= !model.TabsEnabled && model.Items.Count == allBefore;
                report.Add($"tabs: add / switch / remove keep every icon: {tabsOk} (items {allBefore})");
                // portal: a live listing of a folder (never stored in the data); note: text only
                var portalModel = new Opsis.Containers.Container { Title = "Portal", Kind = Opsis.Containers.ContainerKind.Portal, Source = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "drivers", "etc") };
                using (var pw = new MotionDesk.Containers.ContainerWindow(portalModel) { Bounds = new Rectangle(760, 200, 300, 220) })
                {
                    pw.Show(); Pump(600); pw.RefreshPortal();
                    report.Add($"portal: lists the folder live: {pw.Items.Count} entries, stored items in the data: {portalModel.Items.Count}, drop refused: {pw.AcceptDrop(new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "notepad.exe") }, new Point(50, 80)) == 0}");
                }
                var noteModel = new Opsis.Containers.Container { Title = "Note", Kind = Opsis.Containers.ContainerKind.Note, Source = "hello" };
                using (var nw = new MotionDesk.Containers.ContainerWindow(noteModel) { Bounds = new Rectangle(1080, 200, 240, 160) }) { nw.Show(); Pump(300); report.Add($"note: shows its text, no icons: {nw.Items.Count == 0}"); }
                var swIcons = Stopwatch.StartNew(); win.WaitIcons(5000);
                report.Add($"icons: background extraction finished in {swIcons.ElapsedMilliseconds} ms (UI thread kept pumping)");
                int cnt = model.Items.Count; win.RemoveItem(0);
                report.Add($"remove: reference removed {cnt}->{model.Items.Count}; file on disk still there={File.Exists(dupe)}");
                report.Add($"reorder: first item changed={(model.Items[0].Path != firstBefore)} count={model.Items.Count}");
            }
            catch (Exception ex) { report.Add("EXCEPTION: " + ex); }
            finally { win?.Close(); anim.Stop(); GlassService.Shutdown(); }
            File.WriteAllLines(Path.Combine(folder, "container-window.txt"), report);
            return report.Any(l => l.StartsWith("EXCEPTION")) ? 1 : 0;
        }

        /// <summary>Phase 5/9: the host keeps real container windows in step with the stored data driven only through the named pipe. No screenshots, nothing captured.</summary>
        public static int TestContainerHost(string folder)
        {
            Directory.CreateDirectory(folder);
            var report = new System.Collections.Generic.List<string>();
            void Pump(int ms) { var end = DateTime.UtcNow.AddMilliseconds(ms); while (DateTime.UtcNow < end) { Application.DoEvents(); System.Threading.Thread.Sleep(10); } }
            var file = Path.Combine(folder, "containers-host-test.json"); if (File.Exists(file)) File.Delete(file);
            var pipe = "opsis-containers-test-" + Environment.ProcessId;
            try
            {
                using var host = new MotionDesk.Containers.ContainerHost(new Opsis.Containers.ContainersStore(file), pipe);
                var r = Opsis.Containers.PipeClient.Send(pipe, "create", new System.Text.Json.Nodes.JsonObject { ["title"] = "Host test", ["x"] = 200, ["y"] = 200, ["width"] = 300, ["height"] = 220 });
                var id = r["data"]?["id"]?.ToString() ?? ""; Pump(600);
                report.Add($"create via pipe -> windows={host.Windows.Count} (expected 1)");
                Opsis.Containers.PipeClient.Send(pipe, "set-title-style", new System.Text.Json.Nodes.JsonObject { ["id"] = id, ["color"] = "#336699", ["height"] = 40 }); Pump(300);
                Opsis.Containers.PipeClient.Send(pipe, "hide", new System.Text.Json.Nodes.JsonObject { ["id"] = id }); Pump(600);
                report.Add($"hide via pipe -> windows={host.Windows.Count} (expected 0)");
                Opsis.Containers.PipeClient.Send(pipe, "show", new System.Text.Json.Nodes.JsonObject { ["id"] = id }); Pump(600);
                report.Add($"show via pipe -> windows={host.Windows.Count} (expected 1)");
                var w = host.Windows.First();
                // live settings: every change shows in the OPEN window (same object, no recreation)
                void Live(string what, bool ok) => report.Add((ok ? "live ok: " : "FAIL live: ") + what);
                Opsis.Containers.PipeClient.Send(pipe, "set-title-style", new System.Text.Json.Nodes.JsonObject { ["id"] = id, ["color"] = "#aa3300", ["height"] = 52, ["opacity"] = 40, ["fontSize"] = 14 }); Pump(400);
                Live("title colour/height/opacity/font via pipe", ReferenceEquals(host.Windows.First(), w) && w.Model.TitleBar.Color == "#aa3300" && w.Model.TitleBar.Height == 52 && w.Model.TitleBar.Opacity == 40 && (int)w.Model.TitleBar.FontSize == 14);
                host.Apply(id, k => { k.Appearance.IconSize = 72; k.Locked = true; return true; });
                Live("icon size + lock via host.Apply", w.Model.Appearance.IconSize == 72 && w.Model.Locked);
                host.Apply(id, k => { k.Title = "Renamed"; return true; });
                Live("title text", w.Model.Title == "Renamed");
                host.Apply(id, k => { k.TitleBar.Height = 28; k.Locked = false; return true; });
                var moved = new Rectangle(w.Left + 50, w.Top + 40, w.Width, w.Height);
                w.AcceptDrop(new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "notepad.exe") }, new Point(60, 80));
                Pump(900);   // debounced save
                var doc = new Opsis.Containers.ContainersStore(file).Load();
                report.Add($"window change saved: items={doc.Containers[0].Items.Count} (expected 1), titleColor={doc.Containers[0].TitleBar.Color} (expected #aa3300)");
                var ta = Opsis.Containers.PipeClient.Send(pipe, "toggle-all"); Pump(500);
                report.Add($"toggle-all -> resp={ta.ToJsonString()} windows={host.Windows.Count} (expected true, 0)");
                ta = Opsis.Containers.PipeClient.Send(pipe, "toggle-all"); Pump(500);
                report.Add($"toggle-all again -> resp={ta.ToJsonString()} windows={host.Windows.Count} (expected false, 1)");
                Opsis.Containers.PipeClient.Send(pipe, "delete", new System.Text.Json.Nodes.JsonObject { ["id"] = id }); Pump(600);
                report.Add($"delete via pipe -> windows={host.Windows.Count} (expected 0)");
            }
            catch (Exception ex) { report.Add("EXCEPTION: " + ex); }
            finally { GlassService.Shutdown(); }
            File.WriteAllLines(Path.Combine(folder, "container-host.txt"), report);
            return report.Any(l => l.StartsWith("EXCEPTION") || l.StartsWith("FAIL")) ? 1 : 0;
        }

        /// <summary>
        /// REAL drag and drop: the mouse is driven with real input events (cursor moves + button down/up), a stand-in "Explorer" window starts a genuine OLE drag with a file, and the
        /// containers receive it through their OLE drop target (no direct method calls). Checks drop in, reorder inside, and drag from one container to another. Only a temp file made here
        /// is dragged; stray copies of it are removed from the desktop folder afterwards.
        /// </summary>
        public static int TestContainerDragDrop(string folder)
        {
            Directory.CreateDirectory(folder);
            var report = new System.Collections.Generic.List<string>();
            void Pump(int ms) { var end = DateTime.UtcNow.AddMilliseconds(ms); while (DateTime.UtcNow < end) { Application.DoEvents(); System.Threading.Thread.Sleep(10); } }
            var file = Path.Combine(folder, "dnd-test.json"); if (File.Exists(file)) File.Delete(file);
            var f1 = Path.Combine(folder, "opsis-dnd-a-" + Environment.ProcessId + ".txt"); var f2 = Path.Combine(folder, "opsis-dnd-b-" + Environment.ProcessId + ".txt");
            File.WriteAllText(f1, "a"); File.WriteAllText(f2, "b");
            var oldPos = Cursor.Position; string[] deskCleanup = Array.Empty<string>();
            try
            {
                using var host = new MotionDesk.Containers.ContainerHost(new Opsis.Containers.ContainersStore(file), null);
                var wa = Screen.PrimaryScreen!.WorkingArea;
                host.Service.Handle("create", new System.Text.Json.Nodes.JsonObject { ["title"] = "DnD one", ["x"] = wa.Left + 120, ["y"] = wa.Top + 120, ["width"] = 420, ["height"] = 300 });
                host.Service.Handle("create", new System.Text.Json.Nodes.JsonObject { ["title"] = "DnD two", ["x"] = wa.Left + 120, ["y"] = wa.Top + 470, ["width"] = 420, ["height"] = 260 }); host.Sync(); Pump(700);
                MotionDesk.Containers.ContainerHost.KeepOnTopForTests = true; host.ReseatZOrder();
                var ws = host.Windows.OrderBy(x => x.Top).ToList(); var one = ws[0]; var two = ws[1];
                report.Add($"windows={ws.Count} (expected 2)");
                using var src = new Form { FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.Manual, TopMost = true, ShowInTaskbar = false, BackColor = Color.SteelBlue, Bounds = new Rectangle(wa.Left + 620, wa.Top + 130, 120, 120) };
                string? dragFile = null; bool started = false;
                src.MouseDown += (_, e) => report.Add("  (stand-in got mouse down)");
                src.MouseMove += (_, e) => { if (e.Button == MouseButtons.Left && !started && dragFile != null) { started = true; var d = new DataObject(); d.SetData(DataFormats.FileDrop, new[] { dragFile }); var eff = src.DoDragDrop(d, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link); report.Add("  (stand-in drag finished, effect=" + eff + ")"); } };
                src.Show(); Pump(300);

                void RealDrag(Point from, Point to)
                {
                    bool done = false;
                    var th = new System.Threading.Thread(() =>
                    {
                        SetCursorPos(from.X, from.Y); System.Threading.Thread.Sleep(250);
                        mouse_event(0x2, 0, 0, 0, UIntPtr.Zero); System.Threading.Thread.Sleep(120);
                        SetCursorPos(from.X + 12, from.Y + 12); System.Threading.Thread.Sleep(120);
                        for (int i = 1; i <= 20; i++) { SetCursorPos(from.X + 12 + (to.X - from.X - 12) * i / 20, from.Y + 12 + (to.Y - from.Y - 12) * i / 20); System.Threading.Thread.Sleep(30); }
                        System.Threading.Thread.Sleep(250); mouse_event(0x4, 0, 0, 0, UIntPtr.Zero); System.Threading.Thread.Sleep(250); done = true;
                    }) { IsBackground = true };
                    th.Start(); var end = DateTime.UtcNow.AddSeconds(15); while (!done && DateTime.UtcNow < end) { Application.DoEvents(); System.Threading.Thread.Sleep(5); }
                    Pump(500);
                }
                void DragUntil(Func<Point> from, Func<Point> to, Func<bool> ok) { for (int attempt = 0; attempt < 3 && !ok(); attempt++) RealDrag(from(), to()); }
                var srcMid = new Point(src.Left + 60, src.Top + 60);

                dragFile = f1; DragUntil(() => srcMid, () => one.PointToScreen(new Point(80, 90)), () => { started = false; return one.Items.Count >= 1; });
                report.Add((one.Items.Count == 1 ? "ok" : "FAIL") + $": real drag from a stand-in Explorer window into container 1 -> items={one.Items.Count} (expected 1)");
                dragFile = f2; DragUntil(() => srcMid, () => one.PointToScreen(new Point(300, 150)), () => { started = false; return one.Items.Count >= 2; });
                report.Add((one.Items.Count == 2 ? "ok" : "FAIL") + $": second file -> items={one.Items.Count} (expected 2)");

                // reorder inside: press on item 0 and release in the empty space after the last item
                var first = one.Items.Count > 0 ? one.Items[0].Path : "";
                DragUntil(() => one.ItemScreenCenter(0), () => one.PointToScreen(new Point(one.Width - 40, 90)), () => one.Items.Count == 2 && one.Items[1].Path == first);
                report.Add((one.Items.Count == 2 && one.Items[1].Path == first ? "ok" : "FAIL") + $": real drag inside the container reorders -> last={Path.GetFileName(one.Items.LastOrDefault()?.Path)} (expected {Path.GetFileName(first)})");

                // from container 1 to container 2: MOVE by default (the reference leaves container 1)
                DragUntil(() => one.ItemScreenCenter(0), () => two.PointToScreen(new Point(100, 100)), () => two.Items.Count == 1);
                report.Add((two.Items.Count == 1 && one.Items.Count == 1 ? "ok" : "FAIL") + $": real drag container 1 -> container 2 is a MOVE -> two={two.Items.Count} (expected 1), one={one.Items.Count} (expected 1)");
                // and back: 2 -> 1
                DragUntil(() => two.ItemScreenCenter(0), () => one.PointToScreen(new Point(300, 200)), () => two.Items.Count == 0 && one.Items.Count == 2);
                report.Add((two.Items.Count == 0 && one.Items.Count == 2 ? "ok" : "FAIL") + $": real drag container 2 -> container 1 -> two={two.Items.Count} (expected 0), one={one.Items.Count} (expected 2)");

                // single / double click launch with real mouse clicks
                MotionDesk.Containers.ContainerWindow.SuppressLaunch = true; var launched = new System.Collections.Generic.List<string>(); one.ItemLaunched += launched.Add;
                void Click(Point at, int times) { SetCursorPos(at.X, at.Y); Pump(200); for (int i = 0; i < times; i++) { mouse_event(0x2, 0, 0, 0, UIntPtr.Zero); mouse_event(0x4, 0, 0, 0, UIntPtr.Zero); Pump(60); } Pump(400); }
                Click(one.ItemScreenCenter(0), 1);
                report.Add((launched.Count == 0 ? "ok" : "FAIL") + $": one click does not launch by default (launches={launched.Count}, expected 0)");
                for (int attempt = 0; attempt < 3 && launched.Count < 1; attempt++) Click(one.ItemScreenCenter(0), 2);
                report.Add((launched.Count == 1 ? "ok" : "FAIL") + $": double click launches (launches={launched.Count}, expected 1)");
                host.Apply(one.Model.Id, k => { k.Appearance.OpenOnSingleClick = true; return true; }); launched.Clear();
                for (int attempt = 0; attempt < 3 && launched.Count < 1; attempt++) Click(one.ItemScreenCenter(1), 1);
                report.Add((launched.Count == 1 ? "ok" : "FAIL") + $": with the setting on, ONE real click launches (launches={launched.Count}, expected 1)");
                MotionDesk.Containers.ContainerWindow.SuppressLaunch = false;

                // hover roll-up with the real pointer
                host.Apply(two.Model.Id, k => { k.Appearance.HoverRollUp = true; k.Appearance.HoverDelayMs = 100; return true; });
                two.ToggleRollUp(); Pump(300);
                var rolled = two.Model.RolledUp; var rolledH = two.Height;
                SetCursorPos(wa.Right - 5, wa.Bottom - 5); Pump(300);
                for (int attempt = 0; attempt < 3 && two.Model.RolledUp; attempt++) { SetCursorPos(wa.Right - 5, wa.Bottom - 5); Pump(250); SetCursorPos(two.Left + 60, two.Top + 10); Pump(900); }
                report.Add((rolled && !two.Model.RolledUp && two.Height > rolledH ? "ok" : "FAIL") + $": real pointer over a rolled-up container opens it (was rolled={rolled}, now rolled={two.Model.RolledUp}, height {rolledH}->{two.Height})");
                SetCursorPos(wa.Right - 5, wa.Bottom - 5); Pump(1000);
                report.Add((two.Model.RolledUp ? "ok" : "FAIL") + $": pointer leaving rolls it up again (rolled={two.Model.RolledUp})");

                // REAL desktop as the source: a temp file and a temp folder are created ON the desktop, their real icons are dragged with the real mouse into a container
                two.ToggleRollUp(); host.Apply(two.Model.Id, k => { k.Appearance.HoverRollUp = false; return true; }); Pump(300);
                one.Bounds = new Rectangle(wa.Right - 460, wa.Top + 60, 420, 300); two.Bounds = new Rectangle(wa.Right - 460, wa.Top + 400, 420, 300); Pump(400);   // away from the icon grid
                var desk = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                var deskFile = Path.Combine(desk, "opsis-dnd-desk-" + Environment.ProcessId + ".txt"); var deskDir = Path.Combine(desk, "opsis-dnd-folder-" + Environment.ProcessId);
                File.WriteAllText(deskFile, "x"); Directory.CreateDirectory(deskDir); deskCleanup = new[] { deskFile, deskDir };
                Pump(2500);
                // the icons are only reachable when no window covers them: show the desktop (like Win+D) for the test and bring everything back afterwards
                dynamic? shell = null; try { shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!); shell!.MinimizeAll(); Pump(1500); } catch (Exception) { shell = null; }
                MotionDesk.Containers.ContainerWindow.DragLog.Clear();
                foreach (var path in deskCleanup)
                {
                    var name = Path.GetFileName(path); var before = two.Items.Count;
                    var at = MotionDesk.Widgets.DesktopIconVisibilityEngine.DesktopIconCenter(path);
                    if (at == null) { report.Add("SKIP: the desktop icon of " + name + " was not found (desktop view not reachable)"); continue; }
                    var under = MotionDesk.Widgets.DesktopIconVisibilityEngine.IsDesktopListAt(at.Value); report.Add("  desktop list under the icon: " + under);
                    if (one.Bounds.Contains(at.Value) || two.Bounds.Contains(at.Value) || src.Bounds.Contains(at.Value)) { report.Add("SKIP: the desktop icon of " + name + " lies under a test window"); continue; }
                    report.Add("  icon " + name + " at " + at.Value + "; container two " + two.Bounds);
                    DragUntil(() => at.Value, () => two.PointToScreen(new Point(100, 100)), () => two.Items.Any(i => i.Path.Equals(path, StringComparison.OrdinalIgnoreCase)));
                    report.Add("  selected desktop icons after the drag: " + string.Join(" | ", MotionDesk.Widgets.DesktopIconVisibilityEngine.SelectedDesktopPaths().Select(Path.GetFileName)) + "; pos now " + (MotionDesk.Widgets.DesktopIconVisibilityEngine.DesktopIconCenter(path)?.ToString() ?? "hidden/none") + "; selected one is at " + string.Join(" ", MotionDesk.Widgets.DesktopIconVisibilityEngine.SelectedDesktopPaths().Select(q => MotionDesk.Widgets.DesktopIconVisibilityEngine.DesktopIconCenter(q)?.ToString())) + "; cursor " + Cursor.Position);
                    report.Add((two.Items.Any(i => i.Path.Equals(path, StringComparison.OrdinalIgnoreCase)) ? "ok" : "FAIL") + $": REAL desktop icon ({(Directory.Exists(path) ? "folder" : "file")}) dragged into a container -> items {before}->{two.Items.Count}");
                }
                foreach (var l in MotionDesk.Containers.ContainerWindow.DragLog) report.Add("  drag log: " + l);
                try { shell?.UndoMinimizeALL(); } catch (Exception) { }
                MotionDesk.Widgets.DesktopIconVisibilityEngine.RestoreOriginals(); Pump(300);
            }
            catch (Exception ex) { report.Add("EXCEPTION: " + ex); }
            finally
            {
                SetCursorPos(oldPos.X, oldPos.Y);
                try { var desk = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory); foreach (var n in new[] { Path.GetFileName(f1), Path.GetFileName(f2), Path.GetFileNameWithoutExtension(f1) + ".lnk", Path.GetFileNameWithoutExtension(f2) + ".lnk", Path.GetFileNameWithoutExtension(f1) + " - Shortcut.lnk", Path.GetFileNameWithoutExtension(f2) + " - Shortcut.lnk" }) { var p = Path.Combine(desk, n); if (File.Exists(p)) { File.Delete(p); report.Add("cleaned a stray drop from the desktop: " + n); } } } catch (Exception) { }
                try { MotionDesk.Widgets.DesktopIconVisibilityEngine.RestoreOriginals(); } catch (Exception) { }
                foreach (var d in deskCleanup) try { if (Directory.Exists(d)) Directory.Delete(d, true); else if (File.Exists(d)) File.Delete(d); } catch (Exception) { }
                MotionDesk.Containers.ContainerHost.KeepOnTopForTests = false; GlassService.Shutdown();
            }
            File.WriteAllLines(Path.Combine(folder, "container-dnd.txt"), report);
            return report.Any(l => l.StartsWith("EXCEPTION") || l.StartsWith("FAIL")) ? 1 : 0;
        }

        /// <summary>Renders the music overlay card for a made-up track onto a dark stand-in (no desktop content) and checks the placement rule.</summary>
        public static int TestMusicOverlay(string folder)
        {
            Directory.CreateDirectory(folder);
            var report = new System.Collections.Generic.List<string>();
            var svc = new NowPlayingService();
            using var win = new MotionDesk.Widgets.MusicOverlayWindow(svc, OverlayAnchor.BottomRight);
            using var art = new Bitmap(160, 160);
            using (var g = Graphics.FromImage(art)) { using var br = new System.Drawing.Drawing2D.LinearGradientBrush(new Rectangle(0, 0, 160, 160), Color.OrangeRed, Color.MediumPurple, 45f); g.FillRectangle(br, 0, 0, 160, 160); }
            using var card = win.RenderPreview(new NowPlaying("A Made-up Song Title That Is Quite Long Indeed", "Some Artist", "Album", true, TimeSpan.FromSeconds(214), "test"), art);
            using var stage = new Bitmap(card.Width + 60, card.Height + 60);
            using (var g = Graphics.FromImage(stage)) { g.Clear(Color.FromArgb(14, 40, 80)); g.DrawImage(card, 30, 30); }
            stage.Save(Path.Combine(folder, "music-overlay.png"), System.Drawing.Imaging.ImageFormat.Png);
            var wa = new Rectangle(0, 0, 1920, 1040);
            var p = OverlayPlacement.Compute(wa, new Size(340, 88), OverlayAnchor.BottomRight, 24);
            report.Add($"bottom right on 1920x1040 -> {p} (expected 1556,928)");
            report.Add("card rendered " + card.Width + "x" + card.Height);
            File.WriteAllLines(Path.Combine(folder, "music-overlay.txt"), report);
            return p == new Point(1556, 928) ? 0 : 1;
        }

        /// <summary>
        /// Z-order of the container windows against a normal window and the desktop, checked in the real z-order chain: container ABOVE the desktop (Progman/WorkerW), BELOW a normal
        /// window of mine, and still above the desktop after the normal window comes to the front. No screenshot, nothing of the desktop is captured; the windows are not clicked.
        /// </summary>
        public static int TestContainerZOrder(string folder)
        {
            Directory.CreateDirectory(folder);
            var report = new System.Collections.Generic.List<string>();
            void Pump(int ms) { var end = DateTime.UtcNow.AddMilliseconds(ms); while (DateTime.UtcNow < end) { Application.DoEvents(); System.Threading.Thread.Sleep(10); } }
            var file = Path.Combine(folder, "zorder-test.json"); if (File.Exists(file)) File.Delete(file);
            bool ok = true;
            try
            {
                using var host = new MotionDesk.Containers.ContainerHost(new Opsis.Containers.ContainersStore(file));
                host.Service.Handle("create", new System.Text.Json.Nodes.JsonObject { ["title"] = "Z", ["x"] = 300, ["y"] = 300, ["width"] = 300, ["height"] = 200 });
                host.Sync(); Pump(500);
                var win = host.Windows.First();
                using var normal = new Form { FormBorderStyle = FormBorderStyle.FixedToolWindow, Text = "zorder-normal-window", ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Bounds = new Rectangle(320, 320, 200, 120) };
                normal.Show(); normal.Activate(); Pump(400);
                int Idx(IntPtr h) => MotionDesk.Containers.DesktopZOrder.Chain().IndexOf(h);
                var desk = MotionDesk.Containers.DesktopZOrder.DesktopWindows();
                int deskTop = desk.Select(Idx).Where(i => i >= 0).DefaultIfEmpty(-1).Min();
                int c = Idx(win.Handle), n = Idx(normal.Handle);
                report.Add($"chain index: normal window={n} container={c} highest desktop window={deskTop} (smaller = higher)");
                bool a = c > n && n >= 0; report.Add("container is BELOW the normal window: " + a);
                bool b = deskTop >= 0 && c < deskTop; report.Add("container is ABOVE the desktop: " + b);
                // push the container to the bottom, as another topmost change might, and let the host put it back
                win.SendToBack(); Pump(1500);
                c = Idx(win.Handle); deskTop = desk.Select(Idx).Where(i => i >= 0).DefaultIfEmpty(-1).Min();
                bool d = deskTop >= 0 && c >= 0 && c < deskTop; report.Add($"after SendToBack the host re-seated it above the desktop: {d} (container={c}, desktop={deskTop})");
                ok = a && b && d;
            }
            catch (Exception ex) { report.Add("EXCEPTION: " + ex); ok = false; }
            finally { GlassService.Shutdown(); }
            File.WriteAllLines(Path.Combine(folder, "container-zorder.txt"), report);
            return ok ? 0 : 1;
        }

        /// <summary>
        /// Cost of N glass containers over a moving stand-in (my own window): CPU of this process, private memory, handles, GDI/USER objects, capture/render counts, for the budgets of
        /// the design doc (section 6). Idle containers (nothing moving behind them) are measured as well. Nothing from the desktop is captured or saved.
        /// </summary>
        public static int TestContainerBudget(string folder, int count)
        {
            Directory.CreateDirectory(folder);
            var report = new System.Collections.Generic.List<string>();
            void Pump(int ms) { var end = DateTime.UtcNow.AddMilliseconds(ms); while (DateTime.UtcNow < end) { Application.DoEvents(); System.Threading.Thread.Sleep(10); } }
            var settings = AppSettings.Load(); settings.UseGpuGlass = true; settings.Save();
            var proc = Process.GetCurrentProcess();
            [System.Runtime.InteropServices.DllImport("user32.dll")] static extern uint GetGuiResources(IntPtr h, uint flag);
            (double cpu, long priv, int handles, uint gdi, uint user) Snap(TimeSpan cpuBefore, DateTime t0)
            {
                proc.Refresh();
                var cpu = (proc.TotalProcessorTime - cpuBefore).TotalMilliseconds / Math.Max(1, (DateTime.UtcNow - t0).TotalMilliseconds) / Environment.ProcessorCount * 100;
                return (cpu, proc.PrivateMemorySize64, proc.HandleCount, GetGuiResources(proc.Handle, 0), GetGuiResources(proc.Handle, 1));
            }
            var t = System.Diagnostics.Stopwatch.StartNew(); bool moving = true;
            using var stage = new StageForm { FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.Manual, Bounds = new Rectangle(0, 0, 1000, 700), TopMost = true, ShowInTaskbar = false, BackColor = Color.FromArgb(5, 7, 13) };
            stage.Paint += (_, e) =>
            {
                if (!moving) return;
                var g = e.Graphics; g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias; var tt = t.Elapsed.TotalSeconds;
                using var pen = new Pen(Color.FromArgb(230, 0, 230, 255), 2.2f); var pts = new System.Collections.Generic.List<PointF>();
                for (int x = 0; x <= stage.Width; x += 8) pts.Add(new PointF(x, (float)(350 - 40 * Math.Sin(x * 0.004 + tt))));
                g.DrawLines(pen, pts.ToArray());
            };
            var anim = new System.Windows.Forms.Timer { Interval = 33 }; anim.Tick += (_, _) => { if (moving) stage.Invalidate(); }; anim.Start();
            stage.Show(); Pump(600);
            var windows = new System.Collections.Generic.List<MotionDesk.Containers.ContainerWindow>();
            try
            {
                var baseline = Snap(proc.TotalProcessorTime, DateTime.UtcNow);
                report.Add($"before containers: priv={baseline.priv / 1048576} MB handles={baseline.handles} GDI={baseline.gdi} USER={baseline.user}");
                for (int i = 0; i < count; i++)
                {
                    var model = new Opsis.Containers.Container { Title = "C" + i };
                    for (int k = 0; k < 24; k++) model.Items.Add(new Opsis.Containers.ContainerItem { Path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), k % 2 == 0 ? "notepad.exe" : "calc.exe"), DisplayName = "item" + k });
                    var w = new MotionDesk.Containers.ContainerWindow(model) { Bounds = new Rectangle(40 + i * 300, 100, 280, 300), TopMost = true };
                    w.Show(); windows.Add(w);
                }
                Pump(2500);
                var t0 = DateTime.UtcNow; var c0 = proc.TotalProcessorTime; Pump(8000);
                var live = Snap(c0, t0); var mgr = GlassService.Manager;
                report.Add($"{count} glass containers, background MOVING 8 s: cpu={live.cpu:0.0}% of all cores, priv={live.priv / 1048576} MB, handles={live.handles}, GDI={live.gdi}, USER={live.user}" + (mgr != null ? $", panes={mgr.Stats.Panes}, captures={mgr.Stats.Captures}, renders={mgr.Stats.Renders}" : ""));
                moving = false; anim.Stop(); Pump(2500);
                var t1 = DateTime.UtcNow; var c1 = proc.TotalProcessorTime; Pump(8000);
                var idle = Snap(c1, t1);
                report.Add($"{count} glass containers, background STATIC 8 s: cpu={idle.cpu:0.0}% of all cores, priv={idle.priv / 1048576} MB, handles={idle.handles}, GDI={idle.gdi}, USER={idle.user}");
                report.Add($"growth over the run: priv +{(idle.priv - baseline.priv) / 1048576} MB, GDI +{(int)idle.gdi - (int)baseline.gdi}, USER +{(int)idle.user - (int)baseline.user}, handles +{idle.handles - baseline.handles}");
            }
            catch (Exception ex) { report.Add("EXCEPTION: " + ex); }
            finally { foreach (var w in windows) w.Close(); anim.Stop(); GlassService.Shutdown(); }
            File.WriteAllLines(Path.Combine(folder, "container-budget.txt"), report);
            return report.Any(l => l.StartsWith("EXCEPTION")) ? 1 : 0;
        }

        /// <summary>Test helper: loads a local HTML page in WebView2 (same engine as the wallpaper), waits until its title becomes "done", saves a PNG of the page. Used to look at the landscapes without the desktop.</summary>
        public static int ShotHtml(string url, string png)
        {
            int result = 1;
            using var form = new Form { FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.Manual, Location = new Point(-3000, 0), Size = new Size(1280, 720), ShowInTaskbar = false };
            var web = new Microsoft.Web.WebView2.WinForms.WebView2 { Dock = DockStyle.Fill };
            form.Controls.Add(web);
            form.Shown += async (_, _) =>
            {
                try
                {
                    var env = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(null, Path.Combine(Path.GetTempPath(), "opsis-shot-webview"));
                    await web.EnsureCoreWebView2Async(env);
                    web.Source = new Uri(url);
                    for (int i = 0; i < 300 && web.CoreWebView2.DocumentTitle != "done"; i++) await System.Threading.Tasks.Task.Delay(50);
                    await System.Threading.Tasks.Task.Delay(300);
                    using var fs = File.Create(png);
                    await web.CoreWebView2.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png, fs);
                    result = web.CoreWebView2.DocumentTitle == "done" ? 0 : 3;
                }
                catch (Exception ex) { File.WriteAllText(png + ".err.txt", ex.ToString()); result = 2; }
                finally { form.Close(); }
            };
            Application.Run(form);
            return result;
        }
        private sealed class StageForm : Form
        {
            public StageForm() { SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true); }
            protected override bool ShowWithoutActivation => true;
        }

        /// <summary>Read-only: which monitors have a fully covered desktop now, and the answer of AppActivity.IsPausedFor after it has polled twice.</summary>
        public static int TestCovered(string outFile)
        {
            var lines = new List<string>();
            var screens = System.Windows.Forms.Screen.AllScreens;
            var now = DesktopCoverage.Compute();
            lines.Add("covered right now (DesktopCoverage.Compute): " + (now.Count == 0 ? "(none)" : string.Join(", ", now)));
            AppActivity.Start();
            var end = DateTime.UtcNow.AddSeconds(3.5);
            while (DateTime.UtcNow < end) { System.Windows.Forms.Application.DoEvents(); System.Threading.Thread.Sleep(50); }
            foreach (var s in screens)
                lines.Add($"AppActivity.IsPausedFor({s.DeviceName}) = {AppActivity.IsPausedFor(s)}   (primary={s.Primary}, covered={now.Contains(s.DeviceName)})");
            lines.Add("AppActivity.Describe: " + AppActivity.Describe());
            System.IO.File.WriteAllLines(outFile, lines);
            return 0;
        }

        private static bool _showDesktop;
        [DllImport("user32.dll")] private static extern IntPtr FindWindow(string? cls, string? title);
        [DllImport("user32.dll")] private static extern IntPtr FindWindowEx(IntPtr p, IntPtr a, string? c, string? t);
        [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr h, EnumProc p, IntPtr l);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc p, IntPtr l);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr h, StringBuilder s, int n);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr h, int i);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(POINT p);
        [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr h, uint flags);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint f);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr h, int i, IntPtr v);
        [DllImport("user32.dll")] private static extern IntPtr SetParent(IntPtr c, IntPtr p);
        [DllImport("user32.dll")] private static extern IntPtr GetWindowDpiAwarenessContext(IntPtr h);
        [DllImport("user32.dll")] private static extern int GetAwarenessFromDpiAwarenessContext(IntPtr c);
        [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr c);
        private static string Dpi(IntPtr h) => $"dpiAwareness={GetAwarenessFromDpiAwarenessContext(GetWindowDpiAwarenessContext(h))}";
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int c);
        [DllImport("user32.dll")] private static extern bool RedrawWindow(IntPtr h, IntPtr r, IntPtr rg, uint f);
        [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(IntPtr h, uint key, byte a, uint f);
        [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
        private delegate bool EnumProc(IntPtr h, IntPtr l);
        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int L, T, R, B; }

        private static string Cls(IntPtr h) { var sb = new StringBuilder(128); GetClassName(h, sb, 128); return sb.ToString(); }
        private static string Describe(IntPtr h)
        {
            if (h == IntPtr.Zero) return "(none)";
            GetWindowRect(h, out var r);
            return $"0x{h.ToInt64():X} {Cls(h)} visible={IsWindowVisible(h)} exstyle=0x{GetWindowLongPtr(h, -20).ToInt64():X} rect=({r.L},{r.T},{r.R},{r.B})";
        }

        public static int Run(bool showDesktop = false)
        {
            _showDesktop = showDesktop;
            var sb = new StringBuilder();
            void L(string s) => sb.AppendLine(s);
            AppDomain.CurrentDomain.UnhandledException += (_, e) => { try { File.WriteAllText(Path.Combine(AppPaths.AppDataFolder, "diag-crash.txt"), sb + Environment.NewLine + "UNHANDLED: " + e.ExceptionObject); } catch (Exception) { } };
            Application.ThreadException += (_, e) => { try { File.WriteAllText(Path.Combine(AppPaths.AppDataFolder, "diag-crash.txt"), sb + Environment.NewLine + "THREAD: " + e.Exception); } catch (Exception) { } };
            L($"Opsis diagnostics  {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            try
            {
                using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
                L($"Windows: {k?.GetValue("ProductName")} {k?.GetValue("DisplayVersion")} build {k?.GetValue("CurrentBuildNumber")}.{k?.GetValue("UBR")}");
            }
            catch (Exception ex) { L("Windows: ? " + ex.Message); }
            L($"OS: {Environment.OSVersion}  64bit={Environment.Is64BitOperatingSystem}  cores={Environment.ProcessorCount}");
            string? wv = WebView2Support.GetRuntimeVersion();
            L($"WebView2 Runtime: {wv ?? "MISSING  <-- without it the wallpaper/widgets cannot render"}");
            L($"Hardware tier: {HardwareProfileService.CurrentTier}");
            foreach (var s in Screen.AllScreens) L($"Screen {s.DeviceName} {s.Bounds} primary={s.Primary} bpp={s.BitsPerPixel}");

            L("");
            L("--- Desktop window tree ---");
            IntPtr progman = FindWindow("Progman", null);
            if (progman == IntPtr.Zero) L("Progman: NOT FOUND (no explorer shell on this desktop)");
            else
            {
                L("Progman: " + Describe(progman));
                EnumChildWindows(progman, (h, _) => { L("   child " + Describe(h)); return true; }, IntPtr.Zero);
                EnumWindows((h, _) =>
                {
                    if (Cls(h) == "WorkerW")
                    {
                        L("WorkerW(top): " + Describe(h));
                        EnumChildWindows(h, (c, __) => { L("   child " + Describe(c)); return true; }, IntPtr.Zero);
                    }
                    return true;
                }, IntPtr.Zero);
            }

            L("");
            L("--- Attach test (real) ---");
            try { L(progman == IntPtr.Zero ? "SKIPPED (no Progman)" : AttachProbe()); } catch (Exception ex) { L("Attach probe threw: " + ex); }

            L("");
            L("--- Real wallpaper window test (WebView2 page) ---");
            try { L(progman == IntPtr.Zero ? "SKIPPED (no Progman)" : RealWallpaperProbe()); } catch (Exception ex) { L("Real wallpaper probe threw: " + ex); }

            L("");
            L("--- Plugins (loaded by the real PluginHost) ---");
            try
            {
                System.Threading.SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
                PluginHost.Instance.StartAll();
                var infos = PluginHost.Instance.GetAboutInfos();
                if (infos.Count == 0) L("(no plugins found in the plugins folders)");
                foreach (var (id, info) in infos) L($"  {id}  {info.Name} {info.Version}");
                foreach (var nav in PluginHost.Instance.GetNavigationItems()) L($"  nav page: {nav.Key} ({nav.TitleEn}) -> control {nav.CreatePage().GetType().Name}");
                foreach (var tray in PluginHost.Instance.GetTrayMenuItems()) L($"  tray item: {tray.TitleEn}");
                PluginHost.Instance.StopAll();
            }
            catch (Exception ex) { L("Plugin host probe threw: " + ex); }

            L("");
            L("--- Weather providers ---");
            try { L(WeatherProbe()); } catch (Exception ex) { L("Weather probe threw: " + ex.Message); }

            L("");
            L("--- wallpaper-attach.log (tail) ---");
            try
            {
                string log = Path.Combine(AppPaths.AppDataFolder, "wallpaper-attach.log");
                if (File.Exists(log)) foreach (var line in File.ReadLines(log).TakeLast(25)) L(line);
                else L("(no file)");
            }
            catch (Exception ex) { L(ex.Message); }

            string report = sb.ToString();
            try { File.WriteAllText(Path.Combine(AppPaths.AppDataFolder, "diagnostics.txt"), report, Encoding.UTF8); } catch (Exception) { }
            Console.WriteLine(report);
            return report.Contains("RESULT=FAIL") || wv == null ? 1 : 0;
        }

        // Magenta Ο€Ξ±ΟΞ¬ΞΈΟ…ΟΞΏ -> attach ΟΟ€Ο‰Ο‚ Ο„ΞΏ wallpaper -> Ξ΄ΞΉΞ¬Ξ²Ξ±ΟƒΞµ pixel ΞΏΞΈΟΞ½Ξ·Ο‚. Ξ‘Ξ½ ΞµΞ―Ξ½Ξ±ΞΉ magenta, Ο„ΞΏ attach Ξ΄ΞΏΟ…Ξ»ΞµΟΞµΞΉ ΞΊΞ±ΞΉ
        // Ο„ΞΏ Ο€Ξ±ΟΞ¬ΞΈΟ…ΟΞΏ ΞµΞ―Ξ½Ξ±ΞΉ ΞΞ΅Ξ‘Ξ¤Ξ Ο€Ξ―ΟƒΟ‰ Ξ±Ο€Ο Ο„Ξ± ΞµΞΉΞΊΞΏΞ½Ξ―Ξ΄ΞΉΞ±. Ξ‘Ξ»Ξ»ΞΉΟΟ‚: attach "Ο€Ξ­Ο„Ο…Ο‡Ξµ" Ξ±Ξ»Ξ»Ξ¬ Ξ΄ΞµΞ½ ΞµΞΌΟ†Ξ±Ξ½Ξ―Ξ¶ΞµΟ„Ξ±ΞΉ (Ο„ΞΏ ΟƒΟΞΌΟ€Ο„Ο‰ΞΌΞ±).
        private static string AttachProbe()
        {
            var screen = Screen.PrimaryScreen ?? Screen.AllScreens[0];
            string? ctx = Environment.GetEnvironmentVariable("OPSIS_DIAG_DPI");
            if (!string.IsNullOrEmpty(ctx)) SetThreadDpiAwarenessContext(new IntPtr(long.Parse(ctx)));
            using var form = new Form
            {
                FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.Manual, ShowInTaskbar = false,
                Bounds = screen.Bounds, BackColor = Color.FromArgb(255, 0, 255)
            };
            _ = form.Handle;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            bool ok = WallpaperInterop.AttachToDesktop(form.Handle, screen.Bounds);
            var o = new StringBuilder($"DPI: progman {Dpi(FindWindow("Progman", null))}, ours {Dpi(form.Handle)}{Environment.NewLine}AttachToDesktop returned {ok} in {sw.ElapsedMilliseconds} ms; parent={Describe(WallpaperInterop.LastAttachedWorkerW)}{Environment.NewLine}");
            if (!ok) return o + "RESULT=FAIL (attach failed)";
            form.Show();
            WallpaperInterop.KeepBehindIcons(form.Handle);
            string verdict = "RESULT=INCONCLUSIVE (the desktop is fully covered by other windows; rerun with --diag --show-desktop)";
            dynamic? shell = null;
            if (_showDesktop) { try { shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!); shell!.ToggleDesktop(); } catch (Exception) { shell = null; } }
            try
            {
                if (Environment.GetEnvironmentVariable("OPSIS_DIAG_VARIANTS") == "1")
                {
                    IntPtr fh = form.Handle, pm = FindWindow("Progman", null);
                    o.AppendLine("  z-order of Progman children (top->bottom) after attach:");
                    for (IntPtr c = FindWindowEx(pm, IntPtr.Zero, null, null); c != IntPtr.Zero; c = FindWindowEx(pm, c, null, null))
                        o.AppendLine("    " + Describe(c) + (c == fh ? "   <== OUR WINDOW" : ""));
                    var variants = new (string, Action)[]
                    {
                        ("A current (below DefView)", () => { }),
                        ("B HWND_BOTTOM", () => SetWindowPos(fh, new IntPtr(1), 0, 0, 0, 0, 0x13)),
                        ("C HWND_TOP", () => SetWindowPos(fh, IntPtr.Zero, 0, 0, 0, 0, 0x13)),
                        ("D remove WS_EX_LAYERED + BOTTOM", () => { long ex = GetWindowLongPtr(fh, -20).ToInt64(); SetWindowLongPtr(fh, -20, new IntPtr(ex & ~0x80000L)); SetWindowPos(fh, new IntPtr(1), 0, 0, 0, 0, 0x37); }),
                        ("A2 insert right below DefView + invalidate", () => { IntPtr dv = FindWindowEx(pm, IntPtr.Zero, "SHELLDLL_DefView", null); SetWindowPos(fh, dv, 0, 0, 0, 0, 0x13); RedrawWindow(fh, IntPtr.Zero, IntPtr.Zero, 0x185); }),
                        ("H child of system WorkerW + TOP", () => { IntPtr wk = FindWindowEx(pm, IntPtr.Zero, "WorkerW", null); SetParent(fh, wk); SetWindowPos(fh, IntPtr.Zero, 0, 0, 0, 0, 0x13); RedrawWindow(fh, IntPtr.Zero, IntPtr.Zero, 0x185); }),
                        ("J1 ours right below DefView, system WorkerW sent to bottom", () => { IntPtr dv = FindWindowEx(pm, IntPtr.Zero, "SHELLDLL_DefView", null); IntPtr wk = FindWindowEx(pm, IntPtr.Zero, "WorkerW", null); SetParent(fh, pm); SetWindowPos(fh, dv, 0, 0, 0, 0, 0x13); if (wk != IntPtr.Zero) SetWindowPos(wk, new IntPtr(1), 0, 0, 0, 0, 0x13); }),
                        ("J2 system WorkerW hidden", () => { IntPtr wk = FindWindowEx(pm, IntPtr.Zero, "WorkerW", null); if (wk != IntPtr.Zero) ShowWindow(wk, 0); }),
                        ("J3 WorkerW shown again", () => { IntPtr wk = FindWindowEx(pm, IntPtr.Zero, "WorkerW", null); if (wk != IntPtr.Zero) ShowWindow(wk, 5); }),
                        ("F CONTROL top-level topmost (ignores uncovered test)", () => { long st = GetWindowLongPtr(fh, -16).ToInt64(); SetParent(fh, IntPtr.Zero); SetWindowLongPtr(fh, -16, new IntPtr((st & ~0x40000000L) | unchecked((int)0x80000000))); SetWindowPos(fh, new IntPtr(-1), 0, 0, 2560, 1440, 0x40); }),
                        ("E layered alpha 254 + TOP", () => { long ex = GetWindowLongPtr(fh, -20).ToInt64(); SetWindowLongPtr(fh, -20, new IntPtr(ex | 0x80000L)); SetLayeredWindowAttributes(fh, 0, 254, 2); SetWindowPos(fh, IntPtr.Zero, 0, 0, 0, 0, 0x13); }),
                    };
                    foreach (var (name, act) in variants)
                    {
                        act();
                        var e3 = DateTime.UtcNow.AddMilliseconds(800);
                        while (DateTime.UtcNow < e3) { Application.DoEvents(); System.Threading.Thread.Sleep(20); }
                        int un = 0, mg = 0;
                        for (int gx = 1; gx < 14; gx++) for (int gy = 1; gy < 9; gy++)
                        {
                            int x = screen.Bounds.X + screen.Bounds.Width * gx / 14, y = screen.Bounds.Y + screen.Bounds.Height * gy / 9;
                            string rc = Cls(GetAncestor(WindowFromPoint(new POINT { X = x, Y = y }), 2));
                            if (rc != "Progman" && rc != "WorkerW" && !name.StartsWith("F")) continue;
                            un++;
                            using var b1 = new Bitmap(1, 1);
                            using (var g1 = Graphics.FromImage(b1)) g1.CopyFromScreen(x, y, 0, 0, new Size(1, 1));
                            var c1 = b1.GetPixel(0, 0);
                            if (c1.R > 200 && c1.G < 60 && c1.B > 200) mg++;
                        }
                        o.AppendLine($"  variant {name}: uncovered={un} magenta={mg}");
                    }
                }
                for (int i = 0; i < 10; i++)
                {
                    var end2 = DateTime.UtcNow.AddMilliseconds(500);
                    while (DateTime.UtcNow < end2) { Application.DoEvents(); System.Threading.Thread.Sleep(20); }
                    int uncovered = 0, magenta = 0;
                    for (int gx = 1; gx < 14; gx++)
                        for (int gy = 1; gy < 9; gy++)
                        {
                            int x = screen.Bounds.X + screen.Bounds.Width * gx / 14, y = screen.Bounds.Y + screen.Bounds.Height * gy / 9;
                            IntPtr root = GetAncestor(WindowFromPoint(new POINT { X = x, Y = y }), 2);
                            string rc = Cls(root);
                            if (rc != "Progman" && rc != "WorkerW") continue;   // Ο€Ξ¬Ξ½Ο‰ Ξ±Ο€Ο Ο„ΞΏ ΟƒΞ·ΞΌΞµΞ―ΞΏ Ο…Ο€Ξ¬ΟΟ‡ΞµΞΉ Ξ¬Ξ»Ξ»ΞΏ Ο€Ξ±ΟΞ¬ΞΈΟ…ΟΞΏ/taskbar
                            uncovered++;
                            using var bmp = new Bitmap(1, 1);
                            using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(x, y, 0, 0, new Size(1, 1));
                            var c = bmp.GetPixel(0, 0);
                            if (c.R > 200 && c.G < 60 && c.B > 200) magenta++;
                        }
                    o.AppendLine($"  sample {i + 1}: uncovered desktop points={uncovered}, magenta={magenta}");
                    if (magenta > 0) { verdict = "RESULT=OK (window is visible behind the icons)"; break; }
                    if (uncovered > 0 && i >= 3) { verdict = "INFO (a plain GDI test window is not composited on a raised desktop - expected; the WebView2 test below is authoritative)"; break; }
                }
            try
            {
                using var shot = new Bitmap(screen.Bounds.Width, screen.Bounds.Height);
                using (var g = Graphics.FromImage(shot)) g.CopyFromScreen(screen.Bounds.Location, Point.Empty, screen.Bounds.Size);
                string png = Path.Combine(AppPaths.AppDataFolder, "diag-attach.png");
                shot.Save(png, System.Drawing.Imaging.ImageFormat.Png);
                o.AppendLine("  screenshot: " + png);
            }
            catch (Exception) { }
            }
            finally { if (shell != null) { try { shell.ToggleDesktop(); } catch (Exception) { } } }
            form.Close();
            return o + verdict;
        }

        private static System.Collections.Generic.List<(int X, int Y, Color C)> Snapshot()
        {
            var list = new System.Collections.Generic.List<(int, int, Color)>();
            var screen = Screen.PrimaryScreen ?? Screen.AllScreens[0];
            for (int gx = 1; gx < 20; gx++) for (int gy = 1; gy < 12; gy++)
            {
                int x = screen.Bounds.X + screen.Bounds.Width * gx / 20, y = screen.Bounds.Y + screen.Bounds.Height * gy / 12;
                string rc = Cls(GetAncestor(WindowFromPoint(new POINT { X = x, Y = y }), 2));
                if (rc != "Progman" && rc != "WorkerW") continue;
                using var bmp = new Bitmap(1, 1);
                using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(x, y, 0, 0, new Size(1, 1));
                list.Add((x, y, bmp.GetPixel(0, 0)));
            }
            return list;
        }

        // Ξ•Ξ½ΞµΟΞ³ΞΏΟ€ΞΏΞΉΞµΞ― Ο„ΞΏ Ξ Ξ΅Ξ‘Ξ“ΞΞ‘Ξ¤Ξ™ΞΞ wallpaper Ο„ΞΏΟ… Opsis (WallpaperWindow + WebView2 + Waves) ΞΊΞ±ΞΉ ΟƒΟ…Ξ³ΞΊΟΞ―Ξ½ΞµΞΉ pixels Ο„Ξ·Ο‚
        // ΞµΟ€ΞΉΟ†Ξ¬Ξ½ΞµΞΉΞ±Ο‚ ΞµΟΞ³Ξ±ΟƒΞ―Ξ±Ο‚ Ο€ΟΞΉΞ½/ΞΌΞµΟ„Ξ¬. (ΞΞµ OPSIS_APPDATA Ξ±Ο€ΞΏΞΌΞΏΞ½Ο‰ΞΌΞ­Ξ½ΞΏ, Ξ΄ΞµΞ½ Ξ±Ξ³Ξ³Ξ―Ξ¶ΞµΞΉ Ο„ΞΉΟ‚ ΟΟ…ΞΈΞΌΞ―ΟƒΞµΞΉΟ‚ Ο„ΞΏΟ… Ο‡ΟΞ®ΟƒΟ„Ξ·.)
        private static string RealWallpaperProbe()
        {
            var o = new StringBuilder();
            dynamic? shell = null;
            void Pump(int ms) { var end = DateTime.UtcNow.AddMilliseconds(ms); while (DateTime.UtcNow < end) { Application.DoEvents(); System.Threading.Thread.Sleep(15); } }
            Pump(2500);   // Ξ¬Ο†Ξ·ΟƒΞµ Ο„Ξ± Windows Ξ½Ξ± ΞΏΞ»ΞΏΞΊΞ»Ξ·ΟΟΟƒΞΏΟ…Ξ½ Ο„ΞΏ Ο€ΟΞΏΞ·Ξ³ΞΏΟΞΌΞµΞ½ΞΏ restore Ο„ΞΏΟ… ToggleDesktop
            if (_showDesktop) { try { shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!); shell!.ToggleDesktop(); } catch (Exception) { shell = null; } }
            try
            {
                System.Threading.SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
                Pump(2500);
                var before = Snapshot();
                o.AppendLine($"  baseline uncovered points: {before.Count}");
                if (before.Count == 0) return o + "RESULT=INCONCLUSIVE (desktop covered; rerun with --diag --show-desktop)";
                var engine = WallpaperHostEngine.Instance;
                string? mode = Environment.GetEnvironmentVariable("OPSIS_DIAG_MODE");
                string? video = Environment.GetEnvironmentVariable("OPSIS_DIAG_VIDEO");
                if (!string.IsNullOrEmpty(video)) engine.SetVideo(video);
                if (!string.IsNullOrEmpty(mode)) engine.SetMode(mode);
                engine.Enable();
                int changed = 0;
                for (int i = 0; i < int.Parse(Environment.GetEnvironmentVariable("OPSIS_DIAG_SECONDS") ?? "12"); i++)
                {
                    Pump(1000);
                    changed = 0;
                    foreach (var (x, y, c) in before)
                    {
                        using var bmp = new Bitmap(1, 1);
                        using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(x, y, 0, 0, new Size(1, 1));
                        var n = bmp.GetPixel(0, 0);
                        if (Math.Abs(n.R - c.R) + Math.Abs(n.G - c.G) + Math.Abs(n.B - c.B) > 30) changed++;
                    }
                    o.AppendLine($"  t={i + 1}s IsRunning={engine.IsRunning} changed pixels={changed}/{before.Count}");
                }
                try
                {
                    var sc = Screen.PrimaryScreen ?? Screen.AllScreens[0];
                    using var shot = new Bitmap(sc.Bounds.Width, sc.Bounds.Height);
                    using (var g = Graphics.FromImage(shot)) g.CopyFromScreen(sc.Bounds.Location, Point.Empty, sc.Bounds.Size);
                    string png = Path.Combine(AppPaths.AppDataFolder, $"diag-wallpaper-{mode ?? "default"}.png");
                    shot.Save(png, System.Drawing.Imaging.ImageFormat.Png);
                    o.AppendLine("  screenshot: " + png);
                }
                catch (Exception) { }
                engine.Disable();
                Pump(500);
                o.Append(changed > before.Count / 2 ? "RESULT=OK (the animated wallpaper is visible on the desktop)" : "RESULT=FAIL (wallpaper window running but NOT visible on the desktop)");
                return o.ToString();
            }
            finally { if (shell != null) { try { shell.ToggleDesktop(); } catch (Exception) { } } }
        }

        /// <summary>`Opsis.exe --render-sysmon <folder>`: Ξ±Ο€ΞΏΞ΄Ξ―Ξ΄ΞµΞΉ Ο„ΞΏ System Monitor widget ΟƒΞµ PNG ΞΌΞµ Ξ΄ΞµΞ΄ΞΏΞΌΞ­Ξ½Ξ± Ξ΄ΞΏΞΊΞΉΞΌΞ®Ο‚ (ΞΏΟ€Ο„ΞΉΞΊΟΟ‚ Ξ­Ξ»ΞµΞ³Ο‡ΞΏΟ‚).</summary>
        public static int RenderSysmon(string folder)
        {
            Directory.CreateDirectory(folder);
            var rnd = new Random(7);
            foreach (var (w, h) in new[] { (340, 280), (230, 430), (260, 160) })
            {
                using var c = new SystemMonitorRowsControl { Width = w, Height = h, BackColor = Color.FromArgb(20, 24, 34) };
                double cpu = 30, ram = 55, net = 200, gpu = 15;
                for (int i = 0; i < 60; i++)
                {
                    cpu = Math.Clamp(cpu + rnd.Next(-12, 13), 3, 96); ram = Math.Clamp(ram + rnd.Next(-3, 4), 30, 80);
                    net = Math.Clamp(net + rnd.Next(-150, 160), 0, 2500); gpu = Math.Clamp(gpu + rnd.Next(-10, 11), 0, 70);
                    c.SetCpu(cpu); c.SetRam(ram, $"{(100 - ram) * 160 / 100:0} / 16384 MB free"); c.SetNetwork(net, net / 5); c.SetGpu(gpu, "54Β°C");
                }
                using var bmp = new Bitmap(w, h);
                c.DrawToBitmap(bmp, new Rectangle(0, 0, w, h));
                bmp.Save(Path.Combine(folder, $"sysmon_{w}x{h}.png"), System.Drawing.Imaging.ImageFormat.Png);
            }
            using (var eq = new MotionDesk.UI.EqBandsControl { Width = 700, Height = 220, BackColor = Color.FromArgb(20, 24, 34) })
            {
                eq.Bands = new double[] { 6, 5, 3, 1, 0, 0, 1, 2, 3, 4 };
                using var bmp = new Bitmap(700, 220);
                eq.DrawToBitmap(bmp, new Rectangle(0, 0, 700, 220));
                bmp.Save(Path.Combine(folder, "eq_bands.png"), System.Drawing.Imaging.ImageFormat.Png);
            }
            return 0;
        }

        [StructLayout(LayoutKind.Sequential)] private struct ACCENT { public int State, Flags, Gradient, Anim; }
        [StructLayout(LayoutKind.Sequential)] private struct WCAD { public int Attr; public IntPtr Data; public int Size; }
        [DllImport("user32.dll")] private static extern int SetWindowCompositionAttribute(IntPtr h, ref WCAD d);

        /// <summary>`--test-transparency <folder>`: 3 Ο„ΞµΟ‡Ξ½ΞΉΞΊΞ­Ο‚ "Ξ΄ΞΉΞ¬Ο†Ξ±Ξ½ΞΏΟ…" widget Ο€Ξ¬Ξ½Ο‰ ΟƒΟ„ΞΏ Ξ Ξ΅Ξ‘Ξ“ΞΞ‘Ξ¤Ξ™ΞΞ desktop + screenshot.</summary>
        public static int TestTransparency(string folder)
        {
            Directory.CreateDirectory(folder);
            var sc = Screen.PrimaryScreen ?? Screen.AllScreens[0];
            var forms = new System.Collections.Generic.List<Form>();
            Form Make(int x, string name, Color back, Color? key, int accentState)
            {
                var f = new Form { FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.Manual, ShowInTaskbar = false,
                    Bounds = new Rectangle(sc.Bounds.X + x, sc.Bounds.Y + 420, 380, 220), BackColor = back, TopMost = true };
                if (key != null) f.TransparencyKey = key.Value;
                var lbl = new Label { Text = "22.1Β°C", AutoSize = false, Dock = DockStyle.Top, Height = 90, Font = new Font("Segoe UI Semibold", 38), ForeColor = Color.White, BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleCenter };
                var sub = new Label { Text = name, AutoSize = false, Dock = DockStyle.Top, Height = 40, Font = new Font("Segoe UI", 14), ForeColor = Color.FromArgb(220, 255, 255, 255), BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleCenter };
                f.Controls.Add(sub); f.Controls.Add(lbl);
                f.Paint += (_, e) => { e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias; using var br = new SolidBrush(Color.FromArgb(0, 200, 230)); e.Graphics.FillEllipse(br, 20, 150, 340, 14); };
                f.Show();
                if (accentState != 0)
                {
                    var a = new ACCENT { State = accentState, Flags = 2, Gradient = 0 };
                    var ptr = Marshal.AllocHGlobal(Marshal.SizeOf(a)); Marshal.StructureToPtr(a, ptr, false);
                    var d = new WCAD { Attr = 19, Data = ptr, Size = Marshal.SizeOf(a) };
                    SetWindowCompositionAttribute(f.Handle, ref d); Marshal.FreeHGlobal(ptr);
                }
                forms.Add(f); return f;
            }
            dynamic? shell = null;
            try { shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!); shell!.ToggleDesktop(); } catch (Exception) { shell = null; }
            void Pump(int ms) { var end = DateTime.UtcNow.AddMilliseconds(ms); while (DateTime.UtcNow < end) { Application.DoEvents(); System.Threading.Thread.Sleep(15); } }
            Pump(2500);
            Make(40, "A: key=Black", Color.Black, Color.Black, 0);
            Make(450, "B: key=(1,2,3)", Color.FromArgb(1, 2, 3), Color.FromArgb(1, 2, 3), 0);
            Make(860, "C: accent TRANSPARENTGRADIENT", Color.Black, null, 2);
            Make(1270, "D: accent ACRYLIC a=0", Color.Black, null, 4);
            Pump(2500);
            using var shot = new Bitmap(sc.Bounds.Width, 700);
            using (var g = Graphics.FromImage(shot)) g.CopyFromScreen(sc.Bounds.X, sc.Bounds.Y + 380, 0, 0, shot.Size);
            shot.Save(Path.Combine(folder, "transparency.png"), System.Drawing.Imaging.ImageFormat.Png);
            foreach (var f in forms) f.Close();
            if (shell != null) { try { shell.ToggleDesktop(); } catch (Exception) { } }
            return 0;
        }

        /// <summary>`--test-widgets <folder> [mode]`: Ξ±Ξ½ΞΏΞ―Ξ³ΞµΞΉ Ο„Ξ± Ο€ΟΞ±Ξ³ΞΌΞ±Ο„ΞΉΞΊΞ¬ native widgets ΞΌΞµ Ο„ΞΏ Ξ΄ΞΏΟƒΞΌΞ­Ξ½ΞΏ WidgetsThemeMode (Ο€.Ο‡. Transparent) Ο€Ξ¬Ξ½Ο‰ ΟƒΟ„ΞΏ desktop + screenshot.</summary>
        public static int TestWidgets(string folder, string mode)
        {
            Directory.CreateDirectory(folder);
            var st = AppSettings.Load(); st.WidgetsThemeMode = mode; st.Save();
            var sc = Screen.PrimaryScreen ?? Screen.AllScreens[0];
            dynamic? shell = null;
            try { shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!); shell!.ToggleDesktop(); } catch (Exception) { shell = null; }
            void Pump(int ms) { var end = DateTime.UtcNow.AddMilliseconds(ms); while (DateTime.UtcNow < end) { Application.DoEvents(); System.Threading.Thread.Sleep(15); } }
            Pump(2500);
            var ids = new[] { "clock", "sysmon", "weather", "network", "disk" };
            var wins = new System.Collections.Generic.List<Form>();
            int x = 700;
            foreach (var id in ids)
            {
                var w = new WidgetWindow(id, sc.Bounds.X + x, sc.Bounds.Y + 180, 300, 260);
                w.Show(); wins.Add(w); x += 320;
            }
            Pump(5000);
            using var shot = new Bitmap(sc.Bounds.Width, 520);
            using (var g = Graphics.FromImage(shot)) g.CopyFromScreen(sc.Bounds.X, sc.Bounds.Y + 150, 0, 0, shot.Size);
            shot.Save(Path.Combine(folder, $"widgets_{mode}.png"), System.Drawing.Imaging.ImageFormat.Png);
            foreach (var f in wins) f.Close();
            if (shell != null) { try { shell.ToggleDesktop(); } catch (Exception) { } }
            return 0;
        }

        /// <summary>
        /// `--test-plugin-widget &lt;folder&gt;` (CI: workflow plugin-e2e; needs OPSIS_E2E=1 and an isolated OPSIS_APPDATA): loads the plugins of %AppData%Opsisplugins exactly like the app does
        /// (the trust prompt is answered "approve" only in this test mode), opens the calendar widget, saves a screenshot and a result file. Exit 0 = a plugin widget replaced the calendar.
        /// </summary>
        public static int TestPluginWidget(string folder)
        {
            Directory.CreateDirectory(folder);
            if (Environment.GetEnvironmentVariable("OPSIS_E2E") != "1" || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OPSIS_APPDATA")))
            { File.WriteAllText(Path.Combine(folder, "plugin-widget.txt"), "refused: needs OPSIS_E2E=1 and OPSIS_APPDATA"); return 2; }
            void Pump(int ms) { var end = DateTime.UtcNow.AddMilliseconds(ms); while (DateTime.UtcNow < end) { Application.DoEvents(); System.Threading.Thread.Sleep(15); } }
            PluginHost.Instance.StartAll();
            PluginHost.Instance.ResolvePending(_ => true);          // the test mode answers the approval question "yes"
            Pump(1500);
            var replacement = PluginHost.Instance.FindWidgetReplacing("calendar");
            var sc = Screen.PrimaryScreen ?? Screen.AllScreens[0];
            var w = new WidgetWindow("calendar", sc.Bounds.X + 200, sc.Bounds.Y + 200, 420, 340);
            w.Show();
            Pump(3500);
            using (var shot = new Bitmap(w.Width, w.Height))          // only the widget, never the whole screen
            {
                using (var g = Graphics.FromImage(shot)) g.CopyFromScreen(w.Location, Point.Empty, shot.Size);
                shot.Save(Path.Combine(folder, "calendar-widget.png"), System.Drawing.Imaging.ImageFormat.Png);
            }
            w.Close();
            File.WriteAllText(Path.Combine(folder, "plugin-widget.txt"),
                $"plugins={PluginHost.Instance.Plugins.Count} replaced={(replacement != null)} widget={replacement?.Key}");
            PluginHost.Instance.StopAll();
            return replacement != null ? 0 : 1;
        }

        [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);

        /// <summary>`--test-deskstrip <folder> [position]`: Ξ±Ξ½ΞΏΞ―Ξ³ΞµΞΉ Ο„ΞΏ Ξ½Ξ­ΞΏ dock ΞΌΞµ Ο€ΟΞ±Ξ³ΞΌΞ±Ο„ΞΉΞΊΞ­Ο‚ ΞµΟ†Ξ±ΟΞΌΞΏΞ³Ξ­Ο‚ ΞΊΞ±ΞΉ Ξ²Ξ³Ξ¬Ξ¶ΞµΞΉ screenshots (ΞΊΞ±Ξ½ΞΏΞ½ΞΉΞΊΟ, hover+preview).</summary>
        public static int TestDeskStrip(string folder, string position)
        {
            Directory.CreateDirectory(folder);
            var started = new System.Collections.Generic.List<Process>();
            void Pump(int ms) { var end = DateTime.UtcNow.AddMilliseconds(ms); while (DateTime.UtcNow < end) { Application.DoEvents(); System.Threading.Thread.Sleep(15); } }
            GetCursorPos(out var oldPos);
            try
            {
                foreach (var exe in new[] { "charmap.exe" }) { var p = Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true }); if (p != null) started.Add(p); }
                string sys = Environment.GetFolderPath(Environment.SpecialFolder.System);
                var engine = DeskStripHostEngine.Instance;
                engine.UpdateSettings(s =>
                {
                    s.Paths = new System.Collections.Generic.List<string> { Path.Combine(sys, "notepad.exe"), Path.Combine(sys, "mspaint.exe"), Path.Combine(sys, "cmd.exe"), Path.Combine(sys, "taskmgr.exe") };
                    s.Position = Enum.Parse<DockPosition>(position); s.IconSize = 48; s.ShowRunning = true; s.Magnify = true;
                });
                engine.Enable();
                Pump(3500);
                var sc = Screen.PrimaryScreen ?? Screen.AllScreens[0];
                var win = Application.OpenForms.OfType<DeskStripWindow>().First();
                void Shot(string name)
                {
                    var r = win.Bounds; r.Inflate(260, 260); r.Intersect(sc.Bounds);
                    using var bmp = new Bitmap(r.Width, r.Height);
                    using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(r.Location, Point.Empty, r.Size);
                    bmp.Save(Path.Combine(folder, name + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                }
                Shot("dock_" + position + "_idle");
                // hover Ο€Ξ¬Ξ½Ο‰ Ξ±Ο€Ο Ο„ΞΏ 2ΞΏ ΞµΞΉΞΊΞΏΞ½Ξ―Ξ΄ΞΉΞΏ (ΞΊΞ±ΟΟ†ΞΉΟ„ΟƒΟ‰ΞΌΞ­Ξ½ΞΏ, ΞΌΞ· Ο„ΟΞ­Ο‡ΞΏΞ½) ΞΊΞ±ΞΉ ΞΌΞµΟ„Ξ¬ Ο€Ξ¬Ξ½Ο‰ Ξ±Ο€Ο Ο„ΞΏ 1ΞΏ (Ο„ΟΞ­Ο‡ΞΏΞ½: preview)
                var b = win.Bounds;
                bool vert = position is "Left" or "Right";
                Point At(double frac) => vert ? new Point(b.Left + b.Width / 2, b.Top + (int)(b.Height * frac)) : new Point(b.Left + (int)(b.Width * frac), b.Top + b.Height / 2 + 4);
                var cp1 = At(0.30); MoveMouse(win, vert, 0.30); Pump(1500);
                Shot("dock_" + position + "_hover");
                MoveMouse(win, vert, 0.55); Pump(1800);
                Shot("dock_" + position + "_preview");
                Console.WriteLine("items shown; pinned=" + engine.GetPinnedApps().Count);
                engine.Disable();
            }
            finally
            {
                SetCursorPos(oldPos.X, oldPos.Y);
                foreach (var p in started) { try { if (!p.HasExited) p.Kill(); } catch (Exception) { } }
                foreach (var q in Process.GetProcessesByName("charmap")) { try { q.Kill(); } catch (Exception) { } }
            }
            return 0;
        }

        // ΞΞ±Ξ»ΞµΞ― Ξ‘Ξ Ξ•Ξ¥ΞΞ•Ξ™Ξ‘Ξ£ Ο„ΞΏ OnMouseMove Ο„ΞΏΟ… dock (Ο‡Ο‰ΟΞ―Ο‚ Ξ½Ξ± Ο€ΞµΞΉΟΞ¬ΞΎΞµΞΉ Ο„ΞΏΞ½ Ο€ΟΞ±Ξ³ΞΌΞ±Ο„ΞΉΞΊΟ Ξ΄ΞµΞ―ΞΊΟ„Ξ· Ο„ΞΏΟ… Ο‡ΟΞ®ΟƒΟ„Ξ·Β· Ο„Ξ± Windows ΞΈΞ± Ξ­ΟƒΟ„ΞµΞ»Ξ½Ξ±Ξ½
        // Ξ±ΞΌΞ­ΟƒΟ‰Ο‚ WM_MOUSELEAVE Ξ±Ξ½ ΟƒΟ„Ξ­Ξ»Ξ½Ξ±ΞΌΞµ ΞΌΞ·Ξ½ΟΞΌΞ±Ο„Ξ± ΞµΞ½Ο ΞΏ Ξ΄ΞµΞ―ΞΊΟ„Ξ·Ο‚ Ξ΄ΞµΞ½ ΞµΞ―Ξ½Ξ±ΞΉ Ο€Ξ¬Ξ½Ο‰ Ξ±Ο€Ο Ο„ΞΏ Ο€Ξ±ΟΞ¬ΞΈΟ…ΟΞΏ).
        private static void MoveMouse(Form dock, bool vertical, double frac)
        {
            int x = vertical ? dock.Width / 2 : (int)(dock.Width * frac), y = vertical ? (int)(dock.Height * frac) : dock.Height / 2 + 4;
            typeof(Control).GetMethod("OnMouseMove", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(dock, new object[] { new MouseEventArgs(MouseButtons.None, 0, x, y, 0) });
        }

        /// <summary>`--test-containers <folder>`: Folder Portal (Ξ¶Ο‰Ξ½Ο„Ξ±Ξ½Ξ® ΞµΞ½Ξ·ΞΌΞ­ΟΟ‰ΟƒΞ·, Ο€Ξ»ΞΏΞ®Ξ³Ξ·ΟƒΞ·, Ξ±Ξ½Ο„ΞΉΞ³ΟΞ±Ο†Ξ®), ΞΊΞ±Ξ½ΞΏΞ½ΞΉΞΊΟ container, ΞµΟ€ΞΉΞ»ΞΏΞ³Ξ®, screenshot.</summary>
        // `Opsis.exe --test-wallpaper-icons <log>`: runs the real wallpaper engine through the sequences that used to hide the desktop icons
        // (clock/date overlay on and off, mode changes, Enable twice, Disable) and checks after every step that SHELLDLL_DefView (the icons)
        // is still ABOVE every wallpaper window of this process. Use with OPSIS_APPDATA pointing to a temp folder.
        public static int TestWallpaperIcons(string logFile)
        {
            var lines = new System.Collections.Generic.List<string>();
            bool allOk = true;
            void Pump(int ms) { var end = DateTime.UtcNow.AddMilliseconds(ms); while (DateTime.UtcNow < end) { Application.DoEvents(); System.Threading.Thread.Sleep(15); } }
            bool IconsOnTop()
            {
                IntPtr progman = FindWindow("Progman", null);
                var order = new System.Collections.Generic.List<(IntPtr H, string Cls, bool Ours, bool Vis)>();
                EnumChildWindows(progman, (h, l) =>
                {
                    if (GetParentWindow(h) != progman) return true;
                    var sb = new StringBuilder(128); GetClassName(h, sb, 128);
                    GetWindowThreadProcessId(h, out uint pid);
                    order.Add((h, sb.ToString(), pid == (uint)Environment.ProcessId, IsWindowVisible(h)));
                    return true;
                }, IntPtr.Zero);
                int defView = order.FindIndex(o => o.Cls == "SHELLDLL_DefView");
                if (defView < 0) return true;                       // other layout: wallpaper windows are children of a WorkerW below the icons
                for (int i = 0; i < defView; i++) if (order[i].Ours && order[i].Vis) return false;   // one of our visible windows is above the icons
                return true;
            }
            void Step(string name)
            {
                Pump(2500);
                bool ok = IconsOnTop(); allOk &= ok;
                lines.Add((ok ? "PASS " : "FAIL ") + name);
            }
            try
            {
                var eng = WallpaperHostEngine.Instance;
                var s = WallpaperSettings.Load(); s.Mode = "Desktop"; s.WeatherShowInfo = false; s.InfoOnAllModes = true; s.WeatherLocationPrompted = true; s.Save();
                eng.SetMode("Desktop");                                   Step("enable, Desktop mode");
                eng.SetWeatherInfo(true, true, 50, 34, 100, false);       Step("clock/date ON while the wallpaper is enabled");
                eng.SetWeatherInfo(false, true, 50, 34, 100, false);      Step("clock/date OFF while the wallpaper is enabled");
                eng.Enable();                                             Step("Enable pressed again (windows already visible)");
                eng.SetMode("Waves");                                     Step("switch to Waves");
                eng.SetMode("Desktop");                                   Step("switch back to Desktop");
                eng.Disable();                                            Step("disable");
                eng.SetWeatherInfo(true, true, 50, 34, 100, false);       Step("clock/date ON with the wallpaper disabled (overlay only)");
                eng.SetWeatherInfo(false, true, 50, 34, 100, false);      Step("clock/date OFF (overlay only)");
                for (int i = 0; i < 3; i++) { eng.SetWeatherInfo(true, true, 50, 34, 100, false); Pump(600); eng.SetWeatherInfo(false, true, 50, 34, 100, false); Pump(600); }
                Step("clock/date toggled quickly three times");
                eng.SetWeatherInfo(true, true, 50, 34, 100, false);       Step("clock/date ON again");
                eng.Enable();                                             Step("Enable while the overlay is showing");
                eng.Disable();                                            Step("disable again");
                eng.SetWeatherInfo(true, true, 50, 34, 100, false);       Pump(1500);
                eng.SetMode("Desktop");                                   Step("pick Desktop mode while the overlay is showing");
                eng.SetMode("Waves");                                     Step("pick Waves while the overlay is showing");
                eng.Disable(); eng.SetWeatherInfo(false, true, 50, 34, 100, false);   Step("everything off");
            }
            catch (Exception ex) { lines.Add("EXCEPTION " + ex); allOk = false; }
            finally { try { WallpaperHostEngine.Instance.Disable(); } catch (Exception) { } }
            File.WriteAllLines(logFile, lines);
            return allOk ? 0 : 1;
        }

        [DllImport("user32.dll", EntryPoint = "GetParent")] private static extern IntPtr GetParentWindow(IntPtr h);

        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
        [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);

        /// <summary>`--test-iconhide`: Ξ΄ΞΉΟ€Ξ»Ο ΞΊΞ»ΞΉΞΊ ΟƒΞµ ΞΊΞµΞ½Ο ΟƒΞ·ΞΌΞµΞ―ΞΏ Ο„Ξ·Ο‚ ΞµΟ€ΞΉΟ†Ξ¬Ξ½ΞµΞΉΞ±Ο‚ ΞµΟΞ³Ξ±ΟƒΞ―Ξ±Ο‚ (Ο€ΟΞ±Ξ³ΞΌΞ±Ο„ΞΉΞΊΞ¬ ΞΊΞ»ΞΉΞΊ), ΞΌΞµ ΞΊΞ±ΞΉ Ο‡Ο‰ΟΞ―Ο‚ wallpaper, ΞΊΞ±ΞΉ Ξ­Ξ»ΞµΞ³Ο‡ΞΏΟ‚ Ξ±Ξ½ ΞΊΟΟΞ²ΞΏΞ½Ο„Ξ±ΞΉ/ΞΎΞ±Ξ½Ξ±Ο†Ξ±Ξ―Ξ½ΞΏΞ½Ο„Ξ±ΞΉ Ο„Ξ± ΞµΞΉΞΊΞΏΞ½Ξ―Ξ΄ΞΉΞ±.</summary>
        public static int TestIconHide(string mode)
        {
            dynamic? shell = null;
            try { shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!); shell!.ToggleDesktop(); } catch (Exception) { shell = null; }
            void Pump(int ms) { var end = DateTime.UtcNow.AddMilliseconds(ms); while (DateTime.UtcNow < end) { Application.DoEvents(); System.Threading.Thread.Sleep(15); } }
            GetCursorPos(out var old);
            var results = new System.Collections.Generic.List<string>();
            string state = Path.Combine(AppPaths.AppDataFolder, "desktopicons_hidden_state.json");
            try
            {
                System.Threading.SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
                Pump(2500);
                var sc = Screen.PrimaryScreen ?? Screen.AllScreens[0];
                int x = sc.Bounds.X + sc.Bounds.Width * 55 / 100, y = sc.Bounds.Y + sc.Bounds.Height * 70 / 100;   // empty area (icons live at the left/right edges)
                DesktopIconVisibilityEngine.Instance.Start();
                void DoubleClick()
                {
                    SetCursorPos(x, y); Pump(150);
                    for (int i = 0; i < 2; i++) { mouse_event(0x2, 0, 0, 0, UIntPtr.Zero); mouse_event(0x4, 0, 0, 0, UIntPtr.Zero); System.Threading.Thread.Sleep(60); }
                    Pump(900);
                }
                string VisibleIcons()
                {
                    var bf = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
                    var t = typeof(DesktopIconVisibilityEngine);
                    var list = (IntPtr)t.GetMethod("FindDesktopListView", bf)!.Invoke(null, null)!;
                    GetWindowThreadProcessId(list, out uint pid);
                    IntPtr hp = OpenProcess(0x0008 | 0x0010 | 0x0020, false, pid);
                    var kept = new System.Collections.Generic.List<string>(); int total = 0;
                    try
                    {
                        int count = (int)SendMessage(list, 0x1004 /*LVM_GETITEMCOUNT*/, IntPtr.Zero, IntPtr.Zero);
                        for (int i = 0; i < count; i++)
                        {
                            total++;
                            var pos = (Point)t.GetMethod("GetItemPositionRemote", bf)!.Invoke(null, new object[] { hp, list, i })!;
                            string name = (string)t.GetMethod("GetItemTextRemote", bf)!.Invoke(null, new object[] { hp, list, i })!;
                            if (pos.X > -5000 && pos.Y > -5000) kept.Add(name);
                        }
                    }
                    finally { CloseHandle(hp); }
                    try
                    {
                        var wl = (System.Collections.Generic.HashSet<string>)t.GetMethod("BuildWhitelist", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)!.Invoke(null, new object?[] { null })!;
                        File.WriteAllText(Path.Combine(Path.GetTempPath(), "opsis_iconhide_names.txt"), "WHITELIST:" + Environment.NewLine + string.Join(Environment.NewLine, wl) + Environment.NewLine + "VISIBLE:" + Environment.NewLine + string.Join(Environment.NewLine, kept), System.Text.Encoding.UTF8);
                    }
                    catch (Exception) { }
                    return $"{kept.Count}/{total} visible: [{string.Join(" | ", kept)}]";
                }
                void Round(string label)
                {
                    DoubleClick(); bool hidden = File.Exists(state);
                    results.Add($"{label}: 1st double-click -> icons hidden={hidden}; " + VisibleIcons());
                    DoubleClick(); bool restored = !File.Exists(state);
                    results.Add($"{label}: 2nd double-click -> icons restored={restored}");
                    if (File.Exists(state)) DesktopIconVisibilityEngine.Instance.RestoreIfHidden();   // never leave the user's icons off-screen
                }
                Round("no wallpaper");
                var ws = WallpaperSettings.Load(); ws.WeatherShowInfo = false; ws.Save();     // no clock/weather overlay: a visible window after Disable would then be a bug
                var w = WallpaperHostEngine.Instance;
                if (!string.IsNullOrEmpty(mode)) w.SetMode(mode);
                w.Enable(); Pump(6000);
                results.Add($"wallpaper enabled: IsRunning={w.IsRunning}");
                Round("wallpaper " + (mode ?? "Waves"));
                w.Disable(); Pump(1500);
                results.Add($"after ONE Disable(): IsEnabled={w.IsEnabled} visibleWallpaperWindows={VisibleWallpaperWindows()}   (must be 0)");
                // Race: Disable while the attach of a fresh window is still pending (what a quick Enable -> Disable does)
                w.Enable(); Pump(120); w.Disable(); Pump(7000);
                results.Add($"Enable then Disable 120 ms later, after 7 s: IsEnabled={w.IsEnabled} visibleWallpaperWindows={VisibleWallpaperWindows()}   (must be 0)");
                // Race: Disable while the OLD windows are still retiring during a rebuild
                w.Enable(); Pump(5000);
                typeof(WallpaperHostEngine).GetMethod("RebuildWindows", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(w, null);
                Pump(300); w.Disable(); Pump(7000);
                results.Add($"Disable during a rebuild, after 7 s: IsEnabled={w.IsEnabled} visibleWallpaperWindows={VisibleWallpaperWindows()}   (must be 0)");
            }
            finally
            {
                try { DesktopIconVisibilityEngine.Instance.Stop(); } catch (Exception) { }
                SetCursorPos(old.X, old.Y);
                if (shell != null) { try { shell.ToggleDesktop(); } catch (Exception) { } }
            }
            foreach (var r in results) Console.WriteLine(r);
            return 0;
        }

        private static int VisibleWallpaperWindows() => Application.OpenForms.OfType<WallpaperWindow>().Count(f => !f.IsDisposed && f.Visible);

        private static string WeatherProbe()
        {
            var w = System.Diagnostics.Stopwatch.StartNew();
            string json = Task.Run(() => WeatherService.GetWallpaperWeatherJsonAsync(37.9838, 23.7275)).GetAwaiter().GetResult();   // thread pool: never block the UI sync context
            var o = new StringBuilder($"Wallpaper weather ({w.ElapsedMilliseconds} ms): {(json.Contains("\"ok\":true") ? "OK" : "FAILED")} {json[..Math.Min(json.Length, 160)]}\n");
            var n = Task.Run(() => WeatherService.GetNormalizedWeatherAsync(37.9838, 23.7275)).GetAwaiter().GetResult();
            o.Append($"Widget weather: {(n == null ? "FAILED" : $"OK {n.TemperatureC}C via {n.Source}")}");
            return o.ToString();
        }
    }
}

