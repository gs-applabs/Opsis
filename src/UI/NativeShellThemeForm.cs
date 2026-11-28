using System;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using MotionDesk.Services;

namespace MotionDesk.UI;

internal sealed class NativeShellThemeForm : Form
{
    private readonly ComboBox _target = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 420 };
    private readonly ComboBox _theme = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 360 };
    private readonly Label _status = new() { AutoSize = false, Width = 460, Height = 65 };
    private readonly Button _read = new() { AutoSize = true, Text = "Read current / Ανάγνωση" };
    private readonly Button _apply = new() { AutoSize = true, Text = "Apply / Εφαρμογή" };
    private readonly Button _restore = new() { AutoSize = true, Text = "Restore original / Επαναφορά" };
    private NativeShellThemeBridge.Target Selected => NativeShellThemeBridge.Targets[_target.SelectedIndex];

    public NativeShellThemeForm()
    {
        Text = "Opsis — Native Shell settings (optional Windhawk provider)";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(560, 365);
        MinimumSize = new Size(500, 350);
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, AutoScroll = true, Padding = new Padding(12) };
        Controls.Add(panel);
        panel.Controls.Add(new Label { AutoSize = false, Width = 460, Height = 76,
            Text = "Experimental external-provider adapter. Requires compatible Windhawk CLI and the corresponding installed, enabled mod. No mod is installed or enabled automatically. Native appearance changes are performed by Windhawk, not an Opsis hook." });
        _target.Items.AddRange(NativeShellThemeBridge.Targets.Select(x => (object)x.Label).ToArray());
        panel.Controls.Add(_target); panel.Controls.Add(_theme);
        var buttons = new FlowLayoutPanel { AutoSize = true, Width = 460, WrapContents = true };
        buttons.Controls.Add(_read); buttons.Controls.Add(_apply); buttons.Controls.Add(_restore); panel.Controls.Add(buttons);
        panel.Controls.Add(_status);
        _target.SelectedIndexChanged += (_, _) =>
        {
            _theme.Items.Clear();
            foreach (var item in Selected.Themes) _theme.Items.Add(item == "" ? "(None)" : item);
            _theme.SelectedIndex = 0;
            _status.Text = "Read the currently installed theme before applying.";
        };
        _target.SelectedIndex = 0;
        FormClosing += (_, e) => { if (!_target.Enabled) e.Cancel = true; };
        _read.Click += async (_, _) => await Run(() => NativeShellThemeBridge.ReadTheme(Selected.Id));
        _apply.Click += async (_, _) =>
        {
            var target = Selected; var theme = target.Themes[_theme.SelectedIndex];
            if (MessageBox.Show(this, "Change only the selected setting in the existing Windhawk mod? Its original value is backed up; all other settings are preserved.", Text,
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            await Run(() => NativeShellThemeBridge.Apply(target.Id, theme));
        };
        _restore.Click += async (_, _) =>
        {
            var target = Selected;
            if (MessageBox.Show(this, "Restore the original theme for this mod?", Text, MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) == DialogResult.Yes)
                await Run(() => NativeShellThemeBridge.Restore(target.Id));
        };
        if (NativeShellThemeBridge.FindCli() == null)
        {
            _status.Text = "Compatible installed CLI not detected. Native style adapter is unavailable.";
            _read.Enabled = _apply.Enabled = _restore.Enabled = false;
        }
    }

    private async Task Run(Func<Task<string>> operation)
    {
        _read.Enabled = _apply.Enabled = _restore.Enabled = _target.Enabled = _theme.Enabled = false;
        try
        {
            var result = await operation();
            if (IsDisposed) return;
            _status.Text = "Theme setting read back: " + (result == "" ? "(None)" : result) + ". Verify the native Windows UI.";
            int index = Array.IndexOf(Selected.Themes, result);
            if (index >= 0) _theme.SelectedIndex = index;
        }
        catch (Exception ex) { if (!IsDisposed) _status.Text = ex.Message; }
        finally { if (!IsDisposed) _read.Enabled = _apply.Enabled = _restore.Enabled = _target.Enabled = _theme.Enabled = true; }
    }
}
