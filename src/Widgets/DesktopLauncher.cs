using System.Diagnostics;
using MotionDesk.Services;
using MotionDesk.UI;

namespace MotionDesk.Widgets;

internal sealed class DesktopLauncher : Form
{
    private sealed record Entry(string Name, string Target, IntPtr Window = default, string Group = "", int PinRank = int.MaxValue, bool ShowGroup = false)
    {
        public override string ToString() => (PinRank != int.MaxValue ? "★ " : "") + Name +
            (ShowGroup && Group.Length > 0 ? "  ·  " + Group : "");
    }

    private static DesktopLauncher? _visible;
    private readonly TextBox _query = new() { Dock = DockStyle.Fill, Font = UiTheme.FontBody };
    private readonly ListView _results = new() { Dock = DockStyle.Fill, Font = UiTheme.FontBody,
        View = View.Details, HeaderStyle = ColumnHeaderStyle.None, FullRowSelect = true,
        HideSelection = false, MultiSelect = false, ShowGroups = false, ShowItemToolTips = true };
    private Entry? SelectedEntry => _results.SelectedItems.Count == 1 ? _results.SelectedItems[0].Tag as Entry : null;
    private readonly CancellationTokenSource _closing = new();
    private Entry[] _index = [];
    private readonly bool _signature = AppSettings.Peek().ThemeMode == "SignatureGlass" && !SystemInformation.HighContrast;

    internal static void Open()
    {
        if (_visible is { IsDisposed: false } existing) { existing.Activate(); return; }
        var window = _visible = new DesktopLauncher();
        window.FormClosed += (_, _) => { if (ReferenceEquals(_visible, window)) _visible = null; };
        window.Show();
        window.Activate();
    }

    internal static void OpenStart(Rectangle bar)
    {
        if (_visible is { IsDisposed: false } current) { current.Close(); return; }
        var window = _visible = new DesktopLauncher(startMenu: true);
        window.FormClosed += (_, _) => { if (ReferenceEquals(_visible, window)) _visible = null; };
        var screen = Screen.FromRectangle(bar).WorkingArea;
        window.StartPosition = FormStartPosition.Manual;
        bool vertical = bar.Height > bar.Width;
        int x = vertical ? (bar.Left < screen.Left + screen.Width / 2 ? bar.Right : bar.Left - window.Width) : bar.Left;
        int y = vertical ? bar.Top : bar.Top > screen.Top + screen.Height / 2 ? bar.Top - window.Height : bar.Bottom;
        window.Location = new Point(Math.Clamp(x, screen.Left, Math.Max(screen.Left, screen.Right - window.Width)),
            Math.Clamp(y, screen.Top, Math.Max(screen.Top, screen.Bottom - window.Height)));
        window.Show();
        window.ResizeResultColumns();
        window.Activate();
    }

