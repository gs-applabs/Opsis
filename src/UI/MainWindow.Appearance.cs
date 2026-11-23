using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using MotionDesk.Appearance;
using MotionDesk.Services;
using MotionDesk.Widgets;
using UiTheme = MotionDesk.UI.UiTheme;

namespace MotionDesk.UI
{
    // "Windows appearance" cards (2.6.0), part of the Personalization page.
    // The engines and the lifecycle live in MotionDesk.Appearance.AppearanceService; this file only builds the cards (shared Opsis.UI controls, shared theme and language).
    public sealed partial class MainWindow
    {
        private int _winAppearanceScroll;

        // Added to the Personalization page (one tab, no sub-tabs). Rarely used rows sit in collapsed "Advanced" sections; the page search finds every row.
        private void BuildWindowsAppearance(FlowLayoutPanel panel)
        {
            var svc = AppearanceService.Instance;
            string T(string k) => LocalizationManager.T(k);
            void Rebuild() { _winAppearanceScroll = panel.VerticalScroll.Value; BeginInvoke(new Action(() => { if (!IsDisposed && _currentPageKey == "Personalization") ShowPersonalization(); })); }
            // a collapsed "Advanced" section inside a card; returns the column to add the rows to
            FlowLayoutPanel AdvBox(Control card)
            {
                var inner = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Width = 700, Margin = new Padding(0) };
                var adv = new StudioAdvancedSection(); adv.AddControl(inner); card.Controls.Add(adv);
                return inner;
            }
            void Fail(Exception ex) => MessageBox.Show(this, ex.Message, T("Nav.Personalization"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            // every change: the user's original values are saved first (once), then the change is made; a failure is shown, never thrown
            void Do(Action change, bool rebuild = false) { try { svc.Backup(); change(); } catch (Exception ex) { Fail(ex); } if (rebuild) Rebuild(); }

            AddText(panel, T("Win.Intro"));
            if (svc.Started && !svc.EngineActive) AddText(panel, T("Win.EngineBusy"));
            var cur = svc.Engine.ReadCurrent();
            var s = svc.Settings;

            FlatToggle Tg(Control card, string key, bool value, Action<bool> on)
            {
                var t = new FlatToggle { Name = key, RowWidth = 700, Text = T(key), Checked = value, BackColor = UiTheme.Surface, Margin = new Padding(0, 2, 0, 2) };
                t.CheckedChanged += (_, _) => on(t.Checked);
                card.Controls.Add(t); return t;
            }
            FlowLayoutPanel Row(Control card) { var r = new FlowLayoutPanel { AutoSize = true, WrapContents = true, MaximumSize = new Size(700, 0), Margin = new Padding(0, 2, 0, 4) }; card.Controls.Add(r); return r; }
            Label Lb(string key) => new() { Text = T(key), AutoSize = true, ForeColor = UiTheme.TextSecondary, Font = UiTheme.FontBody, Margin = new Padding(0, 8, 8, 0) };
            FlatComboBox Cb(FlowLayoutPanel row, string labelKey, string[] items, int index, Action<int> on, int width = 170)
            {
                row.Controls.Add(Lb(labelKey));
                var c = new FlatComboBox { Name = labelKey, Width = width, Margin = new Padding(0, 4, 12, 0) };
                c.SetItems(items, items[Math.Clamp(index, 0, items.Length - 1)]);
                c.SelectedIndexChanged += (_, _) => { if (c.SelectedIndex >= 0) on(c.SelectedIndex); };
                row.Controls.Add(c); return c;
            }
            HoverButton Bt(FlowLayoutPanel row, string key, Action click, int width = 200) { var b = NewHoverButton(T(key), click); b.Width = width; b.Margin = new Padding(0, 2, 8, 4); row.Controls.Add(b); return b; }
            NumericUpDown Nu(FlowLayoutPanel row, string labelKey, int min, int max, int value, Action<int> on)
            {
                row.Controls.Add(Lb(labelKey));
                var n = new NumericUpDown { Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max), Width = 70, BackColor = UiTheme.PageBackground, ForeColor = UiTheme.TextPrimary, Margin = new Padding(0, 4, 12, 0) };
                n.ValueChanged += (_, _) => on((int)n.Value);
                row.Controls.Add(n); return n;
            }
            int Rgb(Color c) => (c.R << 16) | (c.G << 8) | c.B;
            int? PickColor(int? initial = null)
            {
                using var dlg = new ColorDialog { FullOpen = true };
                if (initial is int i) dlg.Color = Color.FromArgb(255, Color.FromArgb(i));
                return dlg.ShowDialog(this) == DialogResult.OK ? Rgb(dlg.Color) : null;
            }
            void ResetButton(Control card, int category)
            {
                var r = Row(card);
                Bt(r, "Win.ResetSection", () =>
                {
                    if (MessageBox.Show(this, T("Settings.ResetConfirm"), T("Win.ResetSection"), MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
                    svc.ResetCategory(category); Rebuild();
                }, 260);
            }
            string[] Opts(params string[] keys) => keys.Select(k => k.StartsWith("=") ? k[1..] : T(k)).ToArray();

            // ---- theme and colour
            var theme = AddCardSection(panel, T("Win.SecTheme"));
            var modeRow = Row(theme);
            Cb(modeRow, "Win.Mode", Opts("Win.ModeManual", "Win.ModeSun", "Win.ModeFixed"), (int)s.Mode, i => { s.Mode = (ThemeMode)i; svc.SaveSettings(); Rebuild(); }, 260);
            var nowRow = Row(theme);
            Bt(nowRow, "Win.LightNow", () => Do(() => svc.Engine.Apply(new Look { Dark = false })), 150);
            Bt(nowRow, "Win.DarkNow", () => Do(() => svc.Engine.Apply(new Look { Dark = true })), 150);
            if (s.Mode == ThemeMode.AutoFixed)
            {
                var hrs = Row(theme);
                hrs.Controls.Add(Lb("Win.LightAt")); var la = new TextBox { Width = 60, Text = s.LightAt, BackColor = UiTheme.PageBackground, ForeColor = UiTheme.TextPrimary, Margin = new Padding(0, 4, 12, 0) };
                hrs.Controls.Add(la);
                hrs.Controls.Add(Lb("Win.DarkAt")); var da = new TextBox { Width = 60, Text = s.DarkAt, BackColor = UiTheme.PageBackground, ForeColor = UiTheme.TextPrimary, Margin = new Padding(0, 4, 12, 0) };
                hrs.Controls.Add(da);
                void SaveHours() { if (TimeSpan.TryParse(la.Text, out _)) s.LightAt = la.Text; if (TimeSpan.TryParse(da.Text, out _)) s.DarkAt = da.Text; svc.SaveSettings(); }
                la.Leave += (_, _) => SaveHours(); da.Leave += (_, _) => SaveHours();
            }
            if (s.Mode == ThemeMode.AutoSun)
            {
                var geo = Row(theme);
                geo.Controls.Add(Lb("Win.Lat")); var lat = new NumericUpDown { DecimalPlaces = 2, Minimum = -90, Maximum = 90, Value = (decimal)Math.Clamp(s.Latitude, -90, 90), Width = 80, BackColor = UiTheme.PageBackground, ForeColor = UiTheme.TextPrimary, Margin = new Padding(0, 4, 12, 0) };
                geo.Controls.Add(lat);
                geo.Controls.Add(Lb("Win.Lon")); var lon = new NumericUpDown { DecimalPlaces = 2, Minimum = -180, Maximum = 180, Value = (decimal)Math.Clamp(s.Longitude, -180, 180), Width = 80, BackColor = UiTheme.PageBackground, ForeColor = UiTheme.TextPrimary, Margin = new Padding(0, 4, 12, 0) };
                geo.Controls.Add(lon);
                var off = Nu(geo, "Win.OffsetMin", -180, 180, s.SunOffsetMinutes, v => { s.SunOffsetMinutes = v; svc.SaveSettings(); });
                lat.ValueChanged += (_, _) => { s.Latitude = (double)lat.Value; svc.SaveSettings(); };
                lon.ValueChanged += (_, _) => { s.Longitude = (double)lon.Value; svc.SaveSettings(); };
            }
            theme.Controls.Add(new Label { Text = T("Win.Accent"), AutoSize = true, ForeColor = UiTheme.TextSecondary, Font = UiTheme.FontBody, Margin = new Padding(0, 10, 0, 4) });
            var sw = new FlowLayoutPanel { AutoSize = true, WrapContents = true, MaximumSize = new Size(700, 0), Margin = new Padding(0, 0, 0, 4) };
            foreach (int rgb in new[] { 0x0078D4, 0x0099BC, 0x00B294, 0x10893E, 0x7A7574, 0xFF8C00, 0xF7630C, 0xE74856, 0xE3008C, 0x8764B8, 0x744DA9, 0x6B69D6 })
            {
                int c = rgb;
                var b = new Button { Width = 32, Height = 32, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(255, Color.FromArgb(c)), Margin = new Padding(0, 0, 6, 6), Cursor = Cursors.Hand, TabStop = false };
                b.FlatAppearance.BorderSize = 1; b.FlatAppearance.BorderColor = UiTheme.Border;
                b.Click += (_, _) => Do(() => svc.Engine.Apply(new Look { AccentRgb = c }));
                sw.Controls.Add(b);
            }
            theme.Controls.Add(sw);
            var accRow = Row(theme);
            Bt(accRow, "Win.CustomColor", () => { if (PickColor() is int c) Do(() => svc.Engine.Apply(new Look { AccentRgb = c })); }, 190);
            Bt(accRow, "Win.FromWallpaper", () =>
            {
                int? c = null; try { c = svc.Engine.WallpaperAccent(); } catch (Exception) { }
                if (c == null) { MessageBox.Show(this, T("Win.NoWallpaperColor"), T("Nav.Personalization")); return; }
                Do(() => svc.Engine.Apply(new Look { AccentRgb = c }));
            }, 190);
            ResetButton(theme, 0);

            // ---- taskbar (Windows settings)
            var tb = AddCardSection(panel, T("Win.SecTaskbar"));
            Tg(tb, "Settings.TransparencyEffects", WindowsSystemSettings.GetTransparencyEffectsEnabled(), v => Do(() =>
            {
                if (!WindowsSystemSettings.SetTransparencyEffectsEnabled(v))
                    throw new InvalidOperationException(T("Win.TransparencyFailed"));
                if (s.TaskbarLook.Enabled) svc.ApplyTaskbar(askForRestorePoint: false);
            }, rebuild: true));
            Tg(tb, "Win.AccentSurfaces", cur.AccentOnSurfaces == true, v => Do(() => svc.Engine.Apply(new Look { AccentOnSurfaces = v })));
            Tg(tb, "Win.Centered", cur.TaskbarCentered == true, v => Do(() => svc.Engine.Apply(new Look { TaskbarCentered = v })));
            Tg(tb, "Win.TbSearch", cur.ShowSearch == true, v => Do(() => svc.Engine.Apply(new Look { ShowSearch = v })));
            Tg(tb, "Win.TbTaskView", cur.ShowTaskView == true, v => Do(() => svc.Engine.Apply(new Look { ShowTaskView = v })));
            Tg(tb, "Win.TbWidgets", cur.ShowWidgets == true, v => Do(() => svc.Engine.Apply(new Look { ShowWidgets = v })));

            // ---- taskbar look (TranslucentTB / RoundedTB style)
            var bar = AddCardSection(panel, T("Win.SecBar"));
            AddText(bar, T("Win.BarNote"));
            void SaveBar()
            {
                try
                {
                    svc.Store.Save(s); svc.Backup();
                    var rp = svc.ApplyTaskbar(askForRestorePoint: true);
                    if (rp != null && rp.Status != RestorePointStatus.Created) _statusLabel.Text = rp.Message;
                }
                catch (Exception ex) { Fail(ex); }
            }
            var tl = s.TaskbarLook; var ts = s.TaskbarShape;
            Tg(bar, "Win.BarOn", tl.Enabled, v => { tl.Enabled = v; SaveBar(); });
            var barAdv = AdvBox(bar);
            string[] states = Opts("Win.Opt.Default", "Win.Opt.Clear", "Win.Opt.Blur", "=Acrylic", "Win.Opt.Opaque");
            var sr = Row(barAdv);
            Cb(sr, "Win.BarNormal", states, (int)tl.Normal, i => { tl.Normal = (TaskbarState)i; SaveBar(); }, 130);
            Cb(sr, "Win.BarMax", states, (int)tl.Maximized, i => { tl.Maximized = (TaskbarState)i; SaveBar(); }, 130);
            Cb(sr, "Win.BarStart", states, (int)tl.StartOpen, i => { tl.StartOpen = (TaskbarState)i; SaveBar(); }, 130);
            var tr = Row(barAdv);
            Bt(tr, "Win.BarTint", () => { if (PickColor(tl.TintRgb) is int c) { tl.TintRgb = c; SaveBar(); } }, 150);
            Nu(tr, "Win.BarStrength", 0, 100, tl.OpacityPercent, v => { tl.OpacityPercent = v; SaveBar(); });
            Tg(barAdv, "Win.ShapeOn", ts.Enabled, v => { ts.Enabled = v; SaveBar(); });
            var shr = Row(barAdv);
            Nu(shr, "Win.ShapeRadius", 0, 40, ts.Radius, v => { ts.Radius = v; SaveBar(); });
            Nu(shr, "Win.ShapeGapX", 0, 200, ts.MarginX, v => { ts.MarginX = v; SaveBar(); });
            Nu(shr, "Win.ShapeGapY", 0, 60, ts.MarginY, v => { ts.MarginY = v; SaveBar(); });
            ResetButton(bar, 1);

            // ---- window effects (documented DWM APIs, event driven)
            var fxCard = AddCardSection(panel, T("Win.SecEffects"));
            AddText(fxCard, T("Win.EffectsNote"));
            void Fx(Action<EffectsSettings> change) { try { svc.Backup(); change(s.Effects); svc.Store.Save(s); svc.ApplyEffects(); } catch (Exception ex) { Fail(ex); } }
            var fx = s.Effects;
            Tg(fxCard, "Win.Border", fx.ActiveBorder, v => Fx(e => e.ActiveBorder = v));
            var fr1 = Row(fxCard);
            Bt(fr1, "Win.BorderColor", () => { if (PickColor() is int c) Fx(e => { e.BorderRgb = c; e.ActiveBorder = true; }); }, 180);
            var fr2 = Row(fxCard);
            Cb(fr2, "Win.Corners", Opts("Win.Opt.Default", "Win.Opt.Round", "Win.Opt.RoundSmall", "Win.Opt.Square"), (int)fx.Corners, i => Fx(e => e.Corners = (CornerStyle)i));
            Cb(fr2, "Win.TitleBars", Opts("Win.Opt.Off", "Win.Opt.On", "Win.Opt.Follow"), (int)fx.DarkTitle, i => Fx(e => e.DarkTitle = (DarkTitleBars)i), 150);
            Cb(fr2, "Win.Backdrop", Opts("Win.Opt.Default", "Win.Opt.None", "=Mica", "=Acrylic", "=Tabbed"), (int)fx.Backdrop, i => Fx(e => e.Backdrop = (BackdropStyle)i), 150);
            Tg(fxCard, "Win.AutoHideTb", fx.AutoHideTaskbar, v => Fx(e => e.AutoHideTaskbar = v));
            var fxAdv = AdvBox(fxCard);
            var fr3 = Row(fxAdv);
            fr3.Controls.Add(Lb("Win.Exclude"));
            var ex = new TextBox { Width = 300, Text = string.Join(", ", fx.ExcludedProcesses), BackColor = UiTheme.PageBackground, ForeColor = UiTheme.TextPrimary, Margin = new Padding(0, 4, 8, 0) };
            ex.Leave += (_, _) => Fx(e => e.ExcludedProcesses = ex.Text.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(EffectsSettings.NormalizeProcess).Where(x => x.Length > 0).Distinct().ToList());
            fr3.Controls.Add(ex);
            var fr4 = Row(fxAdv);
            Bt(fr4, "Win.CaptionColor", () => { if (PickColor() is int c) Fx(e => e.CaptionRgb = c); }, 190);
            Bt(fr4, "Win.CaptionText", () => { if (PickColor() is int c) Fx(e => e.CaptionTextRgb = c); }, 190);
            Bt(fr4, "Win.ClearColors", () => Fx(e => { e.CaptionRgb = null; e.CaptionTextRgb = null; }), 150);
            ResetButton(fxCard, 2);

            // ---- Explorer and taskbar behaviour
            var shell = AddCardSection(panel, T("Win.SecExplorer"));
            Tg(shell, "Win.FileExt", cur.ShowFileExtensions == true, v => Do(() => svc.Engine.Apply(new Look { ShowFileExtensions = v })));
            Tg(shell, "Win.HiddenFiles", cur.ShowHiddenFiles == true, v => Do(() => svc.Engine.Apply(new Look { ShowHiddenFiles = v })));
            Tg(shell, "Win.ClockSecs", cur.ClockSeconds == true, v => Do(() => svc.Engine.Apply(new Look { ClockSeconds = v })));
            Tg(shell, "Win.Animations", cur.Animations == true, v => Do(() => svc.Engine.Apply(new Look { Animations = v })));
            var cr = Row(shell);
            Cb(cr, "Win.Combine", Opts("Win.Combine.Always", "Win.Combine.Full", "Win.Combine.Never"), Math.Clamp(cur.TaskbarCombine ?? 0, 0, 2), i => Do(() => svc.Engine.Apply(new Look { TaskbarCombine = i })), 220);
            Bt(cr, "Win.RestartExplorer", async () =>
            {
                if (MessageBox.Show(this, T("Win.RestartExplorerAsk"), T("Win.RestartExplorer"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                try { await WindowsSystemSettings.RestartExplorerAsync(); }
                catch (Exception e) { Fail(e); }
            }, 190);

            // ---- wallpaper slideshow (static pictures; the animated wallpaper library is on the Wallpaper page)
            var slide = AddCardSection(panel, T("Win.SecSlide"));
            var sl = s.Slideshow;
            bool PickFolder()
            {
                using var dlg = new FolderBrowserDialog { Description = T("Win.SlideFolder"), UseDescriptionForTitle = true, SelectedPath = sl.Folder };
                if (dlg.ShowDialog(this) != DialogResult.OK) return false;
                sl.Folder = dlg.SelectedPath; svc.SaveSettings(); return true;
            }
            Tg(slide, "Win.SlideOn", sl.Enabled, v =>
            {
                if (v)
                {
                    if (WallpaperSlideshow.Images(sl.Folder).Count == 0) { _statusLabel.Text = T("Win.SlideNone"); Rebuild(); return; }   // choose a folder first (no dialog pops up by itself)
                    svc.Backup(); sl.Enabled = true; sl.LastChangeUtc = null; svc.SaveSettings();
                }
                else svc.StopSlideshow(restoreWallpaper: true);
                Rebuild();
            });
            Tg(slide, "Win.SlideShuffle", sl.Shuffle, v => { sl.Shuffle = v; svc.SaveSettings(); });
            var slr = Row(slide);
            Bt(slr, "Win.SlideFolder", () => { if (PickFolder()) Rebuild(); }, 150);
            Nu(slr, "Win.SlideEvery", 1, 1440, sl.IntervalMinutes, v => { sl.IntervalMinutes = v; svc.SaveSettings(); });
            Cb(slr, "Win.SlideStyle", Opts("Win.Fit.Fill", "Win.Fit.Fit", "Win.Fit.Stretch", "Win.Fit.Tile", "Win.Fit.Center", "Win.Fit.Span"), (int)sl.Style, i => { sl.Style = (WallpaperStyle)i; svc.SaveSettings(); }, 130);
            Bt(slr, "Win.SlideNext", () => { svc.Backup(); if (!svc.SlideshowTick(DateTime.UtcNow, force: true)) MessageBox.Show(this, T("Win.SlideNone"), T("Win.SecSlide")); }, 150);
            AddText(slide, string.IsNullOrWhiteSpace(sl.Folder) ? T("Win.SlideNone") : $"{sl.Folder}  ·  {WallpaperSlideshow.Images(sl.Folder).Count}");
            ResetButton(slide, 4);

            // ---- themes: full themes (.opsistheme) and the Windows .theme files in one place
            var th = AddCardSection(panel, T("Win.SecThemes"));
            var items = svc.Themes.List();
            var list = new ListBox { Width = 440, Height = 150, IntegralHeight = false, BackColor = UiTheme.PageBackground, ForeColor = UiTheme.TextPrimary, BorderStyle = BorderStyle.None, Margin = new Padding(0, 2, 0, 6) };
            foreach (var it in items) list.Items.Add(it.Name);
            if (items.Count > 0) list.SelectedIndex = 0;
            th.Controls.Add(list);
            MotionDesk.Appearance.OpsisTheme? Sel() => list.SelectedIndex >= 0 && list.SelectedIndex < items.Count ? items[list.SelectedIndex] : null;
            var tr1 = Row(th);
            Bt(tr1, "Win.ThemeApply", () =>
            {
                var t = Sel(); if (t == null) return;
                try { var r = svc.ApplyTheme(t); if (r.Errors.Count > 0) MessageBox.Show(this, string.Join(Environment.NewLine, r.Errors), T("Win.SecThemes"), MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                catch (Exception e) { Fail(e); }
                Rebuild();
            }, 130);
            Bt(tr1, "Win.ThemeSave", () =>
            {
                string? name = PromptForText(T("Win.ThemeName"), T("Win.ThemeNameDefault"));
                if (string.IsNullOrWhiteSpace(name)) return;
                bool withWall = MessageBox.Show(this, T("Win.ThemeWallpaperAsk"), T("Win.SecThemes"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
                try { svc.Backup(); var (t, wall) = svc.Applier.CaptureCurrent(name.Trim(), s.Effects, withWall); svc.Themes.Save(t, wall); } catch (Exception e) { Fail(e); }
                Rebuild();
            }, 240);
            Bt(tr1, "Win.ThemeDelete", () =>
            {
                var t = Sel(); if (t == null || t.BuiltIn) return;
                if (MessageBox.Show(this, string.Format(T("Win.ThemeDeleteAsk"), t.Name), T("Win.SecThemes"), MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
                try { svc.Themes.Delete(t.Name); } catch (Exception e) { Fail(e); }
                Rebuild();
            }, 110);
            var tr2 = Row(th);
            Bt(tr2, "Win.ThemeImport", () =>
            {
                using var dlg = new OpenFileDialog { Filter = "Opsis theme (*.opsistheme)|*.opsistheme|Zip (*.zip)|*.zip", Title = T("Win.ThemeImport") };
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try { svc.Themes.Import(dlg.FileName); } catch (Exception e) { MessageBox.Show(this, T("Win.ThemeInvalid") + " " + e.Message, T("Win.SecThemes"), MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                Rebuild();
            }, 220);
            Bt(tr2, "Win.ThemeExport", () =>
            {
                var t = Sel(); if (t == null) return;
                using var dlg = new SaveFileDialog { Filter = "Opsis theme (*.opsistheme)|*.opsistheme", FileName = t.Name + ".opsistheme", Title = T("Win.ThemeExport") };
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try { svc.Themes.Export(t, dlg.FileName); } catch (Exception e) { Fail(e); }
            }, 130);
            Bt(tr2, "Win.OpenFolder", () => { try { Directory.CreateDirectory(svc.Themes.Root); System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(svc.Themes.Root) { UseShellExecute = true }); } catch (Exception) { } }, 150);
            th.Controls.Add(new Label { Text = T("Win.WinThemes"), AutoSize = true, ForeColor = UiTheme.AccentCyan, Font = UiTheme.FontBody, Margin = new Padding(0, 12, 0, 4) });
            AddText(th, T("Win.WinThemeNote"));
            // Windows .theme files (wallpaper + cursors + sounds): the single list of Opsis, with the signature check of the visual style before one is applied
            var winThemes = ThemePackageEngine.ListAvailableThemes();
            if (winThemes.Count > 0)
            {
                var wr = Row(th);
                var wc = new FlatComboBox { Width = 300, Margin = new Padding(0, 4, 8, 0) };
                wc.SetItems(winThemes.Select(x => x.Name + (x.BuiltIn ? T("Personalization.BuiltInSuffix") : "")).ToArray(), winThemes[0].Name + (winThemes[0].BuiltIn ? T("Personalization.BuiltInSuffix") : ""));
                wr.Controls.Add(wc);
                Bt(wr, "Win.ThemeApply", () =>
                {
                    int i = wc.SelectedIndex; if (i < 0 || i >= winThemes.Count) return;
                    var safety = ThemeSafety.Check(winThemes[i].Path);
                    if (safety.NeedsWarning && MessageBox.Show(this, ThemeSafety.Explain(safety, LocalizationManager.CurrentLanguage == "el"), T("Win.WinThemes"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
                    try { ThemePackageEngine.ApplyTheme(winThemes[i].Path); _statusLabel.Text = T("Personalization.ThemeAppliedStatus"); } catch (Exception e) { Fail(e); }
                }, 130);
            }
            var wr2 = Row(th);
            Bt(wr2, "Personalization.SaveCurrentTheme", () =>
            {
                string? name = PromptForText(T("Personalization.ThemeNamePrompt"), "Opsis Custom");
                if (string.IsNullOrWhiteSpace(name)) return;
                try { ThemePackageEngine.SaveCurrentAsTheme(name.Trim()); _statusLabel.Text = T("Personalization.ThemeSavedStatus"); } catch (Exception e) { Fail(e); }
                Rebuild();
            }, 280);
            Bt(wr2, "Personalization.CreateTechGridTheme", () =>
            {
                try { string png = ThemePackageEngine.GenerateTechWallpaperPng(); ThemePackageEngine.SaveCurrentAsTheme("Opsis Tech Grid", png); _statusLabel.Text = T("Personalization.TechGridThemeCreatedStatus"); } catch (Exception e) { Fail(e); }
                Rebuild();
            }, 280);

            // ---- safety and restore
            var safe = AddCardSection(panel, T("Win.SecSafety"));
            AddText(safe, T("Win.SafetyNote"));
            Tg(safe, "Win.RestorePoint", s.Safety.RestorePointBeforeInvasive, v => { s.Safety.RestorePointBeforeInvasive = v; svc.Store.Save(s); });
            var safeAdv = AdvBox(safe);
            var sr1 = Row(safeAdv);
            Bt(sr1, "Win.RestorePointNow", () =>
            {
                var r = svc.CreateRestorePointNow();
                MessageBox.Show(this, r.Message, T("Win.SecSafety"), MessageBoxButtons.OK, r.Status == RestorePointStatus.Created ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            }, 250);
            Bt(sr1, "Win.OpenBackups", () => { try { Directory.CreateDirectory(svc.Vault.Root); System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(svc.Vault.Root) { UseShellExecute = true }); } catch (Exception) { } }, 200);
            var sr2 = Row(safe);
            var restore = Bt(sr2, "Win.RestoreOriginal", () =>
            {
                if (!svc.Store.HasBackup) { MessageBox.Show(this, T("Win.NothingToRestore"), T("Win.SecSafety")); return; }
                try { svc.RestoreOriginal(); } catch (Exception e) { Fail(e); }
                Rebuild();
            }, 280);
            restore.Enabled = svc.Store.HasBackup;
            Bt(sr1, "Win.ImportCheck", () =>
            {
                var report = AppearanceMigration.Scan(AppPaths.AppDataFolder);
                string? saved = report.AnythingFound ? AppearanceMigration.Backup(AppPaths.AppDataFolder) : null;
                MessageBox.Show(this, report.AnythingFound ? AppearanceMigration.Describe(report) + (saved == null ? "" : Environment.NewLine + Environment.NewLine + string.Format(T("Win.ImportBackup"), saved)) : T("Win.ImportNothing"), T("Win.ImportCheck"));
            }, 300);

            if (_winAppearanceScroll > 0) { int keep = _winAppearanceScroll; _winAppearanceScroll = 0; BeginInvoke(new Action(() => { try { panel.VerticalScroll.Value = Math.Min(keep, panel.VerticalScroll.Maximum); panel.PerformLayout(); } catch (Exception) { } })); }
        }
    }
}
