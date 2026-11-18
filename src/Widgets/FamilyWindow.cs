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
                    list.Add(new FamilyApp(id, name, repo, tagline, paths, owner));
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
            BackColor = UiTheme.Background, ForeColor = UiTheme.TextPrimary, Font = UiTheme.FontBody, ShowInTaskbar = true, Icon = SystemIcons.Application,
        };

        public static void ShowWelcome()
        {
            var open = Application.OpenForms.OfType<Form>().FirstOrDefault(f => f.Tag as string == "opsis-welcome");
            if (open != null) { open.Activate(); return; }
            var f = NewForm(OpsisStrings.T("ui.welcomeTitle"), new Size(560, 420)); f.Tag = "opsis-welcome";
            var text = new Label { Text = LocalizationManager.T("Welcome.Body"), Dock = DockStyle.Fill, Padding = new Padding(24, 20, 24, 12), ForeColor = UiTheme.TextPrimary, Font = new Font("Segoe UI", 10.5f) };
            var close = new Opsis.UI.FlatButton { Text = OpsisStrings.T("ui.gotIt"), Primary = true, Width = 120, Height = 34, Dock = DockStyle.Bottom };
            close.Click += (_, _) => f.Close();
            f.Controls.Add(text); f.Controls.Add(close);
            f.Show();
        }

        public static void ShowFamily()
        {
            var open = Application.OpenForms.OfType<Form>().FirstOrDefault(f => f.Tag as string == "opsis-family");
            if (open != null) { open.Activate(); return; }
            var apps = FamilyApp.Load();
            // same layout as the family window of GearWin / Kombos: title + close on top, one card per app (icon, name, description, state, buttons), cards wrap
            var f = NewForm(OpsisStrings.T("ui.familyTitle"), new Size(1040, 640)); f.Tag = "opsis-family"; f.MinimumSize = new Size(420, 360); f.FormBorderStyle = FormBorderStyle.Sizable; f.MaximizeBox = true;
            var header = new Panel { Dock = DockStyle.Top, Height = 64, Padding = new Padding(24, 14, 24, 8), BackColor = UiTheme.Background };
            var close = new Opsis.UI.FlatButton { Text = OpsisStrings.T("ui.close"), Primary = true, Width = 110, Height = 34, Dock = DockStyle.Right };
            close.Click += (_, _) => f.Close();
            header.Controls.Add(new Label { Text = OpsisStrings.T("ui.familyTitle"), Dock = DockStyle.Fill, Font = new Font("Segoe UI Semibold", 18f), ForeColor = UiTheme.TextPrimary, TextAlign = ContentAlignment.MiddleLeft });
            header.Controls.Add(close);
            var list = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, AutoScroll = true, Padding = new Padding(16, 8, 16, 16), BackColor = UiTheme.Background };
            foreach (var app in apps)
            {
                bool self = app.Id.Equals("opsis", StringComparison.OrdinalIgnoreCase);
                string? installed = self ? null : app.InstalledPath;
                var card = new Panel { Width = 320, Height = 190, BackColor = UiTheme.Surface, Margin = new Padding(8), Padding = new Padding(16) };
                card.Paint += (_, e) =>
                {
                    using var pen = new Pen(self || installed != null ? UiTheme.AccentCyan : UiTheme.Border, self ? 2 : 1);
                    e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
                };
                card.Controls.Add(new Label { Text = app.Name.Length > 0 ? app.Name[..1].ToUpperInvariant() : "?", Location = new Point(16, 16), Size = new Size(48, 48), TextAlign = ContentAlignment.MiddleCenter,
                    Font = new Font("Segoe UI Semibold", 18f), BackColor = UiTheme.AccentBlue, ForeColor = Color.White });
                card.Controls.Add(new Label { Text = app.Name, Location = new Point(76, 14), Size = new Size(228, 26), Font = new Font("Segoe UI Semibold", 13f), ForeColor = UiTheme.TextPrimary, AutoEllipsis = true });
                card.Controls.Add(new Label { Text = self || installed != null ? "●" : "○", Location = new Point(76, 42), Size = new Size(228, 20), ForeColor = self || installed != null ? UiTheme.AccentCyan : UiTheme.TextMuted });
                card.Controls.Add(new Label { Text = app.Tagline, Location = new Point(16, 76), Size = new Size(288, 54), ForeColor = UiTheme.TextSecondary });
                if (!self)
                {
                    var captured = app;
                    var btn = new Opsis.UI.FlatButton { Width = 130, Height = 32, Location = new Point(16, 140), Primary = installed == null, Text = installed != null ? OpsisStrings.T("ui.open") : OpsisStrings.T("ui.download") };
                    btn.Click += (_, _) =>
                    {
                        try
                        {
                            if (installed != null && SafeLaunch.IsAllowed(installed)) Process.Start(new ProcessStartInfo(installed) { UseShellExecute = true });
                            else if (captured.Repo.Length > 0 && captured.Owner.Length > 0) Process.Start(new ProcessStartInfo(captured.ReleasesUrl) { UseShellExecute = true });
                        }
                        catch (Exception ex) { RotatingLogger.Write("Family window: " + ex.Message); }
                    };
                    card.Controls.Add(btn);
                    if (app.Repo.Length > 0 && app.Owner.Length > 0)
                    {
                        var more = new Opsis.UI.FlatButton { Width = 100, Height = 32, Location = new Point(156, 140), Text = "GitHub" };
                        more.Click += (_, _) => { try { Process.Start(new ProcessStartInfo(captured.ReleasesUrl) { UseShellExecute = true }); } catch (Exception ex) { RotatingLogger.Write("Family window: " + ex.Message); } };
                        card.Controls.Add(more);
                    }
                }
                list.Controls.Add(card);
            }
            if (apps.Count == 0) list.Controls.Add(new Label { Text = LocalizationManager.T("Family.NoList"), AutoSize = true, ForeColor = UiTheme.TextSecondary });
            f.Controls.Add(list);
            f.Controls.Add(header);
            f.Show();
        }
    }
}
