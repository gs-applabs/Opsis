namespace OpsisMusicOverlay;

internal sealed class MusicOverlayPage : TableLayoutPanel
{
    private readonly Func<DesktopMusicController?> getController;
    private readonly CheckBox enabled;
    private readonly CheckBox hideTrackMetadata;
    private readonly ComboBox presets;
    private readonly ComboBox typefaces;
    private readonly string[] fonts;
    private readonly ComboBox display;
    private readonly NumericUpDown horizontal;
    private readonly NumericUpDown vertical;
    private readonly NumericUpDown width;
    private readonly NumericUpDown height;
    private readonly Button[] swatches = new Button[4];
    private readonly Button resetPalette;
    private readonly Button customColor;
    private readonly HueBar hueBar;
    private int activeSwatch;
    private readonly Color paletteSurface;
    private readonly string defaultDisplay;
    private readonly Button preview;
    private readonly Button reset;
    private readonly System.Windows.Forms.Timer stateRefresh = new() { Interval = 500 };
    private bool loading;

    public MusicOverlayPage(Func<DesktopMusicController?> getController, bool greek, string[] fonts, Color background, Color surface, Color text)
    {
        this.getController = getController;
        this.fonts = fonts;
        paletteSurface = surface;
        string L(string el, string en) => greek ? el : en;
        defaultDisplay = L("Κύρια οθόνη", "Primary monitor");
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(18, 14, 18, 12);
        Margin = new Padding(0, 8, 0, 16);
        ColumnCount = 1;
        GrowStyle = TableLayoutPanelGrowStyle.AddRows;
        BackColor = surface;
        ForeColor = text;
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        Add(new Label
        {
            Text = L("Μουσικό visualizer", "Music visualizer"),
            AutoSize = true,
            Margin = new Padding(3, 0, 3, 9)
        });
        enabled = new CheckBox
        {
            Text = L("Ενεργοποίηση μουσικού visualizer", "Enable music visualizer"),
            AutoSize = true,
            Margin = new Padding(3, 0, 3, 15)
        };
        Add(enabled);
        hideTrackMetadata = new CheckBox
        {
            Text = L("Απόκρυψη τίτλου, καλλιτέχνη και εξωφύλλου κατά την κοινή χρήση οθόνης",
                "Hide song details and cover while sharing your screen"),
            AutoSize = true,
            Margin = new Padding(3, 0, 3, 10),
            AccessibleDescription = L("Ενεργοποίησέ το πριν παρουσιάσεις την οθόνη. Επαναφορά με αποεπιλογή.",
                "Enable before screen sharing and uncheck to reveal song details again.")
        };
        Add(hideTrackMetadata);
        Add(new Label
        {
            Text = L("Το visualizer σχεδιάζεται στο wallpaper όταν παίζει μουσική.",
                "The visualizer is drawn in the wallpaper during playback."),
            Dock = DockStyle.Fill,
            AutoSize = true,
            Margin = new Padding(3, 0, 3, 18)
        });

        Add(new Label { Text = L("Οπτικοποίηση", "Visualization"), AutoSize = true });
        presets = Picker();
        presets.Items.AddRange(VisualPresets.All.Select(preset => preset.Title).ToArray());
        Add(presets);
        Add(new Label { Text = L("Χρώματα οπτικοποίησης", "Visualization colors"), AutoSize = true });
        var palette = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        for (var i = 0; i < swatches.Length; i++)
        {
            int index = i;
            var colorLabel = $"{L("Χρώμα", "Color")} {i + 1}";
            var swatch = new Button
            {
                AccessibleName = colorLabel,
                Text = (i + 1).ToString(),
                Size = new Size(34, 31),
                UseVisualStyleBackColor = false
            };
            swatch.FlatStyle = FlatStyle.Flat;
            swatch.Click += (_, _) => { activeSwatch = index; UpdateFromSettings(); };
            swatches[i] = swatch;
            palette.Controls.Add(swatch);
        }
        customColor = Button(L("Προσαρμογή…", "Custom color…"));
        customColor.Click += (_, _) => ChooseColor(activeSwatch);
        palette.Controls.Add(customColor);
        resetPalette = Button(L("Επαναφορά χρωμάτων", "Reset colors"));
        resetPalette.Click += (_, _) =>
        {
            getController()?.ResetPalette();
            UpdateFromSettings();
        };
        palette.Controls.Add(resetPalette);
        Add(palette);
        hueBar = new HueBar { Dock = DockStyle.Fill, Height = 28, MinimumSize = new Size(230, 28),
            BackColor = paletteSurface, Margin = new Padding(3, 2, 3, 12),
            AccessibleName = L("Μπάρα επιλογής χρώματος", "Color spectrum") };
        hueBar.ColorSelected += color =>
        {
            getController()?.SetColor(activeSwatch, color);
            UpdateFromSettings();
        };
        Add(hueBar);

        Add(new Label { Text = L("Γραμματοσειρά τίτλου", "Title font"), AutoSize = true });
        typefaces = Picker();
        typefaces.Items.Add(L("Προεπιλογή", "Default"));
        foreach (var font in fonts) typefaces.Items.Add(font);
        Add(typefaces);

        Add(new Label { Text = L("Οθόνη", "Monitor"), AutoSize = true });
        display = Picker();
        Add(display);

        horizontal = Coordinate();
        vertical = Coordinate();
        var position = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        position.Controls.Add(new Label { Text = L("Οριζόντια %", "Horizontal %"), AutoSize = true, Margin = new(3, 6, 2, 0) });
        position.Controls.Add(horizontal);
        position.Controls.Add(new Label { Text = L("Κατακόρυφα %", "Vertical %"), AutoSize = true, Margin = new(12, 6, 2, 0) });
        position.Controls.Add(vertical);
        Add(position);

        width = Coordinate(MusicOverlaySettings.MinSize, MusicOverlaySettings.MaxSize);
        height = Coordinate(MusicOverlaySettings.MinSize, MusicOverlaySettings.MaxSize);
        var dimensions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        dimensions.Controls.Add(new Label { Text = L("Πλάτος %", "Width %"), AutoSize = true, Margin = new(3, 6, 2, 0) });
        dimensions.Controls.Add(width);
        dimensions.Controls.Add(new Label { Text = L("Ύψος %", "Height %"), AutoSize = true, Margin = new(12, 6, 2, 0) });
        dimensions.Controls.Add(height);
        Add(dimensions);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = true,
            Margin = new Padding(0, 3, 0, 8)
        };
        preview = Button(L("Προεπισκόπηση στο wallpaper", "Preview on wallpaper"));
        reset = Button(L("Κεντράρισμα", "Center visualizer"));
        actions.Controls.Add(preview);
        actions.Controls.Add(reset);
        Add(actions);
        Add(new Label
        {
            Text = L("Η προεπισκόπηση προβάλλεται για 15 δευτερόλεπτα. Δες την στην επιφάνεια εργασίας.",
                "The preview appears on the desktop for 15 seconds."),
            Dock = DockStyle.Fill,
            AutoSize = true,
            Margin = new Padding(3, 3, 3, 10)
        });

