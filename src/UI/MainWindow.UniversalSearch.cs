using MotionDesk.Services;
using MotionDesk.Widgets;

namespace MotionDesk.UI;

public sealed partial class MainWindow
{
    private TextBox? _universalSearch;
    private UniversalSearchField? _universalSearchField;
    private ListBox? _universalSearchResults;
    private readonly Dictionary<string, string> _visitedSearchText = new(StringComparer.Ordinal);

    private sealed record SearchDestination(string Title, string Terms, Action Open)
    {
        public override string ToString() => Title;
    }

    private static void AddSkinSelector(Control card, Control origin)
    {
        var choices = Enum.GetValues<WindowSkin>();
        string[] labels = choices.Select(skin => skin == WindowSkin.Classic ? StudioText("Classic 2.6.0", "Κλασικό 2.6.0") : skin.ToString()).ToArray();
        FxCombo(card, StudioText("Window skin", "Skin παραθύρου"), labels,
            labels[Array.IndexOf(choices, SelectedSkin(AppSettings.Load()))],
            label => (origin.FindForm() as MainWindow)?.SetWindowSkin(choices[Array.IndexOf(labels, label)]));
    }

    private void AttachUniversalSearch()
    {
        var wrap = new Panel { Dock = DockStyle.Right, Width = 320, BackColor = Color.Transparent };
        var field = _universalSearchField = new UniversalSearchField(() => CurrentSkinColors);
        var input = _universalSearch = field.Input;
        wrap.Controls.Add(field);
        _header.Controls.Add(wrap);
        wrap.BringToFront();
        wrap.Resize += (_, _) => ArrangeField();
        input.FontChanged += (_, _) => ArrangeField();
        void ArrangeField()
        {
            int inset = 8;
            int height = Math.Max(input.PreferredHeight + 10, (int)Math.Round(34d * field.DeviceDpi / 96));
            field.SetBounds(inset, Math.Max(0, (wrap.ClientSize.Height - height) / 2), Math.Max(1, wrap.ClientSize.Width - 2 * inset), height);
            if (_universalSearchResults is { Visible: true } list) PositionSearchResults(list);
        }
        ArrangeField();
        input.TextChanged += (_, _) => UpdateUniversalSearch();
        input.GotFocus += (_, _) => UpdateUniversalSearch();
        input.LostFocus += (_, _) =>
        {
            if (IsHandleCreated) BeginInvoke(() => { if (!input.IsDisposed && !input.Focused && _universalSearchResults?.Focused != true) HideUniversalSearch(); });
        };
        input.KeyDown += (_, e) =>
        {
            var results = _universalSearchResults;
            switch (e.KeyCode)
            {
                case Keys.Down when results is { Visible: true } && results.Items.Count > 0:
                    results.SelectedIndex = Math.Min(results.Items.Count - 1, results.SelectedIndex + 1);
                    break;
                case Keys.Up when results is { Visible: true } && results.Items.Count > 0:
                    results.SelectedIndex = Math.Max(0, results.SelectedIndex - 1);
                    break;
                case Keys.Enter:
                    if (results?.SelectedItem is SearchDestination item) OpenSearchDestination(item);
                    else { HideUniversalSearch(); ShowCommandPalette(input.Text); }
                    break;
                case Keys.Escape:
                    HideUniversalSearch();
                    input.Clear();
                    break;
                default: return;
            }
            e.SuppressKeyPress = true;
        };
    }

    private void ColorUniversalSearch()
    {
        if (_universalSearch == null || _universalSearchField == null) return;
        _universalSearch.AccessibleName = StudioText("Search Opsis", "Αναζήτηση στο Opsis");
        _universalSearch.PlaceholderText = _universalSearch.AccessibleName + "  ·  Ctrl+K";
        var palette = CurrentSkinColors;
        _universalSearchField.BackColor = palette.Surface;
        _universalSearch.BackColor = palette.Surface;
        _universalSearch.ForeColor = palette.Text;
        _universalSearchField.Invalidate();
        if (_universalSearchResults is { } results)
        {
            results.BackColor = palette.Surface;
            results.ForeColor = palette.Text;
        }
    }

    private sealed class UniversalSearchField : Panel
    {
        private readonly Func<WindowSkinColors> _colors;
        internal TextBox Input { get; }

        internal UniversalSearchField(Func<WindowSkinColors> colors)
        {
            _colors = colors;
            DoubleBuffered = true;
            ResizeRedraw = true;
            Input = new TextBox { BorderStyle = BorderStyle.None, Font = UiTheme.FontBody, MaxLength = 160 };
            Input.GotFocus += (_, _) => Invalidate();
            Input.LostFocus += (_, _) => Invalidate();
            Controls.Add(Input);
        }

