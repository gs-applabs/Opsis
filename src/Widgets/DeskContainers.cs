using System.Collections.Generic;
using System.Text.Json;
using MotionDesk.Services;
using MotionDesk.UI;

namespace MotionDesk.Widgets;

internal sealed class DeskContainers
{
    public static DeskContainers Instance { get; } = new();

    private sealed record Container(Guid Id, string Folder, DeskContainerGeometry? Geometry);
    private readonly Dictionary<Guid, Form> _windows = [];
    private readonly List<Container> _entries = [];
    private static string Store => Path.Combine(AppPaths.AppDataFolder, "deskcontainers.json");
    private bool _loaded;
    private bool _hidden;

    public void Restore()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            if (File.Exists(Store))
                _entries.AddRange(JsonSerializer.Deserialize<List<Container>>(AtomicFile.ReadAllText(Store)) ?? []);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            RotatingLogger.Write("DeskContainers load: " + ex.Message);
        }

        foreach (var entry in _entries.Where(e => e.Id != Guid.Empty && !string.IsNullOrWhiteSpace(e.Folder)))
            if (!_windows.ContainsKey(entry.Id) && Path.IsPathFullyQualified(entry.Folder)) Show(entry);
    }

    public void ChooseFolder(IWin32Window? owner)
    {
        Restore();
        using var picker = new FolderBrowserDialog { Description = LocalizationManager.T("DeskContainers.ChooseFolder") };
        var result = owner == null ? picker.ShowDialog() : picker.ShowDialog(owner);
        if (result != DialogResult.OK) return;

        string folder = Path.TrimEndingDirectorySeparator(picker.SelectedPath);
        if (_entries.FirstOrDefault(entry => string.Equals(entry.Folder, folder, StringComparison.OrdinalIgnoreCase)) is { } existing
            && _windows.TryGetValue(existing.Id, out var window))
        {
            window.Show();
            window.Activate();
            return;
        }

        var entry = new Container(Guid.NewGuid(), folder, null);
        _entries.Add(entry);
        Show(entry);
        Save();
    }

    public void SetHidden(bool hidden)
    {
        _hidden = hidden;
        foreach (var form in _windows.Values)
        {
            if (hidden) form.Hide();
            else form.Show();
        }
    }

    private void Show(Container entry)
    {
        string title = Path.GetFileName(entry.Folder);
        if (title.Length == 0) title = entry.Folder;
        var form = new Form
        {
            Text = title,
            ShowInTaskbar = false,
            FormBorderStyle = FormBorderStyle.SizableToolWindow,
            StartPosition = FormStartPosition.Manual,
            Size = new Size(340, 240),
            MinimumSize = new Size(180, 130),
            AutoScaleMode = AutoScaleMode.Dpi,
            BackColor = UiTheme.Background,
            ForeColor = UiTheme.TextPrimary
        };
        var screen = Screen.AllScreens.FirstOrDefault(s => s.DeviceName == entry.Geometry?.Monitor)
            ?? Screen.FromPoint(Cursor.Position);
        form.Bounds = entry.Geometry?.Place(screen.WorkingArea)
            ?? new Rectangle(screen.WorkingArea.Left + 24, screen.WorkingArea.Top + 24, 340, 240);
        var list = new ListBox
        {
            AccessibleName = title,
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            BackColor = UiTheme.Surface,
            ForeColor = UiTheme.TextPrimary,
            DisplayMember = nameof(FolderItem.Name)
        };
        form.Controls.Add(list);
        var refresh = new System.Windows.Forms.Timer { Interval = 250 };
        refresh.Tick += (_, _) => { refresh.Stop(); Reload(); };
        FileSystemWatcher? watcher = null;
        void ScheduleRefresh()
        {
            try
            {
                if (!form.IsHandleCreated || form.IsDisposed) return;
                form.BeginInvoke(new Action(() =>
                {
                    if (form.IsDisposed) return;
                    refresh.Stop();
                    refresh.Start();
                }));
            }
            catch (InvalidOperationException) { }
        }

        // PecoFence's portals mirror a directory and coalesce watcher events on the UI thread.
        void Reload()
        {
            try
            {
                var selected = (list.SelectedItem as FolderItem)?.Path;
                var entries = Directory.EnumerateFileSystemEntries(entry.Folder).Take(1000)
                    .Select(path => new FolderItem(Path.GetFileName(path), path))
                    .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
                list.BeginUpdate();
                try
                {
                    list.Items.Clear();
                    list.Items.AddRange(entries);
                    list.SelectedIndex = Array.FindIndex(entries, item => item.Path == selected);
                }
                finally { list.EndUpdate(); }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                list.Items.Clear();
            }
        }

        void OpenSelection()
        {
            if (list.SelectedItem is not FolderItem selected || !SafeLaunch.IsAllowed(selected.Path)) return;
            try { using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(selected.Path) { UseShellExecute = true }); }
            catch (System.ComponentModel.Win32Exception ex) { RotatingLogger.Write("DeskContainer item: " + ex.Message); }
        }

        list.MouseDoubleClick += (_, e) => { if (list.IndexFromPoint(e.Location) >= 0) OpenSelection(); };
        list.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; OpenSelection(); } };
        form.Shown += (_, _) =>
        {
            try
            {
                watcher = new FileSystemWatcher(entry.Folder);
                watcher.Changed += (_, _) => ScheduleRefresh();
                watcher.Created += (_, _) => ScheduleRefresh();
                watcher.Deleted += (_, _) => ScheduleRefresh();
                watcher.Renamed += (_, _) => ScheduleRefresh();
                watcher.Error += (_, _) => ScheduleRefresh();
                watcher.EnableRaisingEvents = true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                watcher?.Dispose();
                watcher = null;
            }
            Reload();
        };
        form.ResizeEnd += (_, _) =>
        {
            var monitor = Screen.FromRectangle(form.Bounds);
            int index = _entries.FindIndex(e => e.Id == entry.Id);
            _entries[index] = entry with { Geometry = new DeskContainerGeometry(monitor.DeviceName, form.Bounds, monitor.WorkingArea) };
            Save();
        };
        form.FormClosing += (_, args) =>
        {
            if (args.CloseReason != CloseReason.UserClosing) return;
            _entries.RemoveAll(e => e.Id == entry.Id);
            Save();
        };
        form.FormClosed += (_, _) =>
        {
            _windows.Remove(entry.Id);
            watcher?.Dispose();
            refresh.Dispose();
        };
        _windows.Add(entry.Id, form);
        if (!_hidden) form.Show();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Store)!);
            AtomicFile.WriteAllText(Store, JsonSerializer.Serialize(_entries));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { RotatingLogger.Write("DeskContainers save: " + ex.Message); }
    }

    private sealed record FolderItem(string Name, string Path);
}
