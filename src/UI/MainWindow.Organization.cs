using MotionDesk.Containers;
using Opsis.Containers;

namespace MotionDesk.UI;

public sealed partial class MainWindow
{
    private void BuildOrganizationCard(Control panel, ContainerHost host)
    {
        string title = StudioText("Layouts and file organization", "Διατάξεις και οργάνωση αρχείων");
        var card = AddCardSection(panel, title);
        var primary = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = Padding.Empty };
        primary.Controls.Add(StudioButton(StudioText("New smart container", "Νέο έξυπνο container"),
            () => EditSmartFolder(host, null), 210, emphasized: true));
        primary.Controls.Add(StudioButton(StudioText("Manage file rules", "Διαχείριση κανόνων αρχείων"), () =>
        {
            ManageFileRules(host, host.OrganizationStatus ?? new([], [], null, true, [], RulesEnabled: false));
            ShowDeskContainers();
        }, 230));
        card.Controls.Add(primary);
        card.Controls.Add(StudioButton(StudioText("Import layouts from JSON", "Εισαγωγή διατάξεων JSON"), () =>
        {
            using var open = new OpenFileDialog
            {
                Filter = "JSON (*.json;*.bak)|*.json;*.bak", CheckFileExists = true,
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };
            if (open.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                var package = LayoutImport.Read(open.FileName, ContainerHost.OrganizationScreens());
                if (ChooseImportedLayout(package) is not int selection) return;
                host.ImportLayouts(package, selection);
                ShowDeskContainers();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException or ArgumentException)
            {
                MessageBox.Show(this, ex.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }, 225, emphasized: true));

        card.Controls.Add(StudioButton(StudioText("Export current layout", "Εξαγωγή τρέχουσας διάταξης"), () =>
        {
            using var save = new SaveFileDialog { Filter = "JSON (*.json)|*.json", DefaultExt = "json", FileName = "Opsis-Layout.json" };
            if (save.ShowDialog(this) != DialogResult.OK) return;
            try { File.WriteAllText(save.FileName, host.ExportCurrentLayout(Path.GetFileNameWithoutExtension(save.FileName))); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                MessageBox.Show(this, ex.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }, 235));
        card.Controls.Add(StudioButton(StudioText("Refresh smart results and apply rules", "Ανανέωση αποτελεσμάτων και κανόνων"),
            () => host.RefreshOrganization(forceRules: true), 295));
        if (host.OrganizationStatus is not { } saved) return;
        AddText(card, StudioText(
            $"{saved.Layouts.Length} layouts · {saved.Rules.Length} compatible rules · {saved.Issues.Length} import notes",
            $"{saved.Layouts.Length} διατάξεις · {saved.Rules.Length} συμβατοί κανόνες · {saved.Issues.Length} σημειώσεις εισαγωγής"));
        var actions = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = Padding.Empty };
        if (saved.Layouts.Length > 0) actions.Controls.Add(StudioButton(StudioText("Change imported layout", "Αλλαγή εισαγόμενης διάταξης"), () =>
        {
            if (host.OrganizationStatus is not { } current || ChooseImportedLayout(current) is not int selection || selection == current.Selected) return;
            host.ChangeImportedLayout(selection);
            ShowDeskContainers();
        }, 240));
        card.Controls.Add(actions);
        var enabled = new CheckBox
        {
            Text = StudioText("Enable file rules", "Ενεργοί κανόνες αρχείων"), Checked = saved.RulesEnabled,
            AutoSize = true, Margin = new Padding(3, 6, 3, 6)
        };
        enabled.CheckedChanged += (_, _) => host.EnableFileRules(enabled.Checked);
        card.Controls.Add(enabled);
        if (saved.Issues.Length > 0)
            card.Controls.Add(StudioButton(StudioText("Import notes", "Σημειώσεις εισαγωγής"), () =>
                ShowTextDialog(title, StudioText("Compatibility notes", "Σημειώσεις συμβατότητας"), string.Join(Environment.NewLine, saved.Issues)),
                190));
    }

    private void ManageFileRules(ContainerHost host, DesktopOrganization package)
    {
        using var dialog = new MotionDesk.UI.AppDialog
        {
            Text = StudioText("Desktop file rules", "Κανόνες αρχείων επιφάνειας"),
            StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(680, 415),
            MinimumSize = new Size(600, 390), MinimizeBox = false, MaximizeBox = false,
            BackColor = UiTheme.PageBackground, ForeColor = UiTheme.TextPrimary, ShowInTaskbar = false
        };
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(14) };
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var auto = new CheckBox
        {
            Text = StudioText("Keep desktop file routing up to date", "Αυτόματη ενημέρωση ταξινόμησης αρχείων"),
            Checked = package.KeepUpdated, AutoSize = true
        };
        var keep = new CheckBox
        {
            Text = StudioText("Keep the previous destination when no rule matches", "Διατήρηση προηγούμενου προορισμού όταν δεν ταιριάζει κανόνας"),
            Checked = package.KeepUnmatched, AutoSize = true
        };
        var destinations = host.Containers().Where(c => c.Kind == ContainerKind.Data && !LayoutImport.HasSmart(c))
            .SelectMany(c => new[] { new RuleTarget(c.Id, c.Title) }.Concat(c.Tabs
                .Where(t => LayoutImport.TabTarget(t) != null)
                .Select(t => new RuleTarget(LayoutImport.TabTarget(t)!, c.Title + " / " + t.Name))))
            .ToArray();
        if (host.Containers().Any(c => c.Extra?.ContainsKey(LayoutImport.InboxKey) == true) ||
            package.Rules.Any(rule => rule.Target == "inbox") || package.DefaultTarget == "inbox")
            destinations = destinations.Append(new RuleTarget("inbox", StudioText("Inbox", "Εισερχόμενα"))).ToArray();
        var defaultTarget = new ComboBox { Width = 370, DropDownStyle = ComboBoxStyle.DropDownList };
        if (package.DefaultTarget is { } prior && !destinations.Any(d => d.Id == prior))
            destinations = destinations.Append(new RuleTarget(prior, StudioText("Current target (other layout)", "Τρέχων προορισμός (άλλη διάταξη)"))).ToArray();
        var choices = new[] { new RuleTarget("", StudioText("No default destination", "Χωρίς προεπιλεγμένο προορισμό")) }
            .Concat(destinations).ToArray();
        defaultTarget.Items.AddRange(choices);
        defaultTarget.SelectedIndex = Math.Max(0, Array.FindIndex(choices, d => d.Id == package.DefaultTarget));
        var options = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        options.Controls.Add(auto);
        options.Controls.Add(keep);
        options.Controls.Add(defaultTarget);
        var list = new CheckedListBox { Dock = DockStyle.Fill, IntegralHeight = false, CheckOnClick = true,
            BackColor = UiTheme.Surface, ForeColor = UiTheme.TextPrimary };
        var rules = package.Rules.ToList();
        void Populate(int at)
        {
            list.Items.Clear();
            foreach (var rule in rules) list.Items.Add(rule.Name, rule.Enabled);
            list.SelectedIndex = Math.Min(at, list.Items.Count - 1);
        }
        void CaptureChecks()
        {
            for (int i = 0; i < rules.Count; i++) rules[i] = rules[i] with { Enabled = list.GetItemChecked(i) };
        }
        Populate(0);
        var edits = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        var validDestinations = destinations.Where(d => d.Id != "inbox").ToArray();
        var add = StudioButton(StudioText("New rule", "Νέος κανόνας"), () =>
        {
            CaptureChecks();
            if (EditFileRule(null, validDestinations, dialog) is not { } rule) return;
            rules.Add(rule);
            Populate(rules.Count - 1);
        }, 135);
        add.Enabled = validDestinations.Length > 0;
        edits.Controls.Add(add);
        edits.Controls.Add(StudioButton(StudioText("Edit", "Επεξεργασία"), () =>
        {
            int at = list.SelectedIndex;
            if (at < 0) return;
            CaptureChecks();
            var targets = destinations.Any(d => d.Id == rules[at].Target) ? destinations
                : destinations.Append(new RuleTarget(rules[at].Target, StudioText("Current target (other layout)", "Τρέχων προορισμός (άλλη διάταξη)"))).ToArray();
            if (EditFileRule(rules[at], targets, dialog) is not { } rule) return;
            rules[at] = rule;
            Populate(at);
        }, 120));
        edits.Controls.Add(StudioButton(StudioText("Delete", "Διαγραφή"), () =>
        {
            int at = list.SelectedIndex;
            if (at < 0) return;
            CaptureChecks();
            rules.RemoveAt(at);
            Populate(at);
        }, 100));
        foreach (int offset in new[] { -1, 1 })
            edits.Controls.Add(StudioButton(offset < 0 ? "↑" : "↓", () =>
            {
                int at = list.SelectedIndex, to = at + offset;
                if (at < 0 || to < 0 || to >= rules.Count) return;
                CaptureChecks();
                (rules[at], rules[to]) = (rules[to], rules[at]);
                Populate(to);
            }, 48));
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        var preview = StudioButton(StudioText("Preview matches", "Προεπισκόπηση αντιστοιχίσεων"), () => { }, 205);
        using var previewCancellation = new CancellationTokenSource();
        var previewToken = previewCancellation.Token;
        dialog.FormClosed += (_, _) => previewCancellation.Cancel();
        preview.Click += async (_, _) =>
        {
            CaptureChecks();
            var target = (defaultTarget.SelectedItem as RuleTarget)?.Id;
            var draft = package with { Rules = rules.ToArray(), DefaultTarget = string.IsNullOrEmpty(target) ? null : target,
                RulesEnabled = true, KeepUnmatched = keep.Checked };
            host.Flush();
            var models = host.Containers();
            preview.Enabled = false;
            try
            {
                var matches = await Task.Run(() => DesktopFiles.Route(draft, models,
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
                    previewToken), previewToken);
                if (dialog.IsDisposed || previewToken.IsCancellationRequested) return;
                var rows = matches.SelectMany(target => target.Value.Select(item =>
                    item.DisplayName + "  →  " + (destinations.FirstOrDefault(d => d.Id == target.Key)?.Name ?? target.Key))).Take(500);
                using var report = new MotionDesk.UI.AppDialog
                {
                    Text = StudioText("Preview matches", "Προεπισκόπηση αντιστοιχίσεων"),
                    StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(580, 410), ShowInTaskbar = false
                };
                report.Controls.Add(new TextBox { ReadOnly = true, Multiline = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Both,
                    Text = matches.Count == 0 ? StudioText("No files matched.", "Κανένα αρχείο δεν ταίριαξε.") : string.Join(Environment.NewLine, rows) });
                StyleStudioDialog(report);
                report.ShowDialog(dialog);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                if (!dialog.IsDisposed) MessageBox.Show(dialog, ex.Message, dialog.Text);
            }
            finally { if (!preview.IsDisposed) preview.Enabled = true; }
        };
        var save = StudioButton(StudioText("Save", "Αποθήκευση"), () =>
        {
            CaptureChecks();
            string? selected = (defaultTarget.SelectedItem as RuleTarget)?.Id;
            host.SaveFileRules(rules.ToArray(), auto.Checked, string.IsNullOrEmpty(selected) ? null : selected, keep.Checked);
            dialog.DialogResult = DialogResult.OK;
        }, emphasized: true);
        var cancel = StudioButton(StudioText("Cancel", "Ακύρωση"), () => dialog.DialogResult = DialogResult.Cancel);
        actions.Controls.Add(save);
        actions.Controls.Add(cancel);
        actions.Controls.Add(preview);
        grid.Controls.Add(options, 0, 0);
        grid.Controls.Add(list, 0, 1);
        grid.Controls.Add(edits, 0, 2);
        grid.Controls.Add(actions, 0, 3);
        dialog.Controls.Add(grid);
        dialog.AcceptButton = save;
        dialog.CancelButton = cancel;
        StyleStudioDialog(dialog);
        dialog.ShowDialog(this);
    }

    private int? ChooseImportedLayout(DesktopOrganization package)
    {
        using var dialog = new MotionDesk.UI.AppDialog
        {
            Text = StudioText("Imported layouts", "Εισαγόμενες διατάξεις"),
            StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(620, 425),
            MinimumSize = new Size(480, 320), MinimizeBox = false, MaximizeBox = false,
            BackColor = UiTheme.PageBackground, ForeColor = UiTheme.TextPrimary, ShowInTaskbar = false
        };
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, Padding = new Padding(14) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 65));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 35));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(new Label
        {
            AutoSize = true, Font = UiTheme.FontBody,
            Text = StudioText("Choose a layout to use. All listed layouts stay available in DeskContainers.",
                "Διάλεξε την ενεργή διάταξη. Όλες οι διατάξεις της λίστας παραμένουν διαθέσιμες στα DeskContainers.")
        }, 0, 0);
        var list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, BackColor = UiTheme.Surface, ForeColor = UiTheme.TextPrimary };
        list.Items.AddRange(package.Layouts.Select(layout =>
            $"{layout.Name}  ·  {layout.Containers.Count} {StudioText("containers", "containers")}").ToArray());
        list.SelectedIndex = Math.Clamp(package.Selected, 0, list.Items.Count - 1);
        root.Controls.Add(list, 0, 1);
        var notes = new TextBox { Dock = DockStyle.Fill, ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical,
            BackColor = UiTheme.Surface, ForeColor = UiTheme.TextPrimary };
        notes.Text = StudioText($"{package.Rules.Length} compatible sorting rules", $"{package.Rules.Length} συμβατοί κανόνες ταξινόμησης")
            + (package.Issues.Length == 0 ? "" : Environment.NewLine + string.Join(Environment.NewLine, package.Issues));
        root.Controls.Add(notes, 0, 2);
        var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        var apply = StudioButton(StudioText("Apply layout", "Εφαρμογή διάταξης"), () => dialog.DialogResult = DialogResult.OK, emphasized: true);
        var cancel = StudioButton(StudioText("Cancel", "Ακύρωση"), () => dialog.DialogResult = DialogResult.Cancel);
        bar.Controls.Add(apply);
        bar.Controls.Add(cancel);
        root.Controls.Add(bar, 0, 3);
        dialog.Controls.Add(root);
        dialog.AcceptButton = apply;
        dialog.CancelButton = cancel;
        StyleStudioDialog(dialog);
        return dialog.ShowDialog(this) == DialogResult.OK && list.SelectedIndex >= 0 ? list.SelectedIndex : null;
    }
}
