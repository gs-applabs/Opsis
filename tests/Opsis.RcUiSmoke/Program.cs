using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Forms;
using MotionDesk.Services;
using MotionDesk.UI;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var output = args.Length > 0 ? args[0] : "opsis-ui-evidence";
        Directory.CreateDirectory(output);
        Environment.SetEnvironmentVariable("OPSIS_APPDATA", Path.Combine(Path.GetFullPath(output), "appdata"));
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        var checks = new System.Collections.Generic.List<object>();
        void Verify(bool ok, string label)
        {
            checks.Add(new { check = label, passed = ok });
            if (!ok) throw new InvalidOperationException(label);
            Console.WriteLine("PASS " + label);
        }
        var watch = Stopwatch.StartNew();
        try
        {
            var sizes = new[] { new Size(800, 600), new Size(1280, 720), new Size(1600, 900) };
            using (var iconSheet = new Bitmap(11 * 80, 80))
            using (var g = Graphics.FromImage(iconSheet))
            {
                var styles = OpsisStartButtonArt.Styles;
                Verify(styles.Length == 11, "11 start-button designs registered");
                for (int i = 0; i < styles.Length; i++)
                    OpsisStartButtonArt.Draw(g, new Rectangle(i * 80, 0, 80, 80), styles[i]);
                iconSheet.Save(Path.Combine(output, "start-button-gallery.png"));
            }
            var forms = new Func<Form>[]
            {
                () => new StartStudioForm(),
                () => new OpsisTaskbarDesignerForm(),
                () => new AppearancePreviewForm(new AppearancePresets.Preset { Name = "Windows 11 RC preview" })
            };
            var measured = new System.Collections.Generic.List<object>();
            var process = Process.GetCurrentProcess();
            long baselinePrivate = process.PrivateMemorySize64;
            foreach (var factory in forms)
            {
                using var form = factory();
                var title = form.GetType().Name;
                foreach (var size in sizes)
                {
                    form.ClientSize = size;
                    form.PerformLayout();
                    Verify(form.ClientSize.Width == size.Width && form.ClientSize.Height == size.Height,
                        title + " sizing " + size.Width + "x" + size.Height);
                    using var screenshot = new Bitmap(size.Width, size.Height);
                    form.DrawToBitmap(screenshot, new Rectangle(Point.Empty, size));
                    screenshot.Save(Path.Combine(output, title + "-" + size.Width + "x" + size.Height + ".png"));
                }
                var controls = AllControls(form).ToArray();
                if (form is AppearancePreviewForm)
                {
                    Verify(controls.Length == 2 && controls.Any(c => c.AccessibleName == "Appearance preview canvas" && c.Width > 0 && c.Height > 0)
                        && controls.Any(c => c is Label && c.Text.Contains("Visual simulation", StringComparison.Ordinal)),
                        title + " custom canvas and disclosure label");
                }
                else Verify(controls.Length >= 3, title + " child control tree");
                Verify(controls.All(c => c.Width >= 0 && c.Height >= 0), title + " nonnegative control sizes");
                measured.Add(new { title, controlCount = controls.Length, preferredSize = form.PreferredSize.ToString() });
            }
            // Exercise every registered sidebar navigation button, capture the
            // real main window and record layout/scroll properties for each page.
            using (var shell = new MainWindow())
            {
                shell.Size = new Size(1280, 800);
                shell.Show();
                Application.DoEvents();
                var pageKeys = shell.PageKeys.ToArray();
                Verify(pageKeys.Length >= 12, "main sidebar exposes expected native pages");
                var pageTimings = new System.Collections.Generic.List<object>();
                foreach (var key in pageKeys)
                {
                    var elapsed = Stopwatch.StartNew();
                    shell.NavigateTo(key);
                    for (int tick = 0; tick < 6; tick++)
                    {
                        Application.DoEvents();
                        System.Threading.Thread.Sleep(15);
                    }
                    elapsed.Stop();
                    var controls = AllControls(shell).ToArray();
                    Verify(controls.Length > 10, "sidebar page " + key + " renders control tree");
                    var scrollables = controls.OfType<ScrollableControl>().Where(c => c.AutoScroll).ToArray();
                    var buttons = controls.OfType<ButtonBase>().ToArray();
                    var inputs = controls.OfType<TextBoxBase>().Count() +
                                 controls.OfType<ComboBox>().Count() +
                                 controls.OfType<NumericUpDown>().Count();
                    pageTimings.Add(new { page = key, elapsedMs = elapsed.Elapsed.TotalMilliseconds,
                        controls = controls.Length, clickableButtons = buttons.Length, settingsInputs = inputs, scrollingContainers = scrollables.Length,
                        visibleScrollbars = scrollables.Count(c => c.VerticalScroll.Visible) });
                    using var screenshot = new Bitmap(1280, 800);
                    shell.DrawToBitmap(screenshot, new Rectangle(0, 0, 1280, 800));
                    var safeKey = string.Concat(key.Where(char.IsLetterOrDigit));
                    screenshot.Save(Path.Combine(output, "sidebar-page-" + safeKey + ".png"));
                }
                // Validate the sidebar width animation and scroll container in the real shell.
                var navField = typeof(MainWindow).GetField("_nav",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                var navPanel = navField?.GetValue(shell) as FlowLayoutPanel;
                if (navPanel != null)
                {
                    Verify(navPanel.ClientSize.Width > 30, "sidebar navigation occupies usable width");
                    var sideField = typeof(MainWindow).GetField("_sidebar",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    var sidebarPanel = sideField?.GetValue(shell) as Panel;
                    var toggle = typeof(MainWindow).GetMethod("ToggleSidebarCollapsed",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    if (sidebarPanel != null && toggle != null)
                    {
                        var beforeWidth = sidebarPanel.Width;
                        toggle.Invoke(shell, null);
                        for (int t = 0; t < 45; t++)
                        {
                            Application.DoEvents();
                            System.Threading.Thread.Sleep(12);
                        }
                        var changedWidth = sidebarPanel.Width;
                        Verify(changedWidth != beforeWidth, "sidebar collapse transition reaches different width");
                        using (var screenshot = new Bitmap(1280, 800))
                        {
                            shell.DrawToBitmap(screenshot, new Rectangle(0, 0, 1280, 800));
                            screenshot.Save(Path.Combine(output, "sidebar-collapsed.png"));
                        }
                        toggle.Invoke(shell, null);
                        for (int t = 0; t < 45; t++)
                        {
                            Application.DoEvents();
                            System.Threading.Thread.Sleep(12);
                        }
                        Verify(sidebarPanel.Width == beforeWidth,
                            "sidebar expand returns to original width");
                    }
                }
                measured.Add(new { title = "MainWindow pages", controlCount = pageKeys.Length, measurements = pageTimings });
                shell.Close();
            }
            // GDI+/WinForms repeated construction stress; bounded memory observation.
            for (int i = 0; i < 80; i++)
            {
                using var form = new StartStudioForm();
                using var b = new Bitmap(48, 48);
                using var g = Graphics.FromImage(b);
                OpsisStartButtonArt.Draw(g, new Rectangle(0, 0, 48, 48),
                    OpsisStartButtonArt.Styles[i % OpsisStartButtonArt.Styles.Length]);
            }
            process.Refresh();
            var deltaMb = (process.PrivateMemorySize64 - baselinePrivate) / 1048576.0;
            measured.Add(new { title = "Stress", controlCount = 80, privateMemoryDeltaMiB = deltaMb });
            Verify(deltaMb < 256, "80-cycle Start Studio/GDI stress bounded private memory delta");
            File.WriteAllText(Path.Combine(output, "ui-smoke-report.json"),
                JsonSerializer.Serialize(new { passed = true, runtimeSeconds = watch.Elapsed.TotalSeconds,
                    environment = Environment.OSVersion.VersionString, checks, measured }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("UI smoke PASS in " + watch.Elapsed);
            return 0;
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(output, "ui-smoke-report.json"),
                JsonSerializer.Serialize(new { passed = false, runtimeSeconds = watch.Elapsed.TotalSeconds,
                    error = ex.ToString(), checks }, new JsonSerializerOptions { WriteIndented = true }));
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static System.Collections.Generic.IEnumerable<Control> AllControls(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var nested in AllControls(child)) yield return nested;
        }
    }
}