        hideTrackMetadata.CheckedChanged += (_, _) =>
        {
            if (loading) return;
            getController()?.SetTrackPrivacy(hideTrackMetadata.Checked);
        };
        enabled.CheckedChanged += (_, _) =>
        {
            if (loading) return;
            getController()?.SetEnabled(enabled.Checked);
            UpdateFromSettings();
        };
        presets.SelectedIndexChanged += (_, _) =>
        {
            if (loading || presets.SelectedIndex < 0) return;
            getController()?.SetPreset(VisualPresets.All[presets.SelectedIndex].Preset);
            UpdateFromSettings();
        };
        typefaces.SelectedIndexChanged += (_, _) =>
        {
            if (!loading && typefaces.SelectedIndex >= 0)
                getController()?.SetTypeface(typefaces.SelectedIndex == 0 ? null : fonts[typefaces.SelectedIndex - 1]);
        };
        display.SelectedIndexChanged += (_, _) => SavePosition();
        horizontal.ValueChanged += (_, _) => SavePosition();
        vertical.ValueChanged += (_, _) => SavePosition();
        preview.Click += (_, _) => getController()?.Preview();
        width.ValueChanged += (_, _) => SaveSize();
        height.ValueChanged += (_, _) => SaveSize();
        reset.Click += (_, _) =>
        {
            getController()?.ResetPosition();
            UpdateFromSettings();
        };

