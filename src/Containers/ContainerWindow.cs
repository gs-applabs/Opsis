using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;
using MotionDesk.Services;
using Opsis.Containers;

namespace MotionDesk.Containers
{
    /// <summary>
    /// Icons for the container grid. Extraction (shell calls, can block on network drives) NEVER runs on the UI thread: <see cref="Get"/> returns a cheap placeholder at once and queues the
    /// path on one background worker; finished bitmaps are inserted into the BOUNDED cache on the UI thread only (so eviction/dispose never races with painting) and <see cref="Loaded"/> asks for a repaint.
    /// </summary>
    internal sealed class ContainerIcons : IDisposable
    {
        private readonly BoundedCache<(string, int), Bitmap> _cache = new(256);
        private readonly System.Collections.Concurrent.ConcurrentQueue<((string, int) Key, Bitmap Bmp)> _done = new();
        private readonly HashSet<(string, int)> _pending = new();
        private readonly System.Collections.Concurrent.BlockingCollection<(string, int)> _work = new(new System.Collections.Concurrent.ConcurrentQueue<(string, int)>(), 512);
        private readonly Dictionary<int, Bitmap> _placeholders = new();
        private readonly System.Threading.CancellationTokenSource _cts = new();
        private System.Threading.SynchronizationContext? _ui;   // captured on the first Get (UI thread)
        private int _wake;
        private Task? _worker;

        /// <summary>Raised on the UI thread after new icons are ready (the window repaints).</summary>
        public event Action? Loaded;
        public int PendingCount => _pending.Count;

        public Bitmap Get(string path, int size)
        {
            _ui ??= System.Threading.SynchronizationContext.Current;
            Drain();
            var key = (path, size);
            if (_cache.TryGet(key, out var hit)) return hit;
            if (_pending.Add(key))
            {
                _worker ??= Task.Factory.StartNew(Work, _cts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                if (!_work.TryAdd(key)) _pending.Remove(key);
            }
            return Placeholder(size);
        }

        private Bitmap Placeholder(int size)
        {
            if (_placeholders.TryGetValue(size, out var b)) return b;
            b = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(b)) { g.SmoothingMode = SmoothingMode.AntiAlias; using var br = new SolidBrush(Color.FromArgb(40, 190, 210, 230)); g.FillEllipse(br, size / 6, size / 6, size * 2 / 3, size * 2 / 3); }
            return _placeholders[size] = b;
        }

        private void Work()
        {
            try
            {
                foreach (var key in _work.GetConsumingEnumerable(_cts.Token))
                {
                    var bmp = Extract(key.Item1, key.Item2);
                    _done.Enqueue((key, bmp));
                    if (System.Threading.Interlocked.Exchange(ref _wake, 1) == 0) _ui?.Post(_ => { System.Threading.Interlocked.Exchange(ref _wake, 0); if (Drain()) Loaded?.Invoke(); }, null);
                }
            }
            catch (OperationCanceledException) { }
        }

        private static Bitmap Extract(string path, int size)
        {
            try
            {
                using var icon = System.IO.File.Exists(path) || System.IO.Directory.Exists(path) ? Icon.ExtractAssociatedIcon(path) : null;
                var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                using var g = Graphics.FromImage(bmp);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                if (icon != null) g.DrawIcon(icon, new Rectangle(0, 0, size, size));
                else { using var b = new SolidBrush(Color.FromArgb(90, 150, 170, 200)); g.FillEllipse(b, 2, 2, size - 4, size - 4); }
                return bmp;
            }
            catch (Exception) { return new Bitmap(size, size); }
        }

        /// <summary>Moves finished bitmaps into the cache (call on the UI thread). True when something arrived.</summary>
        public bool Drain()
        {
            var any = false;
            while (_done.TryDequeue(out var d)) { _pending.Remove(d.Key); _cache.Set(d.Key, d.Bmp); any = true; }
            return any;
        }

        public void Dispose()
        {
            _cts.Cancel(); _work.CompleteAdding();
            while (_done.TryDequeue(out var d)) d.Bmp.Dispose();
            foreach (var b in _placeholders.Values) b.Dispose();
            _cache.Dispose();
        }
    }

