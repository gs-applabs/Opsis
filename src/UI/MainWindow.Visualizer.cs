using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using MotionDesk.Services;
using MotionDesk.Widgets;
using UiTheme = MotionDesk.UI.UiTheme;

namespace MotionDesk.UI
{
    // Wallpaper page: the "Music visualizer" card (wallpaper element, see wallpaper/visualizer.js and VisualizerConfig).
    public sealed partial class MainWindow
    {
        private static string V(string key) => LocalizationManager.T("Viz." + key);

        // 2.6.0: the music overlay (small now-playing card over the desktop) lives on the Wallpaper page next to the visualizer; all live, no restart
        private void BuildMusicOverlaySection(FlowLayoutPanel panel)
        {
            AddSection(panel, LocalizationManager.T("Settings.MusicOverlay"), () =>
            {
                var d = new AppSettings(); var a = AppSettings.Load();
                a.MusicOverlayEnabled = d.MusicOverlayEnabled; a.MusicOverlayAnchor = d.MusicOverlayAnchor; a.MusicOverlayAutoHideSeconds = d.MusicOverlayAutoHideSeconds; a.MusicOverlayClickThrough = d.MusicOverlayClickThrough;
                a.Save(); MusicOverlay.Apply();
            });
            panel.Controls.Add(SettingsView.Checkbox(LocalizationManager.T("Settings.MusicOverlay"), AppSettings.Load().MusicOverlayEnabled,
                v => { var a = AppSettings.Load(); a.MusicOverlayEnabled = v; a.Save(); MusicOverlay.Apply(); }, LocalizationManager.T("Settings.MusicOverlayDesc")));
            {
                var anchorRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 4, 0, 8) };
                anchorRow.Controls.Add(new Label { Text = LocalizationManager.T("Settings.MusicOverlayPosition"), AutoSize = false, Width = 300, Height = 28, TextAlign = ContentAlignment.MiddleLeft, Font = UiTheme.FontBody, ForeColor = UiTheme.TextSecondary });
                var anchorCombo = new FlatComboBox { Width = 200 };
                var anchors = Enum.GetValues<MotionDesk.Services.OverlayAnchor>();
                var anchorNames = anchors.Select(a => LocalizationManager.T("Anchor." + a)).ToArray();
                var curAnchor = Enum.TryParse<MotionDesk.Services.OverlayAnchor>(AppSettings.Load().MusicOverlayAnchor, true, out var ca) ? ca : MotionDesk.Services.OverlayAnchor.BottomRight;
                anchorCombo.SetItems(anchorNames, anchorNames[Array.IndexOf(anchors, curAnchor)]);
                anchorCombo.SelectedIndexChanged += (_, _) => { var a = AppSettings.Load(); a.MusicOverlayAnchor = anchors[Math.Clamp(anchorCombo.SelectedIndex, 0, anchors.Length - 1)].ToString(); a.Save(); MusicOverlay.Apply(); };
                anchorRow.Controls.Add(anchorCombo);
                panel.Controls.Add(anchorRow);
            }
            panel.Controls.Add(AdvancedStart());
            panel.Controls.Add(SettingsView.NumericRow(LocalizationManager.T("Settings.MusicOverlayAutoHide"), Math.Clamp(AppSettings.Load().MusicOverlayAutoHideSeconds, 0, 600), 0, 600,
                v => { var a = AppSettings.Load(); a.MusicOverlayAutoHideSeconds = v; a.Save(); MusicOverlay.Apply(); }, 0, "s"));
            panel.Controls.Add(SettingsView.Checkbox(LocalizationManager.T("Settings.MusicOverlayClickThrough"), AppSettings.Load().MusicOverlayClickThrough,
                v => { var a = AppSettings.Load(); a.MusicOverlayClickThrough = v; a.Save(); MusicOverlay.Apply(); }, LocalizationManager.T("Settings.MusicOverlayClickThroughDesc")));
        }

        private void BuildVisualizerSection(FlowLayoutPanel panel, WallpaperSettings settings)
        {
            var cfg = settings.Visualizer ?? new VisualizerConfig();
            AddSection(panel, V("Section"));
            AddText(panel, V("Intro"));

            var debounce = new System.Windows.Forms.Timer { Interval = 150 };
            Action? pending = null;
            debounce.Tick += (_, _) => { debounce.Stop(); pending?.Invoke(); pending = null; };
            void Commit(Action<VisualizerConfig> change)
            {
                var s = WallpaperSettings.Load(); s.Visualizer ??= new VisualizerConfig(); change(s.Visualizer); s.Save();
                WallpaperHostEngine.Instance.RefreshElements();
            }
            Color ParseColor(string t) { try { return ColorTranslator.FromHtml(t); } catch (Exception) { return Color.White; } }
            string Html(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

            FlowLayoutPanel Row() { var r = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 2) }; panel.Controls.Add(r); return r; }
            void Toggle(string key, bool value, Action<VisualizerConfig, bool> set)
            {
                var t = new FlatToggle { RowWidth = 440, Text = V(key), Checked = value, BackColor = UiTheme.PageBackground, Margin = new Padding(0, 2, 8, 0) };
                t.CheckedChanged += (_, _) => { bool v = t.Checked; Commit(c => set(c, v)); };
                panel.Controls.Add(t);
            }
            void Slider(string key, int min, int max, int value, Action<VisualizerConfig, int> set)
            {
                var r = Row();
                r.Controls.Add(new Label { Text = V(key), AutoSize = false, Width = 250, Height = 26, TextAlign = ContentAlignment.MiddleLeft, Font = UiTheme.FontBody, ForeColor = UiTheme.TextSecondary, Margin = new Padding(0, 2, 6, 0) });
                var sl = new FlatSlider { Width = 200, Height = 26, Minimum = min, Maximum = max, Margin = new Padding(0, 2, 6, 0) };
                sl.Value = Math.Clamp(value, min, max);
                sl.ValueChanged += (_, _) => { int v = sl.Value; pending = () => Commit(c => set(c, v)); debounce.Stop(); debounce.Start(); };
                r.Controls.Add(sl);
            }
            void ColorPick(string key, string value, Action<VisualizerConfig, string> set, Control? host = null)
            {
                var r = host as FlowLayoutPanel ?? Row();
                r.Controls.Add(new Label { Text = V(key), AutoSize = false, Width = 250, Height = 26, TextAlign = ContentAlignment.MiddleLeft, Font = UiTheme.FontBody, ForeColor = UiTheme.TextSecondary, Margin = new Padding(0, 2, 6, 0) });
                var b = new Button { Width = 60, Height = 26, FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 2, 8, 0), Cursor = Cursors.Hand, TabStop = false, BackColor = ParseColor(value) };
                b.FlatAppearance.BorderColor = UiTheme.Border;
                b.Click += (_, _) =>
                {
                    using var dlg = new ColorDialog { FullOpen = true, Color = b.BackColor };
                    if (dlg.ShowDialog(this) != DialogResult.OK) return;
                    b.BackColor = dlg.Color; string h = Html(dlg.Color); Commit(c => set(c, h));
                };
                r.Controls.Add(b);
            }
            // 2.6.0: one card per function (user test: the settings were mixed without saying what they belong to). Each card shows the everyday rows;
            // fine tuning (sizes, positions, fonts, thresholds) sits in the collapsed "Advanced" group of the same card.
            string Sub(string hdrKey) => V("Section") + " · " + V(hdrKey);
            void Adv() => panel.Controls.Add(AdvancedStart());

            AddText(panel, V("Note"));
            Toggle("Enable", cfg.Enabled, (c, v) => c.Enabled = v);

            AddSection(panel, Sub("HdrColors"));
            Toggle("UseBg", cfg.UseBackgroundColor, (c, v) => c.UseBackgroundColor = v);
            ColorPick("UseBg", cfg.BackgroundColor, (c, v) => c.BackgroundColor = v);
            Toggle("UseBgImage", cfg.UseBackgroundImage, (c, v) => c.UseBackgroundImage = v);
            {
                var r = Row();
                var path = new Label { Text = string.IsNullOrEmpty(cfg.BackgroundImagePath) ? "" : System.IO.Path.GetFileName(cfg.BackgroundImagePath), AutoSize = false, Width = 250, Height = 26, TextAlign = ContentAlignment.MiddleLeft, Font = UiTheme.FontBody, ForeColor = UiTheme.TextSecondary };
                var pick = MediaButton(V("Pick"));
                pick.Click += (_, _) =>
                {
                    using var dlg = new OpenFileDialog { Filter = "Image / video|*.jpg;*.jpeg;*.png;*.webp;*.webm;*.mp4|*.*|*.*" };
                    if (dlg.ShowDialog(this) != DialogResult.OK) return;
                    path.Text = System.IO.Path.GetFileName(dlg.FileName); string f = dlg.FileName; Commit(c => { c.BackgroundImagePath = f; c.UseBackgroundImage = true; });
                };
                r.Controls.Add(path); r.Controls.Add(pick);
            }
            Adv();
            Toggle("Gradient", cfg.CustomGradient, (c, v) => c.CustomGradient = v);
            ColorPick("Color1", cfg.Color1, (c, v) => c.Color1 = v);
            ColorPick("Color2", cfg.Color2, (c, v) => c.Color2 = v);
            Toggle("WhiteInside", cfg.WhiteInside, (c, v) => c.WhiteInside = v);

            AddSection(panel, Sub("HdrRays"));
            Toggle("ShowRays", cfg.ShowRays, (c, v) => c.ShowRays = v);
            {
                var r = Row();
                r.Controls.Add(new Label { Text = V("Layout"), AutoSize = false, Width = 250, Height = 26, TextAlign = ContentAlignment.MiddleLeft, Font = UiTheme.FontBody, ForeColor = UiTheme.TextSecondary });
                var combo = new FlatComboBox { Width = 200 };
                string[] names = { V("Radial"), V("Linear") };
                combo.SetItems(names, cfg.Layout == "Linear" ? names[1] : names[0]);
                combo.SelectedIndexChanged += (_, _) => { string v = combo.SelectedIndex == 1 ? "Linear" : "Radial"; Commit(c => c.Layout = v); };
                r.Controls.Add(combo);
            }
            Toggle("HideSilent", cfg.HideWhenSilent, (c, v) => c.HideWhenSilent = v);
            Adv();
            Toggle("CleanEnd", cfg.CleanLineEnd, (c, v) => c.CleanLineEnd = v);
            Toggle("Interpolate", cfg.Interpolate, (c, v) => c.Interpolate = v);
            Toggle("Rounded", cfg.Rounded, (c, v) => c.Rounded = v);
            Toggle("Reverse", cfg.Reverse, (c, v) => c.Reverse = v);
            Slider("MinLevel", 0, 1000, cfg.MinLevel, (c, v) => c.MinLevel = v);
            Slider("LineWidth", 0, 100, cfg.LineWidth, (c, v) => c.LineWidth = v);
            Slider("Intensity", 0, 99, cfg.Intensity, (c, v) => c.Intensity = v);
            Slider("AddLevel", 0, 500, cfg.AddLevel, (c, v) => c.AddLevel = v);
            Slider("RayAngle", 0, 100, cfg.RayAngle, (c, v) => c.RayAngle = v);
            Slider("Treble", 0, 100, cfg.TrebleBoost, (c, v) => c.TrebleBoost = v);
            Slider("PosX", 0, 100, cfg.PosX, (c, v) => c.PosX = v);
            Slider("PosY", 0, 100, cfg.PosY, (c, v) => c.PosY = v);
            {
                var regen = MediaButton(V("Regen"));
                regen.Click += (_, _) => Commit(c => c.IdleSeed = Environment.TickCount & 0x7fffffff);
                panel.Controls.Add(regen);
            }

            AddSection(panel, Sub("HdrTime"));
            Toggle("Show", cfg.ShowClock, (c, v) => c.ShowClock = v);
            ColorPick("Color", cfg.ClockColor, (c, v) => c.ClockColor = v);
            Toggle("Seconds", cfg.ShowSeconds, (c, v) => c.ShowSeconds = v);
            Toggle("Hour12", cfg.Hour12, (c, v) => c.Hour12 = v);
            Adv();
            {
                var r = Row();
                r.Controls.Add(new Label { Text = V("Font"), AutoSize = false, Width = 250, Height = 26, TextAlign = ContentAlignment.MiddleLeft, Font = UiTheme.FontBody, ForeColor = UiTheme.TextSecondary });
                var fonts = new FlatComboBox { Width = 200 };
                var names = FontFamily.Families.Select(f => f.Name).OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToArray();
                fonts.SetItems(names, names.Contains(cfg.Font) ? cfg.Font : (names.Contains("Segoe UI") ? "Segoe UI" : names[0]));
                fonts.SelectedIndexChanged += (_, _) => { string? v = fonts.SelectedItem; if (v != null) Commit(c => c.Font = v); };
                r.Controls.Add(fonts);
            }
            Toggle("Bold", cfg.Bold, (c, v) => c.Bold = v);
            Slider("Size", 0, 100, cfg.ClockSize, (c, v) => c.ClockSize = v);
            Slider("PosX", 0, 100, cfg.ClockX, (c, v) => c.ClockX = v);
            Slider("PosY", 0, 100, cfg.ClockY, (c, v) => c.ClockY = v);

            AddSection(panel, Sub("HdrDate"));
            Toggle("Show", cfg.ShowDate, (c, v) => c.ShowDate = v);
            Toggle("DayName", cfg.ShowDayName, (c, v) => c.ShowDayName = v);
            Toggle("DayNumber", cfg.ShowDayNumber, (c, v) => c.ShowDayNumber = v);
            Toggle("Year", cfg.ShowYear, (c, v) => c.ShowYear = v);
            Toggle("Numeric", cfg.NumericDate, (c, v) => c.NumericDate = v);
            Adv();
            ColorPick("Color", cfg.DateColor, (c, v) => c.DateColor = v);
            Slider("Size", 0, 100, cfg.DateSize, (c, v) => c.DateSize = v);
            Slider("PosX", 0, 100, cfg.DateX, (c, v) => c.DateX = v);
            Slider("PosY", 0, 100, cfg.DateY, (c, v) => c.DateY = v);

            AddSection(panel, Sub("HdrShadow"));
            Toggle("Shadow", cfg.Shadow, (c, v) => c.Shadow = v);
            Adv();
            Slider("ShadowIntensity", 0, 500, cfg.ShadowIntensity, (c, v) => c.ShadowIntensity = v);
            Toggle("ShadowMusic", cfg.ShadowFollowsMusic, (c, v) => c.ShadowFollowsMusic = v);
        }
    }
}
