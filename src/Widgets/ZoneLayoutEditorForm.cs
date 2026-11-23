using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using MotionDesk.Services;
using MotionDesk.UI;

namespace MotionDesk.Widgets
{
    // Κλώνος του πραγματικού PowerToys FancyZones Editor: επιλογή οθόνης (αν υπάρχουν πολλές) +
    // gallery από templates (No layout/Focus/Columns/Rows/Grid/Priority Grid) με μίνι
    // προεπισκόπιση ζωνών σε κάθε κάρτα, όπως ζητήθηκε ρητά μετά από screenshots.
    public sealed class ZoneLayoutEditorForm : Form
    {
        // Templates που έχουν ρυθμιζόμενο πλήθος στηλών/σειρών — ζητήθηκε ρητά: δεν υπήρχε τρόπος
        // να διαλέξεις π.χ. 2 στήλες/σειρές αντί για το hardcoded 3, ούτε κάποιο "Custom".
        private static readonly string[] Templates = { "No layout", "Focus", "Columns", "Rows", "Grid", "Priority Grid", "Code & Docs", "Custom" };

        private Screen _selectedScreen;
        private string _selectedTemplate;
        private int _cols = 3;
        private int _rows = 2;
        // Imported layouts are preview-only until the user explicitly clicks Apply.
        private ZoneLayoutData? _importedLayout;
        private readonly FlowLayoutPanel _cardGrid;
        private readonly ZonePreviewPanel _bigPreview;
        private readonly Label _colsLabel;
        private readonly Label _rowsLabel;
        private readonly NumericUpDown _colsStepper;
        private readonly NumericUpDown _rowsStepper;

        public ZoneLayoutEditorForm(Screen? initialScreen = null)
        {
            Flicker.Attach(this);
            Icon = MotionDesk.UI.AppWindowIcon.Value;
            _selectedScreen = initialScreen ?? Screen.PrimaryScreen ?? Screen.AllScreens[0];
            _selectedTemplate = ZoneLayoutStore.GetLayout(_selectedScreen.DeviceName).Template;
            LoadStepperValuesFromLayout();

            Text = LocalizationManager.T("DeskZones.EditorTitle");
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(920, 610);
            MinimumSize = new Size(760, 510);
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = UiTheme.Background;
            ForeColor = UiTheme.TextPrimary;
            HandleCreated += (_, _) => GlassBlur.ApplyWindowDropShadow(Handle);
            KeyPreview = true;
            KeyDown += (_, e) =>
            {
                if (e.KeyCode != Keys.F1) return;
                e.Handled = true;
                MessageBox.Show(this,
                    MainWindow.StudioText("Choose a template with Tab and Enter, adjust columns and rows, then Apply. Import previews a JSON layout without changing the desktop until Apply. Export saves only zones, never private monitor names.",
                        "Επίλεξε διάταξη με Tab και Enter, ρύθμισε στήλες/σειρές και πάτησε Εφαρμογή. Η εισαγωγή JSON προεπισκοπείται χωρίς να μεταβάλει την επιφάνεια εργασίας μέχρι την εφαρμογή. Η εξαγωγή δεν περιέχει ονόματα οθονών."),
                    "Opsis · DeskZones", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4, Padding = new Padding(20) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 53));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 47));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            Controls.Add(root);

