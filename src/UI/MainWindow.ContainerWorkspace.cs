using MotionDesk.Containers;
using Opsis.Containers;

namespace MotionDesk.UI;

public sealed partial class MainWindow
{
    private string? _selectedWorkspaceContainer;

    private void ShowContainerWorkspace(ContainerHost host)
    {
        var skin = _windowSkin;
        var workspace = new TabControl { Dock = DockStyle.Fill };
        var layoutPage = new TabPage(StudioText("Containers", "Containers"));
        var settingsPage = new TabPage(StudioText("Tools and settings", "Εργαλεία και ρυθμίσεις"));
        var tools = CreatePagePanel();
        tools.Dock = DockStyle.Fill;
        FillClassicContainersPage(tools, includeContainerPicker: false);
        settingsPage.Controls.Add(tools);
        workspace.TabPages.AddRange([layoutPage, settingsPage]);

        bool index = skin == WindowSkin.Index;
        bool split = skin == WindowSkin.Split;
        bool inspector = skin == WindowSkin.Inspector;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, ColumnCount = 2, RowCount = 2, Padding = new Padding(7, 6, 7, 7) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, index ? 32 : split ? 64 : inspector ? 59 : 63));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, index ? 68 : split ? 36 : inspector ? 41 : 37));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layoutPage.Controls.Add(layout);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = new Padding(0, 0, 0, 8) };
        layout.Controls.Add(actions, 0, 0);
        layout.SetColumnSpan(actions, 2);
        layout.ClientSizeChanged += (_, _) => actions.MaximumSize = new Size(Math.Max(100, layout.ClientSize.Width - layout.Padding.Horizontal), 0);
        var list = new ListBox
        {
            Dock = DockStyle.Fill, IntegralHeight = false, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 50,
            BorderStyle = BorderStyle.FixedSingle, Font = UiTheme.FontBody, BackColor = UiTheme.Surface, ForeColor = UiTheme.TextPrimary,
            AccessibleName = StudioText("Containers", "Λίστα containers"), Margin = new Padding(4)
        };
        var picker = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 210, AccessibleName = list.AccessibleName, Margin = new Padding(4, 7, 6, 0) };
        var editSpace = new Panel { Dock = DockStyle.Fill, Margin = new Padding(4), BackColor = UiTheme.PageBackground };
        ContainerWorkspacePreview? preview = index ? null : new ContainerWorkspacePreview(host, split, () => CurrentSkinColors) { Dock = DockStyle.Fill, Margin = new Padding(4) };
        IReadOnlyList<Container> models = [];
        var choices = new List<(string Id, string Label, int Count, bool Hidden)>();
        string? displayedId = null;
        Label? editorTitle = null;
        bool binding = false;

        string ContainerName(Container model) => string.IsNullOrWhiteSpace(model.Title) ? StudioText("Container", "Container") : model.Title;
        Container? Current() => models.FirstOrDefault(c => c.Id == _selectedWorkspaceContainer);
        // Optional monitor selector for the two editable desktop canvases.
        var monitorPicker = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130, Margin = new Padding(4, 7, 0, 0),
            AccessibleName = StudioText("Preview monitor", "Οθόνη προεπισκόπησης") };
        void SetSelection(string? id, bool fromCanvas = false)
        {
            if (id != null && !choices.Any(choice => choice.Id == id)) return;
            _selectedWorkspaceContainer = id;
            int row = choices.FindIndex(choice => choice.Id == id);
            binding = true;
            try
            {
                if (list.SelectedIndex != row) list.SelectedIndex = row;
                if (picker.SelectedIndex != row) picker.SelectedIndex = row;
            }
            finally { binding = false; }
            if (preview != null && !fromCanvas && Current() is { } selected)
            {
                // Selection from the list stays on the displayed monitor unless the container lives on another one.
                var screens = Screen.AllScreens;
                var monitors = screens.Select(screen => new MonitorInfo(screen.DeviceName, screen.Bounds, screen.Primary)).ToList();
                var point = LegacyImporter.Denormalize(selected.Geometry, monitors);
                var screen = Screen.FromPoint(new Point(point.X, point.Y));
                if (!split && monitorPicker.SelectedItem is string monitor && monitor != screen.DeviceName)
                    monitorPicker.SelectedItem = screen.DeviceName;
            }
            preview?.UpdateModels(models, id);
            if (displayedId == id)
            {
                if (editorTitle != null && Current() is { } current) editorTitle.Text = ContainerName(current);
                return;
            }
            displayedId = id;
            RebuildEditor(Current());
        }

        void RebuildEditor(Container? model)
        {
            if (!index && !split && !inspector) return;
            DisposeChildren(editSpace);
            editorTitle = null;
            if (model == null)
            {
                editSpace.Controls.Add(new Label { Text = StudioText("Select or create a container", "Επίλεξε ή δημιούργησε container"),
                    Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = UiTheme.TextSecondary, Font = UiTheme.FontBody });
                return;
            }
            if (inspector)
            {
                var inspectorTabs = new TabControl { Dock = DockStyle.Fill };
                var general = new TabPage(StudioText("General", "Γενικά"));
                var appearance = new TabPage(StudioText("Appearance", "Εμφάνιση"));
                var rules = new TabPage(StudioText("Rules", "Κανόνες"));
                var generalBody = CreatePagePanel();
                var appearanceBody = CreatePagePanel();
                var rulesBody = CreatePagePanel();
                generalBody.Dock = appearanceBody.Dock = rulesBody.Dock = DockStyle.Fill;
                using var editor = new ContainerSettingsPanel(host, model, ThemeManager.Current.IsDark, compact: true);
                int section = -1;
                foreach (var control in editor.Controls.Cast<Control>().ToArray())
                {
                    if (ReferenceEquals(control.Tag, ContainerSettingsPanel.SectionMarker)) section++;
                    (section is 1 or 2 ? appearanceBody : generalBody).Controls.Add(control);
                }
                rulesBody.Controls.Add(StudioButton(StudioText("Manage file rules", "Διαχείριση κανόνων αρχείων"),
                    () => ManageFileRules(host, host.OrganizationStatus ?? new([], [], null, true, [], RulesEnabled: false)), 237));
                if (LayoutImport.HasSmart(model))
                    rulesBody.Controls.Add(StudioButton(StudioText("Edit smart filters", "Επεξεργασία έξυπνων φίλτρων"), () => EditSmartFolder(host, model), 237));
                general.Controls.Add(generalBody);
                appearance.Controls.Add(appearanceBody);
                rules.Controls.Add(rulesBody);
                inspectorTabs.TabPages.AddRange([general, appearance, rules]);
                editSpace.Controls.Add(inspectorTabs);
            }
            else
            {
                var scroll = CreatePagePanel();
                scroll.Dock = DockStyle.Fill;
                editorTitle = new Label
                {
                    Text = ContainerName(model), Font = UiTheme.FontHeading, ForeColor = UiTheme.TextPrimary, AutoSize = true,
                    MaximumSize = new Size(Math.Max(200, editSpace.ClientSize.Width - 30), 0), UseMnemonic = false, Margin = new Padding(0, 4, 0, 12)
                };
                scroll.Controls.Add(editorTitle);
                scroll.Controls.Add(new ContainerSettingsPanel(host, model, ThemeManager.Current.IsDark));
                if (LayoutImport.HasSmart(model))
                    scroll.Controls.Add(StudioButton(StudioText("Edit smart filters", "Επεξεργασία έξυπνων φίλτρων"), () => EditSmartFolder(host, model), 235));
                editSpace.Controls.Add(scroll);
            }
            StyleStudioInputs(editSpace);
            ApplySkinColors(editSpace);
        }

        void RefreshModels(bool chooseFirst)
        {
            models = host.Containers();
            var fresh = models.Select(c => (c.Id, ContainerName(c), c.Items.Count + c.Tabs.Sum(t => t.Items.Count), c.Hidden)).ToList();
            if (!choices.SequenceEqual(fresh))
            {
                choices = fresh;
                binding = true;
                list.BeginUpdate();
                picker.BeginUpdate();
                try
                {
                    list.Items.Clear();
                    picker.Items.Clear();
                    foreach (var choice in choices) { list.Items.Add(choice.Label); picker.Items.Add(choice.Label); }
                }
                finally { list.EndUpdate(); picker.EndUpdate(); binding = false; }
            }
            string? id = choices.Any(row => row.Id == _selectedWorkspaceContainer) ? _selectedWorkspaceContainer : null;
            if (chooseFirst && id == null && choices.Count > 0) id = choices[0].Id;
            SetSelection(id, fromCanvas: true);
            list.Invalidate();
        }

        void SelectRow(int row) { if (!binding && row >= 0 && row < choices.Count) SetSelection(choices[row].Id); }
        list.SelectedIndexChanged += (_, _) => SelectRow(list.SelectedIndex);
        picker.SelectedIndexChanged += (_, _) => SelectRow(picker.SelectedIndex);
        if (preview != null) preview.ContainerSelected += id => SetSelection(id, fromCanvas: true);
        list.DrawItem += (_, e) =>
        {
            if (e.Index < 0 || e.Index >= choices.Count) return;
            var entry = choices[e.Index];
            bool selected = e.State.HasFlag(DrawItemState.Selected);
            using var brush = new SolidBrush(selected ? BlendChrome(CurrentSkinColors.Surface, SkinAccent, .20f) : CurrentSkinColors.Surface);
            e.Graphics.FillRectangle(brush, e.Bounds);
            IconRenderer.Draw(e.Graphics, "Container", new Rectangle(e.Bounds.Left + 10, e.Bounds.Top + 13, 20, 20), selected ? SkinAccent : CurrentSkinColors.Secondary);
            var titleBounds = new Rectangle(e.Bounds.X + 38, e.Bounds.Y + 4, Math.Max(1, e.Bounds.Width - 46), 22);
            var detailBounds = new Rectangle(titleBounds.X, e.Bounds.Y + 24, titleBounds.Width, 20);
            const TextFormatFlags flags = TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
            TextRenderer.DrawText(e.Graphics, entry.Label, UiTheme.FontBody, titleBounds, CurrentSkinColors.Text, flags);
            int count = models[e.Index].Kind == ContainerKind.Portal
                ? host.Windows.FirstOrDefault(window => window.Model.Id == entry.Id)?.Items.Count ?? entry.Count : entry.Count;
            string detail = StudioText($"{count} items", $"{count} στοιχεία");
            if (entry.Hidden) detail += StudioText(" · Hidden", " · Κρυμμένο");
            TextRenderer.DrawText(e.Graphics, detail, UiTheme.FontBody, detailBounds, CurrentSkinColors.Secondary, flags);
            e.DrawFocusRectangle();
        };

        actions.Controls.Add(StudioButton(StudioText("New container", "Νέο container"), () =>
        {
            _selectedWorkspaceContainer = host.NewContainerAtCursor();
            RefreshModels(chooseFirst: true);
            SetSelection(_selectedWorkspaceContainer);
        }, 145, emphasized: true));
        if (split || inspector) actions.Controls.Add(picker);
        actions.Controls.Add(StudioButton(StudioText("Open settings", "Άνοιγμα ρυθμίσεων"), () =>
        {
            if (Current() is { } selected) ContainerSettingsForm.Open(host, selected.Id);
        }, 158));
        actions.Controls.Add(StudioButton(StudioText("Show / hide", "Εμφάνιση / απόκρυψη"), () =>
        {
            if (Current() is { } selected) { host.SetHidden(selected.Id, !selected.Hidden); RefreshModels(chooseFirst: true); }
        }, 154));
        actions.Controls.Add(StudioButton(StudioText("Delete", "Διαγραφή"), () => { if (Current() is { } selected) host.ConfirmDelete(this, selected.Id); }, 94));

        if (preview != null && !split)
        {
            monitorPicker.Items.AddRange(Screen.AllScreens.Select(screen => screen.DeviceName).Cast<object>().ToArray());
            monitorPicker.SelectedItem = preview.Monitor.DeviceName;
            monitorPicker.SelectedIndexChanged += (_, _) =>
            {
                if (monitorPicker.SelectedItem is string name && Screen.AllScreens.FirstOrDefault(s => s.DeviceName == name) is { } display)
                {
                    preview.Monitor = display;
                    preview.Invalidate();
                }
            };
            if (monitorPicker.Items.Count > 1) actions.Controls.Add(monitorPicker);
        }
        if (index)
        {
            layout.Controls.Add(list, 0, 1);
            layout.Controls.Add(editSpace, 1, 1);
        }
        else if (split)
        {
            layout.Controls.Add(editSpace, 0, 1);
            layout.Controls.Add(preview!, 1, 1);
        }
        else
        {
            layout.Controls.Add(preview!, 0, 1);
            layout.Controls.Add(inspector ? editSpace : list, 1, 1);
        }
        StyleStudioInputs(workspace);
        SetPage("DeskZones.SectionContainers", workspace);

        // The service also receives updates from the desktop and the named pipe.
        int dirty = 0, contentDirty = 0;
        void OnMutation() => Interlocked.Exchange(ref dirty, 1);
        void OnPreviewContentChanged() => Interlocked.Exchange(ref contentDirty, 1);
        var previewTimer = new System.Windows.Forms.Timer { Interval = 420 };
        previewTimer.Tick += (_, _) =>
        {
            if (Interlocked.Exchange(ref dirty, 0) == 1) RefreshModels(chooseFirst: true);
            if (Interlocked.Exchange(ref contentDirty, 0) == 1)
            {
                preview?.Invalidate();
                list.Invalidate();
            }
        };
        host.Service.Mutated += OnMutation;
        host.WorkspacePreviewChanged += OnPreviewContentChanged;
        workspace.Disposed += (_, _) =>
        {
            host.Service.Mutated -= OnMutation;
            host.WorkspacePreviewChanged -= OnPreviewContentChanged;
            previewTimer.Dispose();
            if (list.Parent == null) list.Dispose();
            if (picker.Parent == null) picker.Dispose();
            if (monitorPicker.Parent == null) monitorPicker.Dispose();
            if (editSpace.Parent == null) editSpace.Dispose();
        };
        previewTimer.Start();
        RefreshModels(chooseFirst: true);
        if (Current() == null) RebuildEditor(null);
        else SetSelection(_selectedWorkspaceContainer);
    }
}