        stateRefresh.Tick += (_, _) => UpdateFromSettings();
        VisibleChanged += (_, _) =>
        {
            if (Visible) { UpdateFromSettings(); stateRefresh.Start(); }
            else stateRefresh.Stop();
        };
        UpdateFromSettings();
        if (Visible) stateRefresh.Start();

        void SavePosition()
        {
            if (loading || display.SelectedIndex < 0) return;
            getController()?.SetPosition(display.SelectedIndex == 0 ? null : display.SelectedItem as string,
                (double)horizontal.Value / 100, (double)vertical.Value / 100);
        }

        void SaveSize()
        {
            if (!loading) getController()?.SetSize((int)width.Value, (int)height.Value);
        }

        void ChooseColor(int index)
        {
            using var picker = new ColorDialog { Color = swatches[index].BackColor, FullOpen = true };
            if (picker.ShowDialog(this) != DialogResult.OK) return;
            getController()?.SetColor(index, picker.Color);
            UpdateFromSettings();
        }

        NumericUpDown Coordinate(int min = 0, int max = 100) => new()
        {
            Minimum = min,
            Maximum = max,
            Width = 62,
            BackColor = background,
            ForeColor = text
        };

        void Add(Control control)
        {
            var row = RowCount++;
            RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(control, 0, row);
        }

        ComboBox Picker() => new()
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 242,
            BackColor = background,
            ForeColor = text,
            Margin = new Padding(3, 3, 3, 16)
        };

        Button Button(string title) => new()
        {
            Text = title,
            AutoSize = true,
            Padding = new Padding(7, 3, 7, 3),
            BackColor = background,
            ForeColor = text,
            UseVisualStyleBackColor = false
        };
    }

    private void UpdateFromSettings()
    {
        var settings = getController()?.Settings;
        loading = true;
        try
        {
            enabled.Enabled = settings != null;
            enabled.Checked = settings?.Enabled == true;
            hideTrackMetadata.Enabled = settings != null;
            hideTrackMetadata.Checked = settings?.HideTrackMetadata == true;
            presets.SelectedIndex = Math.Max(0, Array.FindIndex(VisualPresets.All, preset => preset.Preset == (settings?.Preset ?? VisualPreset.Radial)));
            typefaces.SelectedIndex = settings?.Typeface is { } font ? Array.IndexOf(fonts, font) + 1 : 0;
            presets.Enabled = settings != null;
            typefaces.Enabled = settings != null && fonts.Length > 0;
            var displays = Screen.AllScreens.Select(s => s.DeviceName).Prepend(defaultDisplay).ToArray();
            if (!display.Items.Cast<string>().SequenceEqual(displays))
            {
                display.Items.Clear();
                display.Items.AddRange(displays);
            }
            display.SelectedIndex = Math.Max(0, Array.IndexOf(displays, settings?.Display));
            horizontal.Value = (decimal)Math.Round(Math.Clamp(settings?.X ?? 0, 0, 1) * 100);
            vertical.Value = (decimal)Math.Round(Math.Clamp(settings?.Y ?? 0, 0, 1) * 100);
            display.Enabled = horizontal.Enabled = vertical.Enabled = settings != null;
            width.Value = settings?.WidthPercent ?? 100;
            height.Value = settings?.HeightPercent ?? 100;
            width.Enabled = height.Enabled = resetPalette.Enabled = customColor.Enabled = hueBar.Enabled = settings != null;
            if (settings != null) hueBar.SetColor(settings.PaletteColor(activeSwatch));
            for (var i = 0; i < swatches.Length; i++)
            {
                var color = settings?.PaletteColor(i) ?? SystemColors.Control;
                swatches[i].BackColor = color;
                swatches[i].ForeColor = color.GetBrightness() > 0.5 ? Color.Black : Color.White;
                swatches[i].Enabled = settings != null;
                swatches[i].FlatAppearance.BorderSize = i == activeSwatch ? 2 : 0;
                swatches[i].FlatAppearance.BorderColor = swatches[i].ForeColor;
            }
            preview.Enabled = settings != null;
            reset.Enabled = settings != null;
        }
        finally { loading = false; }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) stateRefresh.Dispose();
        base.Dispose(disposing);
    }
}
