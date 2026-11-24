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
                Verify(controls.Length >= 3, title + " child control tree");
                Verify(controls.All(c => c.Width >= 0 && c.Height >= 0), title + " nonnegative control sizes");
                measured.Add(new { title, controlCount = controls.Length, preferredSize = form.PreferredSize.ToString() });
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
