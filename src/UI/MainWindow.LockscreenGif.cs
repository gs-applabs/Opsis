using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using MotionDesk.Services;
using Opsis.Lockscreen;
using UiTheme = MotionDesk.UI.UiTheme;

namespace MotionDesk.UI
{
    // Personalization page, "Lock Screen Background" card: animated GIF lock screen (opt-in, backed up, restorable; elevated work is done by OpsisLockHelper.exe).
    public sealed partial class MainWindow
    {
        private static string LG(string key) => LocalizationManager.T("LockGif." + key);

        private void BuildAnimatedLockScreen(FlowLayoutPanel card)
        {
            AddText(card, LG("Intro"));
            string gifPath = "";
            bool busy = false;

            Label Line(string text = "") { var l = new Label { Text = text, AutoSize = true, MaximumSize = new Size(700, 0), Font = UiTheme.FontBody, ForeColor = UiTheme.TextSecondary, Margin = new Padding(0, 0, 0, 4) }; card.Controls.Add(l); return l; }
            HoverButton Btn(string text, EventHandler click) { var b = MediaButton(text); b.Click += click; return b; }
            FlowLayoutPanel Row() { var r = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = new Padding(0, 2, 0, 6), MaximumSize = new Size(720, 0) }; card.Controls.Add(r); return r; }

