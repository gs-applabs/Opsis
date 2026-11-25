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
        var addonTiles = new List<(WallpaperTile Tile, string Mode, string Path)>();
        var webItems = WallpaperGalleryCatalog.Discover();
        var signature = webItems.FirstOrDefault(x => x.Id == "opsis-glass-signature");
        WallpaperTile? signatureTile = null;
        void ApplyWeb(WallpaperGalleryCatalog.Entry item)
        {
            var valid = WallpaperGalleryCatalog.Discover().FirstOrDefault(x => x.Id == item.Id &&
                string.Equals(x.IndexPath, item.IndexPath, StringComparison.OrdinalIgnoreCase));
            if (valid == null) { MessageBox.Show(this, StudioText("Wallpaper is no longer installed.", "Το φόντο δεν είναι πλέον εγκατεστημένο."), "Opsis"); return; }
            var settings = WallpaperSettings.Load();
            settings.PageSource = valid.IndexPath;
            settings.Mode = "Page";
            settings.Save();
            host.Enable();
            host.RefreshAll();
        }
        var addonItems = OpsisEcosystemGallery.Discover().Where(x => x.Type.Equals("wallpaper", StringComparison.OrdinalIgnoreCase) &&
            Path.GetExtension(x.AssetPath).ToLowerInvariant() is ".jpg" or ".jpeg" or ".png" or ".webp")
            .Select(x => (Title: x.Title, Category: x.Category, Path: x.AssetPath, Poster: (string?)x.AssetPath,
                Mode: "Image", Apply: (Action)(() => OpsisEcosystemGallery.ApplyStaticWallpaper(x))))
            .Concat(webItems.Where(x => x.Id != "opsis-glass-signature")
                .Select(x => (Title: StudioText(x.Name, string.IsNullOrWhiteSpace(x.NameEl) ? x.Name : x.NameEl),
                    Category: x.Category, Path: x.IndexPath, Poster: x.PosterPath, Mode: "Page", Apply: (Action)(() => ApplyWeb(x)))))
            .OrderBy(x => x.Category, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => x.Title, StringComparer.CurrentCultureIgnoreCase).ToArray();
        foreach (var scene in StudioWallpaperCatalog.Scenes)
            AddTile(featured, scene.Mode, scene.Title(StudioGreek), scene.Detail(StudioGreek), scene.Asset, true);
        if (signature != null)
        {
            var item = signature;
            signatureTile = new WallpaperTile("Page", StudioText(item.Name, item.NameEl),
                StudioText(item.Description, item.DescriptionEl), null, externalPoster: item.PosterPath);
            signatureTile.Name = "wallpaper-tile-opsis-glass-signature";
            signatureTile.Click += (_, _) =>
            {
                try { ApplyWeb(item); Sync(); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Opsis", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            };
            featured.Controls.Add(signatureTile);
        }
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
        AddSection(page, StudioText("Installed wallpaper add-ons", "Εγκατεστημένα πρόσθετα φόντων"));
        var packControls = new FlowLayoutPanel { AutoSize = true, WrapContents = true };
        var categoryPicker = new ComboBox { Width = 195, DropDownStyle = ComboBoxStyle.DropDownList };
        var previousPackPage = new Button { Text = "‹", AutoSize = true };
        var nextPackPage = new Button { Text = "›", AutoSize = true };
        var packCount = new Label { AutoSize = true, Margin = new Padding(5, 8, 5, 3) };
        Button? importPackButton = null;
        var importPack = StudioButton(StudioText("Install image pack", "Εγκατάσταση πακέτου"), async () =>
        {
            using var picker = new OpenFileDialog { Filter = "Opsis image packs (*.zip)|*.zip", CheckFileExists = true };
            if (picker.ShowDialog(this) != DialogResult.OK) return;
            if (MessageBox.Show(this, StudioText("Install data-only wallpapers from this ZIP? No files will be executed.", "Εγκατάσταση μόνο εικόνων από αυτό το ZIP; Δεν θα εκτελεστεί κανένα αρχείο."), "Opsis", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            try
            {
                // up to 200 images are read, hashed and decoded: keep that off the UI thread
                string zipPath = picker.FileName;
                if (importPackButton != null) importPackButton.Enabled = false;
                int count;
                try { count = await Task.Run(() => OpsisEcosystemGallery.ImportWallpaperPack(zipPath)); }
                finally { if (importPackButton is { IsDisposed: false }) importPackButton.Enabled = true; }
                if (IsDisposed) return;
                MessageBox.Show(this, count + " wallpaper images installed.", "Opsis");
                ShowWallpaper();
            }
            catch (Exception ex) { if (!IsDisposed) MessageBox.Show(this, ex.Message, "Opsis", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }, 195);
        importPackButton = importPack;
        packControls.Controls.AddRange([categoryPicker, previousPackPage, nextPackPage, packCount, importPack]);
        page.Controls.Add(packControls);
        var addonGrid = NewGrid();
        page.Controls.Add(addonGrid);
        int packPage = 0;
        const int PackPageSize = 18;
        categoryPicker.Items.Add(StudioText("All categories", "Όλες οι κατηγορίες"));
        foreach (var category in addonItems.Select(x => x.Category).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x))
            categoryPicker.Items.Add(category);
        categoryPicker.SelectedIndex = 0;

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
            addonGrid.SuspendLayout();
            try
            {
                featured.MaximumSize = library.MaximumSize = addonGrid.MaximumSize = new Size(area, 0);
                var visibleTiles = signatureTile == null ? tiles.AsEnumerable() : tiles.Append(signatureTile);
                foreach (var tile in visibleTiles.Concat(addonTiles.Select(x => x.Tile))) tile.Size = new Size(width, Math.Max(145, (width - 14) * 9 / 16 + 61));
            }
            finally
            {
                featured.ResumeLayout();
                library.ResumeLayout();
                addonGrid.ResumeLayout();
                page.ResumeLayout();
            }
        }

        void Sync()
        {
            if (page.IsDisposed) return;
            var mode = WallpaperSettings.Load().Mode;
            weatherDepth.Visible = mode == "Weather";
            power.Text = host.IsEnabled ? StudioText("Disable wallpaper", "Απενεργοποίηση φόντου")
                : StudioText("Enable wallpaper", "Ενεργοποίηση φόντου");
            details.Text = mode == "Weather" ? StudioText("Weather settings", "Ρυθμίσεις Weather") : StudioText("Wallpaper settings", "Ρυθμίσεις φόντου");
            foreach (var tile in tiles) tile.SetActive(host.IsEnabled && tile.Mode == mode && tile.Mode is not ("Page" or "Image"));
            var settings = WallpaperSettings.Load();
            string? pagePath = MediaSource.LocalPagePath(settings.PageSource);
            foreach (var tile in tiles.Where(x => x.Mode is "Page" or "Image"))
            {
                bool known = tile.Mode == "Page" && signature != null && string.Equals(pagePath, signature.IndexPath, StringComparison.OrdinalIgnoreCase)
                    || addonItems.Any(x => x.Mode == tile.Mode && string.Equals(x.Path, tile.Mode == "Page" ? pagePath : settings.ImagePath, StringComparison.OrdinalIgnoreCase));
                tile.SetActive(host.IsEnabled && tile.Mode == mode && !known);
            }
            signatureTile?.SetActive(host.IsEnabled && mode == "Page" && string.Equals(pagePath, signature?.IndexPath, StringComparison.OrdinalIgnoreCase));
            foreach (var tile in addonTiles) tile.Tile.SetActive(host.IsEnabled && mode == tile.Mode &&
                string.Equals(tile.Mode == "Page" ? pagePath : settings.ImagePath, tile.Path, StringComparison.OrdinalIgnoreCase));
        }

        void DrawAddonPage()
        {
            var filtered = addonItems.Where(x => categoryPicker.SelectedIndex == 0 ||
                string.Equals(x.Category, categoryPicker.SelectedItem?.ToString(), StringComparison.OrdinalIgnoreCase)).ToArray();
            int pages = Math.Max(1, (filtered.Length + PackPageSize - 1) / PackPageSize);
            packPage = Math.Clamp(packPage, 0, pages - 1);
            addonGrid.SuspendLayout();
            try
            {
                foreach (var old in addonTiles) old.Tile.Dispose();
                addonTiles.Clear();
                foreach (var item in filtered.Skip(packPage * PackPageSize).Take(PackPageSize))
                {
                    var tile = new WallpaperTile(item.Mode, item.Title, item.Category, null, externalPoster: item.Poster);
                    tile.Name = "wallpaper-addon-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(item.Path))).Substring(0, 16);
                    tile.Click += (_, _) =>
                    {
                        try { item.Apply(); Sync(); }
                        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Opsis", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                    };
                    addonTiles.Add((tile, item.Mode, item.Path)); addonGrid.Controls.Add(tile);
                }
            }
            finally { addonGrid.ResumeLayout(); }
            packCount.Text = filtered.Length == 0 ? StudioText("No add-ons installed", "Δεν υπάρχουν πρόσθετα") : $"{packPage + 1}/{pages} • {filtered.Length}";
            previousPackPage.Enabled = packPage > 0; nextPackPage.Enabled = packPage + 1 < pages;
            lastLayoutWidth = 0; ResizeTiles(); Sync();
        }
        categoryPicker.SelectedIndexChanged += (_, _) => { packPage = 0; DrawAddonPage(); };
        previousPackPage.Click += (_, _) => { packPage--; DrawAddonPage(); };
        nextPackPage.Click += (_, _) => { packPage++; DrawAddonPage(); };

        void OnWallpaperChanged()
        {
            if (page.IsDisposed || !page.IsHandleCreated) return;
            if (!page.InvokeRequired) Sync();
            else { try { page.BeginInvoke(Sync); } catch (InvalidOperationException) { } }
        }

        host.StateChanged += OnWallpaperChanged;
        page.Disposed += (_, _) => host.StateChanged -= OnWallpaperChanged;
        page.SizeChanged += (_, _) => ResizeTiles();
        DrawAddonPage();
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

        internal WallpaperTile(string mode, string title, string subtitle, string? asset, bool animate = false, string? externalPoster = null)
        {
            Mode = mode;
            Text = title;
            _subtitle = subtitle;
            AccessibleName = title;
            AccessibleDescription = subtitle;
            Name = "wallpaper-tile-" + (asset ?? mode).ToLowerInvariant();
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
            if (externalPoster != null) _posterPath = externalPoster;
            _builtIn = externalPoster == null ? BuiltInWallpaperPreview.Create(mode) : null;
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
                if (new FileInfo(path).Length > 32L * 1024 * 1024) return null;
                if (Path.GetExtension(path).Equals(".webp", StringComparison.OrdinalIgnoreCase))
                    return WallpaperPackImage.ReadThumbnail(path, width > 0 ? width : 480);
                using var original = Image.FromFile(path);
                if (original.Width <= 0 || original.Height <= 0 || (long)original.Width * original.Height > 40_000_000) return null;
                return width > 0 ? new Bitmap(original, width, Math.Clamp((int)((long)original.Height * width / original.Width), 1, 960)) : new Bitmap(original);
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or System.Runtime.InteropServices.ExternalException or DllNotFoundException or TypeInitializationException or InvalidDataException) { return null; }
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
