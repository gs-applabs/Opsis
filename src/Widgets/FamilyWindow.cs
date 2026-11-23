using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Forms;
using MotionDesk.Services;
using MotionDesk.UI;

namespace MotionDesk.Widgets
{
    /// <summary>One app of the Opsis family as listed in suite/apps.json.</summary>
    public sealed record FamilyApp(string Id, string Name, string Repo, string Tagline, string[] Paths, string Owner)
    {
        public string[] Capabilities { get; init; } = [];

        /// <summary>First existing install path (environment variables expanded), or null when the app is not installed.</summary>
        public string? InstalledPath => Paths.Select(Environment.ExpandEnvironmentVariables).FirstOrDefault(File.Exists);

        /// <summary>
        /// DeskStrip auto-pin (pure, testable): the installed family apps (other than Opsis itself) that should be pinned now. Each app is offered ONCE (its id goes into
        /// <paramref name="alreadyOffered"/>), so an app the user unpins by hand is never pinned again. <paramref name="installedPathOf"/> maps an app to its install path or null.
        /// </summary>
        public static List<(string Id, string Path)> AutoPinCandidates(IEnumerable<FamilyApp> apps, ISet<string> alreadyOffered, Func<FamilyApp, string?> installedPathOf)
        {
            var result = new List<(string, string)>();
            foreach (var a in apps)
            {
                if (a.Id.Equals("opsis", StringComparison.OrdinalIgnoreCase) || alreadyOffered.Contains(a.Id)) continue;
                var path = installedPathOf(a);
                if (string.IsNullOrEmpty(path)) continue;
                result.Add((a.Id, path));
            }
            return result;
        }

        public string ReleasesUrl => $"https://github.com/{Owner}/{Repo}/releases/latest";