    /// <summary>
    /// Draws one container into a Graphics (an ARGB layered surface or a test bitmap). The frame has NO background of its own when the GPU glass pane provides it;
    /// the title bar is its own band with its own colour, opacity, height and font. Only the visible rows are drawn (virtualization).
    /// </summary>
    internal static class ContainerRenderer
    {
        public static Color ParseColor(string? hex, Color fallback)
        {
            if (string.IsNullOrWhiteSpace(hex) || hex.Equals("Auto", StringComparison.OrdinalIgnoreCase)) return fallback;
            try { return ColorTranslator.FromHtml(hex); } catch (Exception) { return fallback; }
        }

        public static void Paint(Graphics g, Size client, Container model, ContainerMetrics m, int scroll, int hover, int selected, ContainerIcons icons, bool glass, float scale, bool? highContrast = null)
        {
            var hc = highContrast ?? SystemInformation.HighContrast;   // Windows High Contrast: system colours only, opaque, no glass
            g.SmoothingMode = SmoothingMode.AntiAlias; g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            var radius = (int)Math.Round(14 * scale);
            var tb = model.TitleBar;
            var accent = MotionDesk.UI.UiTheme.AccentCyan;

            if (hc)
            {
                using var bg = new SolidBrush(SystemColors.Window); g.FillRectangle(bg, 0, 0, client.Width, client.Height);
                using var bp = new Pen(SystemColors.WindowText, Math.Max(1, 2 * scale)); g.DrawRectangle(bp, 1, 1, client.Width - 3, client.Height - 3);
            }
            else if (!glass)   // no GPU glass: a translucent dark surface of its own
            {
                using var path = MotionDesk.UI.UiTheme.RoundedPath(new Rectangle(0, 0, client.Width - 1, client.Height - 1), radius);
                using var fill = new SolidBrush(Color.FromArgb(150, 24, 26, 34)); g.FillPath(fill, path);
                using var pen = new Pen(Color.FromArgb(70, 255, 255, 255)); g.DrawPath(pen, path);
            }

            // ---- title bar: its OWN colour / opacity / height / font
            var barRect = new Rectangle(1, 1, client.Width - 2, m.TitleBarHeight);
            var barColor = hc ? SystemColors.Highlight : ParseColor(tb.Color, Color.FromArgb(255, accent.R / 3, accent.G / 3, accent.B / 3));
            using (var barPath = TopRounded(barRect, radius))
            {
                using var bar = new SolidBrush(Color.FromArgb(hc ? 255 : Math.Clamp(tb.Opacity, 0, 100) * 255 / 100, barColor)); g.FillPath(bar, barPath);
            }
            var textColor = hc ? SystemColors.HighlightText : ParseColor(tb.TextColor, Contrast(barColor));
            using (var font = new Font(string.IsNullOrWhiteSpace(tb.FontFamily) ? "Segoe UI Variable Display" : tb.FontFamily, Math.Max(1f, tb.FontSize * scale), tb.Bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Point))
            using (var tbrush = new SolidBrush(textColor))
            {
                var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
                var title = model.Title + (model.Items.Count > 0 ? $"  ({model.Items.Count})" : "");
                g.DrawString(title, font, tbrush, new RectangleF(m.Margin + 18 * scale, barRect.Top, client.Width - 2 * m.Margin - 36 * scale, m.TitleBarHeight), sf);
            }
            if (model.Locked)
            {
                using var lockPen = new Pen(Color.FromArgb(200, textColor), 1.6f * scale);
                var lx = client.Width - m.Margin - 10 * scale; var ly = barRect.Top + m.TitleBarHeight / 2f;
                g.DrawRectangle(lockPen, lx - 5 * scale, ly - 1 * scale, 10 * scale, 7 * scale);
                g.DrawArc(lockPen, lx - 3.5f * scale, ly - 7 * scale, 7 * scale, 8 * scale, 180, 180);
            }

            // ---- items (only the visible range)
            var (first, last) = ContainerLayout.VisibleRange(client, model.Items.Count, m, scroll);
            var view = ContainerLayout.Viewport(client, m);
            g.SetClip(view);
            using var labelFont = new Font("Segoe UI", Math.Max(1f, 9f * scale), FontStyle.Regular, GraphicsUnit.Point);
            using var labelFmt = new StringFormat { Alignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisWord, FormatFlags = StringFormatFlags.LineLimit };
            using var labelBrush = new SolidBrush(hc ? SystemColors.WindowText : Color.FromArgb(240, 255, 255, 255));
            using var shadowBrush = new SolidBrush(Color.FromArgb(model.Appearance.TextShadow && !hc ? 150 : 0, 0, 0, 0));
            for (var i = first; i <= last; i++)
            {
                var cell = ContainerLayout.CellRect(i, client, m, scroll);
                if (i == hover || i == selected)
                {
                    using var hp = MotionDesk.UI.UiTheme.RoundedPath(cell, (int)(8 * scale));
                    using var hb = hc ? new SolidBrush(i == selected ? SystemColors.Highlight : SystemColors.ControlLight) : new SolidBrush(Color.FromArgb(i == selected ? 70 : 38, 255, 255, 255)); g.FillPath(hb, hp);
                    if (i == selected) { using var fp = new Pen(hc ? SystemColors.HighlightText : Color.FromArgb(220, 255, 255, 255), Math.Max(1f, 1.5f * scale)) { DashStyle = DashStyle.Dot }; g.DrawPath(fp, hp); }   // visible keyboard focus
                }
                var item = model.Items[i];
                var iconRect = new Rectangle(cell.X + (cell.Width - m.IconSize) / 2, cell.Y + (int)(4 * scale), m.IconSize, m.IconSize);
                g.DrawImage(icons.Get(item.Path, m.IconSize), iconRect);
                var name = string.IsNullOrEmpty(item.DisplayName) ? System.IO.Path.GetFileNameWithoutExtension(item.Path) : item.DisplayName;
                var labelRect = new RectangleF(cell.X + 2, iconRect.Bottom + 3 * scale, cell.Width - 4, m.LabelHeight);
                var shadowRect = labelRect; shadowRect.Offset(1, 1);
                g.DrawString(name, labelFont, shadowBrush, shadowRect, labelFmt);
                if (hc && i == selected) { using var sb = new SolidBrush(SystemColors.HighlightText); g.DrawString(name, labelFont, sb, labelRect, labelFmt); }
                else g.DrawString(name, labelFont, labelBrush, labelRect, labelFmt);
            }
            g.ResetClip();

            // ---- slim scroll indicator
            var max = ContainerLayout.MaxScroll(client, model.Items.Count, m);
            if (max > 0)
            {
                var track = view.Height - 2 * m.Margin; var content = ContainerLayout.ContentHeight(client.Width, model.Items.Count, m);
                var h = Math.Max(24, track * view.Height / Math.Max(1, content)); var y = view.Top + m.Margin + (track - h) * scroll / max;
                using var tp = new SolidBrush(hc ? SystemColors.WindowText : Color.FromArgb(110, 255, 255, 255));
                g.FillRectangle(tp, client.Width - 5 * scale, y, 3 * scale, h);
            }
        }

