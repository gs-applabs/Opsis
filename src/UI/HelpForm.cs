using MotionDesk.Services;
using Opsis.UI;

namespace MotionDesk.UI;

internal enum HelpSection { Instructions, License, History }

internal sealed class HelpForm : OpsisWindow
{
    private static HelpForm? _open;
    private readonly TabControl _tabs = new() { Name = "HelpTabs", Dock = DockStyle.Fill, DrawMode = TabDrawMode.OwnerDrawFixed };
    private readonly TabPage _licensePage;
    private readonly TabPage _historyPage;
    private readonly IReadOnlyList<(string Version, string Date)> _history;
    private readonly RichTextBox _instructions;
    private readonly RichTextBox _license;
    private readonly ListBox _versionPicker = new()
    {
        Name = "HistoryVersionPicker", Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle, IntegralHeight = false
    };
    private readonly RichTextBox _notes;
    private readonly TextBox _search = new() { Name = "HelpSearch", Width = 320, MaxLength = 100 };
    private readonly Label _searchHint = new() { AutoSize = true, Padding = new Padding(8, 6, 0, 0) };
    private bool _licenseLoaded;
    private bool _settingContext;

    internal static void ShowSingle(IWin32Window? owner, IReadOnlyList<(string Version, string Date)> history,
        HelpSection section = HelpSection.Instructions, string? query = null, string? controlTopic = null)
    {
        if (_open is { IsDisposed: false } existing)
        {
            existing._tabs.SelectedIndex = (int)section;
            existing.ShowContextualHelp(query, controlTopic);
            if (existing.WindowState == FormWindowState.Minimized) existing.WindowState = FormWindowState.Normal;
            existing.Activate();
            return;
        }
        var form = new HelpForm(history, section);
        _open = form;
        form.ShowContextualHelp(query, controlTopic);
        form.FormClosed += (_, _) => _open = null;
        if (owner == null) form.Show();
        else form.Show(owner);
    }