    private DesktopLauncher(bool startMenu = false)
    {
        Text = MainWindow.StudioText("Desktop launcher", "Εκκινητής εφαρμογών");
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = false;
        TopMost = true;
        KeyPreview = true;
        ClientSize = new Size(575, 390);
        MinimumSize = new Size(430, 320);
        BackColor = UiTheme.PageBackground;
        ForeColor = UiTheme.TextPrimary;
        _query.AccessibleName = Text;
        _query.MaxLength = 1024;
        _query.PlaceholderText = MainWindow.StudioText("Search apps, open windows or desktop files", "Αναζήτηση εφαρμογών, παραθύρων ή αρχείων επιφάνειας");
        _query.BackColor = SystemInformation.HighContrast ? SystemColors.Window : UiTheme.Surface;
        _query.ForeColor = SystemInformation.HighContrast ? SystemColors.WindowText : UiTheme.TextPrimary;
        _results.BackColor = _query.BackColor;
        _results.ForeColor = _query.ForeColor;
        _results.AccessibleName = MainWindow.StudioText("Launcher results", "Αποτελέσματα εκκινητή");
        _results.Columns.Add("Application", 380);
        _results.Columns.Add("Group", 160);
        _results.Resize += (_, _) => ResizeResultColumns();
        if (startMenu)
        {
            try { _results.View = StartStudioPreferences.LauncherView(StartStudioPreferences.Read().DisplayStyle); }
            catch (Exception ex) { RotatingLogger.Write("Start layout: " + ex.Message); }
            if (_results.View == View.Tile)
            {
                int dpi = DeviceDpi;
                _results.TileSize = new Size(230 * dpi / 96, 64 * dpi / 96);
            }
        }
        if (_signature) _results.BorderStyle = BorderStyle.FixedSingle;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Padding = new Padding(14) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(_query, 0, 0);
        layout.Controls.Add(_results, 0, 1);
        if (startMenu)
        {
            Text = MainWindow.StudioText("Opsis Start", "Έναρξη Opsis");
            Deactivate += (_, _) => { if (Visible && !IsDisposed) Close(); };
            layout.RowCount = 3;
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var shortcuts = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
            void Add(string label, Action action)
            {
                var button = new Button { Text = label, AutoSize = true };
                button.Click += (_, _) => { Close(); action(); };
                StyleShortcut(button);
                shortcuts.Controls.Add(button);
            }
            Add(MainWindow.StudioText("Explorer", "Εξερεύνηση"), () => Process.Start("explorer.exe"));
            Add(MainWindow.StudioText("Opsis settings", "Ρυθμίσεις Opsis"), () => TrayApplicationContext.OpenPage?.Invoke("Settings"));
            Add(MainWindow.StudioText("Task View", "Προβολή εργασιών"), VirtualWorkspaces.OpenTaskView);
            try
            {
                foreach (var action in StartStudioPreferences.Read().PowerActions)
                    Add(action, () => RunPowerAction(action));
            }
            catch (Exception ex) { RotatingLogger.Write("Start power settings: " + ex.Message); }
            layout.Controls.Add(shortcuts, 0, 2);
        }
        Controls.Add(layout);
        if (_signature && startMenu)
        {
            var header = new Panel { Dock = DockStyle.Top, Height = 62, BackColor = UiTheme.Sidebar };
            header.Paint += (_, e) =>
            {
                int size = Math.Min(header.Height - 12, 48);
                if (size < 12) return;
                OpsisBrandMark.Draw(e.Graphics, new RectangleF(14, (header.Height-size)/2f, size, size));
                using var font = new Font("Segoe UI", 17, FontStyle.Bold);
                TextRenderer.DrawText(e.Graphics, Text, font, new Rectangle(size+25, 0, Math.Max(1, header.Width-size-40), header.Height),
                    UiTheme.TextPrimary, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                using var rim = new Pen(UiTheme.Border);
                e.Graphics.DrawLine(rim, 0, header.Height-1, header.Width, header.Height-1);
            };
            Controls.Add(header);
        }
        // Quick Look from the launcher: F3/Space shows file contents without executing a handler.
        // Files with reparse points or larger than 64 MiB are not previewed.
        int nextRow = layout.RowCount;
        layout.RowCount++;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var preview = new Button
        {
            Text = MainWindow.StudioText("Preview selected file (F3)", "Προεπισκόπηση αρχείου (F3)"),
            Dock = DockStyle.Fill, Height = 32, AccessibleName = "Preview selected file"
        };
        StyleShortcut(preview);
        preview.Click += (_, _) => PreviewSelection();
        var launcherTools = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        launcherTools.Controls.Add(preview);
        var conflicts = new Button
        {
            Text = MainWindow.StudioText("Shortcut conflicts", "Συγκρούσεις συντομεύσεων"),
            AutoSize = true
        };
        StyleShortcut(conflicts);
        conflicts.Click += (_, _) => HotkeyDiagnosticDialog.Open(this);
        launcherTools.Controls.Add(conflicts);
        layout.Controls.Add(launcherTools, 0, nextRow);
        _query.TextChanged += (_, _) => Filter();
        _results.DoubleClick += (_, _) => LaunchSelection();
        KeyDown += (_, e) =>
        {
            switch (e.KeyCode)
            {
                case Keys.Escape: Close(); break;
                case Keys.Enter: LaunchSelection(); break;
                case Keys.F3: PreviewSelection(); break;
                case Keys.Up when _query.Focused && _results.Items.Count > 0: MoveSelection(-1); break;
                case Keys.Down when _query.Focused && _results.Items.Count > 0: MoveSelection(1); break;
                default: return;
            }
            e.SuppressKeyPress = true;
        };
        Shown += async (_, _) =>
        {
            _query.Focus();
            var token = _closing.Token;
            try
            {
                var entries = await Task.Run(() => ReadEntries(token, startMenu), token);
                if (token.IsCancellationRequested || IsDisposed) return;
                _index = entries;
                Filter();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (!IsDisposed && !Disposing) _query.PlaceholderText = ex.Message; }
        };
        FormClosed += (_, _) => { _closing.Cancel(); _closing.Dispose(); };
    }

    private void ResizeResultColumns()
    {
        if (_results.Columns.Count < 2) return;
        int available = Math.Max(100, _results.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 4);
        int group = Math.Min(180 * DeviceDpi / 96, available / 3);
        _results.Columns[0].Width = available - group;
        _results.Columns[1].Width = group;
        if (_results.View == View.Tile)
            _results.TileSize = new Size(Math.Max(120, Math.Min(230 * DeviceDpi / 96, available)), 64 * DeviceDpi / 96);
        else if (_results.View == View.List)
            _results.Columns[0].Width = Math.Min(240 * DeviceDpi / 96, available);
    }

    private void MoveSelection(int direction)
    {
        int selected = _results.SelectedIndices.Count > 0 ? _results.SelectedIndices[0] : -1;
        int next = Math.Clamp(selected + direction, 0, _results.Items.Count - 1);
        _results.Items[next].Selected = true;
        _results.Items[next].Focused = true;
        _results.EnsureVisible(next);
    }

    private void StyleShortcut(Button button)
    {
        if (!_signature) return;
        button.FlatStyle = FlatStyle.Flat;
        button.UseVisualStyleBackColor = false;
        button.BackColor = UiTheme.Surface;
        button.ForeColor = UiTheme.TextPrimary;
        button.FlatAppearance.BorderColor = UiTheme.Border;
        button.FlatAppearance.MouseOverBackColor = UiTheme.SurfaceHover;
    }

    private static void RunPowerAction(string action)
    {
        if (!StartStudioPreferences.AvailablePowerActions.Contains(action)) return;
        if (action != "Lock" && MessageBox.Show("Save your work first. Continue with " + action + "?", "Opsis",
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        try
        {
            if (action == "Lock") { LockWorkStation(); return; }
            var args = action switch { "Sign out" => "/l", "Restart" => "/r /t 0", "Shut down" => "/s /t 0", _ => throw new InvalidOperationException() };
            Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "shutdown.exe"), args) { UseShellExecute = false, CreateNoWindow = true });
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Opsis"); }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool LockWorkStation();

    private static Entry[] ReadEntries(CancellationToken token, bool startMenu)
    {
        var entries = WinApi.EnumerateAppWindows().Select(window => new Entry(window.Title, window.ExePath, window.Handle)).ToList();
        StartStudioPreferences.Data preferences;
        try { preferences = StartStudioPreferences.Read(); }
        catch (Exception ex) { RotatingLogger.Write("Start preferences: " + ex.Message); preferences = new(); }
        var ranks = preferences.PinnedShortcuts.Select((path, index) => (path, index))
            .GroupBy(x => x.path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().index, StringComparer.OrdinalIgnoreCase);
        foreach (var shortcut in StartStudioPreferences.ScanShortcuts(token))
            entries.Add(new Entry(shortcut.Name, shortcut.Path, Group: shortcut.Group,
                PinRank: ranks.TryGetValue(shortcut.Path, out int rank) ? rank : int.MaxValue,
                ShowGroup: startMenu && preferences.GroupByFolder));
        foreach (var special in new[] { Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.CommonDesktopDirectory })
        {
            string root = Environment.GetFolderPath(special);
            if (!Directory.Exists(root)) continue;
            var options = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = false,
                AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint };
            try
            {
                foreach (var path in Directory.EnumerateFileSystemEntries(root, "*", options).Take(5000))
                {
                    token.ThrowIfCancellationRequested();
                    entries.Add(new Entry(Path.GetFileName(path), path));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return entries.DistinctBy(entry => entry.Window != IntPtr.Zero ? entry.Window.ToString() : entry.Target,
            StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private void Filter()
    {
        string query = _query.Text.Trim();
        var words = query.Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var matches = _index.Select(entry =>
            {
                int sum = 0;
                foreach (string word in words)
                {
                    int score = Score(entry.Name, word);
                    if (score < 0) return (entry, score: -1);
                    sum += score;
                }
                return (entry, score: sum);
            })
            .Where(pair => pair.score >= 0)
            .OrderByDescending(pair => pair.score).ThenBy(pair => pair.entry.PinRank).ThenByDescending(pair => pair.entry.Window != IntPtr.Zero)
            .ThenBy(pair => pair.entry.Name, StringComparer.CurrentCultureIgnoreCase).Take(75).Select(pair => pair.entry).ToList();
        if (query.Length > 0 && Path.IsPathFullyQualified(query) && !query.StartsWith(@"\") && (File.Exists(query) || Directory.Exists(query)))
            matches.Insert(0, new Entry(query, query));
        _results.BeginUpdate();
        try
        {
            var previous = SelectedEntry;
            _results.Items.Clear();
            foreach (var entry in matches)
            {
                string name = (entry.PinRank != int.MaxValue ? "★ " : "") + entry.Name;
                string group = entry.ShowGroup ? entry.Group : "";
                var item = new ListViewItem(_results.View == View.List && group.Length > 0 ? name + " · " + group : name)
                {
                    Tag = entry, ToolTipText = entry.Target
                };
                item.SubItems.Add(group);
                _results.Items.Add(item);
            }
            if (_results.Items.Count > 0)
            {
                int index = previous == null ? 0 : matches.FindIndex(x => x.Window == previous.Window &&
                    string.Equals(x.Target, previous.Target, StringComparison.OrdinalIgnoreCase));
                var item = _results.Items[Math.Max(0, index)];
                item.Selected = true;
                item.Focused = true;
                item.EnsureVisible();
            }
        }
        finally { _results.EndUpdate(); }
    }

    internal static int Score(string label, string term) => SearchLanguage.Score(label, term);

    private void PreviewSelection()
    {
        if (SelectedEntry is not Entry item || item.Window != IntPtr.Zero) return;
        try
        {
            if (!Path.IsPathFullyQualified(item.Target) || !File.Exists(item.Target)) return;
            var file = new FileInfo(item.Target);
            if ((file.Attributes & FileAttributes.ReparsePoint) != 0 || file.Length > 64L * 1024 * 1024) return;
            QuickLookPreview.Show(item.Target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
    }

    private void LaunchSelection()
    {
        if (SelectedEntry is not Entry selected) return;
        try
        {
            if (selected.Window != IntPtr.Zero) WinApi.BringToFront(selected.Window);
            else if (selected.Group.Length > 0 && !StartStudioPreferences.CanLaunchShortcut(selected.Target))
                throw new InvalidOperationException("Start shortcut is no longer available or is linked.");
            else if (SafeLaunch.Reject(selected.Target) is { } reason)
                throw new InvalidOperationException(reason);
            else Process.Start(new ProcessStartInfo(selected.Target) { UseShellExecute = true });
            Close();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        { MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }
}
