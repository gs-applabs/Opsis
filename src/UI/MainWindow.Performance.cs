using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using MotionDesk.Services;
using UiTheme = MotionDesk.UI.UiTheme;

namespace MotionDesk.UI
{
    public sealed partial class MainWindow
    {
        // Performance: dynamic scaling switch and an optional tier for each part of Opsis (Auto = follows the detected tier and the dynamic scaling).
        private static void BuildPerformanceRows(Control panel)
        {
            panel.Controls.Add(SettingsView.Checkbox(LocalizationManager.T("Perf.Dynamic"), AppSettings.Load().PerfDynamic,
                v => { var a = AppSettings.Load(); a.PerfDynamic = v; a.Save(); PerformanceGovernor.RaiseChanged(); },
                LocalizationManager.T("Perf.DynamicDesc")));
            if (!PerformanceGovernor.SessionSamplingAllowed)
                AddText(panel, StudioText("Adaptive sampling suspended for this session; the current reduction is retained while the saved adaptive profile is eligible.", "Η προσαρμοστική δειγματοληψία είναι σε αναστολή· διατηρείται η τρέχουσα μείωση ποιότητας όσο ισχύει το αποθηκευμένο προσαρμοστικό προφίλ."));
            AddText(panel, LocalizationManager.T("Perf.PerFeature"));
            string[] labels =
            {
                OpsisStrings.T("ui.auto"), OpsisStrings.T("ui.low"), OpsisStrings.T("ui.balanced"), OpsisStrings.T("ui.high")
            };
            foreach (var feature in new[] { PerfFeature.Wallpaper, PerfFeature.Widgets, PerfFeature.DeskStrip, PerfFeature.Effects })
            {
                var f = feature;
                var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 6) };
                row.Controls.Add(new Label { Text = LocalizationManager.T("Perf.Feature." + f), AutoSize = false, Width = 230, Height = 28, TextAlign = ContentAlignment.MiddleLeft, Font = UiTheme.FontBody, ForeColor = UiTheme.TextSecondary });
                var combo = new Opsis.UI.FlatComboBox { Width = 180 };
                var current = PerformanceProfile.FeatureOverride(f);
                int idx = current == null ? 0 : (int)current.Value + 1;
                combo.SetItems(labels, labels[idx]);
                combo.SelectedIndexChanged += (_, _) =>
                {
                    int i = Math.Max(0, combo.SelectedIndex);
                    ApplySetting(LocalizationManager.T("Perf.Feature." + f), () => PerformanceProfile.SetFeatureOverride(f, i == 0 ? null : (HardwareTier)(i - 1)));
                };
                row.Controls.Add(combo);
                panel.Controls.Add(row);
            }

#if SIGNATURE_COMPOSITION_PREVIEW
            var glassDiagnostics = new Opsis.UI.FlatButton
            {
                Text = StudioText("Signature Glass diagnostics", "Διαγνωστικά Signature Glass"),
                Width = 480, Height = 38, Margin = new Padding(0, 10, 0, 5)
            };
            glassDiagnostics.Click += (_, _) =>
            {
                var d = GlassPaneBinder.SignatureDiagnostics;
                var report = d == null ? StudioText("No renderer created on this UI thread.", "Δεν έχει δημιουργηθεί renderer σε αυτό το νήμα UI.") :
                    $"Ready: {d.Ready}\nHardware acceleration: {d.HardwareAccelerated}\nAdaptive tier: {d.AdaptiveTier}\nPanes: {d.PaneCount}\nSurface pixels: {d.SurfacePixels:N0}\nBackdrop pixels: {d.BackdropPixels:N0}\nDraw submissions: {d.SuccessfulRenders:N0}\nRejected draws: {d.RejectedRenders:N0}\nSubmission time: {d.SubmissionMilliseconds:F2} ms\nDXGI local memory: {d.LocalVideoMemoryUsage / 1048576.0:F1} / {d.LocalVideoMemoryBudget / 1048576.0:F1} MiB\nHRESULT: 0x{d.LastHResult:X8}\n\nCPU submission time is not GPU utilization. Local memory budget may be unavailable.";
                MessageBox.Show(panel.FindForm(), report, "Signature Glass", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            panel.Controls.Add(glassDiagnostics);
#endif

            // Roadmap #19/#100: explicit, time-limited LOCAL diagnostics. The saved
            // JSON contains counters only, never window titles or file paths.
            var diagnostics = new Opsis.UI.FlatButton
            {
                Text = MainWindow.StudioText("Record 60-second local diagnostic session…", "Καταγραφή τοπικών διαγνωστικών για 60 δευτερόλεπτα…"),
                Width = 480, Height = 38, Margin = new Padding(0, 14, 0, 5)
            };
            diagnostics.Click += (_, _) =>
            {
                if (DiagnosticSessionService.Recording)
                {
                    var answer = MessageBox.Show(panel.FindForm(),
                        MainWindow.StudioText("A session is recording. Cancel it?", "Υπάρχει ενεργή καταγραφή. Να ακυρωθεί;"),
                        "Opsis", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (answer == DialogResult.Yes) DiagnosticSessionService.Cancel();
                    return;
                }
                var owner = panel.FindForm();
                if (owner == null || owner.IsDisposed || !owner.IsHandleCreated) return;
                void Preview(string json)
                {
                    if (owner.IsDisposed || !owner.IsHandleCreated) return;
                    try { owner.BeginInvoke((Action)(() =>
                    {
                        if (owner.IsDisposed) return;
                        using var dialog = new Form
                        {
                            Text = StudioText("Private diagnostic preview", "Προεπισκόπηση ιδιωτικών διαγνωστικών"),
                            StartPosition = FormStartPosition.CenterParent, Width = 850, Height = 620,
                            MinimumSize = new Size(480, 340), BackColor = UiTheme.PageBackground
                        };
                        var tabs = new TabControl { Dock = DockStyle.Fill };
                        var reportTab = new TabPage("JSON");
                        reportTab.Controls.Add(new TextBox { Multiline = true, ReadOnly = true, WordWrap = false, ScrollBars = ScrollBars.Both,
                            Dock = DockStyle.Fill, Text = json, BackColor = UiTheme.PageBackground, ForeColor = UiTheme.TextPrimary });
                        tabs.TabPages.Add(reportTab);
                        var timelineTab = new TabPage(StudioText("CPU timeline", "Χρονογραμμή CPU"));
                        var timeline = new ListBox { Dock = DockStyle.Fill, HorizontalScrollbar = true };
                        using (var doc = System.Text.Json.JsonDocument.Parse(json))
                        {
                            var rows = new System.Collections.Generic.List<(double Time, string Text)>();
                            foreach (var e in doc.RootElement.GetProperty("activityCategories").EnumerateArray())
                                rows.Add((e.GetProperty("ElapsedSeconds").GetDouble(), e.GetProperty("Kind").GetString() ?? ""));
                            foreach (var spike in doc.RootElement.GetProperty("cpuSpikes").EnumerateArray())
                            {
                                string related = spike.GetProperty("RelatedCategory").GetString() ?? "—";
                                rows.Add((spike.GetProperty("ElapsedSeconds").GetDouble(),
                                    $"CPU: {spike.GetProperty("MainCpuCorePercent").GetDouble():0.0}% · WebView2: {spike.GetProperty("WebViewCpuCorePercent").GetDouble():0.0}% · {related}"));
                            }
                            foreach (var row in rows.OrderBy(x => x.Time)) timeline.Items.Add($"{row.Time:0.0}s  {row.Text}");
                        }
                        timelineTab.Controls.Add(timeline); tabs.TabPages.Add(timelineTab);
                        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, FlowDirection = FlowDirection.RightToLeft };
                        var discard = new Button { Text = StudioText("Discard", "Απόρριψη"), AutoSize = true, DialogResult = DialogResult.Cancel };
                        var export = new Button { Text = StudioText("Export reviewed JSON…", "Εξαγωγή ελεγμένου JSON…"), AutoSize = true };
                        export.Click += (_, _) =>
                        {
                            using var save = new SaveFileDialog { Filter = "JSON (*.json)|*.json", DefaultExt = "json", AddExtension = true,
                                FileName = "opsis-resources-redacted.json" };
                            if (save.ShowDialog(dialog) != DialogResult.OK) return;
                            try { AtomicFile.WriteAllText(save.FileName, json); dialog.DialogResult = DialogResult.OK; }
                            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
                            { MessageBox.Show(dialog, ex.Message, "Opsis", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                        };
                        buttons.Controls.Add(discard); buttons.Controls.Add(export);
                        dialog.Controls.Add(tabs); dialog.Controls.Add(buttons); dialog.CancelButton = discard;
                        dialog.ShowDialog(owner);
                    })); } catch (InvalidOperationException) { }
                }
                Action<string?, string?>? onFinished = null;
                onFinished = (result, error) =>
                {
                    DiagnosticSessionService.Finished -= onFinished;
                    if (error == null || owner.IsDisposed || !owner.IsHandleCreated) return;
                    try
                    {
                        owner.BeginInvoke((Action)(() =>
                        {
                            if (owner.IsDisposed) return;
                            MessageBox.Show(owner, error == "cancelled"
                                ? MainWindow.StudioText("Diagnostic recording cancelled.", "Η καταγραφή ακυρώθηκε.")
                                : error,
                                "Opsis", MessageBoxButtons.OK,
                                error != null && error != "cancelled" ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
                        }));
                    }
                    catch (InvalidOperationException) { }
                };
                DiagnosticSessionService.Finished += onFinished;
                try
                {
                    if (!DiagnosticSessionService.StartPreview(TimeSpan.FromSeconds(60), Preview))
                    {
                        DiagnosticSessionService.Finished -= onFinished;
                        return;
                    }
                    MessageBox.Show(panel.FindForm(), MainWindow.StudioText(
                        "Recording started for one minute. Review the report before deciding whether to save it.",
                        "Η καταγραφή ξεκίνησε για ένα λεπτό. Εξέτασε την αναφορά πριν επιλέξεις αν θα την αποθηκεύσεις."),
                        "Opsis", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
                {
                    DiagnosticSessionService.Finished -= onFinished;
                    MessageBox.Show(panel.FindForm(), ex.Message, "Opsis", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            };
            panel.Controls.Add(diagnostics);
            AddText(panel, MainWindow.StudioText(
                "Opt-in; automatic stop, no network, no screenshots, no names or paths. Review the report before sharing.",
                "Προαιρετικό· αυτόματος τερματισμός, χωρίς δίκτυο, εικόνες, ονόματα ή διαδρομές. Έλεγξε την αναφορά πριν από κοινοποίηση."));
        }
    }
}
