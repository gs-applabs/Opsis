using MotionDesk.Services;

namespace MotionDesk.UI;

/// <summary>Shared settings dialog for Opsis and the Windows SCR /c entrypoint.</summary>
internal sealed class OpsisScreensaverSettingsForm : Form
{
    internal OpsisScreensaverSettingsForm()
    {
        Text = "Opsis — Screensaver Studio";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(620, 340);
        MinimumSize = new Size(480, 300);
        var body = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(12), AutoScroll = true };
        var mode = new ComboBox { Width = 220, DropDownStyle = ComboBoxStyle.DropDownList };
        mode.Items.AddRange(new object[] { "Clock", "StaticImage", "ClockAndImage" });
        var image = new TextBox { Width = 450, ReadOnly = true };
        var choose = new Button { AutoSize = true, Text = "Choose image / Επιλογή εικόνας" };
        var dim = new NumericUpDown { Minimum = 0, Maximum = 75, Increment = 5, Width = 80 };
        var seconds = new CheckBox { AutoSize = true, Text = "Show seconds / Δευτερόλεπτα" };
        var status = new Label { Width = 560, Height = 42, AutoEllipsis = true };
        OpsisScreensaverStudio.Profile p;
        try { p = OpsisScreensaverStudio.Read(); }
        catch (Exception ex) { p = new(); status.Text = ex.Message; }
        mode.SelectedItem = p.Mode; image.Text = p.ImagePath; dim.Value = p.DimPercent; seconds.Checked = p.ShowSeconds;
        OpsisScreensaverStudio.Profile Draft() => new() { Mode = mode.SelectedItem as string ?? "Clock", ImagePath = image.Text,
            DimPercent = (int)dim.Value, ShowSeconds = seconds.Checked };
        void Run(Action operation) { try { operation(); } catch (Exception ex) { status.Text = ex.Message; } }
        choose.Click += (_, _) =>
        {
            using var picker = new OpenFileDialog { Filter = "Images (*.jpg;*.jpeg;*.png;*.bmp)|*.jpg;*.jpeg;*.png;*.bmp" };
            if (picker.ShowDialog(this) == DialogResult.OK) image.Text = picker.FileName;
        };
        body.Controls.Add(new Label { AutoSize = true, Text = "Scene / Σκηνή" }); body.Controls.Add(mode);
        body.Controls.Add(image); body.Controls.Add(choose);
        var appearance = new FlowLayoutPanel { AutoSize = true };
        appearance.Controls.Add(new Label { AutoSize = true, Text = "Dim % / Σκοτείνιασμα" });
        appearance.Controls.Add(dim); appearance.Controls.Add(seconds); body.Controls.Add(appearance);
        var actions = new FlowLayoutPanel { Width = 560, AutoSize = true, WrapContents = true };
        void Button(string caption, Action action, bool enabled = true)
        {
            var b = new Button { AutoSize = true, Text = caption, Enabled = enabled };
            b.Click += (_, _) => Run(action); actions.Controls.Add(b);
        }
        Button("Save / Αποθήκευση", () => { OpsisScreensaverStudio.Save(Draft()); status.Text = "Scene saved / Αποθηκεύτηκε."; });
        Button("Preview / Προεπισκόπηση", () => { using var f = new OpsisScreensaverPreviewForm(Draft(), IntPtr.Zero, true); f.ShowDialog(this); });
        Button("Select Opsis .scr / Επιλογή", () =>
        {
            if (MessageBox.Show(this, "Select the installed Opsis screensaver? Windows timeout, active state and secure resume are not changed.", Text,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            OpsisScreensaverStudio.Save(Draft());
            OpsisScreensaverHost.SelectInstalled(); status.Text = "Selected; use Windows settings for activation and timeout.";
        }, OpsisScreensaverHost.IsInstalled);
        Button("Restore original / Επαναφορά", () =>
        {
            if (MessageBox.Show(this, "Restore the original Windows screensaver selection?", Text, MessageBoxButtons.YesNo) == DialogResult.Yes)
            { OpsisScreensaverHost.RestoreSelection(); status.Text = "Original selection restored."; }
        });
        Button("Windows settings / Ρυθμίσεις", DesktopEffects.OpenScreenSaverSettings);
        body.Controls.Add(actions); body.Controls.Add(status); Controls.Add(body);
        if (!OpsisScreensaverHost.IsInstalled) status.Text = "Opsis.scr is not installed; preview and scene editing remain available.";
    }
}
