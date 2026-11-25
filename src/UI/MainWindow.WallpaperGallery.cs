using System.Drawing.Drawing2D;
using MotionDesk.Services;
using MotionDesk.Widgets;

namespace MotionDesk.UI;

public sealed partial class MainWindow
{
    private FlowLayoutPanel WeatherDepthPicker(string selected)
    {
        var spatialRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 5, 0, 8) };
        spatialRow.Controls.Add(new Label { Text = StudioText("Weather scene depth", "Βάθος καιρού / σκηνής"),
            Width = 190, Height = 30, TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = UiTheme.TextSecondary, Font = UiTheme.FontBody });
        var spatialModes = new[] { "Off", "2.5D", "3D" };
        var spatialLabels = new[] {
            StudioText("Off — classic 2D", "Κλειστό — κλασικό 2D"),
            StudioText("2.5D — parallax", "2.5D — παράλλαξη βάθους"),
            StudioText("3D — landscapes and monuments", "3D — τοπία και μνημεία")
        };
        var spatialPicker = new FlatComboBox { Width = 330 };
        var currentSpatial = Array.IndexOf(spatialModes, selected);
        spatialPicker.SetItems(spatialLabels, spatialLabels[Math.Max(0, currentSpatial)]);
        spatialPicker.SelectedIndexChanged += (_, _) =>
        {
            if (spatialPicker.SelectedIndex is >= 0 and < 3)
                WallpaperHostEngine.Instance.SetWeatherSpatialMode(spatialModes[spatialPicker.SelectedIndex]);
        };
        spatialRow.Controls.Add(spatialPicker);
        return spatialRow;
    }

    private void ShowWallpaper()
    {
        _currentPageKey = "Wallpaper";
        var page = CreatePagePanel();
        var host = WallpaperHostEngine.Instance;
        AddHeading(page, StudioText("Wallpaper gallery", "Συλλογή φόντων"));
        var controls = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 4, 0, 15) };
        var power = StudioButton("", () =>
        {
            if (host.IsEnabled) host.Disable(); else host.Enable();
        }, 196);
        var details = StudioButton(StudioText("Wallpaper settings", "Ρυθμίσεις φόντου"), ShowWallpaperDetails, 194);
        controls.Controls.AddRange([power, details]);
        page.Controls.Add(controls);
        var weatherDepth = WeatherDepthPicker(WallpaperSettings.Load().WeatherSpatialMode);
        var featured = NewGrid();
        var library = NewGrid();
        var tiles = new List<WallpaperTile>();
        var addonGrids = new List<FlowLayoutPanel>();
        foreach (var scene in StudioWallpaperCatalog.Scenes)
            AddTile(featured, scene.Mode, scene.Title(StudioGreek), scene.Detail(StudioGreek), scene.Asset, true);
        page.Controls.Add(featured);
        AddSection(page, StudioText("Weather and your library", "Καιρός και βιβλιοθήκη"));
        AddTile(library, "Weather", StudioText("Weather", "Καιρός"),
            StudioText("Live weather and Greek landscapes", "Ζωντανός καιρός και ελληνικά τοπία"), "weather");
        AddTile(library, WallpaperSettings.AthensMode, WallpaperModeLabel(WallpaperSettings.AthensMode),
            StudioText("Seasonal light across the Acropolis", "Μεταβαλλόμενο φως πάνω στην Ακρόπολη"), "athens");
        AddTile(library, "Video", StudioText("Videos", "Βίντεο"), StudioText("Loop your video library", "Αναπαραγωγή της βιβλιοθήκης σου"), null);
        AddTile(library, "Image", StudioText("Image or GIF", "Εικόνα ή GIF"), StudioText("Choose a file", "Επιλογή αρχείου"), null);
        AddTile(library, "Page", StudioText("Web page", "Ιστοσελίδα"), StudioText("Open a web wallpaper", "Άνοιγμα διαδικτυακού φόντου"), null);
        AddTile(library, "Desktop", StudioText("Windows wallpaper", "Φόντο Windows"), StudioText("Show your system wallpaper", "Εμφάνιση του φόντου των Windows"), null);
        AddTile(library, "Waves", StudioText("Waves", "Κύματα"), StudioText("Audio-reactive ribbons", "Κορδέλες που αντιδρούν στον ήχο"), null);
        AddTile(library, "Particles", StudioText("Particles", "Σωματίδια"), StudioText("Floating particles", "Αιωρούμενα σωματίδια"), null);
        page.Controls.Add(weatherDepth);
        page.Controls.Add(library);
        var europeanPhotos = EuropeanPhotoCatalog.Discover();
        if (europeanPhotos.Count > 0)
        {
            AddSection(page, StudioText("European Capitals — live weather", "Ευρωπαϊκές πρωτεύουσες — ζωντανός καιρός"));
            var grid = NewGrid();
            addonGrids.Add(grid);
            foreach (var entry in europeanPhotos)
            {
                var city = entry.Scene;
                var tile = new WallpaperTile("Weather", StudioGreek ? city.Name : city.NameEn,
                    StudioText("Live local weather", "Ζωντανός τοπικός καιρός"), null, false, null, entry.Poster);
                tile.Click += (_, _) =>
                {
                    if (!EuropeanPhotoCatalog.Discover().Any(e => e.Scene.Id == city.Id)) { ShowWallpaper(); return; }
                    var s = WallpaperSettings.Load();
                    s.WeatherLandmarkId = city.Id;
                    s.WeatherLandmarks = true;
                    s.WeatherCity = StudioGreek ? city.Name : city.NameEn;
                    s.WeatherLat = city.Lat; s.WeatherLon = city.Lon;
                    s.WeatherPlaceSource = "OPSIS Europe";
                    s.WeatherPlaceDetail = city.NameEn;
                    s.Mode = "Weather";
                    s.Save();
                    host.Enable();
                    host.RefreshAll();
                    Sync();
                };
                grid.Controls.Add(tile);
                tiles.Add(tile);
            }
            page.Controls.Add(grid);
        }
        var installed = WallpaperGalleryCatalog.Discover();
        if (installed.Count > 0)
        {
            AddSection(page, StudioText("OPSIS 100 Wallpapers — installed", "OPSIS 100 Wallpapers — εγκατεστημένα"));
            foreach (var group in installed.GroupBy(item => item.Category))
            {
                AddSection(page, AddonCategoryLabel(group.Key));
                var grid = NewGrid();
                addonGrids.Add(grid);
                foreach (var item in group)
                {
                    var tile = new WallpaperTile("Page", StudioGreek && !string.IsNullOrWhiteSpace(item.NameEl)
                        ? item.NameEl : item.Name, StudioGreek && !string.IsNullOrWhiteSpace(item.DescriptionEl)
                        ? item.DescriptionEl : item.Description, null, false, item.IndexPath, item.PosterPath);
                    tile.Click += (_, _) =>
                    {
                        // Revalidate on selection: the optional pack may have been uninstalled.
                        var valid = WallpaperGalleryCatalog.Discover().FirstOrDefault(entry =>
                            entry.Id == item.Id && string.Equals(entry.IndexPath, item.IndexPath, StringComparison.OrdinalIgnoreCase));
                        if (valid == null) { ShowWallpaper(); return; }
                        var s = WallpaperSettings.Load();
                        s.PageSource = valid.IndexPath;
                        s.Mode = "Page";
                        s.Save();
                        host.Enable();
                        host.RefreshAll();
                        Sync();
                    };
                    grid.Controls.Add(tile);
                    tiles.Add(tile);
                }
                page.Controls.Add(grid);
            }
        }

        string AddonCategoryLabel(string category) => category switch
        {
            "Abstract" => StudioText("Abstract", "Αφηρημένα"),
            "Nature" => StudioText("Nature", "Φύση"),
            "Tech" => StudioText("Tech", "Τεχνολογία"),
            "Space" => StudioText("Space", "Διάστημα"),
            "City / Night" => StudioText("City / Night", "Πόλεις / Νύχτα"),
            "Minimal" => StudioText("Minimal", "Μινιμαλιστικά"),
            "Weather" => StudioText("Weather", "Καιρός"),
            "Neon" => "Neon",
            "Fantasy" => StudioText("Fantasy", "Φαντασία"),
            "Greek Landmarks" => StudioText("Greek Landmarks", "Ελληνικά τοπόσημα"),
            "European Capitals" => StudioText("European Capitals", "Ευρωπαϊκές πρωτεύουσες"),
            "Historical Cities" => StudioText("Historical Cities", "Ιστορικές πόλεις"),
            _ => category
        };

        int lastLayoutWidth = 0;
        void ResizeTiles()
        {
            int area = Math.Max(285, page.ClientSize.Width - page.Padding.Horizontal - 24);
            if (area < 360 && page.Parent == null) area = 850;
            if (lastLayoutWidth == area) return;
            lastLayoutWidth = area;
            int columns = Math.Clamp(area / 244, 1, 4);
            int width = area / columns - 12;
            page.SuspendLayout();
            featured.SuspendLayout();
            library.SuspendLayout();
            try
            {
                featured.MaximumSize = library.MaximumSize = new Size(area, 0);
                foreach (var grid in addonGrids) grid.MaximumSize = new Size(area, 0);
                foreach (var tile in tiles) tile.Size = new Size(width, Math.Max(145, (width - 14) * 9 / 16 + 61));
            }
            finally
            {
                featured.ResumeLayout();
                library.ResumeLayout();
                page.ResumeLayout();
            }
        }

        void Sync()
        {
            if (page.IsDisposed) return;
            var selection = WallpaperSettings.Load();
            var mode = selection.Mode;
            weatherDepth.Visible = mode == "Weather";
            power.Text = host.IsEnabled ? StudioText("Disable wallpaper", "Απενεργοποίηση φόντου")
                : StudioText("Enable wallpaper", "Ενεργοποίηση φόντου");
            details.Text = mode == "Weather" ? StudioText("Weather settings", "Ρυθμίσεις Weather") : StudioText("Wallpaper settings", "Ρυθμίσεις φόντου");
            foreach (var tile in tiles)
                tile.SetActive(host.IsEnabled && tile.Mode == mode &&
                    (tile.PagePath != null
                        ? string.Equals(tile.PagePath, selection.PageSource, StringComparison.OrdinalIgnoreCase)
                        : mode != "Page" || !tiles.Any(t => t.PagePath != null &&
                            string.Equals(t.PagePath, selection.PageSource, StringComparison.OrdinalIgnoreCase))));
        }

        void OnWallpaperChanged()
        {
            if (page.IsDisposed || !page.IsHandleCreated) return;
            if (!page.InvokeRequired) Sync();
            else { try { page.BeginInvoke(Sync); } catch (InvalidOperationException) { } }
        }

        host.StateChanged += OnWallpaperChanged;
        page.Disposed += (_, _) => host.StateChanged -= OnWallpaperChanged;
        page.SizeChanged += (_, _) => ResizeTiles();
        Sync();
        SetPage("Page.Wallpaper.Title", page);
        ResizeTiles();

        static FlowLayoutPanel NewGrid() => new()
        {
            WrapContents = true, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0, 0, 0, 12)
        };

        void AddTile(FlowLayoutPanel grid, string mode, string title, string subtitle, string? asset, bool animate = false)
        {
            var tile = new WallpaperTile(mode, title, subtitle, asset, animate);
            tile.Click += (_, _) =>
            {
                if (!host.IsEnabled || WallpaperSettings.Load().Mode != mode) host.SetMode(mode);
                Sync();
                // Media modes need the file/library controls on first use.
                if (mode is "Video" or "Image" or "Page") ShowWallpaperDetails();
            };
            grid.Controls.Add(tile);
            tiles.Add(tile);
        }
    }

    private sealed class WallpaperTile : Button
    {
        private const int Columns = 5, FrameWidth = 288, FrameHeight = 162, Frames = 20;
        private readonly string _subtitle;
        private readonly string? _previewPath;
        private readonly string? _posterPath;
        private Image? _poster;
        private readonly BuiltInWallpaperPreview? _builtIn;
        private bool _posterLoaded;
        private readonly System.Windows.Forms.Timer _timer = new();
        private Image? _preview;
        private bool _pointer, _active;
        private int _frame;

        internal string Mode { get; }
        internal string? PagePath { get; }

        internal WallpaperTile(string mode, string title, string subtitle, string? asset, bool animate,
            string? pagePath = null, string? posterPath = null)
        {
            Mode = mode;
            PagePath = pagePath;
            Text = title;
            _subtitle = subtitle;
            AccessibleName = title;
            AccessibleDescription = subtitle;
            Name = "wallpaper-tile-" + (pagePath != null ? Path.GetFileName(Path.GetDirectoryName(pagePath)) : asset ?? mode).ToLowerInvariant();
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            Cursor = Cursors.Hand;
            DoubleBuffered = true;
            Margin = new Padding(0, 0, 12, 12);
            Size = new Size(250, 206);
            var folder = Path.Combine(AppContext.BaseDirectory, "widgets", "wallpaper", "gallery");
            if (asset != null)
            {
                _posterPath = Path.Combine(folder, asset + ".jpg");
                if (animate) _previewPath = Path.Combine(folder, asset + ".preview.jpg");
            }
            if (posterPath != null) _posterPath = posterPath;
            _builtIn = pagePath == null ? BuiltInWallpaperPreview.Create(mode) : null;
            _timer.Interval = _builtIn?.Animated == true ? 50 : 100;
            _timer.Tick += (_, _) =>
            {
                if (!RectangleToScreen(ClientRectangle).Contains(Cursor.Position)) { _pointer = false; StopPreview(); return; }
                _frame = _builtIn?.Animated == true ? (_frame + 1) % 7200 : (_frame + 1) % (Frames * 2 - 2);
                Invalidate();
            };
        }

        internal void SetActive(bool active)
        {
            if (_active == active) return;
            _active = active;
            Invalidate();
        }

        private static Image? ReadImage(string path, int width = 0)
        {
            try
            {
                using var original = Image.FromFile(path);
                return width > 0 ? new Bitmap(original, width, original.Height * width / original.Width) : new Bitmap(original);
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or System.Runtime.InteropServices.ExternalException) { return null; }
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _pointer = true; StartPreview(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _pointer = false; StopPreview(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); if (!Visible && !Disposing) StopPreview(); }

        private void StartPreview()
        {
            if (_builtIn?.Animated == true) { _builtIn.RefreshPalette(); _timer.Start(); }
            if (_previewPath != null && _preview == null)
            {
                _preview = ReadImage(_previewPath);
                if (_preview != null) { _frame = 0; _timer.Start(); }
            }
            Invalidate();
        }

        private void StopPreview()
        {
            _timer.Stop();
            _preview?.Dispose();
            _preview = null;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var bounds = Rectangle.Inflate(ClientRectangle, -1, -1);
            if (bounds.Width < 16 || bounds.Height < 68) return;
            e.Graphics.Clear(UiTheme.PageBackground);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var outline = UiTheme.RoundedPath(bounds, 12);
            using var shell = new SolidBrush(_pointer ? UiTheme.SurfaceHover : UiTheme.Surface);
            e.Graphics.FillPath(shell, outline);
            var photograph = new Rectangle(8, 8, Width - 16, Height - 60);
            var state = e.Graphics.Save();
            using (var crop = UiTheme.RoundedPath(photograph, 7)) e.Graphics.SetClip(crop);
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
            if (!_posterLoaded && _posterPath != null)
            {
                _posterLoaded = true;
                _poster = ReadImage(_posterPath, 480);
            }
            if (_builtIn != null) _builtIn.Draw(e.Graphics, photograph, _frame * .05);
            else if (_preview != null)
            {
                int index = Math.Min(_frame, Frames * 2 - 2 - _frame);
                var frame = new Rectangle(index % Columns * FrameWidth, index / Columns * FrameHeight, FrameWidth, FrameHeight);
                e.Graphics.DrawImage(_preview, photograph, frame, GraphicsUnit.Pixel);
            }
            else if (_poster != null) e.Graphics.DrawImage(_poster, photograph);
            else
            {
                using var gradient = new LinearGradientBrush(photograph, Color.FromArgb(18, 25, 45), Color.FromArgb(52, 73, 109), 26f);
                e.Graphics.FillRectangle(gradient, photograph);
                using var ring = new Pen(Color.FromArgb(95, 177, 198, 233), 2);
                int size = Math.Min(photograph.Height / 2, 56);
                e.Graphics.DrawEllipse(ring, photograph.X + (photograph.Width - size) / 2, photograph.Y + (photograph.Height - size) / 2, size, size);
                TextRenderer.DrawText(e.Graphics, Text, UiTheme.FontBody, photograph, Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
            e.Graphics.Restore(state);
            if (_active)
            {
                using var badge = new SolidBrush(UiTheme.AccentCyan);
                e.Graphics.FillEllipse(badge, Width - 35, 16, 20, 20);
                TextRenderer.DrawText(e.Graphics, "✓", UiTheme.FontBody, new Rectangle(Width - 35, 16, 20, 20),
                    Color.FromArgb(15, 22, 33), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            const TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
            TextRenderer.DrawText(e.Graphics, Text, UiTheme.FontSubheading,
                new Rectangle(12, Height - 49, Width - 24, 24), UiTheme.TextPrimary, flags);
            TextRenderer.DrawText(e.Graphics, _subtitle, UiTheme.FontBody,
                new Rectangle(12, Height - 25, Width - 24, 17), UiTheme.TextSecondary, flags);
            using var border = new Pen(_active || Focused ? UiTheme.AccentCyan : UiTheme.Border, _active ? 2 : 1);
            e.Graphics.DrawPath(border, outline);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _timer.Stop();
                _timer.Dispose();
                _poster?.Dispose();
                _preview?.Dispose();
                _builtIn?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
