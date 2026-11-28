using System;
using System.Drawing;
using System.Windows.Forms;
using MotionDesk.Services;

namespace MotionDesk.UI;

/// <summary>
/// Guarded, reversible HKCU Explorer preferences. Does not manipulate protected
/// Explorer command-bar, undocumented tab hooks or third-party shell extensions.
/// </summary>
public sealed class OpsisExplorerStudioForm : Form
{
    private readonly CheckBox _extensions = new() { Text = "Show file extensions / Επεκτάσεις αρχείων", AutoSize = true };
    private readonly CheckBox _hidden = new() { Text = "Show hidden items / Κρυφά στοιχεία", AutoSize = true };
    private readonly CheckBox _compact = new() { Text = "Compact file spacing / Συμπαγής προβολή", AutoSize = true };
    private readonly CheckBox _statusBar = new() { Text = "Status bar / Γραμμή κατάστασης", AutoSize = true };
    private readonly CheckBox _sync = new() { Text = "Sync provider notifications / Ειδοποιήσεις συγχρονισμού", AutoSize = true };
    private readonly CheckBox _handlers = new() { Text = "Preview handlers / Χειριστές προεπισκόπησης", AutoSize = true };
    private readonly ComboBox _home = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 210 };
    private readonly Label _preview = new() { AutoSize = false, Height = 122, Dock = DockStyle.Bottom };

    public OpsisExplorerStudioForm()
    {
        Text = "Opsis — Explorer Studio";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(700, 560);
        MinimumSize = new Size(560, 390);
        BackColor = UiTheme.PageBackground;
        ForeColor = UiTheme.TextPrimary;
        Font = UiTheme.FontBody;
        var title = new Label
        {
            Dock = DockStyle.Top, Height = 65, Padding = new Padding(14, 10, 12, 8),
            Text = "Explorer Studio 2.0\nHKCU-only, reversible Explorer views. No command-bar DLL injections."
        };
        Controls.Add(title);
        var options = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false,
            Padding = new Padding(17, 15, 8, 7), AutoScroll = true
        };
        _home.Items.AddRange(new object[] { "This PC / Αυτός ο υπολογιστής", "Home / Αρχική" });
        options.Controls.Add(_extensions);
        options.Controls.Add(_hidden);
        options.Controls.Add(_compact);
        options.Controls.Add(_statusBar); options.Controls.Add(_sync); options.Controls.Add(_handlers);
        options.Controls.Add(new Label { Text = "Open Explorer at / Εκκίνηση Explorer", AutoSize = true });
        options.Controls.Add(_home);
        var commands = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Width = 480 };
        foreach (var target in new[] { ("This PC", "shell:MyComputerFolder"), ("Home", "shell:HomeFolder"),
            ("Downloads", "shell:Downloads"), ("Recycle Bin", "shell:RecycleBinFolder") })
        {
            var button = new Button { AutoSize = true, Text = target.Item1 };
            button.Click += (_, _) =>
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target.Item2) { UseShellExecute = true }); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, Text); }
            };
            commands.Controls.Add(button);
        }
        options.Controls.Add(new Label { AutoSize = true, Text = "Explorer shortcuts / Συντομεύσεις (not native command bar)" });
        options.Controls.Add(commands);
        var tabs = new Button { AutoSize = true, Text = "Open folder in Explorer / Άνοιγμα φακέλου" };
        tabs.Click += (_, _) =>
        {
            using var picker = new FolderBrowserDialog();
            if (picker.ShowDialog(this) != DialogResult.OK) return;
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(picker.SelectedPath) { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, Text); }
        };
        options.Controls.Add(tabs);
        var nativeEditor = new Button { Text = "Native Explorer options (Windhawk)", AutoSize = true };
        nativeEditor.Click += (_, _) => { using var dialog = new NativeShellThemeForm(); dialog.ShowDialog(this); };
        options.Controls.Add(nativeEditor);
        var session = new Button { Text = "Explorer session / Συνεδρία φακέλων", AutoSize = true };
        session.Click += (_, _) =>
        {
            using var dialog = new Form { Text = "Explorer folder session", StartPosition = FormStartPosition.CenterParent,
                ClientSize = new Size(650, 390) };
            var folders = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, HorizontalScrollbar = true };
            var footerSession = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 52 };
            var capture = new Button { Text = "Capture open folders", AutoSize = true };
            var save = new Button { Text = "Save checked", AutoSize = true };
            var load = new Button { Text = "Load saved", AutoSize = true };
            var restoreSession = new Button { Text = "Open checked folders", AutoSize = true };
            void Display(string[] paths) { folders.Items.Clear(); foreach (var path in paths) folders.Items.Add(path, true); }
            string[] SelectedFolders() => folders.CheckedItems.Cast<string>().ToArray();
            void TryAction(Action action) { try { action(); } catch (Exception err) { MessageBox.Show(dialog, err.Message, "Opsis"); } }
            async Task RunSessionAction(Func<Task> action)
            {
                capture.Enabled = save.Enabled = load.Enabled = restoreSession.Enabled = false;
                try { await action(); }
                catch (Exception err) { if (!dialog.IsDisposed && !dialog.Disposing) MessageBox.Show(dialog, err.Message, "Opsis"); }
                finally
                {
                    if (!dialog.IsDisposed && !dialog.Disposing)
                        capture.Enabled = save.Enabled = load.Enabled = restoreSession.Enabled = true;
                }
            }
            capture.Click += async (_, _) => await RunSessionAction(async () =>
            {
                var paths = await Task.Run(ExplorerSessionStore.Capture);
                if (!dialog.IsDisposed && !dialog.Disposing) Display(paths);
            });
            save.Click += (_, _) => TryAction(() => ExplorerSessionStore.Save(SelectedFolders()));
            load.Click += (_, _) => TryAction(() => Display(ExplorerSessionStore.Read()));
            restoreSession.Click += async (_, _) =>
            {
                if (MessageBox.Show(dialog, "Open selected local folders? Explorer decides window/tab placement.", "Opsis", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
                var selected = SelectedFolders();
                await RunSessionAction(async () =>
                {
                    int opened = await Task.Run(() => ExplorerSessionStore.Restore(selected));
                    if (!dialog.IsDisposed && !dialog.Disposing) MessageBox.Show(dialog, opened + " folders requested.");
                });
            };
            footerSession.Controls.AddRange([capture, save, load, restoreSession]);
            dialog.Controls.Add(folders); dialog.Controls.Add(footerSession);
            TryAction(() => Display(ExplorerSessionStore.Read()));
            dialog.ShowDialog(this);
        };
        options.Controls.Add(session);
        options.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(490, 0),
            Text = "Windows 11 tabs: Ctrl+T new tab · Ctrl+W close · Ctrl+Tab next. Native tab styling is not available here." });
        Controls.Add(options);

        _preview.BackColor = UiTheme.Surface;
        _preview.ForeColor = UiTheme.TextPrimary;
        _preview.Padding = new Padding(15, 9, 8, 5);
        Controls.Add(_preview);

        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 53, Padding = new Padding(12, 8, 8, 8) };
        var apply = new Button { Text = "Apply / Εφαρμογή", Width = 145, Height = 34 };
        var restore = new Button { Text = "Restore originals", Width = 145, Height = 34 };
        var refresh = new Button { Text = "Reload", Width = 90, Height = 34 };
        footer.Controls.Add(apply);
        footer.Controls.Add(restore);
        footer.Controls.Add(refresh);
        Controls.Add(footer);
        title.BringToFront();
        footer.BringToFront();

        void UpdatePreview() => _preview.Text =
            $"Explorer preview\n{(_extensions.Checked ? "report.pdf" : "report")}       " +
            $"{(_hidden.Checked ? "[hidden] settings.ini" : "")}\n" +
            $"Spacing: {(_compact.Checked ? "Compact" : "Standard")}   ·   Startup: {_home.SelectedItem}";
        _extensions.CheckedChanged += (_, _) => UpdatePreview();
        _hidden.CheckedChanged += (_, _) => UpdatePreview();
        _compact.CheckedChanged += (_, _) => UpdatePreview();
        _home.SelectedIndexChanged += (_, _) => UpdatePreview();
        refresh.Click += (_, _) => LoadValues();
        apply.Click += (_, _) =>
        {
            if (WindowsUpdateGuard.RequiresReview || SafeModeService.IsSafeMode)
            {
                MessageBox.Show(this, "Windows compatibility review is required before changing Explorer.",
                    "Opsis", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (MessageBox.Show(this,
                "Apply these seven HKCU Explorer preferences? A complete Opsis backup is saved first.",
                "Opsis Explorer", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try
            {
                OpsisRecoveryHub.Snapshot("Before Explorer Studio preference changes");
                ExplorerViewProfiles.ApplyOptions(new Dictionary<string, int>
                {
                    ["HideFileExt"] = _extensions.Checked ? 0 : 1,
                    ["Hidden"] = _hidden.Checked ? 1 : 2,
                    ["UseCompactMode"] = _compact.Checked ? 1 : 0,
                    ["LaunchTo"] = _home.SelectedIndex == 0 ? 1 : 2,
                    ["ShowStatusBar"] = _statusBar.Checked ? 1 : 0,
                    ["ShowSyncProviderNotifications"] = _sync.Checked ? 1 : 0,
                    ["ShowPreviewHandlers"] = _handlers.Checked ? 1 : 0
                });
                MessageBox.Show(this, "Explorer settings applied. Some changes may appear in the next Explorer window.",
                    "Opsis Explorer", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Opsis Explorer", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        };
        restore.Click += (_, _) =>
        {
            if (MessageBox.Show(this, "Restore the original Explorer settings recorded before Opsis?",
                "Opsis Explorer", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try
            {
                OpsisRecoveryHub.Snapshot("Before reverting Explorer Studio settings");
                ExplorerViewProfiles.Restore();
                LoadValues();
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Opsis Explorer", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        };
        LoadValues();
    }
    private void LoadValues()
    {
        try
        {
            _extensions.Checked = ExplorerViewProfiles.ReadOption("HideFileExt") == 0;
            _hidden.Checked = ExplorerViewProfiles.ReadOption("Hidden") == 1;
            _compact.Checked = ExplorerViewProfiles.ReadOption("UseCompactMode") == 1;
            _statusBar.Checked = ExplorerViewProfiles.ReadOption("ShowStatusBar") == 1;
            _sync.Checked = ExplorerViewProfiles.ReadOption("ShowSyncProviderNotifications") == 1;
            _handlers.Checked = ExplorerViewProfiles.ReadOption("ShowPreviewHandlers") == 1;
            _home.SelectedIndex = ExplorerViewProfiles.ReadOption("LaunchTo") == 1 ? 0 : 1;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Opsis Explorer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
