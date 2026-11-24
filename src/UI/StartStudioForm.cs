using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using MotionDesk.Services;

namespace MotionDesk.UI;

/// <summary>
/// P1 Start Studio: searchable, optional launcher for Start Menu shortcuts.
/// It does not replace or inject code into the Windows Start menu.
/// </summary>
public sealed class StartStudioForm : Form
{
    private sealed record Shortcut(string Name, string Path, string Group);
    private readonly TextBox _search = new() { Dock = DockStyle.Top, PlaceholderText = "Search installed shortcuts / Αναζήτηση εφαρμογών…" };
    private readonly ListView _results = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false };
    private readonly Label _info = new() { Dock = DockStyle.Bottom, Height = 30, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
    private List<Shortcut> _entries = new();
    private HashSet<string> _pinned = new(StringComparer.OrdinalIgnoreCase);
    private readonly CheckBox _pinnedOnly = new() { Text = "Pinned only / Μόνο καρφιτσωμένα", AutoSize = true, Margin = new Padding(7, 10, 10, 6) };
    private readonly Button _pinButton = new() { Text = "Pin / Unpin", Width = 120, Height = 30 };
    private readonly Button _refreshButton = new() { Text = "Refresh / Ανανέωση", Width = 160, Height = 30 };
    private readonly FlowLayoutPanel _toolbar = new() { Dock = DockStyle.Top, Height = 90, WrapContents = true };
    private readonly ComboBox _layoutStyle = new() { Width = 112, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox _groupByFolder = new() { Text = "Group / Ομάδες", Width = 140, AutoSize = false, Height = 30 };
    private readonly ComboBox _startStyle = new() { Width = 155, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Button _startPreview = new() { Width = 46, Height = 40, Text = "", AccessibleName = "Selected Opsis Start button preview" };
    private readonly System.Drawing.Icon? _startIcon = GetOpsisIcon();
    private bool _loadingPresentation;

    public StartStudioForm()
    {
        Text = "Opsis — Start Studio";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new System.Drawing.Size(560, 380);
        Size = new System.Drawing.Size(760, 560);
        _results.Columns.Add("Application / Εφαρμογή", 360);
        _results.Columns.Add("Group / Ομάδα", 310);
        Controls.Add(_results);
        Controls.Add(_search);
        _toolbar.Controls.Add(_pinnedOnly);
        _toolbar.Controls.Add(_pinButton);
        _toolbar.Controls.Add(_refreshButton);
        _layoutStyle.Items.AddRange(new object[] { "List", "Tiles", "Compact" });
        _toolbar.Controls.Add(_layoutStyle);
        _toolbar.Controls.Add(_groupByFolder);
        _startStyle.Items.AddRange(OpsisStartButtonArt.Styles);
        _toolbar.Controls.Add(new Label { Text = "Start Button", AutoSize = true, Margin = new Padding(8, 11, 3, 0) });
        _toolbar.Controls.Add(_startStyle);
        _startPreview.FlatStyle = FlatStyle.Flat;
        _startPreview.Paint += (_, e) => OpsisStartButtonArt.PaintButton(_startPreview, e,
            _startStyle.SelectedItem as string ?? "Opsis Glass", _startIcon);
        _startPreview.Click += (_, _) => MessageBox.Show(this,
            "This button design applies to the Opsis Replacement Taskbar. Native Windows Start stays unchanged.",
            "Opsis Start Button", MessageBoxButtons.OK, MessageBoxIcon.Information);
        _toolbar.Controls.Add(_startPreview);
        var nativeStart = new Button { Text = "Windows Start / Έναρξη Windows", AutoSize = true, Height = 30 };
        nativeStart.Click += (_, _) => { using var dialog = new NativeStartSettingsForm(); dialog.ShowDialog(this); };
        _toolbar.Controls.Add(nativeStart);
        _toolbar.Height = 120;
        _toolbar.AutoScroll = true;
        Controls.Add(_toolbar);
        Controls.Add(_info);
        _pinnedOnly.CheckedChanged += (_, _) => RefreshResults();
        _pinButton.Click += (_, _) => TogglePinned();
        _refreshButton.Click += (_, _) => LoadShortcuts();
        _startStyle.SelectedIndexChanged += (_, _) => ChangeStartButton();
        _layoutStyle.SelectedIndexChanged += (_, _) => ChangePresentation();
        _groupByFolder.CheckedChanged += (_, _) => ChangePresentation();
        _search.TextChanged += (_, _) => RefreshResults();
        _results.DoubleClick += (_, _) => OpenSelected();
        _results.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { OpenSelected(); e.Handled = true; } };
        Shown += (_, _) => { LoadShortcuts(); _search.Focus(); };
        Disposed += (_, _) => _startIcon?.Dispose();
    }

    private void LoadShortcuts()
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu)
        };
        var items = new List<Shortcut>();
        foreach (var root in roots.Where(Directory.Exists))
        {
            var pending = new Stack<(string Folder, int Depth)>();
            pending.Push((root, 0));
            while (pending.Count > 0 && items.Count < 3000)
            {
                var (folder, depth) = pending.Pop();
                try
                {
                    foreach (var path in Directory.EnumerateFiles(folder, "*.lnk"))
                    {
                        if (items.Count >= 3000) break;
                        var name = Path.GetFileNameWithoutExtension(path);
                        var group = Path.GetRelativePath(root, folder);
                        items.Add(new Shortcut(name, path, group == "." ? "General" : group));
                    }
                    if (depth < 6)
                        foreach (var sub in Directory.EnumerateDirectories(folder))
                        {
                            // Do not recurse into directory links or reparse-point loops.
                            if ((File.GetAttributes(sub) & FileAttributes.ReparsePoint) == 0)
                                pending.Push((sub, depth + 1));
                        }
                }
                catch (UnauthorizedAccessException) { }
                catch (IOException) { }
            }
        }
        _entries = items.GroupBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First()).OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        try
        {
            var preferences = StartStudioPreferences.Read();
            _pinned = preferences.PinnedShortcuts.ToHashSet(StringComparer.OrdinalIgnoreCase);
            _loadingPresentation = true;
            try
            {
                _layoutStyle.SelectedItem = preferences.DisplayStyle;
                _groupByFolder.Checked = preferences.GroupByFolder;
                _startStyle.SelectedItem = preferences.StartButtonStyle;
            }
            finally { _loadingPresentation = false; }
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Start Studio saved shortcuts", MessageBoxButtons.OK, MessageBoxIcon.Warning); _pinned.Clear(); }
        ApplyPresentation();
        RefreshResults();
    }

    private static System.Drawing.Icon? GetOpsisIcon()
    {
        try { return System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
        catch { return null; }
    }

    private void ChangeStartButton()
    {
        _startPreview.Invalidate();
        if (_loadingPresentation || _startStyle.SelectedItem is not string style) return;
        try
        {
            StartStudioPreferences.SetStartButton(style);
            MotionDesk.Widgets.ReplacementTaskbar.RefreshStartButtonArt();
            _info.Text = "Start button: " + style + " — Opsis taskbar only";
        }
        catch (Exception ex) { _info.Text = "Start button: " + ex.Message; }
    }

    private void ChangePresentation()
    {
        if (_layoutStyle.SelectedItem is not string style || _loadingPresentation) return;
        try { StartStudioPreferences.SetDisplay(style, _groupByFolder.Checked); }
        catch (Exception ex) { _info.Text = "Start Studio: " + ex.Message; }
        ApplyPresentation();
        RefreshResults();
    }

    private void ApplyPresentation()
    {
        _results.BeginUpdate();
        try
        {
            var style = _layoutStyle.SelectedItem?.ToString() ?? "List";
            _results.View = style switch
            {
                "Tiles" => View.Tile,
                "Compact" => View.SmallIcon,
                _ => View.Details
            };
            _results.TileSize = new System.Drawing.Size(210, 66);
            _results.ShowGroups = _groupByFolder.Checked;
            if (style == "Compact") _results.Alignment = ListViewAlignment.Left;
        }
        finally { _results.EndUpdate(); }
    }

    private void TogglePinned()
    {
        if (_results.SelectedItems.Count != 1 || _results.SelectedItems[0].Tag is not string selected) return;
        if (!_entries.Any(x => string.Equals(x.Path, selected, StringComparison.OrdinalIgnoreCase))) return;
        try
        {
            var next = !_pinned.Contains(selected);
            StartStudioPreferences.SetPinned(selected, next);
            if (next) _pinned.Add(selected); else _pinned.Remove(selected);
            RefreshResults();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Start Studio", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void RefreshResults()
    {
        var term = _search.Text.Trim();
        _results.BeginUpdate();
        try
        {
            _results.Items.Clear();
            _results.Groups.Clear();
            var groups = new Dictionary<string, ListViewGroup>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in _entries.Where(x => (!_pinnedOnly.Checked || _pinned.Contains(x.Path)) &&
                (term.Length == 0 ||
                x.Name.Contains(term, StringComparison.CurrentCultureIgnoreCase) ||
                x.Group.Contains(term, StringComparison.CurrentCultureIgnoreCase)))
                .OrderByDescending(x => _pinned.Contains(x.Path))
                .ThenBy(x => x.Group, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).Take(300))
            {
                var item = new ListViewItem((_pinned.Contains(entry.Path) ? "★ " : "") + entry.Name);
                item.SubItems.Add(entry.Group);
                item.Tag = entry.Path;
                if (_groupByFolder.Checked)
                {
                    if (!groups.TryGetValue(entry.Group, out var group))
                    {
                        group = new ListViewGroup(entry.Group);
                        groups.Add(entry.Group, group);
                        _results.Groups.Add(group);
                    }
                    item.Group = group;
                }
                _results.Items.Add(item);
            }
            _info.Text = $"{_results.Items.Count} displayed / {_entries.Count} indexed — double click or Enter to launch";
        }
        finally { _results.EndUpdate(); }
    }

    private void OpenSelected()
    {
        if (_results.SelectedItems.Count != 1 || _results.SelectedItems[0].Tag is not string path) return;
        // Only launch shortcuts found in the Start Menu scan, never paths supplied in search text.
        if (!_entries.Any(x => string.Equals(x.Path, path, StringComparison.OrdinalIgnoreCase)) ||
            !path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) return;
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            Close();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Opsis — Start Studio", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
}