        private static GraphicsPath TopRounded(Rectangle r, int radius)
        {
            var d = Math.Max(2, radius * 2); var p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddLine(r.Right, r.Y + radius, r.Right, r.Bottom); p.AddLine(r.Right, r.Bottom, r.X, r.Bottom); p.CloseFigure();
            return p;
        }

        private static Color Contrast(Color back) => (back.R * 299 + back.G * 587 + back.B * 114) / 1000 > 140 ? Color.FromArgb(20, 20, 24) : Color.White;
    }

    /// <summary>
    /// A container window (phase 6 part B, first slice): a LAYERED per-pixel-alpha window drawn by <see cref="ContainerRenderer"/>, with an Opsis.Glass pane directly below it when
    /// GPU glass is on. Title drag with snapping, corner resize, wheel scroll, double click on the title = roll-up, lock. Not activating, not in Alt+Tab.
    /// z-order against the desktop icon layer / wallpaper window and Win+D are NOT solved here (manual tests listed in PLAN-2.6.0-PROGRESS.md).
    /// </summary>
    internal sealed class ContainerWindow : Form
    {
        private readonly ContainerIcons _icons = new();
        private readonly LayeredSurface _surface = new();
        private readonly System.Windows.Forms.Timer _repaint = new() { Interval = 8 };
        private GlassPaneBinder? _pane;
        private int _scroll, _hover = -1, _selected = -1;
        private bool _dirty = true;
        private Point _dragStart; private Rectangle _boundsAtDragStart; private enum Drag { None, Move, Resize } private Drag _drag;
        private readonly Func<IEnumerable<Rectangle>> _others;

