using System.Collections.Specialized;
using Microsoft.VisualBasic.FileIO;
using MotionDesk.Services;
using MotionDesk.UI;
using Opsis.Containers;

namespace MotionDesk.Containers;

internal sealed partial class ContainerWindow
{
    internal const string AutoHeightKey = "opsisAutoHeight";
    internal static bool AutoHeightOf(Container item) => item.Extra?.TryGetValue(AutoHeightKey, out var value) == true &&
        value.ValueKind == System.Text.Json.JsonValueKind.True;

    private readonly HashSet<string> _selection = new(StringComparer.OrdinalIgnoreCase);
    private string? _selectionAnchor;
    private readonly Dictionary<ContainerTab, int> _tabScroll = new();
    private string? _portalDirectory;
    private string? PortalDirectory => _portalDirectory ?? Model.Source;
    private bool CanWritePortal => Model.Kind == ContainerKind.Portal && !Model.Locked &&
        PortalDirectory is { Length: > 0 };

    private void UpdateSelection(int index, bool extend, bool toggle)
    {
        var items = Items;
        if (index < 0 || index >= items.Count)
        {
            if (!extend && !toggle) { _selection.Clear(); _selectionAnchor = null; _selected = -1; }
            return;
        }
        string path = items[index].Path;
        int anchor = extend && _selectionAnchor != null
            ? items.FindIndex(item => string.Equals(item.Path, _selectionAnchor, StringComparison.OrdinalIgnoreCase)) : -1;
        if (!toggle) _selection.Clear();
        if (anchor >= 0)
        {
            for (int i = Math.Min(anchor, index); i <= Math.Max(anchor, index); i++) _selection.Add(items[i].Path);
        }
        else
        {
            if (!toggle || !_selection.Remove(path)) _selection.Add(path);
            _selectionAnchor = path;
        }
        _selected = index;
    }

    private string[] SelectionPaths()
    {
        return Items.Where(item => _selection.Contains(item.Path)).Select(item => item.Path).ToArray();
    }

