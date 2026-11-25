using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using MotionDesk.Services;

namespace MotionDesk.UI;

internal sealed class NativeStartSettingsForm : Form
{
    private readonly Dictionary<string, ComboBox> _options = new();
    private readonly Button _apply = new() { AutoSize = true, Text = "Apply / Εφαρμογή" };
    private readonly Button _restore = new() { AutoSize = true, Text = "Restore original / Επαναφορά" };

    public NativeStartSettingsForm()
    {
        Text = "Opsis — Native Windows Start";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(680, 450);
        MinimumSize = new Size(520, 390);
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, AutoScroll = true, Padding = new Padding(12) };
        Controls.Add(panel);
        panel.Controls.Add(new Label { AutoSize = false, Width = 470, Height = 65,
            Text = "Native Windows settings / Εγγενείς ρυθμίσεις Windows 11.\r\nChanges may require reopening Start or signing out; some builds or policies ignore these settings." });
        Add(panel, "Start_Layout", "Layout / Διάταξη", "Default", "More pins", "More recommendations");
        Add(panel, "ShowAllPinsList", "Show all pins / Όλα τα καρφιτσωμένα", "Off", "On");
        Add(panel, "Start_IrisRecommendations", "Tips and app recommendations / Προτάσεις", "Off", "On");
        var actions = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Width = 470 };
        actions.Controls.Add(_apply); actions.Controls.Add(_restore);
        var settings = new Button { AutoSize = true, Text = "Windows Start settings" };
        var folders = new Button { AutoSize = true, Text = "Start folders / Φάκελοι" };
        actions.Controls.Add(settings); actions.Controls.Add(folders); panel.Controls.Add(actions);
        settings.Click += (_, _) => Run(() => NativeStartPreferences.OpenWindowsSettings());
        folders.Click += (_, _) => Run(() => NativeStartPreferences.OpenWindowsSettings(true));
        _apply.Click += (_, _) =>
        {
            if (MessageBox.Show(this, "Apply selected current-user Windows Start settings?", Text,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            Run(() =>
            {
                var changes = new Dictionary<string, int>();
                foreach (var pair in _options)
                    if (pair.Value.SelectedIndex > 0) changes[pair.Key] = pair.Value.SelectedIndex - 1;
                NativeStartPreferences.Apply(changes);
                Reload();
                MessageBox.Show(this, "Settings written. Reopen Windows Start to verify.", Text);
            });
        };
        _restore.Click += (_, _) =>
        {
            if (MessageBox.Show(this, "Restore the original Start preferences?", Text,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            Run(() => { NativeStartPreferences.Restore(); Reload(); });
        };
        Shown += (_, _) => Run(Reload);
        _apply.Enabled = NativeStartPreferences.IsSupported;
        _restore.Enabled = false;
    }

    private void Add(FlowLayoutPanel panel, string id, string label, params string[] values)
    {
        panel.Controls.Add(new Label { Text = label, AutoSize = true });
        var combo = new ComboBox { Width = 340, DropDownStyle = ComboBoxStyle.DropDownList };
        combo.Items.Add("Not set / Unchanged");
        combo.Items.AddRange(values);
        combo.SelectedIndex = 0;
        panel.Controls.Add(combo);
        _options.Add(id, combo);
    }

    private void Reload()
    {
        if (!NativeStartPreferences.IsSupported) return;
        var values = NativeStartPreferences.Read();
        foreach (var pair in _options) pair.Value.SelectedIndex = values[pair.Key] is int value ? value + 1 : 0;
        _restore.Enabled = NativeStartPreferences.HasBackup;
    }

    private void Run(Action action)
    {
        try { action(); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }
}
