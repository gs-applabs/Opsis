using System.Diagnostics;
using MotionDesk.Services;
using MotionDesk.UI;

namespace MotionDesk.Widgets;

internal sealed class DesktopLauncher : Form
{
    private sealed record Entry(string Name, string Target, IntPtr Window = default)
    {
        public override string ToString() => Name;
    }

    private static DesktopLauncher? _visible;
    private readonly TextBox _query = new() { Dock = DockStyle.Fill, Font = UiTheme.FontBody };
    private readonly ListBox _results = new() { Dock = DockStyle.Fill, IntegralHeight = false, Font = UiTheme.FontBody };
    private readonly CancellationTokenSource _closing = new();
    private Entry[] _index = [];

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
        _results.BackColor = UiTheme.Surface;
        _results.ForeColor = UiTheme.TextPrimary;
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
        preview.Click += (_, _) => PreviewSelection();
        var launcherTools = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        launcherTools.Controls.Add(preview);
        var conflicts = new Button
        {
            Text = MainWindow.StudioText("Shortcut conflicts", "Συγκρούσεις συντομεύσεων"),
            AutoSize = true
        };
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
                case Keys.Up when _results.Items.Count > 0: _results.SelectedIndex = Math.Max(0, _results.SelectedIndex - 1); break;
                case Keys.Down when _results.Items.Count > 0: _results.SelectedIndex = Math.Min(_results.Items.Count - 1, _results.SelectedIndex + 1); break;
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
                var entries = await Task.Run(() => ReadEntries(token), token);
                if (token.IsCancellationRequested || IsDisposed) return;
                _index = entries;
                Filter();
            }
            catch (OperationCanceledException) { }
        };
        FormClosed += (_, _) => { _closing.Cancel(); _closing.Dispose(); };
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

    private static Entry[] ReadEntries(CancellationToken token)
    {
        var entries = WinApi.EnumerateAppWindows().Select(window => new Entry(window.Title, window.ExePath, window.Handle)).ToList();
        foreach (var special in new[] { Environment.SpecialFolder.Programs, Environment.SpecialFolder.CommonPrograms,
            Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.CommonDesktopDirectory })
        {
            string root = Environment.GetFolderPath(special);
            if (!Directory.Exists(root)) continue;
            bool apps = special is Environment.SpecialFolder.Programs or Environment.SpecialFolder.CommonPrograms;
            var options = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = apps, MaxRecursionDepth = 6,
                AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint };
            try
            {
                foreach (var path in Directory.EnumerateFileSystemEntries(root, apps ? "*.lnk" : "*", options).Take(5000))
                {
                    token.ThrowIfCancellationRequested();
                    entries.Add(new Entry(apps ? Path.GetFileNameWithoutExtension(path) : Path.GetFileName(path), path));
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
        var matches = _index.Select(entry => (entry, score: words.Sum(word => Score(entry.Name, word))))
            .Where(pair => pair.score >= 0)
            .OrderByDescending(pair => pair.score).ThenByDescending(pair => pair.entry.Window != IntPtr.Zero)
            .ThenBy(pair => pair.entry.Name, StringComparer.CurrentCultureIgnoreCase).Take(75).Select(pair => pair.entry).ToList();
        if (query.Length > 0 && Path.IsPathFullyQualified(query) && !query.StartsWith(@"\") && (File.Exists(query) || Directory.Exists(query)))
            matches.Insert(0, new Entry(query, query));
        _results.BeginUpdate();
        try
        {
            _results.Items.Clear();
            _results.Items.AddRange(matches.ToArray());
            if (_results.Items.Count > 0) _results.SelectedIndex = 0;
        }
        finally { _results.EndUpdate(); }
    }

    internal static int Score(string label, string term) => SearchLanguage.Score(label, term);

    private void PreviewSelection()
    {
        if (_results.SelectedItem is not Entry item || item.Window != IntPtr.Zero) return;
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
        if (_results.SelectedItem is not Entry selected) return;
        try
        {
            if (selected.Window != IntPtr.Zero) WinApi.BringToFront(selected.Window);
            else if (SafeLaunch.Reject(selected.Target) is { } reason)
                throw new InvalidOperationException(reason);
            else Process.Start(new ProcessStartInfo(selected.Target) { UseShellExecute = true });
            Close();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        { MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }
}