            // ---- Screen selector (αν υπάρχουν >1 οθόνες) ----
            if (Screen.AllScreens.Length > 1)
            {
                var screenRow = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = new Padding(0, 0, 0, 10) };
                foreach (var screen in Screen.AllScreens)
                {
                    var tile = new ScreenTile(screen, screen.DeviceName == _selectedScreen.DeviceName);
                    tile.Click += (_, _) =>
                    {
                        _importedLayout = null;
                        _selectedScreen = screen;
                        _selectedTemplate = ZoneLayoutStore.GetLayout(screen.DeviceName).Template;
                        LoadStepperValuesFromLayout();
                        if (_colsStepper != null) _colsStepper.Value = _cols;
                        if (_rowsStepper != null) _rowsStepper.Value = _rows;
                        foreach (Control c in screenRow.Controls) if (c is ScreenTile t) t.SetSelected(t == tile);
                        RefreshBigPreview();
                        RefreshCardSelection();
                        RefreshStepperVisibility();
                    };
                    screenRow.Controls.Add(tile);
                }
                root.Controls.Add(screenRow, 0, 0);
            }
            else
            {
                root.Controls.Add(new Label { AutoSize = true, Text = LocalizationManager.T("DeskZones.EditorSingleScreen"), ForeColor = UiTheme.TextMuted, Font = UiTheme.FontBody }, 0, 0);
            }

            // ---- Μεγάλη προεπισκόπιση της τρέχουσας επιλογής ----
            _bigPreview = new ZonePreviewPanel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 10) };
            root.Controls.Add(_bigPreview, 0, 1);

            // ---- Ρυθμιζόμενο πλήθος στηλών/σειρών — μόνο για Columns/Rows/Grid/Custom ----
            var stepperRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 10) };
            _colsLabel = new Label { Text = LocalizationManager.T("DeskZones.EditorColumns") + ":", AutoSize = true, ForeColor = UiTheme.TextSecondary, Font = UiTheme.FontBody, Padding = new Padding(0, 6, 6, 0) };
            _colsStepper = new NumericUpDown { Minimum = 1, Maximum = 6, Value = _cols, Width = 56 };
            _colsStepper.ValueChanged += (_, _) => { _cols = (int)_colsStepper.Value; _importedLayout = null; RefreshBigPreview(); };
            _rowsLabel = new Label { Text = LocalizationManager.T("DeskZones.EditorRows") + ":", AutoSize = true, ForeColor = UiTheme.TextSecondary, Font = UiTheme.FontBody, Padding = new Padding(16, 6, 6, 0) };
            _rowsStepper = new NumericUpDown { Minimum = 1, Maximum = 4, Value = _rows, Width = 56 };
            _rowsStepper.ValueChanged += (_, _) => { _rows = (int)_rowsStepper.Value; _importedLayout = null; RefreshBigPreview(); };
            stepperRow.Controls.Add(_colsLabel);
            stepperRow.Controls.Add(_colsStepper);
            stepperRow.Controls.Add(_rowsLabel);
            stepperRow.Controls.Add(_rowsStepper);
            root.Controls.Add(stepperRow, 0, 2);

            // ---- Template gallery ----
            _cardGrid = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = true, FlowDirection = FlowDirection.LeftToRight };
            GlassBlur.ApplyScrollbarTheme(_cardGrid, UiTheme.Background.GetBrightness() < 0.5f);
            int templateTabOrder = 20;
            foreach (var t in Templates)
            {
                var card = new TemplateCard(t, t == _selectedTemplate) { TabIndex = templateTabOrder++ };
                card.Click += (_, _) =>
                {
                    _importedLayout = null;
                    _selectedTemplate = t;
                    RefreshCardSelection();
                    RefreshBigPreview();
                    RefreshStepperVisibility();
                };
                _cardGrid.Controls.Add(card);
            }
            root.Controls.Add(_cardGrid, 1, 1);
            root.SetRowSpan(_cardGrid, 2);

            // ---- Κουμπιά ----
            var buttonRow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            var applyBtn = new PillButton { Text = LocalizationManager.T("DeskZones.EditorApply"), Width = 140, Height = 34, BackColor = UiTheme.AccentCyan, ForeColor = Color.Black };
            applyBtn.Click += (_, _) =>
            {
                ZoneLayoutStore.SetLayout(_selectedScreen.DeviceName, BuildSelected());
                DialogResult = DialogResult.OK;
                Close();
            };
            var cancelBtn = new PillButton { Text = LocalizationManager.T("Common.Close"), Width = 110, Height = 34, BackColor = UiTheme.SurfaceHover, ForeColor = UiTheme.TextPrimary };
            cancelBtn.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
            buttonRow.Controls.Add(applyBtn);
            buttonRow.Controls.Add(cancelBtn);

            // Roadmap #47: explicit JSON export / preview import, never silently
            // applies imported window positions to the user's live desktop.
            var exportBtn = new PillButton
            {
                Text = MainWindow.StudioText("Export JSON…", "Εξαγωγή JSON…"),
                Width = 138, Height = 34, BackColor = UiTheme.SurfaceHover, ForeColor = UiTheme.TextPrimary,
                AccessibleName = MainWindow.StudioText("Export zone layout", "Εξαγωγή διάταξης ζωνών")
            };
            exportBtn.Click += (_, _) =>
            {
                using var save = new SaveFileDialog
                {
                    Filter = "JSON (*.json)|*.json", DefaultExt = "json", AddExtension = true,
                    FileName = "opsis-deskzones.json"
                };
                if (save.ShowDialog(this) != DialogResult.OK) return;
                try { File.WriteAllText(save.FileName, ZoneLayoutStore.ExportLayoutJson(BuildSelected())); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
                { MessageBox.Show(this, ex.Message, "Opsis", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            };
            var importBtn = new PillButton
            {
                Text = MainWindow.StudioText("Import JSON…", "Εισαγωγή JSON…"),
                Width = 138, Height = 34, BackColor = UiTheme.SurfaceHover, ForeColor = UiTheme.TextPrimary,
                AccessibleName = MainWindow.StudioText("Preview imported zone layout", "Προεπισκόπηση εισαγόμενης διάταξης ζωνών")
            };
            importBtn.Click += (_, _) =>
            {
                using var open = new OpenFileDialog { Filter = "JSON (*.json)|*.json", CheckFileExists = true };
                if (open.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var info = new FileInfo(open.FileName);
                    if (info.Length > 65536) throw new InvalidDataException("Layout file exceeds 64 KiB.");
                    if (!ZoneLayoutStore.TryImportLayoutJson(File.ReadAllText(open.FileName), out var imported, out var problem))
                        throw new InvalidDataException(problem);
                    _selectedTemplate = imported.Template;
                    _importedLayout = imported;
                    RefreshCardSelection(); RefreshBigPreview(); RefreshStepperVisibility();
                    MessageBox.Show(this,
                        MainWindow.StudioText("Imported layout is shown in the preview. Click Apply to use it on this monitor, or select another template to discard it.",
                            "Η εισαγόμενη διάταξη εμφανίζεται στην προεπισκόπηση. Πάτησε Εφαρμογή για αυτή την οθόνη ή επίλεξε άλλο πρότυπο για ακύρωση."),
                        "Opsis", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
                { MessageBox.Show(this, ex.Message, "Opsis", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            };
            buttonRow.Controls.Add(exportBtn);
            buttonRow.Controls.Add(importBtn);
            root.Controls.Add(buttonRow, 0, 3);
            root.SetColumnSpan(buttonRow, 2);

            AcceptButton = applyBtn;
            CancelButton = cancelBtn;

            RefreshStepperVisibility();
            RefreshBigPreview();
        }

        private void RefreshCardSelection()
        {
            foreach (Control c in _cardGrid.Controls)
                if (c is TemplateCard card) card.SetSelected(card.Template == _selectedTemplate);
        }

        // ΔΙΟΡΘΩΣΗ πραγματικού bug (ζητήθηκε ρητά): "επέλεξα δύο σειρές, μου δείχνει τρεις
        // στήλες" — το "Rows" preset ήταν ΚΛΕΙΔΩΜΕΝΟ σε σταθερό πλήθος (χωρίς stepper), οπότε ο
        // μόνος τρόπος να αλλάξει ο χρήστης το πλήθος σειρών ήταν να περάσει σε "Custom" — αλλά
        // το "Custom" εφαρμόζει ΠΑΝΤΑ πλήρες πλέγμα (στήλες ΚΑΙ σειρές μαζί, ίδιος κώδικας με το
        // "Grid"), οπότε αλλάζοντας μόνο το stepper σειρών εμφανιζόταν αναπάντεχα ΚΑΙ η
        // προεπιλεγμένη στήλωση (3 στήλες) μαζί με τις 2 σειρές — αυτό έβλεπε ο χρήστης ως
        // "3 στήλες" αντί για τις 2 σειρές που περίμενε. Τώρα το κάθε preset δείχνει ΜΟΝΟ το
        // δικό του σχετικό stepper (Columns->στήλες, Rows->σειρές, Grid/Custom->και τα δύο) και
        // BuildSelected() περνάει πάντα τις τρέχουσες τιμές — το "Rows" με 2 σειρές είναι πλέον
        // πραγματικά 2 πλήρους-πλάτους σειρές, όχι πλέγμα.
        private void RefreshStepperVisibility()
        {
            bool showCols = _selectedTemplate is "Columns" or "Grid" or "Custom";
            bool showRows = _selectedTemplate is "Rows" or "Grid" or "Custom";
            _colsLabel.Visible = _colsStepper.Visible = showCols;
            _rowsLabel.Visible = _rowsStepper.Visible = showRows;
        }

        // Οι τιμές στηλών/σειρών δεν αποθηκεύονται ξεχωριστά — πριν, κάθε άνοιγμα του editor τις επανέφερε στο
        // 3×2 και ένα "Εφαρμογή" πάνω σε υπάρχον Custom/Grid layout το άλλαζε σιωπηλά. Τις συμπεραίνουμε από
        // το αποθηκευμένο layout (πλήθος διακριτών X/Y θέσεων των ζωνών).
        private void LoadStepperValuesFromLayout()
        {
            var layout = ZoneLayoutStore.GetLayout(_selectedScreen.DeviceName);
            if (layout.Zones.Count == 0) return;
            int distinctX = layout.Zones.Select(z => Math.Round(z.X, 3)).Distinct().Count();
            int distinctY = layout.Zones.Select(z => Math.Round(z.Y, 3)).Distinct().Count();
            switch (layout.Template)
            {
                case "Columns": _cols = Math.Clamp(layout.Zones.Count, 1, 6); break;
                case "Rows": _rows = Math.Clamp(layout.Zones.Count, 1, 4); break;
                case "Grid":
                case "Custom": _cols = Math.Clamp(distinctX, 1, 6); _rows = Math.Clamp(distinctY, 1, 4); break;
            }
        }

        private ZoneLayoutData BuildSelected() => _importedLayout ?? (_selectedTemplate is "Columns" or "Rows" or "Grid" or "Custom"
            ? ZoneLayoutStore.BuildTemplate(_selectedTemplate, _cols, _rows)
            : ZoneLayoutStore.BuildTemplate(_selectedTemplate));

        private void RefreshBigPreview() => _bigPreview.SetLayout(BuildSelected());

        // Μικρή "pill" κάρτα οθόνης (π.χ. "1  1920×1080") — μιμείται το πραγματικό FancyZones Editor.
        private sealed class ScreenTile : Panel
        {
            private readonly Screen _screen;
            private bool _selected;
            public ScreenTile(Screen screen, bool selected)
            {
                _screen = screen; _selected = selected;
                Size = new Size(140, 60);
                Margin = new Padding(0, 0, 10, 0);
                Cursor = Cursors.Hand;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            }
            public void SetSelected(bool s) { _selected = s; Invalidate(); }
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
                using var path = UiTheme.RoundedPath(new Rectangle(0, 0, Width - 1, Height - 1), 8);
                using var fill = new SolidBrush(_selected ? UiTheme.AccentSoft : UiTheme.Surface);
                using var border = new Pen(_selected ? UiTheme.AccentCyan : UiTheme.Border, _selected ? 2f : 1f);
                g.FillPath(fill, path); g.DrawPath(border, path);
                using var font = new Font("Segoe UI", 9f);
                using var brush = new SolidBrush(_selected ? UiTheme.AccentCyan : UiTheme.TextSecondary);
                var text = $"{_screen.Bounds.Width}×{_screen.Bounds.Height}" + (_screen.Primary ? "\n(primary)" : "");
                using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString(text, font, brush, new RectangleF(0, 0, Width, Height), sf);
            }
        }

        // Κάρτα template με μίνι owner-draw προεπισκόπιση ζωνών — ίδια ιδέα με τις μικρές
        // εικόνες προεπισκόπησης στο πραγματικό FancyZones Editor (No layout/Focus/Columns/...).
        private sealed class TemplateCard : Panel
        {
            public string Template { get; }
            private bool _selected;
            private readonly ZonePreviewPanel _mini;
            private readonly Label _label;

            public TemplateCard(string template, bool selected)
            {
                Template = template; _selected = selected;
                // Bug fix: με 150x110 και μονογράμμιο label (Height=22), μεγαλύτερα ονόματα
                // (π.χ. "Πλέγμα Προτεραιότητας") έκαναν word-wrap σε 2 γραμμές αλλά η 2η γραμμή
                // έκοβε αόρατη έξω από το ύψος του label. Φαρδύτερη κάρτα + ψηλότερο, δίγραμμο
                // label διορθώνει και τα δύο.
                Size = new Size(176, 122);
                Margin = new Padding(6);
                Cursor = Cursors.Hand;
                BackColor = Color.Transparent;
                AccessibleRole = AccessibleRole.RadioButton;
                AccessibleName = template == "Code & Docs" ? MainWindow.StudioText("Code and documentation layout", "Διάταξη κώδικα και τεκμηρίωσης") : template;
                TabStop = true;
                SetStyle(ControlStyles.Selectable, true);
                KeyDown += (_, e) =>
                {
                    if (e.KeyCode is not (Keys.Space or Keys.Enter)) return;
                    e.SuppressKeyPress = true;
                    OnClick(EventArgs.Empty);
                };
                GotFocus += (_, _) => Invalidate();
                LostFocus += (_, _) => Invalidate();
                MouseDown += (_, _) => Focus();

                _mini = new ZonePreviewPanel { Location = new Point(8, 8), Size = new Size(160, 76), Enabled = false };
                _mini.SetLayout(ZoneLayoutStore.BuildTemplate(template));
                Controls.Add(_mini);

                _label = new Label { Text = template == "Code & Docs"
                    ? MainWindow.StudioText("Code & documentation", "Κώδικας & τεκμηρίωση")
                    : LocalizationManager.T("DeskZones.Template." + template.Replace(" ", "")), AutoSize = false, Size = new Size(176, 34), Location = new Point(0, 86), TextAlign = ContentAlignment.MiddleCenter, ForeColor = UiTheme.TextSecondary, Font = UiTheme.FontBody };
                Controls.Add(_label);

                // ΚΡΙΣΙΜΟ bug fix: "this" ΔΕΝ πρέπει να είναι σε αυτή τη λίστα — το card παίρνει
                // ήδη πραγματικά Click events από τα Windows όταν κλικαριστεί απευθείας (π.χ. στο
                // περιθώριο γύρω από τα _mini/_label). Το να κάνεις this.Click += (_,_) =>
                // OnClick(...) είναι ΑΠΕΙΡΗ ΑΝΑΔΡΟΜΗ: το OnClick() είναι ακριβώς η μέθοδος που
                // ΠΥΡΟΔΟΤΕΙ το ίδιο το Click event, άρα ο handler καλούσε συνεχώς τον εαυτό του —
                // StackOverflowException, μη-πιάσιμο από .NET, τερματίζει αμέσως όλη τη
                // διεργασία χωρίς κανένα exception log (αυτό ήταν το "η εφαρμογή έκλεινε σιωπηλά
                // όταν πατούσα οποιαδήποτε κάρτα template" — επιβεβαιώθηκε με προσωρινό debug
                // log: ο handler δεν πρόλαβε καν να γράψει το πρώτο του μήνυμα). Μόνο τα ΠΑΙΔΙΑ
                // (_mini/_label) χρειάζονται αυτή τη χειροκίνητη προώθηση, αφού αλλιώς θα
                // "κατάπιναν" το κλικ χωρίς να φτάσει ποτέ στο ίδιο το card.
                foreach (Control c in new Control[] { _mini, _label })
                {
                    c.Click += (_, _) => OnClick(EventArgs.Empty);
                    c.Cursor = Cursors.Hand;
                }
                Cursor = Cursors.Hand;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            }

            public void SetSelected(bool s) { _selected = s; _label.ForeColor = s ? UiTheme.AccentCyan : UiTheme.TextSecondary; Invalidate(); }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
                using var path = UiTheme.RoundedPath(new Rectangle(0, 0, Width - 1, Height - 1), 10);
                using var fill = new SolidBrush(_selected ? UiTheme.AccentSoft : UiTheme.Surface);
                using var border = new Pen(_selected || Focused ? UiTheme.AccentCyan : UiTheme.Border, _selected || Focused ? 2.5f : 1f);
                g.FillPath(fill, path); g.DrawPath(border, path);
                if (Focused) ControlPaint.DrawFocusRectangle(g, new Rectangle(4, 4, Width - 8, Height - 8));
            }
        }

    }

    // Draw-only panel: ζωγραφίζει ένα ZoneLayoutData μέσα στα δικά του όρια — reused στη μεγάλη
    // προεπισκόπιση του editor, στις μίνι κάρτες template, ΚΑΙ στη σελίδα DeskZones.
    public sealed class ZonePreviewPanel : Panel
    {
        private ZoneLayoutData _layout = new();
        public ZonePreviewPanel() { SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true); BackColor = Color.Transparent; }
        public void SetLayout(ZoneLayoutData layout) { _layout = layout; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            using var bg = new SolidBrush(UiTheme.Background);
            using var bgPath = UiTheme.RoundedPath(ClientRectangle, 6);
            g.FillPath(bg, bgPath);

            // ΔΙΟΡΘΩΣΗ "η προεπισκόπηση κόβεται ελαφρά στα δεξιά": ζώνες που ακουμπάνε στην άκρη
            // (X+Width=1 ή Y+Height=1) έδιναν rect.Right/Bottom == Width/Height ακριβώς, οπότε το
            // πάχος 1.4px του περιγράμματος (μισό εκατέρωθεν της γραμμής) ζωγραφιζόταν ΜΙΣΟ έξω
            // από το ClientRectangle και κοβόταν. Λύση: 1px εσωτερικό περιθώριο γύρω-γύρω πριν
            // μετατραπούν τα fractional X/Y/Width/Height σε pixels.
            const int previewInset = 1;
            int drawW = Math.Max(1, Width - previewInset * 2);
            int drawH = Math.Max(1, Height - previewInset * 2);
            foreach (var z in _layout.Zones)
            {
                var rect = new Rectangle(previewInset + (int)(z.X * drawW), previewInset + (int)(z.Y * drawH), (int)(z.Width * drawW), (int)(z.Height * drawH));
                if (rect.Width <= 2 || rect.Height <= 2) continue;
                using var fill = new SolidBrush(Color.FromArgb(70, UiTheme.AccentCyan));
                using var border = new Pen(UiTheme.AccentCyan, 1.4f);
                using var path = UiTheme.RoundedPath(rect, 4);
                g.FillPath(fill, path);
                g.DrawPath(border, path);
            }
            if (_layout.Zones.Count == 0)
            {
                using var mutedPen = new Pen(UiTheme.Border, 1.4f) { DashStyle = DashStyle.Dash };
                g.DrawRectangle(mutedPen, 4, 4, Width - 9, Height - 9);
            }
        }
    }

    // Πλήρως στρογγυλεμένο ("pill") κουμπί — ζητήθηκε ρητά ώστε τα ορθογώνια κουμπιά να γίνουν pills.
    internal sealed class PillButton : Button
    {
        public PillButton()
        {
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
            UseVisualStyleBackColor = false;
            UseMnemonic = false;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            MouseEnter += (_, _) => Invalidate();
            MouseLeave += (_, _) => Invalidate();
            MainWindow.ClipActionButton(this);
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            MainWindow.ClipActionButton(this);
        }

        protected override void OnPaintBackground(PaintEventArgs e) => MainWindow.PaintActionBackdrop(this, e);

        protected override void OnPaint(PaintEventArgs e)
        {
            bool hovered = Enabled && ClientRectangle.Contains(PointToClient(Cursor.Position));
            MainWindow.PaintActionButton(this, e.Graphics, hovered ? ControlPaint.Light(BackColor, .12f) : BackColor,
                Enabled ? ForeColor : UiTheme.TextSecondary, UiTheme.AccentCyan);
        }
    }
}
