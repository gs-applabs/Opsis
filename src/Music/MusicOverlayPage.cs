using MotionDesk.Services;

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
    private readonly ComboBox audioInput;
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
    private readonly System.Windows.Forms.Timer stateRefresh = new() { Interval = 5000 };
    private long nextDeviceRefresh;
    private readonly RadioButton[] anchors = new RadioButton[9];
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
        Add(new Label { Text = L("Πηγή ήχου visualizer", "Visualizer audio source"), AutoSize = true });
        audioInput = Picker();
        Add(audioInput);
        Add(new Label { Text = L("Προεπιλογή: έξοδος συστήματος. Επίλεξε ρητά μικρόφωνο για λήψη εισόδου.",
            "Default: system output. Explicitly choose a microphone to capture input."), AutoSize = true });

        Add(new Label { Text = L("Θέση στην επιφάνεια εργασίας", "Desktop placement"), AutoSize = true, Font = MotionDesk.UI.UiTheme.FontHeading });
        Add(new Label { Text = L("Διάλεξε θέση στο πλέγμα ή ρύθμισε τα ποσοστά. Η ώρα και ο καιρός έχουν προτεραιότητα· η αυτόματη μετατόπιση δεν αλλάζει τη θέση σου.", "Choose an anchor or fine-tune the percentages. Clock and weather take priority; automatic relocation does not change your saved position."), AutoSize = true, MaximumSize = new Size(630, 0) });
        horizontal = Coordinate();
        vertical = Coordinate();
        horizontal.AccessibleName = L("Οριζόντια θέση (%)", "Horizontal position (%)");
        vertical.AccessibleName = L("Κατακόρυφη θέση (%)", "Vertical position (%)");
        var anchorGrid = new TableLayoutPanel { ColumnCount = 3, RowCount = 3, AutoSize = true, Margin = new Padding(3, 5, 3, 10), AccessibleName = L("Επιλογή θέσης", "Position anchors") };
        string[] columns = [L("Αριστερά", "Left"), L("Κέντρο", "Center"), L("Δεξιά", "Right")];
        string[] rows = [L("Πάνω", "Top"), L("Μέση", "Middle"), L("Κάτω", "Bottom")];
        for (int i = 0; i < anchors.Length; i++)
        {
            int index = i;
            var anchor = new RadioButton { Appearance = Appearance.Button, AutoSize = false, Size = new Size(102, 34), TextAlign = ContentAlignment.MiddleCenter,
                Text = rows[i / 3] + " " + columns[i % 3], AccessibleName = rows[i / 3] + " " + columns[i % 3], FlatStyle = FlatStyle.Flat,
                Margin = new Padding(3), UseVisualStyleBackColor = true };
            anchor.CheckedChanged += (_, _) =>
            {
                if (loading || !anchor.Checked || display.SelectedIndex < 0) return;
                double x = index % 3 * .5, y = index / 3 * .5;
                getController()?.SetPosition(display.SelectedIndex == 0 ? null : display.SelectedItem as string, x, y);
                UpdateFromSettings();
            };
            anchors[i] = anchor;
            anchorGrid.Controls.Add(anchor, i % 3, i / 3);
        }
        Add(anchorGrid);
        var position = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        position.Controls.Add(new Label { Text = L("Οριζόντια %", "Horizontal %"), AutoSize = true, Margin = new(3, 6, 2, 0) });
        position.Controls.Add(horizontal);
        position.Controls.Add(new Label { Text = L("Κατακόρυφα %", "Vertical %"), AutoSize = true, Margin = new(12, 6, 2, 0) });
        position.Controls.Add(vertical);
        Add(position);

        width = Coordinate(MusicOverlaySettings.MinSize, MusicOverlaySettings.MaxSize);
        height = Coordinate(MusicOverlaySettings.MinSize, MusicOverlaySettings.MaxSize);
        width.AccessibleName = L("Πλάτος (%)", "Width (%)");
        height.AccessibleName = L("Ύψος (%)", "Height (%)");
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
        reset = Button(L("Προεπιλεγμένη θέση", "Default position"));
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
        audioInput.SelectedIndexChanged += (_, _) =>
        {
            if (loading || audioInput.SelectedIndex < 0) return;
            string? selected = audioInput.SelectedIndex == 0 ? null :
                (audioInput.SelectedItem as InputDeviceItem)?.Id;
            if (selected != null && MessageBox.Show(FindForm(),
                L("Το μικρόφωνο μπορεί να καταγράψει ιδιωτικούς ήχους. Να χρησιμοποιηθεί μόνο τοπικά για οπτικοποίηση;",
                  "A microphone can capture private sound. Use it locally for visualization?"),
                "Opsis", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            { UpdateFromSettings(); return; }
            getController()?.SetAudioInput(selected);
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

        // Never interrupt an open picker or an in-progress keyboard/numeric edit.
        stateRefresh.Tick += (_, _) => { if (!ContainsFocus) UpdateFromSettings(); };
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
            RefreshAnchors((double)horizontal.Value / 100, (double)vertical.Value / 100, true);
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
            AccessibleName = title,
            FlatStyle = FlatStyle.Flat,
            MinimumSize = new Size(0, 34),
            AutoSize = true,
            Padding = new Padding(12, 4, 12, 4),
            BackColor = background,
            ForeColor = text,
            UseVisualStyleBackColor = false
        };
    }

    private void RefreshAnchors(double x, double y, bool available)
    {
        bool wasLoading = loading;
        loading = true;
        try
        {
            for (int i = 0; i < anchors.Length; i++)
            {
                anchors[i].Enabled = available;
                anchors[i].Checked = available && Math.Abs(x - i % 3 * .5) < .001 && Math.Abs(y - i / 3 * .5) < .001;
            }
        }
        finally { loading = wasLoading; }
    }

    private sealed record InputDeviceItem(string Id, string Name)
    { public override string ToString() => Name; }

    private void UpdateFromSettings()
    {
        var controller = getController();
        var settings = controller?.Settings;
        bool permitted = controller?.SessionPermitted == true;
        loading = true;
        try
        {
            enabled.Enabled = settings != null;
            enabled.Checked = settings?.Enabled == true;
            enabled.Text = settings != null && !permitted
                ? L("Ενεργοποίηση μουσικού visualizer (αναστολή συνεδρίας)", "Enable music visualizer (session suspended)")
                : L("Ενεργοποίηση μουσικού visualizer", "Enable music visualizer");
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
            RefreshAnchors(settings?.X ?? MusicOverlaySettings.DefaultX, settings?.Y ?? MusicOverlaySettings.DefaultY, settings != null);
            if (Environment.TickCount64 >= nextDeviceRefresh && !audioInput.DroppedDown)
            {
                nextDeviceRefresh = Environment.TickCount64 + 5000;
                var sources = SharedLoopbackCapture.AvailableInputs();
                var items = new List<object> { defaultDisplay + " · output loopback" };
                items.AddRange(sources.Select(item => new InputDeviceItem(item.Id, item.Name)));
                if (!audioInput.Items.Cast<object>().SequenceEqual(items))
                {
                    audioInput.Items.Clear();
                    audioInput.Items.AddRange(items.ToArray());
                }
            }
            audioInput.SelectedIndex = Math.Max(0, audioInput.Items.Cast<object>().ToList().FindIndex(item =>
                item is InputDeviceItem device && device.Id == settings?.InputDeviceId));
            audioInput.Enabled = settings != null;
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
            preview.Enabled = settings != null && permitted;
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