            var panel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0) };
            var settings = AppSettings.Load();

            var consent = new FlatToggle { RowWidth = 600, Text = LG("Enable"), Checked = settings.LockscreenGifConsent, BackColor = UiTheme.PageBackground, Margin = new Padding(0, 2, 8, 6) };
            card.Controls.Add(consent);
            card.Controls.Add(panel);

            // everything below is created only for the panel; it is shown only while the opt-in is on
            var prereq = new Label { AutoSize = true, MaximumSize = new Size(700, 0), Font = UiTheme.FontBody, ForeColor = UiTheme.TextSecondary, Margin = new Padding(0, 0, 0, 6) };
            var restoreInfo = new Label { AutoSize = true, MaximumSize = new Size(700, 0), Font = UiTheme.FontBody, ForeColor = UiTheme.TextSecondary, Margin = new Padding(0, 0, 0, 6) };
            var preview = new PictureBox { Width = 320, Height = 180, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Black, Margin = new Padding(0, 4, 0, 6) };
            var gifLabel = new Label { AutoSize = true, MaximumSize = new Size(700, 0), Font = UiTheme.FontBody, ForeColor = UiTheme.TextSecondary, Margin = new Padding(0, 0, 0, 4) };
            var ffLabel = new Label { AutoSize = true, MaximumSize = new Size(700, 0), Font = UiTheme.FontBody, ForeColor = UiTheme.TextSecondary, Margin = new Padding(0, 0, 0, 4), Text = string.IsNullOrEmpty(settings.FfmpegPath) ? LG("FfmpegNeed") : settings.FfmpegPath };

            HoverButton? bDisable = null, bApply = null, bSave = null, bRestore = null, bConvert = null;

            void Status(string key) { if (!IsDisposed) _statusLabel.Text = LG(key); }
            void SetBusy(bool b) { busy = b; foreach (var x in new[] { bDisable, bApply, bSave, bRestore, bConvert }) if (x != null) x.Enabled = !b; }

            string Mark(bool? ok) => ok == true ? "[OK] " : ok == false ? "[!] " : "[?] ";
            void Refresh()
            {
                var st = LockscreenGifService.GetStatus();
                bool? pic = st.Mode == LockScreenMode.PictureOrOther ? true : st.Mode == LockScreenMode.Unknown ? null : false;
                string feature = !st.FeatureApplies ? "[-] " + LG("FeatureNA") : Mark(st.FeatureDisabled) + LG(st.FeatureDisabled == true ? "FeatureOff" : st.FeatureDisabled == false ? "FeatureOn" : "FeatureUnknown");
                prereq.Text = LG("PrereqTitle") + "\n" + Mark(pic) + LG("PrereqPicture") + "\n" + Mark(st.CachePresent) + LG("PrereqCache") + "\n" + feature + (st.HelperPresent ? "" : "\n[!] " + LG("NoHelper"));
                restoreInfo.Text = st.HasBackup ? LG("HasBackup") : "";
                if (bRestore != null) { bRestore.Visible = st.HasBackup; }
                if (bDisable != null) bDisable.Visible = st.FeatureApplies && st.FeatureDisabled != true;
            }

            void LoadPreview(string path)
            {
                try
                {
                    var old = preview.Image; var oldTag = preview.Tag as IDisposable;
                    var ms = new MemoryStream(File.ReadAllBytes(path));
                    preview.Image = Image.FromStream(ms); preview.Tag = ms;     // the stream stays open while the animated image is shown
                    old?.Dispose(); oldTag?.Dispose();
                }
                catch (Exception) { }
            }
            void SetGif(string path) { gifPath = path; gifLabel.Text = path; LoadPreview(path); if (bApply != null) bApply.Enabled = !busy; if (bSave != null) bSave.Enabled = !busy; }

            void Add(Control c) => panel.Controls.Add(c);
            Add(prereq);
            var r1 = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = new Padding(0, 0, 0, 6), MaximumSize = new Size(720, 0) };
            r1.Controls.Add(Btn(LG("Refresh"), (_, _) => Refresh()));
            bDisable = Btn(LG("DisableFeature"), async (_, _) =>
            {
                if (busy) return; SetBusy(true);
                try
                {
                    var r = await LockscreenGifService.RunHelperAsync("disable-feature");
                    if (IsDisposed) return;
                    if (r.Ok) { Status("FeatureDone"); AskReboot(); } else _statusLabel.Text = r.Cancelled ? LG("Cancelled") : LG("Failed") + " " + r.Message;
                }
                finally { SetBusy(false); Refresh(); }
            });
            r1.Controls.Add(bDisable);
            Add(r1); Add(restoreInfo);

            var r2 = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = new Padding(0, 0, 0, 2), MaximumSize = new Size(720, 0) };
            r2.Controls.Add(Btn(LG("ChooseGif"), (_, _) =>
            {
                using var dlg = new OpenFileDialog { Filter = "GIF|*.gif", Title = LG("ChooseGif") };
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try { LockscreenGifService.Stage(dlg.FileName); SetGif(dlg.FileName); }
                catch (Exception ex) { _statusLabel.Text = LG("NotGif") + " " + ex.Message; }
            }));
            bApply = Btn(LG("Apply"), async (_, _) =>
            {
                if (busy) return;
                if (string.IsNullOrEmpty(gifPath)) { Status("NeedGif"); return; }
                if (!AppSettings.Load().LockscreenGifConsent) return;
                SetBusy(true);
                try
                {
                    LockscreenGifService.Stage(gifPath);
                    var r = await LockscreenGifService.RunHelperAsync("apply");
                    if (IsDisposed) return;
                    _statusLabel.Text = r.Ok ? LG("Applied") : r.Cancelled ? LG("Cancelled") : LG("Failed") + " " + r.Message;
                }
                catch (Exception ex) { _statusLabel.Text = LG("Failed") + " " + ex.Message; }
                finally { SetBusy(false); Refresh(); }
            });
            r2.Controls.Add(bApply);
            bSave = Btn(LG("SaveGif"), (_, _) =>
            {
                if (string.IsNullOrEmpty(gifPath) || !File.Exists(gifPath)) { Status("NeedGif"); return; }
                using var dlg = new SaveFileDialog { Filter = "GIF|*.gif", FileName = "lockscreen.gif" };
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try { File.Copy(gifPath, dlg.FileName, true); } catch (Exception ex) { _statusLabel.Text = LG("Failed") + " " + ex.Message; }
            });
            r2.Controls.Add(bSave);
            bRestore = Btn(LG("Restore"), async (_, _) =>
            {
                if (busy) return; SetBusy(true);
                try
                {
                    var r = await LockscreenGifService.RunHelperAsync("restore");
                    if (IsDisposed) return;
                    _statusLabel.Text = r.Ok ? LG("RestoreDone") : r.Cancelled ? LG("Cancelled") : LG("Failed") + " " + r.Message;
                }
                finally { SetBusy(false); Refresh(); }
            });
            r2.Controls.Add(bRestore);
            Add(r2); Add(gifLabel); Add(preview);

            // video -> GIF (FFmpeg only; the user picks an existing ffmpeg.exe, nothing is bundled or downloaded)
            Add(new Label { Text = LG("VideoTitle"), AutoSize = true, Font = UiTheme.FontBody, ForeColor = UiTheme.TextPrimary, Margin = new Padding(0, 8, 0, 4) });
            string video = "";
            var vLabel = new Label { AutoSize = true, MaximumSize = new Size(700, 0), Font = UiTheme.FontBody, ForeColor = UiTheme.TextSecondary, Margin = new Padding(0, 0, 0, 4) };
            var r3 = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = new Padding(0, 0, 0, 4), MaximumSize = new Size(720, 0) };
            r3.Controls.Add(Btn(LG("ChooseFfmpeg"), async (_, _) =>
            {
                using var dlg = new OpenFileDialog { Filter = "ffmpeg.exe|ffmpeg.exe", Title = LG("ChooseFfmpeg") };
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                if (!await LockscreenGifService.IsFfmpegAsync(dlg.FileName)) { Status("FfmpegBad"); return; }
                var a = AppSettings.Load(); a.FfmpegPath = dlg.FileName; a.Save(); ffLabel.Text = dlg.FileName;
            }));
            r3.Controls.Add(Btn(LG("ChooseVideo"), (_, _) =>
            {
                using var dlg = new OpenFileDialog { Filter = "Video|*.mp4;*.mkv;*.mov;*.webm;*.avi;*.wmv;*.m4v|*.*|*.*" };
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                video = dlg.FileName; vLabel.Text = video;
            }));
            Add(ffLabel); Add(r3); Add(vLabel);
            NumericUpDown Num(decimal v, decimal min, decimal max, int dec) => new NumericUpDown { Minimum = min, Maximum = max, Value = v, DecimalPlaces = dec, Width = 70, Margin = new Padding(0, 2, 10, 0) };
            var start = Num(0, 0, 86400, 1); var dur = Num(5, 0.5m, FfmpegGif.MaxSeconds, 1); var width = Num(1280, 64, 3840, 0); var fps = Num(15, 1, 30, 0);
            var r4 = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = new Padding(0, 0, 0, 4), MaximumSize = new Size(720, 0) };
            void Lbl(string k) => r4.Controls.Add(new Label { Text = LG(k), AutoSize = true, Font = UiTheme.FontBody, ForeColor = UiTheme.TextSecondary, Margin = new Padding(0, 6, 4, 0) });
            Lbl("Start"); r4.Controls.Add(start); Lbl("Duration"); r4.Controls.Add(dur); Lbl("Width"); r4.Controls.Add(width); Lbl("Fps"); r4.Controls.Add(fps);
            Add(r4);
            bConvert = Btn(LG("Convert"), async (_, _) =>
            {
                if (busy) return;
                var ff = AppSettings.Load().FfmpegPath;
                if (!FfmpegGif.LooksLikeFfmpeg(ff)) { Status("FfmpegBad"); return; }
                if (string.IsNullOrEmpty(video) || !File.Exists(video)) { Status("NeedVideo"); return; }
                SetBusy(true); _statusLabel.Text = LG("Converting");
                try
                {
                    string outGif = Path.Combine(LockscreenGifService.DataRoot, "work", "video.gif");
                    var (ok, msg) = await LockscreenGifService.ConvertVideoAsync(ff, new FfmpegGif.Options(video, (double)start.Value, (double)dur.Value, (int)width.Value, (int)fps.Value), outGif);
                    if (IsDisposed) return;
                    if (ok) { try { LockscreenGifService.Stage(outGif); SetGif(outGif); Status("ConvertDone"); } catch (Exception ex) { _statusLabel.Text = LG("ConvertFailed") + " " + ex.Message; } }
                    else _statusLabel.Text = LG("ConvertFailed") + " " + msg;
                }
                finally { SetBusy(false); }
            });
            Add(bConvert);
            Add(Btn(LG("OpenLogs"), (_, _) =>
            {
                try { Process.Start(new ProcessStartInfo(Path.Combine(AppPaths.AppDataFolder, "logs")) { UseShellExecute = true }); } catch (Exception) { }
            }));

            bApply.Enabled = false; bSave.Enabled = false;
            panel.Visible = settings.LockscreenGifConsent;
            Refresh();

            consent.CheckedChanged += (_, _) =>
            {
                if (consent.Checked)
                {
                    var answer = MessageBox.Show(this, LG("ConfirmBody"), LG("ConfirmTitle"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                    if (answer != DialogResult.Yes) { consent.Checked = false; return; }
                }
                var a = AppSettings.Load(); a.LockscreenGifConsent = consent.Checked; a.Save();
                RotatingLogger.Write("Lockscreen GIF opt-in " + (consent.Checked ? "ON" : "OFF"));
                panel.Visible = consent.Checked; Refresh();
            };
        }

        // "Restart now" / "Later": a restart may be needed after the Windows feature was changed. Never automatic.
        private void AskReboot()
        {
            using var f = new Form { Text = LG("RebootTitle"), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false, ClientSize = new Size(460, 150), BackColor = UiTheme.PageBackground };
            f.Controls.Add(new Label { Text = LG("RebootBody"), Left = 16, Top = 14, Width = 428, Height = 80, ForeColor = UiTheme.TextPrimary, Font = UiTheme.FontBody });
            var now = new Button { Text = LG("RebootNow"), Left = 16, Top = 104, Width = 200, Height = 32, DialogResult = DialogResult.Yes };
            var later = new Button { Text = LG("RebootLater"), Left = 244, Top = 104, Width = 200, Height = 32, DialogResult = DialogResult.No };
            f.Controls.Add(now); f.Controls.Add(later); f.AcceptButton = later; f.CancelButton = later;
            if (f.ShowDialog(this) != DialogResult.Yes) return;
            try
            {
                var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "shutdown.exe")) { UseShellExecute = false, CreateNoWindow = true };
                psi.ArgumentList.Add("/r"); psi.ArgumentList.Add("/t"); psi.ArgumentList.Add("0");      // no /f: applications can still ask to save
                RotatingLogger.Write("Lockscreen GIF: restart requested by the user");
                Process.Start(psi);
            }
            catch (Exception ex) { RotatingLogger.WriteException("Restart request", ex); }
        }
    }
}