        private int Dpi(int pixels) => (int)Math.Round(pixels * DeviceDpi / 96d);

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (Input == null) return;
            int left = Dpi(40), right = Dpi(14);
            Input.SetBounds(left, Math.Max(1, (Height - Input.PreferredHeight) / 2), Math.Max(1, Width - left - right), Input.PreferredHeight);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (Width < 2 || Height < 2) return;
            var palette = _colors();
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var outline = UiTheme.RoundedPath(new Rectangle(1, 1, Width - 2, Height - 2), Height / 2);
            using var border = new Pen(Input.Focused ? palette.Accent : palette.Border);
            e.Graphics.DrawPath(border, outline);
            int size = Dpi(18);
            IconRenderer.Draw(e.Graphics, "Search", new Rectangle(Dpi(14), (Height - size) / 2, size, size),
                Input.Focused ? palette.Accent : palette.Secondary);
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (Width > 2 && Height > 2) UiTheme.ApplyRoundedRegion(this, Height / 2);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left) Input.Focus();
        }
    }

    private void HideUniversalSearch()
    {
        if (_universalSearchResults != null) _universalSearchResults.Visible = false;
    }

    private IEnumerable<SearchDestination> SearchDestinations()
    {
        var labels = new Dictionary<string, string>
        {
            ["Dashboard"] = "overview αρχική επισκόπηση",
            ["Wallpaper"] = "weather athens wallpaper lock gif music visualizer workspace φόντο καιρός αρχαία αθήνα κλείδωμα μουσική εικονική επιφάνεια",
            ["DeskMusicVisualizer"] = "music visualizer media oscilloscope spectrum audio μουσική ήχος οπτικοποίηση",
            ["DeskContainers"] = "containers rules smart layouts quick hide icons τίτλος κανόνες έξυπνα διατάξεις εικονίδια απόκρυψη ορατά",
            ["DeskStrip"] = "taskbar start toolbar dock replacement γραμμή εργασιών έναρξη",
            ["DeskZones"] = "tiling zones gaps zoning snap snapping threshold grid παράθυρα ζώνες στοίβα πλέγμα στοίχιση προσκόλληση",
            ["Theme"] = "skin atlas inspector index split studio classic colors theme θέμα χρώματα κλασικό εμφάνιση",
            ["Widgets"] = "widget catalog sdk plugin library κατάλογος πρόσθετα",
            ["Settings"] = "language sound scale settings γλώσσα μέγεθος ρυθμίσεις",
            ["Automation"] = "profiles automation triggers προφίλ αυτοματισμός",
            ["Personalization"] = "lockscreen cursors εξατομίκευση κλείδωμα δρομέας"
        };
        foreach (var (key, button) in _navButtons)
        {
            string header = button.AccessibleName ?? button.Text;
            string words = labels.GetValueOrDefault(key) + " " + _visitedSearchText.GetValueOrDefault(key);
            string query = _universalSearch?.Text ?? "";
            yield return new SearchDestination(header, header + " " + key + " " + words, () =>
            {
                NavigateTo(key);
                PageSearch.Reveal(_content, query);
            });
        }
    }

    private void UpdateUniversalSearch()
    {
        if (_universalSearch is not { } input) return;
        string query = input.Text.Trim();
        if (query.Length == 0) { HideUniversalSearch(); return; }
        if (_universalSearchResults == null)
        {
            _universalSearchResults = new ListBox { IntegralHeight = false, BorderStyle = BorderStyle.FixedSingle, Font = UiTheme.FontBody, TabStop = false };
            _universalSearchResults.MouseUp += (_, e) =>
            {
                int hit = _universalSearchResults.IndexFromPoint(e.Location);
                if (hit >= 0 && _universalSearchResults.Items[hit] is SearchDestination item) OpenSearchDestination(item);
            };
            _universalSearchResults.LostFocus += (_, _) => { if (!input.Focused) HideUniversalSearch(); };
            _universalSearchResults.KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Escape) { HideUniversalSearch(); input.Focus(); }
                else if (e.KeyCode == Keys.Enter && _universalSearchResults.SelectedItem is SearchDestination item) OpenSearchDestination(item);
            };
            Controls.Add(_universalSearchResults);
        }
        var list = _universalSearchResults;
        var entries = SearchDestinations().Where(item => PageSearch.Matches(item.Terms, query))
            .OrderByDescending(item => PageSearch.Matches(item.Title, query)).Take(8).ToList();
        entries.Add(new SearchDestination(StudioText("More commands and apps…", "Περισσότερες εντολές και εφαρμογές…"), "", () => ShowCommandPalette(query)));
        list.Items.Clear();
        list.Items.AddRange(entries.ToArray());
        list.SelectedIndex = 0;
        PositionSearchResults(list);
        list.BackColor = CurrentSkinColors.Surface;
        list.ForeColor = CurrentSkinColors.Text;
        list.Visible = true;
        list.BringToFront();
    }

    private void PositionSearchResults(ListBox list)
    {
        if (_universalSearchField is not { IsHandleCreated: true } field) return;
        var anchor = PointToClient(field.PointToScreen(new Point(0, field.Height + 3)));
        list.SetBounds(anchor.X, anchor.Y, field.Width, Math.Min(222, list.ItemHeight * list.Items.Count + 5));
    }

    private void OpenSearchDestination(SearchDestination item)
    {
        HideUniversalSearch();
        _universalSearch?.Clear();
        item.Open();
    }
}