    private void RestoreFocus(string? path)
    {
        _selected = path == null ? -1 : Items.FindIndex(item => string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase));
        _selection.IntersectWith(Items.Select(item => item.Path));
    }

    private void ClearSelection()
    {
        _selection.Clear();
        _selected = -1;
        _selectionAnchor = null;
    }

    private void RemoveReferences(IEnumerable<string> paths, bool restoreOriginal = true, List<ContainerItem>? from = null)
    {
        if (!CanEditItems) return;
        var source = from ?? BaseItems;
        var requested = paths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var removed = source.Where(item => requested.Contains(item.Path)).Select(item => item.Path).ToArray();
        if (removed.Length == 0) return;
        source.RemoveAll(item => requested.Contains(item.Path));
        _organizationMerged.Clear();
        _sortCache = null;
        ClearSelection();
        if (restoreOriginal) foreach (var path in removed) ItemRemoved?.Invoke(path);
        _scroll = ItemLayout.ClampScroll(_scroll, Items.Count);
        Dirty();
        Changed?.Invoke(Model);
    }

    private static WeakReference<ContainerWindow>? _cutFrom;
    private static uint _cutSequence;
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();

    private void CopySelection(bool cut)
    {
        var entries = new StringCollection();
        entries.AddRange(SelectionPaths());
        if (entries.Count == 0) return;
        try
        {
            var clipboard = new DataObject();
            clipboard.SetFileDropList(entries);
            if (cut && CanWritePortal)
                clipboard.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(2)));
            Clipboard.SetDataObject(clipboard, true, 4, 40);
            _cutFrom = cut && CanEditItems ? new WeakReference<ContainerWindow>(this) : null;
            _cutSequence = GetClipboardSequenceNumber();
        }
        catch (System.Runtime.InteropServices.ExternalException) { }
    }

    private void PasteSelection()
    {
        try
        {
            var data = Clipboard.GetDataObject();
            if (data?.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) return;
            bool nativeCut = data.GetData("Preferred DropEffect") is MemoryStream effect && effect.Length >= 4 &&
                (BitConverter.ToInt32(effect.ToArray()) & 2) != 0;
            if (CanWritePortal) { TransferFiles(files, nativeCut); return; }
            if (!CanEditItems) return;
            var added = AddReferences(files, BaseItems, BaseItems.Count);
            if (added.Length == 0) return;
            if (_cutSequence == GetClipboardSequenceNumber() && _cutFrom?.TryGetTarget(out var source) == true && !source.IsDisposed && source != this)
                source.RemoveReferences(added, restoreOriginal: false);
            _cutFrom = null;
        }
        catch (System.Runtime.InteropServices.ExternalException) { }
    }

    private static readonly FileTransferJournal Transfers = new();

    private async void TransferFiles(string[] paths, bool move)
    {
        if (!CanWritePortal || PortalDirectory is not { } folder) return;
        var result = await Transfers.TransferAsync(paths, folder, move);
        if (IsDisposed) return;
        if (result.Warnings.Length > 0 || result.WithoutUndo > 0)
        {
            string label = MainWindow.StudioText("File transfer", "Μεταφορά αρχείων");
            string message = string.Join(Environment.NewLine, result.Warnings);
            if (result.WithoutUndo > 0) message += Environment.NewLine + MainWindow.StudioText(
                "Some files moved without an undo record.", "Για ορισμένα αρχεία δεν δημιουργήθηκε ιστορικό αναίρεσης.");
            MessageBox.Show(this, message.Trim(), label, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        RefreshPortal();
    }

    private async void UndoFileTransfer()
    {
        if (!Transfers.CanUndo) return;
        var outcome = await Transfers.UndoAsync((path, directory) =>
        {
            var drive = new DriveInfo(Path.GetPathRoot(path)!);
            if (drive.DriveType is not (DriveType.Fixed or DriveType.Removable))
                throw new IOException("The destination does not provide a local Recycle Bin.");
            if (directory) FileSystem.DeleteDirectory(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin, UICancelOption.ThrowException);
            else FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin, UICancelOption.ThrowException);
        });
        if (IsDisposed) return;
        RefreshPortal();
        if (outcome.Warnings.Length != 0)
        {
            MessageBox.Show(this, MainWindow.StudioText(
                "Undo stopped for items whose paths or contents changed.", "Η αναίρεση δεν εφαρμόστηκε στα στοιχεία των οποίων άλλαξε η θέση ή το περιεχόμενο.") +
                Environment.NewLine + string.Join(Environment.NewLine, outcome.Warnings),
                MainWindow.StudioText("Undo file transfer", "Αναίρεση μεταφοράς"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private void OpenPortalFolder(string folder)
    {
        if (Model.Kind != ContainerKind.Portal || !Directory.Exists(folder)) return;
        _portalDirectory = folder;
        ClearSelection();
        _scroll = 0;
        RefreshPortal();
    }

    private bool PortalUp()
    {
        if (_portalDirectory == null || string.Equals(_portalDirectory, Model.Source, StringComparison.OrdinalIgnoreCase)) return false;
        string? parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(_portalDirectory));
        if (parent == null || !Directory.Exists(parent)) parent = Model.Source;
        if (parent == null) return false;
        OpenPortalFolder(parent);
        return true;
    }

    private string[] AddReferences(IEnumerable<string> paths, List<ContainerItem> destination, int index)
    {
        if (!CanEditItems) return [];
        var plan = DropRules.Plan(paths, destination, Math.Clamp(index, 0, destination.Count), Directory.Exists);
        if (plan.Effect != DropEffect.AddReference) return [];
        destination.InsertRange(plan.InsertAt, plan.Add);
        _organizationMerged.Clear();
        _sortCache = null;
        var added = plan.Add.Select(item => item.Path).ToArray();
        ItemsAdded?.Invoke(added);
        _scroll = ItemLayout.ClampScroll(_scroll, Items.Count);
        Dirty();
        Changed?.Invoke(Model);
        return added;
    }

    private int TabAt(Point point)
    {
        if (point.Y < Metrics.TitleBarHeight || point.Y >= Metrics.Top) return -1;
        return TabRects().FindIndex(bounds => bounds.Contains(point));
    }

    private void MoveTab(int from, int to)
    {
        if (!TabsOn || from < 0 || from >= Model.Tabs.Count || to < 0 || to >= Model.Tabs.Count || to == from) return;
        ContainerHost.EnsureTabTargets(Model);
        var selected = Model.Tabs[Math.Clamp(Model.CurrentTab, 0, Model.Tabs.Count - 1)];
        var tab = Model.Tabs[from];
        Model.Tabs.RemoveAt(from);
        Model.Tabs.Insert(to, tab);
        Model.CurrentTab = Model.Tabs.IndexOf(selected);
        _organizationMerged.Clear();
        Dirty();
        Changed?.Invoke(Model);
    }

    internal const string SortKey = "opsisSortMode";
    private string ItemSort => Model.Extra?.TryGetValue(SortKey, out var value) == true &&
        value.ValueKind == System.Text.Json.JsonValueKind.String ? value.GetString() ?? "manual" : "manual";

    private void ChooseSort(string order)
    {
        if (Model.Kind == ContainerKind.Note) return;
        string? focused = Items.ElementAtOrDefault(_selected)?.Path;
        (Model.Extra ??= new())[SortKey] = System.Text.Json.JsonSerializer.SerializeToElement(order);
        _sortCache = null;
        RestoreFocus(focused);
        Dirty();
        Changed?.Invoke(Model);
    }

    private List<ContainerItem>? _sortInput, _sortCache;
    private string? _sortMode;
    private List<ContainerItem> Sorted(List<ContainerItem> items)
    {
        var order = ItemSort;
        if (order == "manual") return items;
        // Mutations invalidate the sorted view; static lists reuse it while painting.
        if (!ReferenceEquals(_sortInput, items) || _sortMode != order || _sortCache == null)
        {
            _sortInput = items;
            _sortMode = order;
            string Name(ContainerItem item) => string.IsNullOrEmpty(item.DisplayName) ? Path.GetFileName(item.Path) : item.DisplayName;
            var ordered = order.StartsWith("type", StringComparison.Ordinal)
                ? items.OrderByDescending(item => item.IsFolder).ThenBy(item => Path.GetExtension(item.Path), StringComparer.OrdinalIgnoreCase)
                    .ThenBy(Name, StringComparer.CurrentCultureIgnoreCase)
                : items.OrderBy(Name, StringComparer.CurrentCultureIgnoreCase);
            _sortCache = (order.EndsWith("-desc", StringComparison.Ordinal) ? ordered.Reverse() : ordered).ToList();
        }
        return _sortCache;
    }

    private void SelectAllItems()
    {
        _selection.Clear();
        _selection.UnionWith(Items.Select(item => item.Path));
        if (Items.Count > 0 && _selected < 0) _selected = 0;
        Dirty();
    }

    private void DropEffectFor(DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) != true) { e.Effect = DragDropEffects.None; return; }
        if (CanWritePortal)
        {
            bool moving = (e.KeyState & 4) != 0 && (e.AllowedEffect & DragDropEffects.Move) != 0;
            e.Effect = moving ? DragDropEffects.Move :
                (e.AllowedEffect & DragDropEffects.Copy) != 0 ? DragDropEffects.Copy : DragDropEffects.None;
        }
        else if (CanEditItems)
        {
            e.Effect = (e.AllowedEffect & DragDropEffects.Link) != 0 ? DragDropEffects.Link :
                (e.AllowedEffect & DragDropEffects.Copy) != 0 ? DragDropEffects.Copy : DragDropEffects.None;
        }
        else e.Effect = DragDropEffects.None;
    }
}
