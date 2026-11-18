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

        public static void Paint(Graphics g, Size client, Container model, List<ContainerItem> items, ContainerMetrics m, int scroll, int hover, int selected, ContainerIcons icons, bool glass, float scale, bool? highContrast = null)
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

            // ---- tint of the frame and the glossy highlight (per container, schema 2)
            if (!hc)
            {
                var ap = model.Appearance;
                if (!string.IsNullOrWhiteSpace(ap.FrameColor) && !ap.FrameColor.Equals("Auto", StringComparison.OrdinalIgnoreCase))
                {
                    using var tp0 = MotionDesk.UI.UiTheme.RoundedPath(new Rectangle(0, 0, client.Width - 1, client.Height - 1), radius);
                    using var tb0 = new SolidBrush(Color.FromArgb(Math.Clamp(ap.TintOpacity, 0, 100) * 255 / 100, ParseColor(ap.FrameColor, Color.Transparent))); g.FillPath(tb0, tp0);
                }
                if (ap.Glossy)
                {
                    var gr = new Rectangle(0, 0, Math.Max(2, client.Width - 1), Math.Max(2, Math.Min(client.Height - 1, m.TitleBarHeight + (client.Height - m.TitleBarHeight) / 3)));
                    using var gp = MotionDesk.UI.UiTheme.RoundedPath(gr, radius);
                    using var gb = new LinearGradientBrush(gr, Color.FromArgb(95, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f); g.FillPath(gb, gp);
                }
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
                var title = model.Title + (items.Count > 0 ? $"  ({items.Count})" : "");
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
            var (first, last) = ContainerLayout.VisibleRange(client, items.Count, m, scroll);
            var view = ContainerLayout.Viewport(client, m);
            g.SetClip(view);
            using var labelFont = new Font(string.IsNullOrWhiteSpace(model.Appearance.ItemFontFamily) ? "Segoe UI" : model.Appearance.ItemFontFamily, Math.Max(1f, (model.Appearance.ItemFontSize > 0 ? model.Appearance.ItemFontSize : 9f) * scale), FontStyle.Regular, GraphicsUnit.Point);
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
                var item = items[i];
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

            // ---- tab strip (under the title bar) and the text of a note container
            if (m.TopInset > 0 && model.Kind == ContainerKind.Data && model.Tabs.Count > 0)
            {
                using var tf = new Font("Segoe UI", 9f * scale, FontStyle.Regular, GraphicsUnit.Point);
                int x = m.Margin / 2;
                for (int ti = 0; ti < model.Tabs.Count; ti++)
                {
                    var tw = TextRenderer.MeasureText(g, model.Tabs[ti].Name, tf).Width + (int)(16 * scale);
                    var tr = new Rectangle(x, barRect.Bottom, tw, m.TopInset);
                    bool cur = ti == model.CurrentTab;
                    if (cur) { using var tabBrush = new SolidBrush(hc ? SystemColors.Highlight : Color.FromArgb(60, 255, 255, 255)); g.FillRectangle(tabBrush, tr); using var up = new Pen(hc ? SystemColors.HighlightText : accent, Math.Max(1.5f, 2 * scale)); g.DrawLine(up, tr.Left, tr.Bottom - 1, tr.Right, tr.Bottom - 1); }
                    TextRenderer.DrawText(g, model.Tabs[ti].Name, tf, tr, hc ? (cur ? SystemColors.HighlightText : SystemColors.WindowText) : Color.FromArgb(cur ? 255 : 190, 255, 255, 255), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                    x += tw + 2;
                }
            }
            if (model.Kind == ContainerKind.Note)
            {
                using var nf = new Font("Segoe UI", 10f * scale, FontStyle.Regular, GraphicsUnit.Point);
                var nr = new Rectangle(m.Margin, m.Top + m.Margin / 2, client.Width - 2 * m.Margin, Math.Max(10, client.Height - m.Top - m.Margin));
                TextRenderer.DrawText(g, string.IsNullOrEmpty(model.Source) ? "(double-click to write a note)" : model.Source, nf, nr, hc ? SystemColors.WindowText : Color.FromArgb(string.IsNullOrEmpty(model.Source) ? 130 : 235, 255, 255, 255), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
            }

            // ---- slim scroll indicator
            var max = ContainerLayout.MaxScroll(client, items.Count, m);
            if (max > 0)
            {
                var track = view.Height - 2 * m.Margin; var content = ContainerLayout.ContentHeight(client.Width, items.Count, m);
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

        // a drag that started in a container: lets another container tell "my own item came back" from "an item of that container" and lets the source remove the item when it was MOVED
        private sealed class ItemDrag { public ContainerWindow Source = null!; public string Path = ""; public bool Moved; }
        private static ItemDrag? _itemDrag;

        public Container Model { get; }
        public bool GlassOn => MotionDesk.Services.GlassService.Enabled;
        public event Action<Container>? Changed;
        /// <summary>The user asked for this container's own settings (title bar right-click menu).</summary>
        public event Action<string>? SettingsRequested, RenameRequested, HideRequested, DeleteRequested;

        /// <summary>Live settings: takes the look (title bar, appearance, title, lock) from the stored container, so a change made anywhere shows at once without recreating the window.</summary>
        private string _styleKey = "";
        public void ApplyStyle(Container stored)
        {
            // nothing painted from the stored data changed (every Apply of ANY container re-syncs all windows): do not repaint, that was a source of flicker
            var key = ContainerStyleKey.Of(stored); var same = key == _styleKey; _styleKey = key;
            Model.Title = stored.Title; Model.Locked = stored.Locked; Model.TitleBar = stored.TitleBar; Model.Appearance = stored.Appearance;
            if (IsHandleCreated && !same) { Dirty(); Invalidate(); }
        }
        /// <summary>The global look (theme, glass, tint, performance tier) changed: repaint with the current settings.</summary>
        public void Restyle() { if (IsHandleCreated) { Dirty(); Invalidate(); } }
        /// <summary>Raised after files were dropped in (the host may hide their desktop originals) and after an item was removed (the host gives its original back).</summary>
        public event Action<IReadOnlyList<string>>? ItemsAdded; public event Action<string>? ItemRemoved;

        public ContainerWindow(Container model, Func<IEnumerable<Rectangle>>? others = null)
        {
            Model = model; _styleKey = ContainerStyleKey.Of(model); _others = others ?? (() => Array.Empty<Rectangle>());
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; AllowDrop = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.Opaque, true);
            _repaint.Tick += (_, _) => { if (_dirty) Repaint(); else _repaint.Stop(); };
            _pane = new GlassPaneBinder(this, 14, () => GlassOn);
            _icons.Loaded += Dirty;
            _portalTimer.Tick += (_, _) => { _portalTimer.Stop(); RefreshPortal(); };
            _hoverTimer.Tick += (_, _) => HoverTick();
            Load += (_, _) => RefreshPortal();
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
        private bool TabsOn => Model.Kind == ContainerKind.Data && Model.TabsEnabled && Model.Tabs.Count > 0;
        private ContainerMetrics Metrics => ContainerMetrics.Default(Scale, Model.TitleBar.Height, Model.Appearance.IconSize > 0 ? Model.Appearance.IconSize : 48) with { TopInset = TabsOn ? (int)(26 * Scale) : 0 };

        // The items shown now: the container's own list, the list of the current tab, or - for a portal - a LIVE listing of its folder (never stored). A note has none.
        private readonly List<ContainerItem> _portalItems = new(); private static readonly List<ContainerItem> NoItems = new();
        public List<ContainerItem> Items => Model.Kind switch
        {
            ContainerKind.Portal => _portalItems,
            ContainerKind.Note => NoItems,
            _ => TabsOn ? Model.Tabs[Math.Clamp(Model.CurrentTab, 0, Model.Tabs.Count - 1)].Items : Model.Items
        };
        private System.IO.FileSystemWatcher? _watch; private readonly System.Windows.Forms.Timer _portalTimer = new() { Interval = 400 };

        /// <summary>Re-reads the folder of a portal (folders first, then files, at most 400 entries) and watches it for changes.</summary>
        public void RefreshPortal()
        {
            if (Model.Kind != ContainerKind.Portal) return;
            _portalItems.Clear();
            var dir = Model.Source;
            try
            {
                if (!string.IsNullOrWhiteSpace(dir) && System.IO.Directory.Exists(dir))
                    foreach (var p in System.IO.Directory.EnumerateFileSystemEntries(dir).Select(p => (p, d: System.IO.Directory.Exists(p))).OrderByDescending(t => t.d).ThenBy(t => t.p, StringComparer.CurrentCultureIgnoreCase).Take(400))
                        _portalItems.Add(new ContainerItem { Path = p.p, DisplayName = System.IO.Path.GetFileNameWithoutExtension(p.p) is { Length: > 0 } n ? n : System.IO.Path.GetFileName(p.p), IsFolder = p.d });
                if (_watch == null || !string.Equals(_watch.Path, dir, StringComparison.OrdinalIgnoreCase))
                {
                    _watch?.Dispose(); _watch = null;
                    if (!string.IsNullOrWhiteSpace(dir) && System.IO.Directory.Exists(dir))
                    {
                        _watch = new System.IO.FileSystemWatcher(dir) { EnableRaisingEvents = true, IncludeSubdirectories = false };
                        void Kick(object? s, EventArgs e) { try { if (IsHandleCreated && !IsDisposed) BeginInvoke(new Action(() => { _portalTimer.Stop(); _portalTimer.Start(); })); } catch (Exception) { } }
                        _watch.Created += Kick; _watch.Deleted += Kick; _watch.Renamed += Kick;
                    }
                }
            }
            catch (Exception) { }
            _scroll = ContainerLayout.ClampScroll(_scroll, Client, Items.Count, Metrics); Dirty();
        }

        private string? Ask(string prompt, string initial)
        {
            using var f = new Form { Text = prompt, StartPosition = FormStartPosition.CenterScreen, ClientSize = new Size(380, 112), FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, TopMost = true };
            var box = new TextBox { Location = new Point(14, 14), Width = 352, Text = initial }; var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(286, 66), Size = new Size(80, 30) };
            f.Controls.AddRange(new Control[] { box, ok }); f.AcceptButton = ok; box.SelectAll();
            return f.ShowDialog() == DialogResult.OK ? box.Text : null;
        }

        private void EditNote()
        {
            using var f = new Form { Text = Model.Title, StartPosition = FormStartPosition.CenterScreen, ClientSize = new Size(460, 320), TopMost = true };
            var box = new TextBox { Multiline = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, Text = Model.Source ?? "", AcceptsReturn = true };
            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Dock = DockStyle.Bottom, Height = 32 };
            f.Controls.Add(box); f.Controls.Add(ok);
            if (f.ShowDialog() == DialogResult.OK) { Model.Source = box.Text; Dirty(); Changed?.Invoke(Model); }
        }

        // ---- tabs ----
        private List<Rectangle> TabRects()
        {
            var list = new List<Rectangle>(); if (!TabsOn) return list;
            using var tf = new Font("Segoe UI", 9f * Scale, FontStyle.Regular, GraphicsUnit.Point);
            int x = Metrics.Margin / 2;
            foreach (var t in Model.Tabs) { var w = TextRenderer.MeasureText(t.Name, tf).Width + (int)(16 * Scale); list.Add(new Rectangle(x, Metrics.TitleBarHeight, w, Metrics.TopInset)); x += w + 2; }
            return list;
        }
        public void SelectTab(int i) { if (!TabsOn) return; Model.CurrentTab = Math.Clamp(i, 0, Model.Tabs.Count - 1); _scroll = 0; _selected = -1; Dirty(); Changed?.Invoke(Model); }
        public void AddTab(string name)
        {
            if (Model.Kind != ContainerKind.Data) return;
            if (Model.Tabs.Count == 0) { Model.Tabs.Add(new ContainerTab { Name = "Tab 1", Items = new List<ContainerItem>(Model.Items) }); Model.Items.Clear(); }     // enabling tabs moves the icons into the first tab
            Model.Tabs.Add(new ContainerTab { Name = string.IsNullOrWhiteSpace(name) ? "Tab " + (Model.Tabs.Count + 1) : name }); Model.TabsEnabled = true; Model.CurrentTab = Model.Tabs.Count - 1; _scroll = 0; Dirty(); Changed?.Invoke(Model);
        }
        public void RemoveTab(int i)
        {
            if (!TabsOn || i < 0 || i >= Model.Tabs.Count) return;
            var items = Model.Tabs[i].Items; Model.Tabs.RemoveAt(i);
            if (Model.Tabs.Count == 0) { Model.Items.AddRange(items); Model.TabsEnabled = false; }
            else { Model.Tabs[Math.Max(0, i - 1)].Items.AddRange(items); Model.CurrentTab = Math.Clamp(Model.CurrentTab, 0, Model.Tabs.Count - 1); }          // the icons of a removed tab move to its neighbour, nothing is lost
            Dirty(); Changed?.Invoke(Model);
        }
        private Size Client => Size;

        public void Dirty() { _dirty = true; if (!_repaint.Enabled) _repaint.Start(); }
        protected override void OnInvalidated(InvalidateEventArgs e) { base.OnInvalidated(e); Dirty(); }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Dirty(); }
        protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); Dirty(); }
        protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); _scroll = ContainerLayout.ClampScroll(_scroll, Client, Items.Count, Metrics); Dirty(); }
        protected override void OnPaint(PaintEventArgs e) { }
        protected override void OnPaintBackground(PaintEventArgs e) { }

        /// <summary>Draws the window; also usable for a test (renders into a bitmap of the same size).</summary>
        public void RenderTo(Graphics g, bool? highContrast = null) => ContainerRenderer.Paint(g, Client, Model, Items, Metrics, _scroll, _hover, _selected, _icons, GlassOn, Scale, highContrast);

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
            if (e.Button == MouseButtons.Left && TabsOn && e.Y >= Metrics.TitleBarHeight && e.Y < Metrics.Top) { var tr = TabRects(); for (int ti = 0; ti < tr.Count; ti++) if (tr[ti].Contains(e.Location)) { SelectTab(ti); return; } return; }
            if (e.Button != MouseButtons.Left) return;
            _dragStart = Cursor.Position; _boundsAtDragStart = Bounds;
            if (Grip.Contains(e.Location) && !Model.RolledUp) _drag = Drag.Resize;
            else if (e.Y < Metrics.TitleBarHeight) _drag = Drag.Move;
            else { _selected = ContainerLayout.HitTest(e.Location, Client, Items.Count, Metrics, _scroll); _dragItem = _selected; _pressAt = e.Location; Dirty(); }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragItem >= 0 && (e.Button & MouseButtons.Left) != 0 && !Model.Locked && (Math.Abs(e.X - _pressAt.X) > SystemInformation.DragSize.Width || Math.Abs(e.Y - _pressAt.Y) > SystemInformation.DragSize.Height))
            {
                var idx = _dragItem; _dragItem = -1;
                if (idx < Items.Count)
                {
                    var dobj = new DataObject(); dobj.SetData(InternalFormat, idx); dobj.SetData(DataFormats.FileDrop, new[] { Items[idx].Path });
                    _itemDrag = new ItemDrag { Source = this, Path = Items[idx].Path };
                    try { DoDragDrop(dobj, DragDropEffects.Link | DragDropEffects.Copy); }     // never Move: outside Opsis a move would take the real file
                    finally
                    {
                        var dr = _itemDrag; _itemDrag = null;
                        if (dr != null && dr.Moved && Model.Kind == ContainerKind.Data && !Model.Locked) { var at = Items.FindIndex(x => x.Path == dr.Path); if (at >= 0) RemoveItem(at); }       // dropped into another container as a MOVE: the reference leaves this one
                    }
                }
                return;
            }
            if (_drag == Drag.None)
            {
                var h = e.Y < Metrics.TitleBarHeight ? -1 : ContainerLayout.HitTest(e.Location, Client, Items.Count, Metrics, _scroll);
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

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left && _dragItem >= 0 && Model.Appearance.OpenOnSingleClick && e.Y >= Metrics.TitleBarHeight && ContainerLayout.HitTest(e.Location, Client, Items.Count, Metrics, _scroll) == _dragItem) { var open = _dragItem; _dragItem = -1; OpenItem(open); return; }
            AfterMouseUp(e);
        }
        private void AfterMouseUp(MouseEventArgs e) { _dragItem = -1; if (_drag != Drag.None) { _drag = Drag.None; Model.Geometry = LegacyImporter.Normalize(Left, Top, Width, Height, System.Windows.Forms.Screen.AllScreens.Select(s => new MonitorInfo(s.DeviceName, s.Bounds, s.Primary)).ToList()); Changed?.Invoke(Model); } }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); if (_hover != -1) { _hover = -1; Dirty(); } HoverLeft(); }
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); HoverEntered(); }

        // ---- hover roll-up: a rolled-up container (setting per container) opens while the pointer is on it and rolls up again shortly after the pointer left
        private readonly System.Windows.Forms.Timer _hoverTimer = new();
        private bool _hoverOpened;
        private void HoverEntered()
        {
            if (!Model.Appearance.HoverRollUp) return;
            _hoverTimer.Stop(); _hoverTimer.Interval = Math.Max(50, Model.Appearance.HoverDelayMs);
            if (Model.RolledUp && !_hoverOpened) { _hoverTimer.Tag = "open"; _hoverTimer.Start(); }
        }
        private void HoverLeft()
        {
            if (!Model.Appearance.HoverRollUp) return;
            _hoverTimer.Stop(); _hoverTimer.Interval = Math.Max(50, Model.Appearance.HoverDelayMs);
            if (_hoverOpened) { _hoverTimer.Tag = "close"; _hoverTimer.Start(); }
        }
        private void HoverTick()
        {
            _hoverTimer.Stop();
            if ((string?)_hoverTimer.Tag == "open") { if (Model.RolledUp && Bounds.Contains(Cursor.Position)) { Bounds = ContainerBehavior.Unroll(Bounds, Math.Max(Model.UnrolledHeight, 120), WorkArea); Model.RolledUp = false; _hoverOpened = true; Dirty(); } }
            else if (_hoverOpened && !Bounds.Contains(Cursor.Position) && _drag == Drag.None)
            {
                _hoverOpened = false; var (b, h) = ContainerBehavior.RollUp(Bounds, Metrics.TitleBarHeight); Model.UnrolledHeight = h; Model.RolledUp = true; Bounds = b; Dirty();
            }
        }
        /// <summary>For tests: runs the hover roll-up decision at once (the timer is not waited for).</summary>
        public void HoverStep(bool pointerInside) { _hoverTimer.Tag = pointerInside ? "open" : "close"; if (pointerInside) { if (Model.RolledUp) { Bounds = ContainerBehavior.Unroll(Bounds, Math.Max(Model.UnrolledHeight, 120), WorkArea); Model.RolledUp = false; _hoverOpened = true; Dirty(); } } else if (_hoverOpened) { _hoverOpened = false; var (b, h) = ContainerBehavior.RollUp(Bounds, Metrics.TitleBarHeight); Model.UnrolledHeight = h; Model.RolledUp = true; Bounds = b; Dirty(); } }

        protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); ScrollBy(-e.Delta / 120 * (int)(Metrics.CellHeight * 0.6)); }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Y >= Metrics.TitleBarHeight)
            {
                if (Model.Kind == ContainerKind.Note) { EditNote(); return; }
                var hit = ContainerLayout.HitTest(e.Location, Client, Items.Count, Metrics, _scroll);
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
        public void FocusForKeyboard() { if (_selected < 0 && Items.Count > 0) { _selected = 0; Dirty(); } Activate(); }

        /// <summary>Handles one navigation key; returns true when it was used. Public so it can be tested without real keyboard input.</summary>
        public bool HandleKey(Keys key)
        {
            var cols = ContainerLayout.Columns(Client.Width, Metrics);
            NavKey? nav = key switch { Keys.Left => NavKey.Left, Keys.Right => NavKey.Right, Keys.Up => NavKey.Up, Keys.Down => NavKey.Down, Keys.Home => NavKey.Home, Keys.End => NavKey.End, Keys.PageUp => NavKey.PageUp, Keys.PageDown => NavKey.PageDown, _ => null };
            if (nav != null)
            {
                _selected = ContainerNavigation.Move(_selected, nav.Value, cols, Items.Count, Math.Max(1, ContainerLayout.Viewport(Client, Metrics).Height / Math.Max(1, Metrics.CellHeight)));
                if (_selected >= 0) _scroll = ContainerLayout.ScrollIntoView(_selected, Client, Items.Count, Metrics, _scroll);
                Dirty(); return true;
            }
            if (key == Keys.Enter && _selected >= 0 && _selected < Items.Count) { OpenItem(_selected); return true; }
            if (key == Keys.Delete && _selected >= 0 && !Model.Locked) { RemoveItem(_selected); return true; }
            if (key == Keys.F2) { RenameRequested?.Invoke(Model.Id); return true; }
            if (key == Keys.Apps || key == (Keys.F10 | Keys.Shift)) { ShowMenu(_selected >= 0 ? ContainerLayout.CellRect(_selected, Client, Metrics, _scroll).Location : new Point(Metrics.Margin, Metrics.TitleBarHeight)); return true; }
            return false;
        }

        /// <summary>Raised with the path of every item that is launched; when <see cref="SuppressLaunch"/> is set (tests) the item is only reported, not started.</summary>
        public event Action<string>? ItemLaunched; public static bool SuppressLaunch;
        public void OpenItem(int index)
        {
            try { var it = Items[index]; ItemLaunched?.Invoke(it.Path); if (SuppressLaunch) return; System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(it.Path) { UseShellExecute = true, Arguments = it.Arguments ?? "" }); }
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
            public override string? Description => $"{_w.Items.Count} items" + (_w.Model.Locked ? ", locked" : "");
            public override int GetChildCount() => _w.Items.Count;
            public override AccessibleObject? GetChild(int index) => index >= 0 && index < _w.Items.Count ? new ItemAccessible(this, _w, index) : null;
            public override AccessibleObject? HitTest(int x, int y)
            {
                var p = _w.PointToClient(new Point(x, y)); var i = ContainerLayout.HitTest(p, _w.Client, _w.Items.Count, _w.Metrics, _w._scroll);
                return i >= 0 ? GetChild(i) : this;
            }
        }

        private sealed class ItemAccessible : AccessibleObject
        {
            private readonly AccessibleObject _parent; private readonly ContainerWindow _w; private readonly int _i;
            public ItemAccessible(AccessibleObject parent, ContainerWindow w, int i) { _parent = parent; _w = w; _i = i; }
            private ContainerItem Item => _w.Items[Math.Min(_i, _w.Items.Count - 1)];
            public override string? Name { get => string.IsNullOrEmpty(Item.DisplayName) ? System.IO.Path.GetFileNameWithoutExtension(Item.Path) : Item.DisplayName; set { } }
            public override string? Description => Item.Path;
            public override AccessibleObject? Parent => _parent;
            public override AccessibleRole Role => AccessibleRole.ListItem;
            public override string DefaultAction => "Open";
            public override void DoDefaultAction() => _w.OpenItem(_i);
            public override AccessibleStates State => AccessibleStates.Selectable | AccessibleStates.Focusable | (_w._selected == _i ? AccessibleStates.Selected | AccessibleStates.Focused : 0);
            public override Rectangle Bounds => _w.RectangleToScreen(ContainerLayout.CellRect(_i, _w.Client, _w.Metrics, _w._scroll));
            public override void Select(AccessibleSelection flags) { if ((flags & AccessibleSelection.TakeSelection) != 0) { _w._selected = _i; _w._scroll = ContainerLayout.ScrollIntoView(_i, _w.Client, _w.Items.Count, _w.Metrics, _w._scroll); _w.Dirty(); } }
        }

        // ---- context menu: the container's OWN small menu (no shell extensions loaded into our process); every action is isolated by try/catch ----
        private void ShowMenu(Point at)
        {
            var idx = at.Y < Metrics.TitleBarHeight ? -1 : ContainerLayout.HitTest(at, Client, Items.Count, Metrics, _scroll);
            var menu = new ContextMenuStrip();
            void Add(string text, Action act, bool enabled = true)
            {
                var it = menu.Items.Add(text); it.Enabled = enabled;
                it.Click += (_, _) => { try { act(); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine("container menu: " + ex.Message); } };
            }
            if (idx >= 0)
            {
                var item = Items[idx];
                Add("Open", () => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(item.Path) { UseShellExecute = true, Arguments = item.Arguments ?? "" }));
                Add("Show in folder", () => System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + item.Path + "\""));
                Add("Remove from container (the file stays)", () => RemoveItem(idx), !Model.Locked);
                menu.Items.Add(new ToolStripSeparator());
            }
            if (Model.Kind == ContainerKind.Data)
            {
                Add("New tab...", () => { var n = Ask("Tab name", "Tab " + (Model.Tabs.Count + 1)); if (n != null) AddTab(n); });
                if (TabsOn) { Add("Rename this tab...", () => { var t = Model.Tabs[Math.Clamp(Model.CurrentTab, 0, Model.Tabs.Count - 1)]; var n = Ask("Tab name", t.Name); if (n != null) { t.Name = n; Dirty(); Changed?.Invoke(Model); } }); Add("Remove this tab (icons move to the next one)", () => RemoveTab(Model.CurrentTab)); }
                menu.Items.Add(new ToolStripSeparator());
            }
            else if (Model.Kind == ContainerKind.Portal)
            {
                Add("Choose folder...", () => { using var fb = new FolderBrowserDialog { SelectedPath = Model.Source ?? "" }; if (fb.ShowDialog() == DialogResult.OK) { Model.Source = fb.SelectedPath; RefreshPortal(); Changed?.Invoke(Model); } });
                menu.Items.Add(new ToolStripSeparator());
            }
            else { Add("Edit note...", EditNote); menu.Items.Add(new ToolStripSeparator()); }
            Add(MotionDesk.Services.LocalizationManager.T("DCM.ContainerSettings"), () => SettingsRequested?.Invoke(Model.Id));
            Add(MotionDesk.Services.LocalizationManager.T("DCM.Rename"), () => RenameRequested?.Invoke(Model.Id));
            Add(MotionDesk.Services.LocalizationManager.T("DCM.Hide"), () => HideRequested?.Invoke(Model.Id));
            Add(MotionDesk.Services.LocalizationManager.T("DCX.DeleteContainer"), () => DeleteRequested?.Invoke(Model.Id), !Model.Locked);
            Add(Model.Locked ? "Unlock container" : "Lock container" , () => { Model.Locked = !Model.Locked; Dirty(); Changed?.Invoke(Model); });
            Add(Model.RolledUp ? "Unroll" : "Roll up", ToggleRollUp);
            menu.Closed += (_, _) => BeginInvoke(() => menu.Dispose());
            menu.Show(this, at);
        }

        /// <summary>Removes the REFERENCE only; the file on disk is never touched.</summary>
        public void RemoveItem(int index)
        {
            if (Model.Kind != ContainerKind.Data) return;
            if (index < 0 || index >= Items.Count) return;
            var removed = Items[index].Path; Items.RemoveAt(index); ItemRemoved?.Invoke(removed); _selected = -1; _scroll = ContainerLayout.ClampScroll(_scroll, Client, Items.Count, Metrics); Dirty(); Changed?.Invoke(Model);
        }

        // ---- drag and drop (phase 3): items are REFERENCES, dropping never copies or moves the file --------------------
        private int _dragItem = -1; private Point _pressAt; private const string InternalFormat = "Opsis.ContainerItemIndex";

        /// <summary>Adds dropped paths as references at the position under <paramref name="clientPoint"/>. Returns how many were added. Used by the drop handler and by tests.</summary>
        public int AcceptDrop(IEnumerable<string> paths, Point clientPoint)
        {
            if (Model.Kind != ContainerKind.Data) return 0;                         // a portal shows a folder, a note shows text: nothing is dropped into them
            var at = ContainerLayout.DropIndex(clientPoint, Client, Items.Count, Metrics, _scroll);
            var plan = DropRules.Plan(paths, Items, at, p => System.IO.Directory.Exists(p));
            if (plan.Effect != DropEffect.AddReference) return 0;
            Items.InsertRange(plan.InsertAt, plan.Add); ItemsAdded?.Invoke(plan.Add.Select(a => a.Path).ToList());
            _scroll = ContainerLayout.ClampScroll(_scroll, Client, Items.Count, Metrics); Dirty(); Changed?.Invoke(Model);
            return plan.Add.Count;
        }

        /// <summary>Moves an item inside this container (drag to reorder).</summary>
        public void MoveItem(int from, Point clientPoint)
        {
            var at = ContainerLayout.DropIndex(clientPoint, Client, Items.Count, Metrics, _scroll);
            var list = DropRules.Reorder(Items, from, at);
            Items.Clear(); Items.AddRange(list); _selected = -1; Dirty(); Changed?.Invoke(Model);
        }

        /// <summary>Last drag events seen by any container (diagnostics for the real-input test).</summary>
        public static readonly List<string> DragLog = new();
        protected override void OnDragEnter(DragEventArgs e) { base.OnDragEnter(e); if (DragLog.Count < 400) DragLog.Add("enter allowed=" + e.AllowedEffect + " formats=" + string.Join(",", e.Data?.GetFormats() ?? Array.Empty<string>())); e.Effect = e.Data != null && (e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(InternalFormat)) ? DragDropEffects.Link : DragDropEffects.None; }
        protected override void OnDragOver(DragEventArgs e) { base.OnDragOver(e); e.Effect = e.Data != null && (e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(InternalFormat)) ? DragDropEffects.Link : DragDropEffects.None; }
        protected override void OnDragDrop(DragEventArgs e)
        {
            base.OnDragDrop(e);
            if (e.Data == null) return;
            var pt = PointToClient(new Point(e.X, e.Y));
            var drag = _itemDrag;
            if (drag != null && ReferenceEquals(drag.Source, this) && e.Data.GetData(InternalFormat) is int from) MoveItem(from, pt);           // reorder inside the same container
            else if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
            {
                var added = AcceptDrop(files, pt);
                // from another container: Move (default) takes the reference out of the source, Ctrl keeps it there (Copy); the setting can make Copy the default
                if (drag != null && added > 0) drag.Moved = (MotionDesk.Services.AppSettings.Load().ContainerDragCopies ? (e.KeyState & 8) != 0 : (e.KeyState & 8) == 0);
            }
        }

        /// <summary>Screen position of the centre of an item's cell (used by the real-input tests).</summary>
        public Point ItemScreenCenter(int i) { var r = ContainerLayout.CellRect(i, Client, Metrics, _scroll); return PointToScreen(new Point(r.X + r.Width / 2, r.Y + r.Height / 2)); }

        /// <summary>Scroll by wheel notches (positive = down).</summary>
        public void ScrollBy(int pixels) { _scroll = ContainerLayout.ClampScroll(_scroll + pixels, Client, Items.Count, Metrics); Dirty(); }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _hoverTimer.Dispose(); _watch?.Dispose(); _portalTimer.Dispose(); _repaint.Stop(); _repaint.Dispose(); _pane?.Dispose(); _surface.Dispose(); _icons.Dispose(); }
            base.Dispose(disposing);
        }
    }

}