        /// <summary>Reads the family list (pure, testable). Unknown / broken entries are skipped.</summary>
        public static IReadOnlyList<FamilyApp> Parse(string json, string language)
        {
            var list = new List<FamilyApp>();
            try
            {
                using var doc = JsonDocument.Parse(json);
                string owner = doc.RootElement.TryGetProperty("owner", out var o) ? o.GetString() ?? "" : "";
                foreach (var a in doc.RootElement.GetProperty("apps").EnumerateArray())
                {
                    string id = a.TryGetProperty("id", out var i) ? i.GetString() ?? "" : "";
                    string name = a.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    string repo = a.TryGetProperty("repo", out var r) ? r.GetString() ?? "" : "";
                    if (id.Length == 0 || name.Length == 0) continue;
                    string tagline = "";
                    if (a.TryGetProperty("tagline", out var t) && t.ValueKind == JsonValueKind.Object)
                        tagline = (t.TryGetProperty(language, out var tl) ? tl.GetString() : null) ?? (t.TryGetProperty("en", out var te) ? te.GetString() : null) ?? "";
                    var paths = a.TryGetProperty("paths", out var p) && p.ValueKind == JsonValueKind.Array ? p.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0).ToArray() : Array.Empty<string>();
                    string[] capabilities = [];
                    if (a.TryGetProperty("capabilities", out var descriptions) && descriptions.ValueKind == JsonValueKind.Object &&
                        (descriptions.TryGetProperty(language, out var features) || descriptions.TryGetProperty("en", out features)) &&
                        features.ValueKind == JsonValueKind.Array)
                    {
                        capabilities = features.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String)
                            .Select(x => x.GetString()!).Where(x => !string.IsNullOrWhiteSpace(x)).Take(5).ToArray();
                    }
                    list.Add(new FamilyApp(id, name, repo, tagline, paths, owner) { Capabilities = capabilities });
                }
            }
            catch (Exception) { }
            return list;
        }

        public static IReadOnlyList<FamilyApp> Load()
        {
            try
            {
                string path = Path.Combine(AppContext.BaseDirectory, "suite", "apps.json");
                return File.Exists(path) ? Parse(File.ReadAllText(path), LocalizationManager.CurrentLanguage) : Array.Empty<FamilyApp>();
            }
            catch (Exception) { return Array.Empty<FamilyApp>(); }
        }
    }

    /// <summary>The "Opsis family" window and the "Welcome guide" window: both belong to Opsis itself (plugins do not offer them when they are hosted by Opsis).</summary>
    internal static class FamilyWindows
    {
        private static Form NewForm(string title, Size size) => new()
        {
            Text = title, Size = size, StartPosition = FormStartPosition.CenterScreen, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false,
            BackColor = UiTheme.Background, ForeColor = UiTheme.TextPrimary, Font = UiTheme.FontBody, ShowInTaskbar = true, Icon = AppWindowIcon.Value,
        };

        public static void ShowWelcome()
        {
            var open = Application.OpenForms.OfType<Form>().FirstOrDefault(f => f.Tag as string == "opsis-welcome");
            if (open != null) { open.Activate(); return; }
            var f = NewForm($"Opsis Desktop Customization {MotionDesk.Services.AppVersion.Label}", new Size(560, 420)); f.Tag = "opsis-welcome";
            var text = new Label { Text = LocalizationManager.T("Welcome.Body"), Dock = DockStyle.Fill, Padding = new Padding(24, 20, 24, 12), ForeColor = UiTheme.TextPrimary, Font = new Font("Segoe UI", 10.5f) };
            var close = new Opsis.UI.FlatButton { Text = OpsisStrings.T("ui.gotIt"), Primary = true, Width = 120, Height = 34, Dock = DockStyle.Bottom };
            close.Click += (_, _) => f.Close();
            f.Controls.Add(text); f.Controls.Add(close);
            f.Show();
        }

        public static void ShowFamily()
        {
            var open = Application.OpenForms.OfType<Form>().FirstOrDefault(f => f.Tag as string == "opsis-family");
            if (open != null)
            {
                if (open.WindowState == FormWindowState.Minimized) open.WindowState = FormWindowState.Normal;
                open.Activate();
                return;
            }
            var apps = FamilyApp.Load();
            var screen = Screen.FromPoint(Cursor.Position).WorkingArea;
            var title = OpsisStrings.T("ui.familyTitle");
            var f = new Opsis.UI.OpsisWindow
            {
                Text = title, Icon = AppWindowIcon.Value, Tag = "opsis-family", StartPosition = FormStartPosition.Manual,
                Size = new Size(Math.Min(1070, screen.Width), Math.Min(770, screen.Height)),
                MinimumSize = new Size(Math.Min(450, screen.Width), Math.Min(360, screen.Height)),
                BackColor = UiTheme.Background, ForeColor = UiTheme.TextPrimary, Font = UiTheme.FontBody,
                KeyPreview = true
            };
            f.Location = new Point(screen.X + (screen.Width - f.Width) / 2, screen.Y + (screen.Height - f.Height) / 2);
            f.KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) f.Close(); };
            var header = new TableLayoutPanel { Dock = DockStyle.Top, Height = 88, ColumnCount = 2, RowCount = 2, Padding = new Padding(21, 12, 21, 8) };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 114));
            header.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            header.Controls.Add(new Label { Text = title, Font = UiTheme.FontHeading, AutoSize = true }, 0, 0);
            header.Controls.Add(new Label { Text = LocalizationManager.T("Family.Subtitle"), ForeColor = UiTheme.TextSecondary, Dock = DockStyle.Fill }, 0, 1);
            var close = FamilyButton(LocalizationManager.T("Family.Close"), false, f.Close);
            header.Controls.Add(close, 1, 0);
            header.SetRowSpan(close, 2);

            var list = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, AutoScroll = true, Padding = new Padding(18, 6, 18, 18) };
            foreach (var app in apps) list.Controls.Add(BuildFamilyCard(app));
            if (apps.Count == 0) list.Controls.Add(new Label { Text = LocalizationManager.T("Family.NoList"), AutoSize = true, ForeColor = UiTheme.TextSecondary });
            f.Controls.Add(list);
            f.Controls.Add(header);
            list.BringToFront();
            f.Show();
        }

        private static Control BuildFamilyCard(FamilyApp app)
        {
            bool self = app.Id == "opsis";
            string? installed = self ? null : app.InstalledPath;
            bool launchable = installed != null && SafeLaunch.IsAllowed(installed, requireExe: true);
            var card = new TableLayoutPanel
            {
                Name = $"Family_{app.Id}", AccessibleName = app.Name, BackColor = UiTheme.Surface,
                Width = 311, Height = 348, Margin = new Padding(6), Padding = new Padding(13), ColumnCount = 2, RowCount = 5
            };
            card.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 63));
            card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            foreach (int height in new[] { 28, 49, 29 }) card.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            card.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            card.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            card.Paint += (_, e) =>
            {
                using var pen = new Pen(self ? UiTheme.AccentCyan : UiTheme.Border);
                e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
            };
            var image = new PictureBox { Name = $"Icon_{app.Id}", Size = new Size(54, 54), Margin = new Padding(1, 3, 8, 0), SizeMode = PictureBoxSizeMode.Zoom };
            if (app.Id.Length > 0 && app.Id.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))
            {
                try
                {
                    using var icon = new Icon(Path.Combine(AppContext.BaseDirectory, "suite", "icons", app.Id + ".ico"), 64, 64);
                    image.Image = icon.ToBitmap();
                }
                catch (Exception ex) when (ex is IOException or ArgumentException or System.Runtime.InteropServices.ExternalException) { }
            }
            image.Image ??= SystemIcons.Application.ToBitmap();
            image.Disposed += (_, _) => image.Image?.Dispose();
            card.Controls.Add(image, 0, 0);
            card.SetRowSpan(image, 2);
            card.Controls.Add(new Label { Text = app.Name, Font = UiTheme.FontSubheading, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 1, 0);
            card.Controls.Add(new Label { Text = app.Tagline, ForeColor = UiTheme.TextSecondary, Dock = DockStyle.Fill }, 1, 1);
            string status = self ? "Family.Current" : installed == null ? "Family.NotInstalled" : "Family.Installed";
            var state = new Label { Text = LocalizationManager.T(status), ForeColor = self ? UiTheme.AccentCyan : UiTheme.TextSecondary, Dock = DockStyle.Fill };
            card.Controls.Add(state, 0, 2);
            card.SetColumnSpan(state, 2);
            var features = new Label { Text = string.Join("\n", app.Capabilities.Select(x => "• " + x)), Dock = DockStyle.Fill };
            card.Controls.Add(features, 0, 3);
            card.SetColumnSpan(features, 2);

            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0) };
            if (launchable)
                actions.Controls.Add(FamilyButton(OpsisStrings.T("ui.open"), true, () => OpenTarget(installed!)));
            else if (installed == null && !self && app.Repo.Length > 0 && app.Owner.Length > 0)
                actions.Controls.Add(FamilyButton(OpsisStrings.T("ui.download"), true, () => OpenTarget(app.ReleasesUrl)));
            if (app.Repo.Length > 0 && app.Owner.Length > 0)
                actions.Controls.Add(FamilyButton(LocalizationManager.T("Family.Details"), false, () => OpenTarget($"https://github.com/{app.Owner}/{app.Repo}")));
            card.Controls.Add(actions, 0, 4);
            card.SetColumnSpan(actions, 2);
            return card;
        }

        private static Opsis.UI.FlatButton FamilyButton(string text, bool primary, Action click)
        {
            var button = new Opsis.UI.FlatButton { Text = text, Primary = primary, Width = 112, Height = 32, Margin = new Padding(0, 0, 8, 3) };
            button.Click += (_, _) => click();
            return button;
        }

        private static void OpenTarget(string target)
        {
            try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
            catch (Exception ex) { RotatingLogger.Write("Family window: " + ex.Message); }
        }
    }
}
