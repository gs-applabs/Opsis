using MotionDesk.UI;

namespace MotionDesk.Widgets;

/// <summary>View hotkeys that Opsis failed to register and probe a new combination without retaining it.</summary>
internal static class HotkeyDiagnosticDialog
{
    internal static void Open(IWin32Window owner)
    {
        using var form = new Form
        {
            Text = MainWindow.StudioText("Keyboard shortcut conflicts", "Συγκρούσεις συντομεύσεων"),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.SizableToolWindow,
            MinimumSize = new Size(480, 390),
            ClientSize = new Size(560, 440)
        };
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), RowCount = 5, ColumnCount = 1 };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(new Label { AutoSize = true, Text = MainWindow.StudioText("Unavailable shortcuts in this session:", "Μη διαθέσιμες συντομεύσεις σε αυτή τη συνεδρία:") });
        var failures = new ListBox { Dock = DockStyle.Fill };
        failures.Items.AddRange(HotkeyManager.GetConflictCatalog());
        panel.Controls.Add(failures);
        var keys = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130 };
        keys.Items.AddRange(new object[] { "F6", "F7", "F8", "F9", "F10", "F11", "F12", "A", "B", "K", "P", "Space", "Left", "Right" });
        keys.SelectedIndex = 0;
        var ctrl = new CheckBox { Text = "Ctrl", Checked = true, AutoSize = true };
        var alt = new CheckBox { Text = "Alt", Checked = true, AutoSize = true };
        var shift = new CheckBox { Text = "Shift", AutoSize = true };
        var controls = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        controls.Controls.Add(ctrl); controls.Controls.Add(alt); controls.Controls.Add(shift); controls.Controls.Add(keys);
        panel.Controls.Add(controls);
        var result = new Label { AutoSize = true, Text = MainWindow.StudioText("No key has been probed.", "Δεν έχει δοκιμαστεί συνδυασμός.") };
        panel.Controls.Add(result);
        var probe = new Button { Text = MainWindow.StudioText("Check combination", "Έλεγχος συνδυασμού"), AutoSize = true };
        probe.Click += (_, _) =>
        {
            if (!Enum.TryParse<Keys>((string)keys.SelectedItem!, out var key)) return;
            var (available, error) = HotkeyManager.Probe(key, ctrl.Checked, alt.Checked, shift.Checked);
            result.Text = available
                ? MainWindow.StudioText("Available now. Nothing was registered.", "Διαθέσιμο τώρα. Δεν καταχωρήθηκε συντόμευση.")
                : MainWindow.StudioText($"Unavailable / Win32 error {error}", $"Μη διαθέσιμο / σφάλμα Windows {error}");
        };
        panel.Controls.Add(probe);
        form.Controls.Add(panel);
        form.ShowDialog(owner);
    }
}
