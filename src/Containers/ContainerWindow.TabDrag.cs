using System.Runtime.InteropServices;
using Opsis.Containers;

namespace MotionDesk.Containers;

internal sealed partial class ContainerWindow
{
    private int _pressedTab = -1;
    private Point _tabPointer;
    private bool _draggingTab;
    private ContainerWindow? _tabTarget;
    private int _tabSlot;
    private Rectangle _tabMarker;
    private System.Windows.Forms.Timer? _tabDragKeys;

    private void PressTab(int tab)
    {
        _pressedTab = tab;
        _tabPointer = Cursor.Position;
        Capture = true;
    }

    private bool UpdateTabDrag()
    {
        if (_pressedTab < 0) return false;
        if (!_draggingTab && Math.Abs(Cursor.Position.X - _tabPointer.X) < SystemInformation.DragSize.Width &&
            Math.Abs(Cursor.Position.Y - _tabPointer.Y) < SystemInformation.DragSize.Height) return true;
        if (!_draggingTab)
        {
            _draggingTab = true;
            _tabDragKeys ??= new System.Windows.Forms.Timer { Interval = 35 };
            _tabDragKeys.Tick -= CheckTabEscape;
            _tabDragKeys.Tick += CheckTabEscape;
            _tabDragKeys.Start();
        }
        var point = Cursor.Position;
        var window = ContainerHost.Current?.TabTargetAt(point) ?? (TitleStripContains(point) ? this : null);
        if (!ReferenceEquals(_tabTarget, window)) _tabTarget?.SetTabMarker(Rectangle.Empty);
        _tabTarget = window;
        if (window == null)
        {
            Cursor = Model.Tabs.Count > 1 ? Cursors.SizeAll : Cursors.No;
            return true;
        }
        var local = window.PointToClient(point);
        var tabs = window.TabRects();
        Rectangle bar;
        if (window.IsCollapsed || tabs.Count == 0 || local.Y < window.Metrics.TitleBarHeight)
        {
            _tabSlot = tabs.Count;
            bar = new Rectangle(6, Math.Max(1, window.Metrics.TitleBarHeight - 5), Math.Max(1, window.Width - 12), 3);
        }
        else
        {
            _tabSlot = ContainerTabStrip.Insertion(tabs, local.X);
            int right = tabs.FindLastIndex(rect => !rect.IsEmpty);
            int x = _tabSlot < tabs.Count && !tabs[_tabSlot].IsEmpty ? tabs[_tabSlot].Left :
                right >= 0 ? tabs[right].Right : window.Metrics.Margin;
            bar = new Rectangle(Math.Clamp(x, 2, Math.Max(2, window.Width - 4)), window.Metrics.TitleBarHeight + 3,
                3, Math.Max(2, window.Metrics.TopInset - 6));
        }
        window.SetTabMarker(bar);
        Cursor = Cursors.SizeAll;
        return true;
    }

    internal bool TitleStripContains(Point screenPoint) => Visible && !IsDisposed && ContainerHost.SupportsTabs(Model) &&
        new Rectangle(Left, Top, Width, Math.Min(Height, Metrics.TitleBarHeight + Metrics.TopInset)).Contains(screenPoint);

    private void SetTabMarker(Rectangle marker)
    {
        if (marker == _tabMarker) return;
        _tabMarker = marker;
        Dirty();
    }

    private void CheckTabEscape(object? sender, EventArgs args)
    {
        if ((GetAsyncKeyState((int)Keys.Escape) & 0x8000) != 0) CancelTabDrag();
        else UpdateTabDrag();
    }

    private void CancelTabDrag()
    {
        if (_pressedTab < 0) return;
        _pressedTab = -1;
        _draggingTab = false;
        _tabDragKeys?.Stop();
        _tabTarget?.SetTabMarker(Rectangle.Empty);
        _tabTarget = null;
        Cursor = Cursors.Default;
        Capture = false;
    }

    private bool ReleaseTab()
    {
        if (_pressedTab < 0) return false;
        int from = _pressedTab;
        if (_draggingTab) UpdateTabDrag();
        bool dragging = _draggingTab;
        var target = _tabTarget;
        int slot = _tabSlot;
        Point point = Cursor.Position;
        CancelTabDrag();
        if (!dragging) SelectTab(from);
        else if (ReferenceEquals(target, this))
        {
            int destination = Math.Clamp(slot > from ? slot - 1 : slot, 0, Model.Tabs.Count - 1);
            MoveTab(from, destination);
        }
        else if (ContainerHost.Current is { } host)
        {
            if (target != null)
            {
                string targetId = target.Model.Id;
                BeginInvoke(() => host.TransferTab(Model.Id, from, targetId, slot));
            }
            else if (Model.Tabs.Count > 1 && !Bounds.Contains(point))
                BeginInvoke(() => host.DetachTab(Model.Id, from, point));
        }
        return true;
    }

    private void PaintTabIndicator(Graphics graphics)
    {
        if (_tabMarker.IsEmpty) return;
        using var brush = new SolidBrush(MotionDesk.UI.UiTheme.AccentCyan);
        graphics.FillRectangle(brush, _tabMarker);
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);
}
