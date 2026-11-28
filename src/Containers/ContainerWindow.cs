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
        private readonly BoundedCache<(string, int), ItemDetails> _details = new(256);
        internal sealed record ItemDetails(long? Bytes, DateTime Modified);
        internal ItemDetails? Details(string path, int size) => _details.TryGet((path, size), out var value) ? value : null;
        private readonly System.Collections.Concurrent.ConcurrentQueue<((string, int) Key, Bitmap Bmp, ItemDetails Details)> _done = new();
        private readonly HashSet<(string, int)> _pending = new();
        private readonly System.Collections.Concurrent.BlockingCollection<(string, int)> _work = new(new System.Collections.Concurrent.ConcurrentQueue<(string, int)>(), 512);
        private readonly Dictionary<int, Bitmap> _placeholders = new();
        private readonly System.Threading.CancellationTokenSource _cts = new();
        private System.Threading.SynchronizationContext? _ui;   // captured on the first Get (UI thread)
        private readonly object _resultLock = new();
        private bool _disposed;
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
                _worker ??= Task.Factory.StartNew(Work, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
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
                    var bitmap = Extract(key.Item1, key.Item2);
                    var details = ReadDetails(key.Item1);
                    lock (_resultLock)
                    {
                        if (_disposed) { bitmap.Dispose(); return; }
                        _done.Enqueue((key, bitmap, details));
                    }
                    if (Interlocked.Exchange(ref _wake, 1) == 0)
                    {
                        try
                        {
                            _ui?.Post(_ =>
                            {
                                Interlocked.Exchange(ref _wake, 0);
                                if (!_disposed && Drain()) Loaded?.Invoke();
                            }, null);
                        }
                        catch (InvalidOperationException) { }
                        catch (System.ComponentModel.InvalidAsynchronousStateException) { }
                    }
                }
            }
            catch (OperationCanceledException) { }
        }

        private static ItemDetails ReadDetails(string path)
        {
            try
            {
                var file = new FileInfo(path);
                if (file.Exists) return new ItemDetails(file.Length, file.LastWriteTime);
                var folder = new DirectoryInfo(path);
                if (folder.Exists) return new ItemDetails(null, folder.LastWriteTime);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
            return new ItemDetails(null, default);
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
            while (_done.TryDequeue(out var d)) { _pending.Remove(d.Key); _cache.Set(d.Key, d.Bmp); _details.Set(d.Key, d.Details); any = true; }
            return any;
        }

        public void Dispose()
        {
            lock (_resultLock)
            {
                if (_disposed) return;
                _disposed = true;
                while (_done.TryDequeue(out var d)) d.Bmp.Dispose();
            }
            _cts.Cancel();
            _work.CompleteAdding();
            if (_worker is { } worker)
                _ = worker.ContinueWith(_ => ReleaseWorker(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            else ReleaseWorker();
            foreach (var bitmap in _placeholders.Values) bitmap.Dispose();
            _placeholders.Clear();
            _pending.Clear();
            _cache.Dispose();
            _details.Dispose();
        }

        private void ReleaseWorker()
        {
            _work.Dispose();
            _cts.Dispose();
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

        public static void Paint(Graphics g, Size client, Container model, List<ContainerItem> items, ContainerMetrics m, int scroll, int hover, int selected, ContainerIcons icons, bool glass, float scale, bool? highContrast = null, int pulseIndex = -1, float pulse = 0f, IReadOnlySet<string>? selectedPaths = null, bool titleOnly = false, bool? rowsOverride = null)
        {
            var hc = highContrast ?? SystemInformation.HighContrast;   // Windows High Contrast: system colours only, opaque, no glass
            g.SmoothingMode = SmoothingMode.AntiAlias; g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            var material = DesktopMaterials.Get(DesktopSurface.Containers);
            var radius = (int)Math.Round(material.EffectiveRadius(14) * scale);
            var tb = model.TitleBar;
            var accent = DesktopMaterial.OptionalColor(material.AccentColor, MotionDesk.UI.UiTheme.AccentCyan);

            if (hc)
            {
                using var bg = new SolidBrush(SystemColors.Window); g.FillRectangle(bg, 0, 0, client.Width, client.Height);
                using var bp = new Pen(SystemColors.WindowText, Math.Max(1, 2 * scale)); g.DrawRectangle(bp, 1, 1, client.Width - 3, client.Height - 3);
            }
            else if (!glass)   // no GPU glass: a translucent dark surface of its own
            {
                using var path = MotionDesk.UI.UiTheme.RoundedPath(new Rectangle(0, 0, client.Width - 1, client.Height - 1), radius);
                var tint = material.UsesCustom ? Color.FromArgb(Math.Max(12, material.Opacity * 255 / 100), material.Color) : Color.FromArgb(150, 24, 26, 34);
                using var fill = new SolidBrush(tint); g.FillPath(fill, path);
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
            using (var barPath = titleOnly ? MotionDesk.UI.UiTheme.RoundedPath(barRect, Math.Min(radius, barRect.Height / 2)) : TopRounded(barRect, radius))
            {
                using var bar = new SolidBrush(Color.FromArgb(hc ? 255 : Math.Clamp(tb.Opacity, 0, 100) * 255 / 100, barColor)); g.FillPath(bar, barPath);
            }
            var textColor = hc ? SystemColors.HighlightText : ParseColor(tb.TextColor, DesktopMaterial.OptionalColor(material.TitleColor ?? material.ItemColor, Contrast(barColor)));
            using (var font = new Font(string.IsNullOrWhiteSpace(tb.FontFamily) ? material.TitleFont ?? "Segoe UI Variable Display" : tb.FontFamily, Math.Max(1f, tb.FontSize * scale), tb.Bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Point))
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

            if (titleOnly) return;

            // ---- items (only the visible range)
            string mode = ContainerItemLayout.ViewOf(model);
            bool rows = rowsOverride ?? (mode is "list" or "details");
            var layout = new ContainerItemLayout(client, m, rows);
            var (first, last) = layout.VisibleRange(items.Count, scroll);
            var view = layout.Viewport;
            g.SetClip(view);
            using var labelFont = new Font(string.IsNullOrWhiteSpace(model.Appearance.ItemFontFamily) ? material.ItemFont ?? "Segoe UI" : model.Appearance.ItemFontFamily, Math.Max(1f, (material.ItemFontSize > 0 ? material.ItemFontSize : model.Appearance.ItemFontSize > 0 ? model.Appearance.ItemFontSize : 9f) * scale), FontStyle.Regular, GraphicsUnit.Point);
            using var labelFmt = rows
                ? new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap }
                : new StringFormat { Alignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisWord, FormatFlags = StringFormatFlags.LineLimit };
            using var labelBrush = new SolidBrush(hc ? SystemColors.WindowText : DesktopMaterial.OptionalColor(material.ItemColor, Color.FromArgb(240, 255, 255, 255)));
            using var shadowBrush = new SolidBrush(Color.FromArgb(model.Appearance.TextShadow && !hc ? 150 : 0, 0, 0, 0));
            for (var i = first; i <= last; i++)
            {
                var cell = layout.CellRect(i, scroll);
                bool chosen = selectedPaths == null ? i == selected : selectedPaths.Contains(items[i].Path);
                if (i == hover || chosen || i == selected)
                {
                    using var hp = MotionDesk.UI.UiTheme.RoundedPath(cell, (int)(8 * scale));
                    using var hb = hc ? new SolidBrush(chosen ? SystemColors.Highlight : SystemColors.ControlLight) : new SolidBrush(Color.FromArgb(chosen ? 70 : 38, 255, 255, 255)); g.FillPath(hb, hp);
                    if (i == selected) { using var fp = new Pen(hc ? SystemColors.HighlightText : Color.FromArgb(220, 255, 255, 255), Math.Max(1f, 1.5f * scale)) { DashStyle = DashStyle.Dot }; g.DrawPath(fp, hp); }   // visible keyboard focus
                }
                if (i == pulseIndex && pulse > 0f && pulse < 1f)                      // launch effect: a ring grows out of the launched item and fades
                {
                    var grow = (int)(cell.Width * 0.35f * pulse);
                    var ring = Rectangle.Inflate(cell, grow, grow);
                    using var pp = MotionDesk.UI.UiTheme.RoundedPath(ring, (int)(10 * scale)); using var pen = new Pen(Color.FromArgb((int)(190 * (1 - pulse)), accent), Math.Max(1.5f, 2.5f * scale)); g.DrawPath(pen, pp);
                }
                var item = items[i];
                var iconRect = rows
                    ? new Rectangle(cell.X + (int)(4 * scale), cell.Y + (cell.Height - m.IconSize) / 2, m.IconSize, m.IconSize)
                    : new Rectangle(cell.X + (cell.Width - m.IconSize) / 2, cell.Y + (int)(4 * scale), m.IconSize, m.IconSize);
                g.DrawImage(icons.Get(item.Path, m.IconSize), iconRect);
                if (BrokenShortcutTools.IsKnownBroken(item.Path))
                {
                    var bad = new Rectangle(iconRect.Right - 15, iconRect.Bottom - 15, 15, 15);
                    using var warningFill = new SolidBrush(Color.FromArgb(235, 196, 45, 36));
                    g.FillEllipse(warningFill, bad);
                    using var warningFont = new Font("Segoe UI", 9, FontStyle.Bold, GraphicsUnit.Pixel);
                    TextRenderer.DrawText(g, "!", warningFont, bad, Color.White,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
                var name = string.IsNullOrEmpty(item.DisplayName) ? System.IO.Path.GetFileNameWithoutExtension(item.Path) : item.DisplayName;
                float textLeft = iconRect.Right + 7 * scale;
                bool details = mode == "details" && cell.Width >= 260 * scale;
                float nameRight = details ? cell.X + cell.Width * 0.54f : cell.Right - 4 * scale;
                var labelRect = rows
                    ? new RectangleF(textLeft, cell.Y, Math.Max(1, nameRight - textLeft), cell.Height)
                    : new RectangleF(cell.X + 2, iconRect.Bottom + 3 * scale, cell.Width - 4, m.LabelHeight);
                var shadowRect = labelRect; shadowRect.Offset(1, 1);
                g.DrawString(name, labelFont, shadowBrush, shadowRect, labelFmt);
                using var selectedBrush = hc && chosen ? new SolidBrush(SystemColors.HighlightText) : null;
                var foreground = selectedBrush ?? labelBrush;
                g.DrawString(name, labelFont, foreground, labelRect, labelFmt);
                if (details && icons.Details(item.Path, m.IconSize) is { } info)
                {
                    float dateLeft = cell.X + cell.Width * 0.73f;
                    string size = info.Bytes is long bytes ? $"{Math.Max(1, Math.Ceiling(bytes / 1024d)):N0} KB" : "";
                    string date = info.Modified != default ? info.Modified.ToShortDateString() : "";
                    g.DrawString(size, labelFont, foreground, new RectangleF(nameRight, cell.Y, dateLeft - nameRight, cell.Height), labelFmt);
                    g.DrawString(date, labelFont, foreground, new RectangleF(dateLeft, cell.Y, cell.Right - dateLeft, cell.Height), labelFmt);
                }
            }
            g.ResetClip();

            // ---- tab strip (under the title bar) and the text of a note container
            if (m.TopInset > 0 && model.Kind == ContainerKind.Data && model.Tabs.Count > 0)
            {
                using var tf = new Font("Segoe UI", 9f * scale, FontStyle.Regular, GraphicsUnit.Point);
                var sizes = model.Tabs.Select(tab => TextRenderer.MeasureText(g, tab.Name, tf).Width + (int)(16 * scale)).ToArray();
                var tabs = ContainerTabStrip.Arrange(client.Width, m.TitleBarHeight, m.TopInset, m.Margin / 2, model.CurrentTab, sizes);
                for (int ti = 0; ti < tabs.Length; ti++)
                {
                    var tr = tabs[ti];
                    if (tr.IsEmpty) continue;
                    bool cur = ti == model.CurrentTab;
                    if (cur) { using var tabBrush = new SolidBrush(hc ? SystemColors.Highlight : Color.FromArgb(60, 255, 255, 255)); g.FillRectangle(tabBrush, tr); using var up = new Pen(hc ? SystemColors.HighlightText : accent, Math.Max(1.5f, 2 * scale)); g.DrawLine(up, tr.Left, tr.Bottom - 1, tr.Right, tr.Bottom - 1); }
                    TextRenderer.DrawText(g, model.Tabs[ti].Name, tf, tr, hc ? (cur ? SystemColors.HighlightText : SystemColors.WindowText) : Color.FromArgb(cur ? 255 : 190, 255, 255, 255), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                }
            }
            if (model.Kind == ContainerKind.Note)
            {
                using var nf = new Font("Segoe UI", 10f * scale, FontStyle.Regular, GraphicsUnit.Point);
                int left = ContainerItemLayout.LeftInset(m);
                var nr = new Rectangle(left, m.Top + m.Margin / 2, Math.Max(1, client.Width - left - m.Margin), Math.Max(10, client.Height - m.Top - m.Margin));
                TextRenderer.DrawText(g, string.IsNullOrEmpty(model.Source) ? "(double-click to write a note)" : model.Source, nf, nr, hc ? SystemColors.WindowText : Color.FromArgb(string.IsNullOrEmpty(model.Source) ? 130 : 235, 255, 255, 255), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
            }

            // ---- slim scroll indicator
            var max = layout.MaxScroll(items.Count);
            if (max > 0)
            {
                var track = view.Height - 2 * m.Margin; var content = layout.ContentHeight(items.Count);
                var h = Math.Max(24, track * view.Height / Math.Max(1, content)); var y = view.Top + m.Margin + (track - h) * scroll / max;
                using var tp = new SolidBrush(hc ? SystemColors.WindowText : Color.FromArgb(110, 255, 255, 255));
                g.FillRectangle(tp, client.Width - 5 * scale, y, 3 * scale, h);
            }
        }

        private static GraphicsPath TopRounded(Rectangle r, int radius)
        {
            var p = new GraphicsPath();
            if (r.Width <= 0 || r.Height <= 0) return p;
            radius = Math.Clamp(radius, 0, Math.Min(r.Width, r.Height) / 2);
            if (radius == 0) { p.AddRectangle(r); return p; }
            int d = radius * 2;
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
    internal sealed partial class ContainerWindow : Form
    {
        private readonly ContainerIcons _icons = new();
        private readonly LayeredSurface _surface = new();
        private readonly System.Windows.Forms.Timer _repaint = new() { Interval = 8 };
        private GlassPaneBinder? _pane;
        private int _scroll, _hover = -1, _selected = -1;
        private Point _dragStart; private Rectangle _boundsAtDragStart; private enum Drag { None, Move, Resize } private Drag _drag;
        private readonly Func<IEnumerable<Rectangle>> _others;

        // a drag that started in a container: lets another container tell "my own item came back" from "an item of that container" and lets the source remove the item when it was MOVED
        private sealed class ItemDrag
        {
            public required ContainerWindow Source { get; init; }
            public required List<ContainerItem> Original { get; init; }
            public string[] Moved { get; set; } = [];
        }
        private static ItemDrag? _itemDrag;

        public Container Model { get; }
        public bool GlassOn => _pane?.HasPane == true && GlassService.Enabled;
        public event Action<Container>? Changed;
        /// <summary>The user asked for this container's own settings (title bar right-click menu).</summary>
        public event Action<string>? SettingsRequested;
        internal event Action<string, string>? Renamed;
        internal event Action? DeleteRequested;
        internal event Action? PreviewContentChanged;

        /// <summary>Live settings: takes the look (title bar, appearance, title, lock) from the stored container, so a change made anywhere shows at once without recreating the window.</summary>
        public void ApplyStyle(Container stored)
        {
            bool modeChanged = Model.Appearance.HoverRollUp != stored.Appearance.HoverRollUp;
            Model.Extra = stored.Extra;
            _filteredInput = null;
            Model.Title = stored.Title;
            Model.Locked = stored.Locked;
            Model.ExcludeFromQuickHide = stored.ExcludeFromQuickHide;
            Model.Geometry = stored.Geometry;
            Model.RolledUp = stored.RolledUp;
            if (Model.Kind == ContainerKind.Portal && Model.Source != stored.Source)
            {
                Model.Source = stored.Source;
                _portalDirectory = null;
                RefreshPortal();
            }
            Model.UnrolledHeight = stored.UnrolledHeight;
            Model.TitleBar = stored.TitleBar;
            Model.Appearance = stored.Appearance;
            if (modeChanged) ResetAutoHide();
            if (IsHandleCreated)
            {
                if (!IsCollapsed) _scroll = ItemLayout.ClampScroll(_scroll, Items.Count);
                Dirty();
            }
        }
        /// <summary>The global look (theme, glass, tint, performance tier) changed: repaint with the current settings.</summary>
        public void Restyle()
        {
            _pane?.Sync();
            Dirty();
        }
        /// <summary>Raised after files were dropped in (the host may hide their desktop originals) and after an item was removed (the host gives its original back).</summary>
        public event Action<IReadOnlyList<string>>? ItemsAdded; public event Action<string>? ItemRemoved;

        public ContainerWindow(Container model, Func<IEnumerable<Rectangle>>? others = null)
        {
            Model = model;
            _autoCollapsed = model.Appearance.HoverRollUp;
            _others = others ?? (() => Array.Empty<Rectangle>());
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; AllowDrop = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.Opaque, true);
            _repaint.Tick += (_, _) =>
            {
                _repaint.Stop();
                if (Visible && IsHandleCreated && Width > 0 && Height > 0) PresentFrame(Bounds);
            };
            _pane = new GlassPaneBinder(this, 14, () => DesktopMaterials.Get(DesktopSurface.Containers).Mode != "Solid", DesktopSurface.Containers, cornerOverride: () => (int)Math.Round(DesktopMaterials.Get(DesktopSurface.Containers).EffectiveRadius(14) * Scale));
            _pane.PaneChanged += Dirty;
            _icons.Loaded += Dirty;
            _portalTimer.Tick += (_, _) => { _portalTimer.Stop(); RefreshPortal(); };
            _hoverTimer.Tick += (_, _) => UpdateAutoHide();
            Load += (_, _) =>
            {
                RefreshPortal();
                // Probe .lnk files off-thread; renderer only reads the cached answer.
                var paths = Model.Items.Concat(Model.Tabs.SelectMany(t => t.Items)).Select(i => i.Path).ToArray();
                _ = Task.Run(() => BrokenShortcutTools.Find(paths)).ContinueWith(_ =>
                {
                    if (IsHandleCreated && !IsDisposed)
                        try { BeginInvoke(new Action(Dirty)); } catch (InvalidOperationException) { }
                }, TaskScheduler.Default);
            };
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
        private bool UsesRows => ContainerItemLayout.ViewOf(Model) is "list" or "details" ||
            (ContainerItemLayout.ViewOf(Model) == "grid" &&
             Screen.FromRectangle(Bounds) is { Bounds: var monitorBounds } &&
             monitorBounds.Height > monitorBounds.Width * 1.15 &&
             Bounds.Width < 420); // portrait secondary displays: readable single-column density
        private ContainerItemLayout ItemLayout => new(Client, Metrics, UsesRows);
        private ContainerMetrics Metrics => ContainerMetrics.Default(Scale, Model.TitleBar.Height, UsesRows ? 20 : Model.Appearance.IconSize > 0 ? Model.Appearance.IconSize : 48) with { TopInset = TabsOn ? (int)(26 * Scale) : 0 };

        // The items shown now: the container's own list, the list of the current tab, or - for a portal - a LIVE listing of its folder (never stored). A note has none.
        private readonly List<ContainerItem> _portalItems = new(); private static readonly List<ContainerItem> NoItems = new();
        private readonly Dictionary<(string Target, int Count), List<ContainerItem>> _organizationMerged = new();
        private IReadOnlyDictionary<string, List<ContainerItem>>? _organizationRoutes;
        private List<ContainerItem>? _organizationSmart;
        private bool IsSmartFolder => LayoutImport.HasSmart(Model);

        private List<ContainerItem> BaseItems => Model.Kind switch
        {
            ContainerKind.Portal => _portalItems,
            ContainerKind.Note => NoItems,
            _ => TabsOn ? Model.Tabs[Math.Clamp(Model.CurrentTab, 0, Model.Tabs.Count - 1)].Items : Model.Items
        };

        public List<ContainerItem> Items => FilterItems(Sorted(UnsortedItems));
        private List<ContainerItem> UnsortedItems
        {
            get
            {
                if (IsSmartFolder) return _organizationSmart ?? NoItems;
                var original = BaseItems;
                if (_organizationRoutes == null || Model.Kind != ContainerKind.Data) return original;
                string target = Model.Id;
                if (TabsOn)
                {
                    int index = Math.Clamp(Model.CurrentTab, 0, Model.Tabs.Count - 1);
                    if (LayoutImport.TabTarget(Model.Tabs[index]) is { } tabTarget) target = tabTarget;
                    else if (index != 0) return original;
                }
                if (!_organizationRoutes.TryGetValue(target, out var added) || added.Count == 0) return original;
                var key = (target, original.Count);
                if (!_organizationMerged.TryGetValue(key, out var visible))
                {
                    visible = original.Concat(added).DistinctBy(item => item.Path, StringComparer.OrdinalIgnoreCase).ToList();
                    _organizationMerged[key] = visible;
                }
                return visible;
            }
        }

        internal void SetOrganizationItems(List<ContainerItem>? smart, IReadOnlyDictionary<string, List<ContainerItem>> routes)
        {
            if (IsSmartFolder && smart != null && _organizationSmart != null &&
                smart.Select(item => item.Path).SequenceEqual(_organizationSmart.Select(item => item.Path), StringComparer.OrdinalIgnoreCase))
                return;
            bool hadRoutes = _organizationRoutes != null;
            bool relevantRoutes = routes.ContainsKey(Model.Id) || Model.Tabs.Any(tab =>
                LayoutImport.TabTarget(tab) is { } id && routes.ContainsKey(id));
            _organizationRoutes = relevantRoutes ? routes : null;
            _organizationSmart = smart;
            if (!IsSmartFolder && !hadRoutes && !relevantRoutes) return;
            _organizationMerged.Clear();
            _sortCache = null; _filteredInput = null;
            _selected = Math.Min(_selected, Items.Count - 1);
            _hover = Math.Min(_hover, Items.Count - 1);
            if (AutoHeightOf(Model)) LayoutHeight();
            _scroll = ItemLayout.ClampScroll(_scroll, Items.Count);
            Dirty();
            PreviewContentChanged?.Invoke();
        }

        private bool CanEditItems => Model.Kind == ContainerKind.Data && !Model.Locked && !IsSmartFolder;

        private System.IO.FileSystemWatcher? _watch; private readonly System.Windows.Forms.Timer _portalTimer = new() { Interval = 400 };

        private int _portalRefreshRevision;

        /// <summary>Reads a bounded folder listing off the UI thread and ignores results for a previous folder.</summary>
        public async void RefreshPortal()
        {
            // #53: a hidden folder container must not watch or enumerate disk contents.
            if (Model.Kind != ContainerKind.Portal || !Visible) return;
            int revision = ++_portalRefreshRevision;
            string? requested = PortalDirectory;
            string? root = Model.Source;
            try
            {
                var result = await Task.Run(() =>
                {
                    var directory = Directory.Exists(requested) ? requested : root;
                    if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                        return (Directory: (string?)null, Items: new List<ContainerItem>());
                    var options = new EnumerationOptions
                    {
                        IgnoreInaccessible = true, RecurseSubdirectories = false,
                        AttributesToSkip = FileAttributes.Hidden | FileAttributes.System
                    };
                    var items = new DirectoryInfo(directory).EnumerateFileSystemInfos("*", options).Take(6000)
                        .Select(item => new ContainerItem { Path = item.FullName, DisplayName = item.Name, IsFolder = item is DirectoryInfo })
                        .OrderByDescending(item => item.IsFolder).ThenBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                        .Take(400).ToList();
                    return (Directory: directory, Items: items);
                });
                if (IsDisposed || revision != _portalRefreshRevision) return;
                string? previous = Items.ElementAtOrDefault(_selected)?.Path;
                _portalItems.Clear();
                _portalItems.AddRange(result.Items);
                _sortCache = null; _filteredInput = null;
                if (!string.Equals(requested, result.Directory, StringComparison.OrdinalIgnoreCase)) _portalDirectory = result.Directory;
                RestoreFocus(previous);
                if (_watch == null || !string.Equals(_watch.Path, result.Directory, StringComparison.OrdinalIgnoreCase))
                {
                    _watch?.Dispose();
                    _watch = null;
                    if (result.Directory != null)
                    {
                        _watch = new FileSystemWatcher(result.Directory) { IncludeSubdirectories = false };
                        void Kick(object? sender, EventArgs args)
                        {
                            try
                            {
                                if (Visible && IsHandleCreated && !IsDisposed)
                                    BeginInvoke(() => { if (Visible && !IsDisposed) { _portalTimer.Stop(); _portalTimer.Start(); } });
                            }
                            catch (InvalidOperationException) { }
                        }
                        _watch.Created += Kick; _watch.Deleted += Kick; _watch.Renamed += Kick; _watch.Error += Kick;
                        _watch.EnableRaisingEvents = Visible;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                if (revision != _portalRefreshRevision || IsDisposed) return;
            }
            if (IsDisposed || revision != _portalRefreshRevision) return;
            if (AutoHeightOf(Model)) LayoutHeight();
            _scroll = ItemLayout.ClampScroll(_scroll, Items.Count);
            Dirty();
            PreviewContentChanged?.Invoke();
        }

        private string? Ask(string prompt, string initial)
        {
            using var f = new MotionDesk.UI.AppDialog { Text = prompt, StartPosition = FormStartPosition.CenterScreen, ClientSize = new Size(380, 112), FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, TopMost = true };
            var box = new TextBox { Location = new Point(14, 14), Width = 352, Text = initial }; var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(286, 66), Size = new Size(80, 30) };
            f.Controls.AddRange(new Control[] { box, ok }); f.AcceptButton = ok; box.SelectAll();
            MotionDesk.UI.MainWindow.StyleStudioDialog(f);
            _menuInteractions++;
            try { return f.ShowDialog() == DialogResult.OK ? box.Text : null; }
            finally { _menuInteractions--; }
        }

        private void EditNote()
        {
            using var f = new MotionDesk.UI.AppDialog { Text = Model.Title, StartPosition = FormStartPosition.CenterScreen, ClientSize = new Size(460, 320), TopMost = true };
            var box = new TextBox { Multiline = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, Text = Model.Source ?? "", AcceptsReturn = true };
            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Dock = DockStyle.Bottom, Height = 32 };
            f.Controls.Add(box); f.Controls.Add(ok);
            MotionDesk.UI.MainWindow.StyleStudioDialog(f);
            if (f.ShowDialog() == DialogResult.OK) { Model.Source = box.Text; Dirty(); Changed?.Invoke(Model); }
        }

        // ---- tabs ----
        private List<Rectangle> TabRects()
        {
            if (!TabsOn || IsCollapsed) return [];
            using var font = new Font("Segoe UI", 9f * Scale, FontStyle.Regular, GraphicsUnit.Point);
            int[] sizes = Model.Tabs.Select(tab => TextRenderer.MeasureText(tab.Name, font).Width + (int)(16 * Scale)).ToArray();
            return ContainerTabStrip.Arrange(Width, Metrics.TitleBarHeight, Metrics.TopInset, Metrics.Margin / 2,
                Model.CurrentTab, sizes).ToList();
        }
        public void SelectTab(int i)
        {
            if (!TabsOn || i < 0 || i >= Model.Tabs.Count || i == Model.CurrentTab) return;
            _tabScroll[Model.Tabs[Math.Clamp(Model.CurrentTab, 0, Model.Tabs.Count - 1)]] = _scroll;
            Model.CurrentTab = i;
            _scroll = ItemLayout.ClampScroll(_tabScroll.GetValueOrDefault(Model.Tabs[i]), Items.Count);
            ClearSelection();
            Dirty();
            Changed?.Invoke(Model);
        }
        public void AddTab(string name)
        {
            if (Model.Kind != ContainerKind.Data) return;
            if (Model.Tabs.Count == 0) { Model.Tabs.Add(new ContainerTab { Name = "Tab 1", Items = new List<ContainerItem>(Model.Items) }); Model.Items.Clear(); }     // enabling tabs moves the icons into the first tab
            Model.Tabs.Add(new ContainerTab { Name = string.IsNullOrWhiteSpace(name) ? "Tab " + (Model.Tabs.Count + 1) : name });
            ContainerHost.EnsureTabTargets(Model);
            Model.TabsEnabled = true; Model.CurrentTab = Model.Tabs.Count - 1; _scroll = 0; ClearSelection(); Dirty(); Changed?.Invoke(Model);
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

        public void Dirty()
        {
            // Native invalidation can follow UpdateLayeredWindow. Only content changes schedule a frame.
            if (IsHandleCreated && Visible && !Disposing && !_repaint.Enabled) _repaint.Start();
        }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Dirty(); }
        protected override void SetVisibleCore(bool value)
        {
            if (value && !Visible && Width > 0 && Height > 0)
            {
                _ = Handle;
                PresentFrame(Bounds);
            }
            base.SetVisibleCore(value);
        }
        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            UpdateAutoHideTimer();
            if (Visible) UpdateAutoHide();
            if (Model.Kind == ContainerKind.Portal)
            {
                if (_watch != null) _watch.EnableRaisingEvents = Visible;
                if (Visible) RefreshPortal();
                else { _portalRefreshRevision++; _portalTimer.Stop(); }
            }
            if (Visible) Dirty(); else _repaint.Stop();
        }
        protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); if (!IsCollapsed) _scroll = ItemLayout.ClampScroll(_scroll, Items.Count); Dirty(); }
        protected override void OnDpiChanged(DpiChangedEventArgs e) { base.OnDpiChanged(e); Dirty(); }
        protected override void OnPaint(PaintEventArgs e) { }
        protected override void OnPaintBackground(PaintEventArgs e) { }

        /// <summary>Draws the window; also usable for a test (renders into a bitmap of the same size).</summary>
        public void RenderTo(Graphics g, bool? highContrast = null) => PaintFrame(g, Client, highContrast);

        private void PaintFrame(Graphics g, Size size, bool? highContrast = null)
        {
            var pulse = PulseProgress();
            ContainerRenderer.Paint(g, size, Model, Items, Metrics, _scroll, _hover, _selected, _icons, GlassOn, Scale, highContrast, _pulseIndex, pulse, _selection, titleOnly: IsCollapsed, rowsOverride: UsesRows);
            if (!IsCollapsed) PaintTabIndicator(g);
        }

        // ---- launch effect ("Pulse", per container): a ring around the launched item for 450 ms
        private int _pulseIndex = -1; private DateTime _pulseAt;
        private float PulseProgress() { if (_pulseIndex < 0) return 0f; var p = (float)((DateTime.UtcNow - _pulseAt).TotalMilliseconds / 450.0); if (p >= 1f) { _pulseIndex = -1; return 0f; } return p; }

        private bool PresentFrame(Rectangle target)
        {
            using (var g = _surface.Begin(target.Width, target.Height)) PaintFrame(g, target.Size);
            bool updated = _surface.Present(Handle, target.Location);
            if (_pulseIndex >= 0) Dirty();
            return updated;
        }

        private void SetFrameBounds(Rectangle target)
        {
            if (target == Bounds) return;
            if (target.Size != Size && Visible && IsHandleCreated)
            {
                if (!IsCollapsed) _scroll = new ContainerItemLayout(target.Size, Metrics, UsesRows).ClampScroll(_scroll, Items.Count);
                // The native call applies the size and the new pixels in the same update.
                if (!PresentFrame(target)) Dirty();
            }
            if (Bounds != target) Bounds = target;
            _pane?.Sync();
        }

        // ---- interaction ---------------------------------------------------------------------------------------------
        private Rectangle Grip => new(Width - (int)(18 * Scale), Height - (int)(18 * Scale), (int)(18 * Scale), (int)(18 * Scale));
        private Rectangle WorkArea => Screen.FromControl(this).WorkingArea;

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Right) { if (_pressedTab >= 0) CancelTabDrag(); else ShowMenu(e.Location); return; }
            if (e.Button == MouseButtons.Left && TabAt(e.Location) is int tab && tab >= 0) { PressTab(tab); return; }
            if (e.Button != MouseButtons.Left) return;
            _dragStart = Cursor.Position; _boundsAtDragStart = Bounds;
            if (Grip.Contains(e.Location) && !IsCollapsed) _drag = Drag.Resize;
            else if (e.Y < Metrics.TitleBarHeight) _drag = Drag.Move;
            else
            {
                int hit = ItemLayout.HitTest(e.Location, Items.Count, _scroll);
                bool extend = (ModifierKeys & Keys.Shift) != 0, toggle = (ModifierKeys & Keys.Control) != 0;
                if (hit < 0 || extend || toggle || !_selection.Contains(Items[hit].Path))
                    UpdateSelection(hit, extend, toggle);
                _dragItem = hit;
                _pressAt = e.Location;
                Dirty();
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (UpdateTabDrag()) return;
            if (_dragItem >= 0 && _dragItem < Items.Count && BaseItems.Any(item => item.Path == Items[_dragItem].Path) && (e.Button & MouseButtons.Left) != 0 && CanEditItems &&
                (Math.Abs(e.X - _pressAt.X) > SystemInformation.DragSize.Width || Math.Abs(e.Y - _pressAt.Y) > SystemInformation.DragSize.Height))
            {
                var idx = _dragItem;
                _dragItem = -1;
                string[] selected = SelectionPaths().Where(path => BaseItems.Any(item => item.Path == path)).ToArray();
                if (selected.Length == 0) selected = [Items[idx].Path];
                var data = new DataObject();
                data.SetData(InternalFormat, idx);
                data.SetData(DataFormats.FileDrop, selected);
                _itemDrag = new ItemDrag { Source = this, Original = BaseItems };
                try { DoDragDrop(data, DragDropEffects.Link | DragDropEffects.Copy); }
                finally
                {
                    var drag = _itemDrag;
                    _itemDrag = null;
                    if (drag != null && drag.Moved.Length > 0) RemoveReferences(drag.Moved, restoreOriginal: false, from: drag.Original);
                }
                return;
            }
            if (_drag == Drag.None)
            {
                var h = e.Y < Metrics.TitleBarHeight ? -1 : ItemLayout.HitTest(e.Location, Items.Count, _scroll);
                if (h != _hover) { _hover = h; Dirty(); }
                Cursor = Grip.Contains(e.Location) && !IsCollapsed ? Cursors.SizeNWSE : Cursors.Default;
                return;
            }
            var d = new Size(Cursor.Position.X - _dragStart.X, Cursor.Position.Y - _dragStart.Y);
            Rectangle wanted = _drag == Drag.Move ? new Rectangle(_boundsAtDragStart.Location + d, _boundsAtDragStart.Size) : ContainerBehavior.Resize(_boundsAtDragStart, _boundsAtDragStart.Size + d, WorkArea);
            if (_drag == Drag.Move) wanted = ContainerBehavior.Snap(wanted, _others(), WorkArea, (int)(12 * Scale));
            wanted = ContainerBehavior.ApplyLock(Model.Locked, Bounds, wanted);
            SetFrameBounds(wanted);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left && ReleaseTab()) return;
            if (e.Button == MouseButtons.Left && _dragItem >= 0 && ModifierKeys == Keys.None && Model.Appearance.OpenOnSingleClick && e.Y >= Metrics.TitleBarHeight && ItemLayout.HitTest(e.Location, Items.Count, _scroll) == _dragItem) { var open = _dragItem; _dragItem = -1; OpenItem(open); return; }
            if (e.Button == MouseButtons.Left && _dragItem >= 0 && ModifierKeys == Keys.None)
                UpdateSelection(_dragItem, false, false);
            AfterMouseUp(e);
            Dirty();
        }
        private void AfterMouseUp(MouseEventArgs e)
        {
            _dragItem = -1;
            CommitGeometry();
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (!Capture) { CancelTabDrag(); CommitGeometry(); }
        }

        private void CommitGeometry()
        {
            if (_drag == Drag.None) return;
            _drag = Drag.None;
            if (Bounds == _boundsAtDragStart) { UpdateAutoHide(); return; }
            _expandedBounds = new Rectangle(Location, new Size(Width, IsCollapsed ? Math.Max(60, _expandedBounds.Height) : Height));
            Model.UnrolledHeight = _expandedBounds.Height;
            Model.Geometry = LegacyImporter.Normalize(_expandedBounds.X, _expandedBounds.Y, _expandedBounds.Width,
                _expandedBounds.Height, Screen.AllScreens.Select(screen => new MonitorInfo(screen.DeviceName, screen.Bounds, screen.Primary)).ToList());
            Changed?.Invoke(Model);
            UpdateAutoHide();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hover != -1) { _hover = -1; Dirty(); }
            UpdateAutoHide();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            UpdateAutoHide();
        }

        private readonly System.Windows.Forms.Timer _hoverTimer = new() { Interval = 65 };
        private readonly ContainerHoverDelay _hoverDelay = new();
        private Rectangle _expandedBounds;
        private bool _autoCollapsed, _waitForPointerExit, _dropActive;
        private bool _expandedForExternalDrop;
        private int _menuInteractions;

        // #56: expand a collapsed container only while it is a valid drop target.
        // Do not persist the temporary roll-up state into the model.
        private void ExpandForDrop()
        {
            if (_expandedForExternalDrop || !IsCollapsed) return;
            _expandedForExternalDrop = true;
            if (Model.Appearance.HoverRollUp) _autoCollapsed = false;
            else Model.RolledUp = false;
            _hoverDelay.Reset();
            LayoutHeight();
        }

        private void EndDropExpansion()
        {
            if (!_expandedForExternalDrop) return;
            _expandedForExternalDrop = false;
            if (Model.Appearance.HoverRollUp) _autoCollapsed = true;
            else Model.RolledUp = true;
            LayoutHeight();
        }

        internal bool IsCollapsed => Model.Appearance.HoverRollUp ? _autoCollapsed : Model.RolledUp;

        internal void SetPlacement(Rectangle expandedBounds)
        {
            if (_drag != Drag.None) return;
            _expandedBounds = expandedBounds;
            LayoutHeight();
        }

        private void LayoutHeight()
        {
            if (_expandedBounds.IsEmpty) return;
            var area = Screen.FromRectangle(_expandedBounds).WorkingArea;
            var expanded = _expandedBounds;
            if (AutoHeightOf(Model) && Model.Kind != ContainerKind.Note)
            {
                var layout = new ContainerItemLayout(expanded.Size, Metrics, UsesRows);
                int maximum = Math.Max(100, area.Height);
                expanded.Height = Math.Clamp(Metrics.Top + layout.ContentHeight(Items.Count), 96, maximum);
            }
            // Use the expanded window's reachable anchor in both states. This keeps the title in
            // place even on a secondary monitor or when full height requires clamping to its work area.
            expanded = ContainerBehavior.Unroll(expanded, expanded.Height, area);
            var target = IsCollapsed ? ContainerHoverDelay.TitleBounds(expanded, Metrics.TitleBarHeight) : expanded;
            SetFrameBounds(target);
        }

        private void ResetAutoHide()
        {
            _autoCollapsed = false;
            _waitForPointerExit = false;
            _hoverDelay.Reset();
            UpdateAutoHideTimer();
        }

        private void UpdateAutoHideTimer()
        {
            if (Visible && Model.Appearance.HoverRollUp) _hoverTimer.Start();
            else { _hoverTimer.Stop(); _hoverDelay.Reset(); }
        }

        private void UpdateAutoHide()
        {
            if (!Visible || !IsHandleCreated || !Model.Appearance.HoverRollUp) return;
            var pointer = Cursor.Position;
            bool inside = Bounds.Contains(pointer);
            if (!inside) _waitForPointerExit = false;
            if (_waitForPointerExit) return;
            // A covered title bar cannot be opened through another app's window.
            if (inside && IsCollapsed && !_dropActive)
                inside = WindowFromPoint(pointer) == Handle || !_tabMarker.IsEmpty;
            bool busy = _drag != Drag.None || Capture || _menuInteractions > 0 ||
                (!IsCollapsed && (ContainsFocus || _dropActive || _itemDrag?.Source == this));
            if (_hoverDelay.Update(IsCollapsed, inside, busy, Model.Appearance.HoverDelayMs, Environment.TickCount64) is not bool collapse)
                return;
            _autoCollapsed = collapse;
            LayoutHeight();
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(Point point);

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (TabsOn && TabAt(e.Location) >= 0 && e.Delta != 0 && ModifierKeys == Keys.None)
            {
                SelectTab(Math.Clamp(Model.CurrentTab - Math.Sign(e.Delta), 0, Model.Tabs.Count - 1));
                return;
            }
            if ((ModifierKeys & Keys.Control) != 0 && !UsesRows && e.Delta != 0)
            {
                int[] sizes = [32, 48, 64, 96];
                int current = Model.Appearance.IconSize;
                int next = e.Delta > 0 ? sizes.FirstOrDefault(size => size > current, sizes[^1]) : sizes.LastOrDefault(size => size < current, sizes[0]);
                Model.Appearance.IconSize = next;
                Changed?.Invoke(Model);
                Dirty();
                return;
            }
            var lines = SystemInformation.MouseWheelScrollLines;
            int distance = lines < 0 ? ItemLayout.Viewport.Height : Math.Max(0, lines) * ItemLayout.RowHeight;
            ScrollBy((int)Math.Round(-e.Delta / 120d * distance));
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button != MouseButtons.Left) return;
            if (e.Y >= Metrics.TitleBarHeight)
            {
                if (Model.Kind == ContainerKind.Note) { EditNote(); return; }
                var hit = ItemLayout.HitTest(e.Location, Items.Count, _scroll);
                if (hit >= 0) OpenItem(hit);                 // double click launches the item
                return;
            }
            ToggleRollUp();
        }

        public void ToggleRollUp()
        {
            if (Model.Appearance.HoverRollUp)
            {
                _autoCollapsed = !IsCollapsed;
                _waitForPointerExit = _autoCollapsed && Bounds.Contains(Cursor.Position);
                _hoverDelay.Reset();
            }
            else
            {
                Model.RolledUp = !Model.RolledUp;
                Model.UnrolledHeight = Math.Max(60, _expandedBounds.Height);
                Changed?.Invoke(Model);
            }
            LayoutHeight();
        }

        private void RenameContainer()
        {
            string? title = Ask(LocalizationManager.T("DCM.RenameContainer").TrimEnd('…'), Model.Title)?.Trim();
            if (string.IsNullOrEmpty(title) || title == Model.Title) return;
            Renamed?.Invoke(Model.Id, title);
        }

        // ---- keyboard (phase 7/8): the window does not steal focus, so keys work after FocusForKeyboard() (hotkey / tray) or when Windows activates it for assistive tools ----
        public int Selected => _selected;
        public void FocusForKeyboard() { if (Model.Appearance.HoverRollUp && IsCollapsed) { _autoCollapsed = false; _waitForPointerExit = false; _hoverDelay.Reset(); LayoutHeight(); } if (_selected < 0 && Items.Count > 0) { UpdateSelection(0, false, false); Dirty(); } Activate(); }

        /// <summary>Handles one navigation key; returns true when it was used. Public so it can be tested without real keyboard input.</summary>
        public bool HandleKey(Keys key)
        {
            if (key == Keys.F2) { RenameContainer(); return true; }
            if (IsCollapsed && key != Keys.Apps && key != (Keys.F10 | Keys.Shift)) return false;
            if (key == (Keys.Control | Keys.A)) { SelectAllItems(); return true; }
            if (key == (Keys.Control | Keys.C) || key == (Keys.Control | Keys.X)) { CopySelection(key == (Keys.Control | Keys.X)); return true; }
            if (key == (Keys.Control | Keys.V)) { PasteSelection(); return true; }
            if (key == (Keys.Control | Keys.Z)) { UndoFileTransfer(); return true; }
            if (key == Keys.F5 && Model.Kind == ContainerKind.Portal) { RefreshPortal(); return true; }
            if ((key == Keys.Back || key == (Keys.Alt | Keys.Up)) && PortalUp()) return true;
            if (TabsOn && (key == (Keys.Control | Keys.Tab) || key == (Keys.Control | Keys.Shift | Keys.Tab)))
            {
                int step = (key & Keys.Shift) != 0 ? -1 : 1;
                SelectTab((Model.CurrentTab + Model.Tabs.Count + step) % Model.Tabs.Count);
                return true;
            }
            if (key == (Keys.Control | Keys.Space)) { UpdateSelection(_selected, false, true); Dirty(); return true; }
            var cols = ItemLayout.Columns;
            NavKey? nav = (key & Keys.Alt) != 0 ? null : (key & Keys.KeyCode) switch { Keys.Left => NavKey.Left, Keys.Right => NavKey.Right, Keys.Up => NavKey.Up, Keys.Down => NavKey.Down, Keys.Home => NavKey.Home, Keys.End => NavKey.End, Keys.PageUp => NavKey.PageUp, Keys.PageDown => NavKey.PageDown, _ => null };
            if (nav != null)
            {
                int index = ContainerNavigation.Move(_selected, nav.Value, cols, Items.Count, Math.Max(1, ItemLayout.Viewport.Height / Math.Max(1, ItemLayout.RowHeight)));
                if ((key & Keys.Control) != 0 && (key & Keys.Shift) == 0) _selected = index;
                else UpdateSelection(index, (key & Keys.Shift) != 0, false);
                if (_selected >= 0) _scroll = ItemLayout.ScrollIntoView(_selected, Items.Count, _scroll);
                Dirty(); return true;
            }
            if (key == Keys.Enter && _selected >= 0 && _selected < Items.Count) { OpenItem(_selected); return true; }
            if (key == Keys.Delete && CanEditItems) { RemoveReferences(SelectionPaths()); return true; }
            if (key == Keys.Apps || key == (Keys.F10 | Keys.Shift)) { ShowMenu(_selected >= 0 ? ItemLayout.CellRect(_selected, _scroll).Location : new Point(Metrics.Margin, Metrics.TitleBarHeight)); return true; }
            return false;
        }

        /// <summary>Raised with the path of every item that is launched; when <see cref="SuppressLaunch"/> is set (tests) the item is only reported, not started.</summary>
        public event Action<string>? ItemLaunched; public static bool SuppressLaunch;
        public void OpenItem(int index)
        {
            if (string.Equals(Model.Appearance.LaunchEffect, "Pulse", StringComparison.OrdinalIgnoreCase)) { _pulseIndex = index; _pulseAt = DateTime.UtcNow; Dirty(); }
            try { var it = Items[index]; ItemLaunched?.Invoke(it.Path); if (SuppressLaunch) return;
                if (Model.Kind == ContainerKind.Portal && it.IsFolder) { OpenPortalFolder(it.Path); return; }
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(it.Path) { UseShellExecute = true, Arguments = it.Arguments ?? "" }); }
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
                var p = _w.PointToClient(new Point(x, y)); var i = _w.ItemLayout.HitTest(p, _w.Items.Count, _w._scroll);
                return i >= 0 ? GetChild(i) : this;
            }
        }

        private sealed class ItemAccessible : AccessibleObject
        {
            private readonly AccessibleObject _parent; private readonly ContainerWindow _w; private readonly int _i;
            public ItemAccessible(AccessibleObject parent, ContainerWindow w, int i) { _parent = parent; _w = w; _i = i; }
            private ContainerItem? Item => _w.Items.ElementAtOrDefault(_i);
            public override string? Name
            {
                get => Item is { } item ? (string.IsNullOrEmpty(item.DisplayName) ? Path.GetFileNameWithoutExtension(item.Path) : item.DisplayName) : null;
                set { }
            }
            public override string? Description => Item?.Path;
            public override AccessibleObject? Parent => _parent;
            public override AccessibleRole Role => AccessibleRole.ListItem;
            public override string DefaultAction => "Open";
            public override void DoDefaultAction() => _w.OpenItem(_i);
            public override AccessibleStates State => AccessibleStates.Selectable | AccessibleStates.Focusable |
                (Item is { } item && _w._selection.Contains(item.Path) ? AccessibleStates.Selected : 0) | (_w._selected == _i ? AccessibleStates.Focused : 0);
            public override Rectangle Bounds => _w.RectangleToScreen(_w.ItemLayout.CellRect(_i, _w._scroll));
            public override void Select(AccessibleSelection flags) { if ((flags & AccessibleSelection.TakeSelection) != 0) { _w.UpdateSelection(_i, false, false); _w._scroll = _w.ItemLayout.ScrollIntoView(_i, _w.Items.Count, _w._scroll); _w.Dirty(); } }
        }

        // ---- context menu: the container's OWN small menu (no shell extensions loaded into our process); every action is isolated by try/catch ----
        private void ShowMenu(Point at)
        {
            var idx = at.Y < Metrics.TitleBarHeight ? -1 : ItemLayout.HitTest(at, Items.Count, _scroll);
            var menu = new ContextMenuStrip();
            _menuInteractions++;
            string Local(string en, string el) => MotionDesk.UI.MainWindow.StudioText(en, el);
            if (idx >= 0 && !_selection.Contains(Items[idx].Path)) { UpdateSelection(idx, false, false); Dirty(); }
            void Add(string text, Action act, bool enabled = true)
            {
                var it = menu.Items.Add(text); it.Enabled = enabled;
                it.Click += (_, _) =>
                {
                    _menuInteractions++;
                    try { act(); }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine("container menu: " + ex.Message); }
                    finally { _menuInteractions--; }
                };
            }
            if (idx >= 0)
            {
                var item = Items[idx];
                Add(Local("Open", "Άνοιγμα"), () => OpenItem(idx));
                Add(Local("Copy", "Αντιγραφή"), () => CopySelection(false));
                Add(Local("Cut", "Αποκοπή"), () => CopySelection(true), CanEditItems || CanWritePortal);
                Add("Show in folder", () => System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + item.Path + "\""));
                Add(Local("Remove selected references", "Αφαίρεση επιλεγμένων αναφορών"), () => RemoveReferences(SelectionPaths()), CanEditItems);
                menu.Items.Add(new ToolStripSeparator());
            }
            if (CanEditItems || CanWritePortal) Add(Local("Paste", "Επικόλληση"), PasteSelection);
            Add(Local("Undo file transfer", "Αναίρεση μεταφοράς αρχείων"), UndoFileTransfer, Transfers.CanUndo);
            if (Model.Kind == ContainerKind.Data && !IsSmartFolder)
            {
                if (ContainerHost.Current is { } host)
                {
                    var merge = new ToolStripMenuItem(Local("Merge as tabs into…", "Συγχώνευση ως καρτέλες σε…"));
                    foreach (var other in host.Containers().Where(item => item.Id != Model.Id && ContainerHost.SupportsTabs(item)))
                        merge.DropDownItems.Add(other.Title, null, (_, _) => host.MergeIntoTabs(Model.Id, other.Id));
                    if (merge.DropDownItems.Count > 0) menu.Items.Add(merge);
                    if (TabsOn && Model.Tabs.Count > 1)
                    {
                        int detach = TabAt(at);
                        Add(Local("Detach tab", "Απόσπαση καρτέλας"), () => host.DetachTab(Model.Id, detach >= 0 ? detach : Model.CurrentTab));
                    }
                    var copyTo = new ToolStripMenuItem(Local("Copy to another workspace…", "Αντιγραφή σε άλλο workspace…"));
                    foreach (var target in host.OtherLayoutTargets())
                    {
                        int targetIndex = target.Index;
                        copyTo.DropDownItems.Add(target.Name, null, (_, _) => host.CopyContainerIntoLayout(Model.Id, targetIndex));
                    }
                    copyTo.DropDownItems.Add(Local("New workspace…", "Νέο workspace…"), null, (_, _) =>
                    {
                        var newName = Ask(Local("Workspace name", "Όνομα workspace"), "New workspace");
                        if (newName == null) return;
                        if (MessageBox.Show(this,
                            Local("Create a managed workspace copy of this container? No user files are copied; the current layout is backed up.",
                                  "Δημιουργία διαχειριζόμενου workspace; Δεν αντιγράφονται αρχεία και κρατείται backup διάταξης."),
                            "Opsis", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                            MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
                        if (!host.CopyContainerIntoNewLayout(Model.Id, newName))
                            MessageBox.Show(this, Local("Name unavailable or workspace limit reached", "Το όνομα υπάρχει ήδη ή εξαντλήθηκε το όριο"), "Opsis");
                    });
                    menu.Items.Add(copyTo);
                }
                Add("New tab...", () => { var n = Ask("Tab name", "Tab " + (Model.Tabs.Count + 1)); if (n != null) AddTab(n); });
                if (TabsOn) { Add("Rename this tab...", () => { var t = Model.Tabs[Math.Clamp(Model.CurrentTab, 0, Model.Tabs.Count - 1)]; var n = Ask("Tab name", t.Name); if (n != null) { t.Name = n; Dirty(); Changed?.Invoke(Model); } }); Add("Remove this tab (icons move to the next one)", () => RemoveTab(Model.CurrentTab)); }
                int clickedTab = TabAt(at);
                if (TabsOn)
                {
                    int tab = clickedTab >= 0 ? clickedTab : Model.CurrentTab;
                    Add(Local("Move tab left", "Μετακίνηση καρτέλας αριστερά"), () => MoveTab(tab, tab - 1), tab > 0);
                    Add(Local("Move tab right", "Μετακίνηση καρτέλας δεξιά"), () => MoveTab(tab, tab + 1), tab + 1 < Model.Tabs.Count);
                }
                menu.Items.Add(new ToolStripSeparator());
            }
            else if (Model.Kind == ContainerKind.Portal)
            {
                Add(Local("Parent folder", "Γονικός φάκελος"), () => PortalUp(), _portalDirectory != null && _portalDirectory != Model.Source);
                Add(Local("Portal root", "Αρχικός φάκελος"), () => { if (Model.Source is { } root) OpenPortalFolder(root); });
                Add("Choose folder...", () => { using var fb = new FolderBrowserDialog { SelectedPath = Model.Source ?? "" }; if (fb.ShowDialog() == DialogResult.OK) { Model.Source = fb.SelectedPath; _portalDirectory = null; RefreshPortal(); Changed?.Invoke(Model); } });
                menu.Items.Add(new ToolStripSeparator());
            }
            else if (Model.Kind == ContainerKind.Note) { Add("Edit note...", EditNote); menu.Items.Add(new ToolStripSeparator()); }
            if (Model.Kind != ContainerKind.Note)
            {
                var sort = new ToolStripMenuItem(Local("Sort", "Ταξινόμηση"));
                foreach (var (key, title) in new[]
                {
                    ("manual", Local("Manual", "Χειροκίνητα")), ("name", Local("Name ↑", "Όνομα ↑")),
                    ("name-desc", Local("Name ↓", "Όνομα ↓")), ("type", Local("Type ↑", "Τύπος ↑")),
                    ("type-desc", Local("Type ↓", "Τύπος ↓"))
                })
                    sort.DropDownItems.Add(new ToolStripMenuItem(title, null, (_, _) => ChooseSort(key)) { Checked = ItemSort == key });
                menu.Items.Add(sort);
                var filter = new ToolStripMenuItem(Local("Filter view", "Φιλτράρισμα προβολής"));
                foreach (var (key, title) in new[]
                {
                    ("all", Local("Show all", "Όλα")),
                    ("folders", Local("Folders", "Φάκελοι")),
                    ("images", Local("Images", "Εικόνες")),
                    ("documents", Local("Documents", "Έγγραφα")),
                    ("recent", Local("Modified in last 7 days", "Τροποποίηση τελευταίων 7 ημερών")),
                    ("recent30", Local("Modified in last 30 days", "Τροποποίηση τελευταίων 30 ημερών")),
                    ("tag", Local("Tagged (# in name)", "Ετικέτα (# στο όνομα"))
                })
                    filter.DropDownItems.Add(new ToolStripMenuItem(title, null, (_, _) => ChooseFilter(key))
                    { Checked = ItemFilter == key });
                filter.DropDownItems.Add(new ToolStripMenuItem(Local("Specific #tag…", "Συγκεκριμένη #ετικέτα…"), null, (_, _) =>
                {
                    using var prompt = new Form { Text = Local("Filter by tag", "Φίλτρο ετικέτας"), Width = 380, Height = 150,
                        StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog };
                    var input = new TextBox { Dock = DockStyle.Top, MaxLength = 64, Text = ItemFilter.StartsWith("tag:") ? ItemFilter[4..] : "" };
                    var ok = new Button { Text = "OK", Dock = DockStyle.Bottom, DialogResult = DialogResult.OK };
                    prompt.Controls.Add(input); prompt.Controls.Add(ok); prompt.AcceptButton = ok;
                    if (prompt.ShowDialog(this) == DialogResult.OK)
                    {
                        string tag = input.Text.Trim().TrimStart('#');
                        if (tag.Length > 0) ChooseFilter("tag:" + tag);
                    }
                }) { Checked = ItemFilter.StartsWith("tag:") });
                menu.Items.Add(filter);
            }
            if (ContainerHost.Current is { } localHost)
            {
                Add(Local("Restore previous layout", "Επαναφορά προηγούμενης διάταξης"),
                    () => localHost.RestorePreviousLayout(Model.Id, this), localHost.HasLayoutHistory(Model.Id));
                if (Model.Kind == ContainerKind.Data)
                    Add(Local("Inspect and repair broken shortcuts…", "Έλεγχος/επιδιόρθωση συντομεύσεων…"),
                        () => _ = localHost.InspectBrokenShortcutsAsync(Model.Id, this));
            }
            Add(LocalizationManager.T("DCM.RenameContainer"), RenameContainer);
            Add(MotionDesk.Services.LocalizationManager.T("DCM.ContainerSettings"), () => SettingsRequested?.Invoke(Model.Id));
            Add(Model.Locked ? "Unlock container" : "Lock container" , () => { Model.Locked = !Model.Locked; Dirty(); Changed?.Invoke(Model); });
            Add(IsCollapsed ? LocalizationManager.T("DCM.ExpandContainer") : LocalizationManager.T("DCM.CollapseContainer"), ToggleRollUp);
            var autoHide = menu.Items.Add(LocalizationManager.T("DCM.HoverRollUp"));
            if (autoHide is ToolStripMenuItem option) option.Checked = Model.Appearance.HoverRollUp;
            autoHide.Click += (_, _) =>
            {
                Model.Appearance.HoverRollUp = !Model.Appearance.HoverRollUp;
                Model.RolledUp = false;
                ResetAutoHide();
                LayoutHeight();
                Changed?.Invoke(Model);
            };
            if (DeleteRequested != null)
            {
                menu.Items.Add(new ToolStripSeparator());
                Add(LocalizationManager.T("DCX.DeleteTitle"), () => DeleteRequested?.Invoke());
            }
            menu.Closed += (_, _) =>
            {
                _menuInteractions--;
                if (IsHandleCreated && !IsDisposed) BeginInvoke(() => menu.Dispose());
                else menu.Dispose();
            };
            menu.Show(this, at);
        }

        /// <summary>One-shot organise: sorts the icons of this container (of the current tab) by name, or by type (folders first, then extension, then name). References only; nothing on disk changes.</summary>
        public void SortItems(bool byType)
        {
            if (!CanEditItems) return;
            string Name(ContainerItem i) => string.IsNullOrEmpty(i.DisplayName) ? System.IO.Path.GetFileNameWithoutExtension(i.Path) : i.DisplayName;
            var sorted = (byType ? BaseItems.OrderByDescending(i => i.IsFolder).ThenBy(i => System.IO.Path.GetExtension(i.Path), StringComparer.OrdinalIgnoreCase).ThenBy(Name, StringComparer.CurrentCultureIgnoreCase)
                                 : BaseItems.OrderBy(Name, StringComparer.CurrentCultureIgnoreCase)).ToList();
            BaseItems.Clear(); BaseItems.AddRange(sorted); _organizationMerged.Clear(); _sortCache = null; _filteredInput = null; ClearSelection(); Dirty(); Changed?.Invoke(Model);
        }

        /// <summary>Removes the REFERENCE only; the file on disk is never touched.</summary>
        public void RemoveItem(int index)
        {
            if (index >= 0 && index < Items.Count) RemoveReferences([Items[index].Path]);
        }

        // ---- drag and drop (phase 3): items are REFERENCES, dropping never copies or moves the file --------------------
        private int _dragItem = -1; private Point _pressAt; private const string InternalFormat = "Opsis.ContainerItemIndex";

        /// <summary>Adds dropped paths as references at the position under <paramref name="clientPoint"/>. Returns how many were added. Used by the drop handler and by tests.</summary>
        public int AcceptDrop(IEnumerable<string> paths, Point clientPoint)
        {
            int at = ItemLayout.DropIndex(clientPoint, Items.Count, _scroll);
            return AddReferences(paths, BaseItems, at).Length;
        }

        /// <summary>Moves an item inside this container (drag to reorder).</summary>
        public void MoveItem(int from, Point clientPoint)
        {
            if (!CanEditItems) return;
            var at = ItemLayout.DropIndex(clientPoint, Items.Count, _scroll);
            var list = DropRules.Reorder(BaseItems, from, Math.Min(at, BaseItems.Count));
            BaseItems.Clear(); BaseItems.AddRange(list); _organizationMerged.Clear(); _sortCache = null; _filteredInput = null; ClearSelection(); Dirty(); Changed?.Invoke(Model);
        }

        /// <summary>Last drag events seen by any container (diagnostics for the real-input test).</summary>
        public static readonly List<string> DragLog = new();
        protected override void OnDragEnter(DragEventArgs e)
        {
            base.OnDragEnter(e);
            _dropActive = true;
            if (DragLog.Count < 400) DragLog.Add("enter allowed=" + e.AllowedEffect + " formats=" + string.Join(",", e.Data?.GetFormats() ?? Array.Empty<string>()));
            DropEffectFor(e);
            if (e.Effect != DragDropEffects.None) { ExpandForDrop(); ShowTransferPathPreview(e); }
        }
        protected override void OnDragOver(DragEventArgs e) { base.OnDragOver(e); DropEffectFor(e); }
        protected override void OnDragLeave(EventArgs e)
        {
            base.OnDragLeave(e);
            HideTransferPathPreview();
            _dropActive = false;
            EndDropExpansion();
        }

        protected override void OnDragDrop(DragEventArgs e)
        {
            base.OnDragDrop(e);
            HideTransferPathPreview();
            _dropActive = false;
            // End on the next message turn, after the drag/drop operation has finished.
            // This also covers a successful drop (which need not raise DragLeave).
            if (_expandedForExternalDrop && IsHandleCreated && !IsDisposed)
                BeginInvoke(new Action(EndDropExpansion));
            if (e.Effect == DragDropEffects.None) return;
            if (e.Data?.GetData(DataFormats.FileDrop) is not string[] files) return;
            if (CanWritePortal) { TransferFiles(files, e.Effect == DragDropEffects.Move); return; }
            var pt = PointToClient(new Point(e.X, e.Y));
            var drag = _itemDrag;
            int tab = TabsOn ? TabAt(pt) : -1;
            var destination = tab >= 0 ? Model.Tabs[tab].Items : BaseItems;
            bool internalDrop = drag != null && ReferenceEquals(drag.Source, this);
            if (internalDrop && ReferenceEquals(drag!.Original, destination))
            {
                if (tab < 0 && ItemSort == "manual" && e.Data.GetData(InternalFormat) is int from) MoveItem(from, pt);
                return;
            }
            var added = AddReferences(files, destination, tab >= 0 ? destination.Count : ItemLayout.DropIndex(pt, Items.Count, _scroll));
            bool copyReference = (e.KeyState & 4) == 0 && ((e.KeyState & 8) != 0 || AppSettings.Load().ContainerDragCopies);
            if (drag != null && added.Length > 0 && !copyReference)
                drag.Moved = added;
        }

        /// <summary>Screen position of the centre of an item's cell (used by the real-input tests).</summary>
        public Point ItemScreenCenter(int i) { var r = ItemLayout.CellRect(i, _scroll); return PointToScreen(new Point(r.X + r.Width / 2, r.Y + r.Height / 2)); }

        /// <summary>Scroll by wheel notches (positive = down).</summary>
        // 2.8.0.2510: smooth scrolling. The wheel / keys set a TARGET and the position glides to it (ease-out, about 150 ms) instead of jumping a whole step; with animations off it jumps as before.
        private int _scrollTarget; private bool _scrollGliding; private System.Windows.Forms.Timer? _scrollTimer;
        public void ScrollBy(int pixels)
        {
            if (!MotionDesk.Services.AppSettings.Peek().EnableAnimations) { _scroll = ItemLayout.ClampScroll(_scroll + pixels, Items.Count); _scrollGliding = false; Dirty(); return; }
            _scrollTarget = ItemLayout.ClampScroll((_scrollGliding ? _scrollTarget : _scroll) + pixels, Items.Count);
            _scrollGliding = true;
            _scrollTimer ??= new System.Windows.Forms.Timer { Interval = 8 };
            _scrollTimer.Tick -= GlideTick; _scrollTimer.Tick += GlideTick;
            _scrollTimer.Start();
        }

        private void GlideTick(object? sender, EventArgs e)
        {
            int diff = _scrollTarget - _scroll;
            if (!_scrollGliding || IsDisposed || Math.Abs(diff) <= 1) { _scroll = ItemLayout.ClampScroll(_scrollTarget, Items.Count); _scrollGliding = false; _scrollTimer?.Stop(); Dirty(); return; }
            int step = (int)Math.Round(diff * 0.26); if (step == 0) step = Math.Sign(diff);
            _scroll = ItemLayout.ClampScroll(_scroll + step, Items.Count);
            Dirty();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _transferPathTip?.Dispose(); _transferPathTip = null; CancelTabDrag(); _tabDragKeys?.Dispose(); _hoverTimer.Dispose(); _scrollTimer?.Dispose(); _watch?.Dispose(); _portalTimer.Dispose(); _repaint.Stop(); _repaint.Dispose(); _pane?.Dispose(); _surface.Dispose(); _icons.Dispose(); }
            base.Dispose(disposing);
        }
    }

}