        public Container Model { get; }
        public bool GlassOn => MotionDesk.Services.GlassService.Enabled;
        public event Action<Container>? Changed;

        public ContainerWindow(Container model, Func<IEnumerable<Rectangle>>? others = null)
        {
            Model = model; _others = others ?? (() => Array.Empty<Rectangle>());
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; AllowDrop = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.Opaque, true);
            _repaint.Tick += (_, _) => { if (_dirty) Repaint(); else _repaint.Stop(); };
            _pane = new GlassPaneBinder(this, 14, () => GlassOn);
            _icons.Loaded += Dirty;
        }

        /// <summary>For tests: waits until the background icon extraction has finished (UI messages keep pumping).</summary>
        public void WaitIcons(int timeoutMs)
        {
            var end = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < end && _icons.PendingCount > 0) { Application.DoEvents(); System.Threading.Thread.Sleep(10); }
        }

        protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x00080000 | 0x08000000 | 0x80; return cp; } }
        protected override bool ShowWithoutActivation => true;

        private float Scale => DeviceDpi / 96f;
        private ContainerMetrics Metrics => ContainerMetrics.Default(Scale, Model.TitleBar.Height, Model.Appearance.IconSize > 0 ? Model.Appearance.IconSize : 48);
        private Size Client => Size;

        public void Dirty() { _dirty = true; if (!_repaint.Enabled) _repaint.Start(); }
        protected override void OnInvalidated(InvalidateEventArgs e) { base.OnInvalidated(e); Dirty(); }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Dirty(); }
        protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); Dirty(); }
        protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); _scroll = ContainerLayout.ClampScroll(_scroll, Client, Model.Items.Count, Metrics); Dirty(); }
        protected override void OnPaint(PaintEventArgs e) { }
        protected override void OnPaintBackground(PaintEventArgs e) { }

        /// <summary>Draws the window; also usable for a test (renders into a bitmap of the same size).</summary>
        public void RenderTo(Graphics g, bool? highContrast = null) => ContainerRenderer.Paint(g, Client, Model, Metrics, _scroll, _hover, _selected, _icons, GlassOn, Scale, highContrast);

        private void Repaint()
        {
            if (!IsHandleCreated || IsDisposed || !Visible || Width <= 0 || Height <= 0) return;
            _dirty = false;
            using (var g = _surface.Begin(Width, Height)) RenderTo(g);
            _surface.Present(Handle, Location);
        }

        // ---- interaction ---------------------------------------------------------------------------------------------
        private Rectangle Grip => new(Width - (int)(18 * Scale), Height - (int)(18 * Scale), (int)(18 * Scale), (int)(18 * Scale));
        private Rectangle WorkArea => Screen.FromControl(this).WorkingArea;

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Right) { ShowMenu(e.Location); return; }
            if (e.Button != MouseButtons.Left) return;
            _dragStart = Cursor.Position; _boundsAtDragStart = Bounds;
            if (Grip.Contains(e.Location) && !Model.RolledUp) _drag = Drag.Resize;
            else if (e.Y < Metrics.TitleBarHeight) _drag = Drag.Move;
            else { _selected = ContainerLayout.HitTest(e.Location, Client, Model.Items.Count, Metrics, _scroll); _dragItem = _selected; _pressAt = e.Location; Dirty(); }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragItem >= 0 && (e.Button & MouseButtons.Left) != 0 && !Model.Locked && (Math.Abs(e.X - _pressAt.X) > SystemInformation.DragSize.Width || Math.Abs(e.Y - _pressAt.Y) > SystemInformation.DragSize.Height))
            {
                var idx = _dragItem; _dragItem = -1;
                if (idx < Model.Items.Count) { var dobj = new DataObject(); dobj.SetData(InternalFormat, idx); dobj.SetData(DataFormats.FileDrop, new[] { Model.Items[idx].Path }); DoDragDrop(dobj, DragDropEffects.Link | DragDropEffects.Copy); }
                return;
            }
            if (_drag == Drag.None)
            {
                var h = e.Y < Metrics.TitleBarHeight ? -1 : ContainerLayout.HitTest(e.Location, Client, Model.Items.Count, Metrics, _scroll);
                if (h != _hover) { _hover = h; Dirty(); }
                Cursor = Grip.Contains(e.Location) && !Model.RolledUp ? Cursors.SizeNWSE : Cursors.Default;
                return;
            }
            var d = new Size(Cursor.Position.X - _dragStart.X, Cursor.Position.Y - _dragStart.Y);
            Rectangle wanted = _drag == Drag.Move ? new Rectangle(_boundsAtDragStart.Location + d, _boundsAtDragStart.Size) : ContainerBehavior.Resize(_boundsAtDragStart, _boundsAtDragStart.Size + d, WorkArea);
            if (_drag == Drag.Move) wanted = ContainerBehavior.Snap(wanted, _others(), WorkArea, (int)(12 * Scale));
            wanted = ContainerBehavior.ApplyLock(Model.Locked, Bounds, wanted);
            if (wanted != Bounds) { Bounds = wanted; _surface.Present(Handle, Location); Dirty(); }
        }

        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _dragItem = -1; if (_drag != Drag.None) { _drag = Drag.None; Model.Geometry = LegacyImporter.Normalize(Left, Top, Width, Height, System.Windows.Forms.Screen.AllScreens.Select(s => new MonitorInfo(s.DeviceName, s.Bounds, s.Primary)).ToList()); Changed?.Invoke(Model); } }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); if (_hover != -1) { _hover = -1; Dirty(); } }

        protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); ScrollBy(-e.Delta / 120 * (int)(Metrics.CellHeight * 0.6)); }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Y >= Metrics.TitleBarHeight)
            {
                var hit = ContainerLayout.HitTest(e.Location, Client, Model.Items.Count, Metrics, _scroll);
                if (hit >= 0) OpenItem(hit);                 // double click launches the item
                return;
            }
            ToggleRollUp();
        }

        public void ToggleRollUp()
        {
            if (!Model.RolledUp)
            {
                var (b, h) = ContainerBehavior.RollUp(Bounds, Metrics.TitleBarHeight);
                Model.UnrolledHeight = h; Model.RolledUp = true; Bounds = b;
            }
            else { Bounds = ContainerBehavior.Unroll(Bounds, Model.UnrolledHeight, WorkArea); Model.RolledUp = false; }
            Dirty(); Changed?.Invoke(Model);
        }

        // ---- keyboard (phase 7/8): the window does not steal focus, so keys work after FocusForKeyboard() (hotkey / tray) or when Windows activates it for assistive tools ----
        public int Selected => _selected;
        public void FocusForKeyboard() { if (_selected < 0 && Model.Items.Count > 0) { _selected = 0; Dirty(); } Activate(); }

        /// <summary>Handles one navigation key; returns true when it was used. Public so it can be tested without real keyboard input.</summary>
        public bool HandleKey(Keys key)
        {
            var cols = ContainerLayout.Columns(Client.Width, Metrics);
            NavKey? nav = key switch { Keys.Left => NavKey.Left, Keys.Right => NavKey.Right, Keys.Up => NavKey.Up, Keys.Down => NavKey.Down, Keys.Home => NavKey.Home, Keys.End => NavKey.End, Keys.PageUp => NavKey.PageUp, Keys.PageDown => NavKey.PageDown, _ => null };
            if (nav != null)
            {
                _selected = ContainerNavigation.Move(_selected, nav.Value, cols, Model.Items.Count, Math.Max(1, ContainerLayout.Viewport(Client, Metrics).Height / Math.Max(1, Metrics.CellHeight)));
                if (_selected >= 0) _scroll = ContainerLayout.ScrollIntoView(_selected, Client, Model.Items.Count, Metrics, _scroll);
                Dirty(); return true;
            }
            if (key == Keys.Enter && _selected >= 0 && _selected < Model.Items.Count) { OpenItem(_selected); return true; }
            if (key == Keys.Delete && _selected >= 0 && !Model.Locked) { RemoveItem(_selected); return true; }
            if (key == Keys.Apps || key == (Keys.F10 | Keys.Shift)) { ShowMenu(_selected >= 0 ? ContainerLayout.CellRect(_selected, Client, Metrics, _scroll).Location : new Point(Metrics.Margin, Metrics.TitleBarHeight)); return true; }
            return false;
        }

        public void OpenItem(int index)
        {
            try { var it = Model.Items[index]; System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(it.Path) { UseShellExecute = true, Arguments = it.Arguments ?? "" }); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("container open: " + ex.Message); }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData) => HandleKey(keyData) || base.ProcessCmdKey(ref msg, keyData);

        // ---- UI Automation / MSAA: a custom-drawn window must describe itself, otherwise screen readers see an empty window ----
        protected override AccessibleObject CreateAccessibilityInstance() => new WindowAccessible(this);

        private sealed class WindowAccessible : ControlAccessibleObject
        {
            private readonly ContainerWindow _w;
            public WindowAccessible(ContainerWindow w) : base(w) { _w = w; }
            public override string? Name { get => _w.Model.Title + " container"; set { } }
            public override AccessibleRole Role => AccessibleRole.List;
            public override string? Description => $"{_w.Model.Items.Count} items" + (_w.Model.Locked ? ", locked" : "");
            public override int GetChildCount() => _w.Model.Items.Count;
            public override AccessibleObject? GetChild(int index) => index >= 0 && index < _w.Model.Items.Count ? new ItemAccessible(this, _w, index) : null;
            public override AccessibleObject? HitTest(int x, int y)
            {
                var p = _w.PointToClient(new Point(x, y)); var i = ContainerLayout.HitTest(p, _w.Client, _w.Model.Items.Count, _w.Metrics, _w._scroll);
                return i >= 0 ? GetChild(i) : this;
            }
        }

        private sealed class ItemAccessible : AccessibleObject
        {
            private readonly AccessibleObject _parent; private readonly ContainerWindow _w; private readonly int _i;
            public ItemAccessible(AccessibleObject parent, ContainerWindow w, int i) { _parent = parent; _w = w; _i = i; }
            private ContainerItem Item => _w.Model.Items[Math.Min(_i, _w.Model.Items.Count - 1)];
            public override string? Name { get => string.IsNullOrEmpty(Item.DisplayName) ? System.IO.Path.GetFileNameWithoutExtension(Item.Path) : Item.DisplayName; set { } }
            public override string? Description => Item.Path;
            public override AccessibleObject? Parent => _parent;
            public override AccessibleRole Role => AccessibleRole.ListItem;
            public override string DefaultAction => "Open";
            public override void DoDefaultAction() => _w.OpenItem(_i);
            public override AccessibleStates State => AccessibleStates.Selectable | AccessibleStates.Focusable | (_w._selected == _i ? AccessibleStates.Selected | AccessibleStates.Focused : 0);
            public override Rectangle Bounds => _w.RectangleToScreen(ContainerLayout.CellRect(_i, _w.Client, _w.Metrics, _w._scroll));
            public override void Select(AccessibleSelection flags) { if ((flags & AccessibleSelection.TakeSelection) != 0) { _w._selected = _i; _w._scroll = ContainerLayout.ScrollIntoView(_i, _w.Client, _w.Model.Items.Count, _w.Metrics, _w._scroll); _w.Dirty(); } }
        }

        // ---- context menu: the container's OWN small menu (no shell extensions loaded into our process); every action is isolated by try/catch ----
        private void ShowMenu(Point at)
        {
            var idx = at.Y < Metrics.TitleBarHeight ? -1 : ContainerLayout.HitTest(at, Client, Model.Items.Count, Metrics, _scroll);
            var menu = new ContextMenuStrip();
            void Add(string text, Action act, bool enabled = true)
            {
                var it = menu.Items.Add(text); it.Enabled = enabled;
                it.Click += (_, _) => { try { act(); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine("container menu: " + ex.Message); } };
            }
            if (idx >= 0)
            {
                var item = Model.Items[idx];
                Add("Open", () => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(item.Path) { UseShellExecute = true, Arguments = item.Arguments ?? "" }));
                Add("Show in folder", () => System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + item.Path + "\""));
                Add("Remove from container (the file stays)", () => RemoveItem(idx), !Model.Locked);
                menu.Items.Add(new ToolStripSeparator());
            }
            Add(Model.Locked ? "Unlock container" : "Lock container", () => { Model.Locked = !Model.Locked; Dirty(); Changed?.Invoke(Model); });
            Add(Model.RolledUp ? "Unroll" : "Roll up", ToggleRollUp);
            menu.Closed += (_, _) => BeginInvoke(() => menu.Dispose());
            menu.Show(this, at);
        }

        /// <summary>Removes the REFERENCE only; the file on disk is never touched.</summary>
        public void RemoveItem(int index)
        {
            if (index < 0 || index >= Model.Items.Count) return;
            Model.Items.RemoveAt(index); _selected = -1; _scroll = ContainerLayout.ClampScroll(_scroll, Client, Model.Items.Count, Metrics); Dirty(); Changed?.Invoke(Model);
        }

        // ---- drag and drop (phase 3): items are REFERENCES, dropping never copies or moves the file --------------------
        private int _dragItem = -1; private Point _pressAt; private const string InternalFormat = "Opsis.ContainerItemIndex";

        /// <summary>Adds dropped paths as references at the position under <paramref name="clientPoint"/>. Returns how many were added. Used by the drop handler and by tests.</summary>
        public int AcceptDrop(IEnumerable<string> paths, Point clientPoint)
        {
            var at = ContainerLayout.DropIndex(clientPoint, Client, Model.Items.Count, Metrics, _scroll);
            var plan = DropRules.Plan(paths, Model.Items, at, p => System.IO.Directory.Exists(p));
            if (plan.Effect != DropEffect.AddReference) return 0;
            Model.Items.InsertRange(plan.InsertAt, plan.Add);
            _scroll = ContainerLayout.ClampScroll(_scroll, Client, Model.Items.Count, Metrics); Dirty(); Changed?.Invoke(Model);
            return plan.Add.Count;
        }

        /// <summary>Moves an item inside this container (drag to reorder).</summary>
        public void MoveItem(int from, Point clientPoint)
        {
            var at = ContainerLayout.DropIndex(clientPoint, Client, Model.Items.Count, Metrics, _scroll);
            var list = DropRules.Reorder(Model.Items, from, at);
            Model.Items.Clear(); Model.Items.AddRange(list); _selected = -1; Dirty(); Changed?.Invoke(Model);
        }

        protected override void OnDragEnter(DragEventArgs e) { base.OnDragEnter(e); e.Effect = e.Data != null && (e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(InternalFormat)) ? DragDropEffects.Link : DragDropEffects.None; }
        protected override void OnDragOver(DragEventArgs e) { base.OnDragOver(e); e.Effect = e.Data != null && (e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(InternalFormat)) ? DragDropEffects.Link : DragDropEffects.None; }
        protected override void OnDragDrop(DragEventArgs e)
        {
            base.OnDragDrop(e);
            if (e.Data == null) return;
            var pt = PointToClient(new Point(e.X, e.Y));
            if (e.Data.GetData(InternalFormat) is int from) MoveItem(from, pt);
            else if (e.Data.GetData(DataFormats.FileDrop) is string[] files) AcceptDrop(files, pt);
        }

        /// <summary>Scroll by wheel notches (positive = down).</summary>
        public void ScrollBy(int pixels) { _scroll = ContainerLayout.ClampScroll(_scroll + pixels, Client, Model.Items.Count, Metrics); Dirty(); }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _repaint.Stop(); _repaint.Dispose(); _pane?.Dispose(); _surface.Dispose(); _icons.Dispose(); }
            base.Dispose(disposing);
        }
    }

}