    private void ShowContextualHelp(string? page, string? topic)
    {
        // This is a read-only, local contextual strip: never treat user-facing
        // control captions as HTML, a URL or an executable command.
        if (string.IsNullOrWhiteSpace(page) && string.IsNullOrWhiteSpace(topic)) return;
        page = (page ?? string.Empty).Trim();
        topic = (topic ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (topic.Length > 120) topic = topic[..120];
        if (page.Length > 80) page = page[..80];

        // Search for the more specific control first; if the manual has not yet
        // documented it, the page topic is always the safe navigation fallback.
        if (!string.IsNullOrWhiteSpace(topic) && SearchLanguage.Matches(_instructions.Text, topic))
            Search(topic);
        else if (!string.IsNullOrWhiteSpace(page)) Search(page);
        // Keep the exact focused control visible in the input even on fallback.
        _settingContext = true;
        try { _search.Text = !string.IsNullOrWhiteSpace(topic) ? topic : page; }
        finally { _settingContext = false; }
        _searchHint.Text = MainWindow.StudioText(
            $"Page: {page} · Focused control: {topic}",
            $"Σελίδα: {page} · Στοιχείο: {topic}");
        _searchHint.AutoEllipsis = true;
    }

    internal static void SavePreview(string png, IReadOnlyList<(string Version, string Date)> history, int tab)
    {
        var section = tab switch { 1 => HelpSection.History, 2 => HelpSection.License, _ => HelpSection.Instructions };
        using var form = new HelpForm(history, section) { StartPosition = FormStartPosition.Manual, Location = new Point(-5000, -5000) };
        form.Show();
        form.PerformLayout();
        Application.DoEvents();
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height));
        bitmap.Save(png, System.Drawing.Imaging.ImageFormat.Png);
    }

    private HelpForm(IReadOnlyList<(string Version, string Date)> history, HelpSection section)
    {
        _history = history;
        Name = "OpsisHelp";
        Text = $"Opsis Desktop Customization {AppVersion.Label} · {LocalizationManager.T("Help.Title")}";
        Icon = AppWindowIcon.Value;
        ShowIcon = true;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(810, 660);
        MinimumSize = new Size(735, 410);
        KeyPreview = true;
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape) Close();
            if (e.KeyCode == Keys.F3) { Search(_search.Text); e.Handled = true; }
        };

        _instructions = CreateReader("HelpInstructions");
        _instructions.Text = (LocalizationManager.T("Welcome.Body") + "\n\n" + LocalizationManager.T("About.InstructionsList") + "\n\n" +
            LocalizationManager.T("About.ShortcutsList")).Replace("\\n", "\n");
        // Each section has both translated guidance and searchable feature keys.
        // F1 from the host jumps straight to the topic for the active page.
        _instructions.AppendText("\n\n" + string.Join("\n\n", new[]
        {
            "Dashboard · Overview / Αρχική: system information and quick actions · σύστημα και γρήγορες ενέργειες.",
            "Wallpaper · Weather / Φόντο: configure weather, animated wallpaper and scene selection · καιρός, κινούμενο φόντο και τοπία.",
            "DeskZones · Windows / Ζώνες: pick a template, export or import JSON after preview · διάταξη παραθύρων, εξαγωγή και εισαγωγή JSON.",
            "DeskContainers · Files / Αρχεία: group desktop items into containers · ομαδοποίηση εικονιδίων σε containers.",
            "Widgets · Ρολόι και καιρός: add and arrange desktop widgets · προσθήκη και στοίχιση widget.",
            "Theme · Appearance / Θέμα: change palette and preview high contrast · ρύθμιση χρωμάτων και αντίθεσης.",
            "Settings · Ρυθμίσεις: language, scale, permissions and application options · γλώσσα, κλίμακα και άδειες.",
            "Performance · Απόδοση: tune effects and use optional local diagnostics · ρύθμιση εφέ και τοπικά διαγνωστικά.",
            "Automation · Αυτοματισμός: define profiles and triggers · ορισμός προφίλ και κανόνων.",
            "Profiles · Προφίλ: save and restore personal desktop profiles · αποθήκευση και ανάκτηση.",
            "DeskStrip · Dock / Γραμμή: pin commands and organise the strip · οργάνωση γραμμής εφαρμογών.",
            "DeskMusicVisualizer · Audio / Ήχος: configure audio visualisations · ρύθμιση οπτικοποίησης μουσικής.",
            "Personalization · Εξατομίκευση: lock screen and cursor effects · κλείδωμα και δείκτες.",
            "About · Information / Πληροφορίες: version, licences and updates · εκδόσεις και ενημερώσεις."
        }));
        _license = CreateReader("HelpLicense");
        _notes = CreateReader("HistoryNotes");
        _notes.AccessibleName = LocalizationManager.T("Help.TabHistory");
        _versionPicker.AccessibleName = LocalizationManager.T("VersionHistory.VersionPrefix");
        _versionPicker.Font = UiTheme.FontBody;
        _versionPicker.SelectedIndexChanged += (_, _) => ReadSelectedNotes();

        var instructionsPage = CreatePage("Help.TabInstructions", _instructions);
        _licensePage = CreatePage("Help.TabLicense", _license);
        var historyLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1
        };
        historyLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 27));
        historyLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 73));
        _versionPicker.Margin = new Padding(0, 0, 13, 0);
        _notes.Margin = new Padding(0);
        historyLayout.Controls.Add(_versionPicker, 0, 0);
        historyLayout.Controls.Add(_notes, 1, 0);
        _historyPage = CreatePage("Help.TabHistory", historyLayout);
        _historyPage.Name = "HelpHistory";

        _tabs.Font = UiTheme.FontBody;
        _tabs.Padding = new Point(18, 8);
        _tabs.DrawItem += (_, e) => DrawTab(e);
        _tabs.TabPages.AddRange([instructionsPage, _licensePage, _historyPage]);
        _tabs.SelectedIndexChanged += (_, _) => LoadActivePage();
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var findRow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(12, 7, 6, 2) };
        _search.PlaceholderText = MainWindow.StudioText("Search help (Greeklish supported)", "Αναζήτηση βοήθειας (και Greeklish)");
        _search.AccessibleName = _search.PlaceholderText;
        _search.Font = UiTheme.FontBody;
        _search.TextChanged += (_, _) => { if (!_settingContext) Search(_search.Text); };
        _searchHint.Text = MainWindow.StudioText("Enter a topic or word; F3 finds again", "Γράψε θέμα ή λέξη· F3 για νέα αναζήτηση");
        _searchHint.Font = UiTheme.FontBody;
        findRow.Controls.Add(_search);
        findRow.Controls.Add(_searchHint);
        root.Controls.Add(findRow, 0, 0);
        root.Controls.Add(_tabs, 0, 1);
        Controls.Add(root);
        _tabs.SelectedIndex = (int)section;
        LoadActivePage();
        Recolour();
        OpsisTheme.Changed += Recolour;
        Disposed += (_, _) => OpsisTheme.Changed -= Recolour;
    }

    private void Search(string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return;
        // SearchLanguage has accent-insensitive Greek, transliteration and synonyms.
        // Use whole lines rather than a raw IndexOf, so Greeklish also finds Greek.
        var readers = new[] { (_instructions, 0), (_notes, 2), (_license, 1) };
        foreach (var (reader, tabIndex) in readers)
        {
            if (tabIndex == 1 && !_licenseLoaded) _license.Text = ReadLegalText();
            if (tabIndex == 2 && _versionPicker.Items.Count == 0) continue;
            int start = 0;
            foreach (var line in reader.Lines)
            {
                if (SearchLanguage.Matches(line, query))
                {
                    _tabs.SelectedIndex = tabIndex;
                    reader.SelectionStart = start;
                    reader.SelectionLength = Math.Min(line.Length, reader.TextLength - start);
                    reader.ScrollToCaret();
                    _searchHint.Text = MainWindow.StudioText("Found", "Βρέθηκε");
                    return;
                }
                start += line.Length + 1;
            }
        }
        _searchHint.Text = MainWindow.StudioText("No matching help topic", "Δεν βρέθηκε αντίστοιχο θέμα");
    }

    private static TabPage CreatePage(string titleKey, Control content)
    {
        var page = new TabPage(LocalizationManager.T(titleKey)) { Padding = new Padding(14), UseVisualStyleBackColor = false };
        page.Controls.Add(content);
        return page;
    }

    private static RichTextBox CreateReader(string name) => new()
    {
        Name = name, Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None, Font = UiTheme.FontBody,
        DetectUrls = false, ScrollBars = RichTextBoxScrollBars.Vertical
    };

    private static string ReadLegalText()
    {
        string Read(string name)
        {
            try { return File.ReadAllText(Path.Combine(AppContext.BaseDirectory, name)).Trim(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return ""; }
        }
        var license = Read("LICENSE");
        if (license.Length == 0) license = LocalizationManager.T("About.LicenseSummary");
        var notices = Read("THIRD_PARTY_NOTICES.md");
        return notices.Length == 0 ? license : license + "\n\n" + notices;
    }

    private void LoadActivePage()
    {
        if (_tabs.SelectedTab == _licensePage && !_licenseLoaded)
        {
            _license.Text = ReadLegalText();
            _licenseLoaded = true;
        }
        if (_tabs.SelectedTab != _historyPage || _versionPicker.Items.Count != 0) return;
        if (_history.Count == 0)
        {
            _notes.Text = LocalizationManager.T("VersionHistory.NoNotes");
            return;
        }

        _versionPicker.BeginUpdate();
        try
        {
            _versionPicker.Items.AddRange(_history.Select(item => item.Version).Cast<object>().ToArray());
            _versionPicker.SelectedIndex = 0;
        }
        finally { _versionPicker.EndUpdate(); }
    }

    private void ReadSelectedNotes()
    {
        int index = _versionPicker.SelectedIndex;
        if (index < 0 || index >= _history.Count) return;
        var (version, date) = _history[index];
        var title = $"{LocalizationManager.T("VersionHistory.VersionPrefix")} {version}   •   {date}";
        if (index == 0) title += "   •   " + LocalizationManager.T("VersionHistory.Current");
        string key = "VersionNotes." + version;
        var notes = LocalizationManager.T(key);
        var content = notes == key || string.IsNullOrWhiteSpace(notes)
            ? LocalizationManager.T("VersionHistory.NoNotes")
            : string.Join("\n\n", notes.Replace("\\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(line => line.StartsWith("+ ") || line.StartsWith("~ ") ? "• " + line[2..] :
                    line.StartsWith("= ") ? line[2..] : line));
        _notes.Text = title + "\n\n" + content;
        _notes.SelectionStart = 0;
        _notes.ScrollToCaret();
    }

    private void DrawTab(DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        var theme = OpsisTheme.Current;
        using var background = new SolidBrush(e.Index == _tabs.SelectedIndex ? theme.Background : BackColor);
        e.Graphics.FillRectangle(background, e.Bounds);
        TextRenderer.DrawText(e.Graphics, _tabs.TabPages[e.Index].Text, _tabs.Font, e.Bounds, theme.Text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        if (e.Index == _tabs.SelectedIndex)
        {
            using var accent = new Pen(UiTheme.AccentCyan, 2);
            e.Graphics.DrawLine(accent, e.Bounds.Left + 5, e.Bounds.Bottom - 2, e.Bounds.Right - 5, e.Bounds.Bottom - 2);
        }
    }

    private void Recolour()
    {
        if (IsDisposed || Disposing) return;
        if (InvokeRequired) { try { BeginInvoke(Recolour); } catch (InvalidOperationException) { } return; }
        var theme = OpsisTheme.Current;
        BackColor = theme.Background;
        foreach (TabPage page in _tabs.TabPages) page.BackColor = theme.Background;
        _instructions.BackColor = _license.BackColor = _notes.BackColor = _versionPicker.BackColor = theme.Background;
        _instructions.ForeColor = _license.ForeColor = _notes.ForeColor = _versionPicker.ForeColor = theme.Text;
        _tabs.Invalidate();
    }
}
