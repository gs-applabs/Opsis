using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using MotionDesk.Services;
using MotionDesk.Widgets;
using UiTheme = MotionDesk.UI.UiTheme;

namespace MotionDesk.UI
{
    // Settings of the native DeskContainers core (2.6.0): shown on the DeskContainers page; every change goes through the in-process host (same service as the pipe), live.
    public sealed partial class MainWindow
    {
        private static string Lx(string el, string en) => LocalizationManager.CurrentLanguage == "el" ? el : en;

        private static FlatComboBox FxCombo(Control card, string label, string[] items, string current, Action<string> onChange)
        {
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 6) };
            row.Controls.Add(new Label { Text = label + ":", AutoSize = true, ForeColor = UiTheme.TextSecondary, Font = UiTheme.FontBody, Padding = new Padding(0, 7, 10, 0), MinimumSize = new Size(170, 0) });
            var combo = new FlatComboBox { Width = 200 };
            combo.SetItems(items, items.Contains(current) ? current : items[0]);
            combo.SelectedIndexChanged += (_, _) => { if (combo.SelectedItem is string s) onChange(s); };
            row.Controls.Add(combo);
            card.Controls.Add(row);
            return combo;
        }

        // "Transparent" widgets: same colour/tint settings as the DeskContainers, and a text colour that follows the desktop behind them.
        private static Control BuildWidgetsTransparencyRows()
        {
            var col = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0, 0, 0, 10) };
            col.Controls.Add(SettingsView.Checkbox(Lx("Πλήρης διαφάνεια: αυτόματη αντίθεση κειμένου με βάση το wallpaper πίσω από τα widgets (και όταν κινείται)", "Fully transparent: automatic text contrast from the wallpaper behind the widgets (also while it moves)"),
                AppSettings.Load().WidgetsAutoContrast, v => { var a = AppSettings.Load(); a.WidgetsAutoContrast = v; a.Save(); ThemeManager.RaiseRepainted(); }));
            FxCombo(col, Lx("Χρώμα επιφάνειας widgets", "Widget surface colour"), WidgetTheme.ColorNames, AppSettings.Load().WidgetsTintColor,
                v =>
                {
                    var a = AppSettings.Load(); a.WidgetsTintColor = v;
                    if (a.WidgetsTintPercent == 0) a.WidgetsTintPercent = 35;
                    a.Save(); ThemeManager.NotifyChanged();
                });
            col.Controls.Add(SettingsView.NumericRow(Lx("Απόχρωση (0 = πλήρως διάφανο)", "Tint (0 = fully transparent)"), Math.Clamp(AppSettings.Load().WidgetsTintPercent, 0, 100), 0, 100,
                v => { var a = AppSettings.Load(); a.WidgetsTintPercent = v; a.Save(); ThemeManager.NotifyChanged(); }, 0, "%"));
            return col;
        }
        // DeskContainers page: on/off, the containers (title bar colour / opacity / height / font, lock, delete) and the layout snapshots.
        private void BuildContainersSettings(Control panel, bool includeContainerPicker)
        {
            var engine = AddCardSection(panel, Lx("DeskContainers (νέος πυρήνας)", "DeskContainers (new core)"));
            engine.Controls.Add(SettingsView.Checkbox(Lx("Ενεργοποίηση DeskContainers", "Enable DeskContainers"), AppSettings.Load().UseNativeContainers,
                v => { var a = AppSettings.Load(); a.UseNativeContainers = v; a.Save(); }, Lx("Τα containers ομαδοποιούν συντομεύσεις/αρχεία σε γυάλινα παράθυρα στην επιφάνεια εργασίας.", "Containers group shortcuts and files in glass windows on the desktop."), restart: true));
            var host = MotionDesk.Containers.ContainerHost.Current;
            if (host == null) { AddText(engine, Lx("Τα containers δεν τρέχουν αυτή τη στιγμή (απενεργοποιημένα ή ασφαλής λειτουργία).", "Containers are not running right now (disabled or safe mode).")); return; }
            BuildOrganizationCard(panel, host);
            engine.Controls.Add(SettingsView.Checkbox(Lx("Απόκρυψη του αρχικού εικονιδίου της επιφάνειας όταν το ρίχνεις σε container", "Hide the desktop icon when you drop it into a container"), AppSettings.Load().HideOriginalsOnDrop, v => { var a = AppSettings.Load(); a.HideOriginalsOnDrop = v; a.Save(); if (!v) MotionDesk.Widgets.DesktopIconVisibilityEngine.RestoreOriginals(); }, Lx("Τα εικονίδια επιστρέφουν πάντα όταν κλείσει το Opsis, στην επόμενη εκκίνηση και με «Επαναφορά εικονιδίων».", "Icons always come back when Opsis exits, at the next start and with Restore desktop icons.")));
            engine.Controls.Add(SettingsView.Checkbox(LocalizationManager.T("DCM.DragCopies"), AppSettings.Load().ContainerDragCopies, v => { var a = AppSettings.Load(); a.ContainerDragCopies = v; a.Save(); }));
            engine.Controls.Add(SettingsView.Checkbox(LocalizationManager.T("DCM.OfferSelection"), AppSettings.Load().OfferContainerFromSelection, v => { var a = AppSettings.Load(); a.OfferContainerFromSelection = v; a.Save(); }));
            var backupBtn = new HoverButton { Text = LocalizationManager.T("DCM.BackupNow"), Width = 260, Height = 32, FlatStyle = FlatStyle.Flat, ForeColor = UiTheme.TextPrimary, BackColor = UiTheme.SurfaceHover, Cursor = Cursors.Hand, Margin = new Padding(0, 4, 0, 4) };
            backupBtn.FlatAppearance.BorderSize = 0; backupBtn.Click += (_, _) => { try { host.Backup(true); } catch (Exception) { } };
            engine.Controls.Add(backupBtn);
            AddText(engine, Lx("Συντομεύσεις: Ctrl+Alt+Shift+N νέο container, Ctrl+Alt+Shift+H απόκρυψη/εμφάνιση όλων, Ctrl+Alt+Shift+F εστίαση στο επόμενο container.", "Shortcuts: Ctrl+Alt+Shift+N new container, Ctrl+Alt+Shift+H hide / show all, Ctrl+Alt+Shift+F focus the next container."));

            AddIconButtonGrid(panel,
                ("Container", LocalizationManager.T("DeskZones.NewContainerBtn"), (_, _) => { host.NewContainerAtCursor(); ShowDeskContainers(); }),
                ("Close", LocalizationManager.T("DeskZones.CloseAllContainers"), (_, _) => host.ToggleAll()));

            // ---- one container at a time
            if (includeContainerPicker)
            {
                var list = host.Containers();
                var card = AddCardSection(panel, Lx("Container", "Container"));
                if (list.Count == 0) { AddText(card, LocalizationManager.T("DCX.NoContainers")); }
                else
                {
                    var names = list.Select((c, n) => (string.IsNullOrWhiteSpace(c.Title) ? "Container" : c.Title) + "  (" + (n + 1) + ")").ToArray();
                    int sel = 0;
                    bool updatingNames = false;
                    var holder = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0) };
                    void Build()
                    {
                        foreach (Control old in holder.Controls.Cast<Control>().ToList()) { holder.Controls.Remove(old); old.Dispose(); }
                        var c = host.Containers().ElementAtOrDefault(sel); if (c == null) return;
                        string id = c.Id;
                        // the SAME panel as in the container's own right-click settings: palette, fonts, sizes, tint, glossy, click mode, hover roll-up, exclusion from quick-hide
                        holder.Controls.Add(new MotionDesk.Containers.ContainerSettingsPanel(host, c, dark: ThemeManager.Current.IsDark));
                        if (MotionDesk.Containers.LayoutImport.HasSmart(c))
                            holder.Controls.Add(StudioButton(StudioText("Edit smart filters", "Επεξεργασία έξυπνων φίλτρων"),
                                () => EditSmartFolder(host, c), 235));
                        holder.Controls.Add(SettingsView.Checkbox(Lx("Κρυμμένο", "Hidden"), c.Hidden, v => host.SetHidden(id, v)));
                        var del = new HoverButton { Text = LocalizationManager.T("DCX.DeleteContainer"), Width = 230, Height = 32, FlatStyle = FlatStyle.Flat, ForeColor = UiTheme.TextPrimary, BackColor = UiTheme.SurfaceHover, Cursor = Cursors.Hand, Margin = new Padding(0, 6, 0, 0) };
                        del.FlatAppearance.BorderSize = 0;
                        del.Click += (_, _) => host.ConfirmDelete(this, id);
                        holder.Controls.Add(del);
                    }
                    var pick = new FlatComboBox { Width = 300, Margin = new Padding(0, 0, 0, 8) };
                    pick.SetItems(names, names[0]);
                    pick.SelectedIndexChanged += (_, _) => { if (!updatingNames) { sel = Math.Max(0, pick.SelectedIndex); Build(); } };
                    void OnContainerRenamed(string id, string title)
                    {
                        int index = list.ToList().FindIndex(container => container.Id == id);
                        if (index < 0) return;
                        names[index] = title + "  (" + (index + 1) + ")";
                        updatingNames = true;
                        try { pick.SetItems(names, names[sel]); }
                        finally { updatingNames = false; }
                    }
                    void OnContainerDeleted(string id)
                    {
                        if (list.Any(container => container.Id == id) && _currentPageKey == "DeskContainers")
                            ShowDeskContainers();
                    }
                    host.ContainerRenamed += OnContainerRenamed;
                    host.ContainerDeleted += OnContainerDeleted;
                    holder.Disposed += (_, _) =>
                    {
                        host.ContainerRenamed -= OnContainerRenamed;
                        host.ContainerDeleted -= OnContainerDeleted;
                    };
                    card.Controls.Add(pick); card.Controls.Add(holder); Build();
                }
            }

            // ---- layout snapshots (named layouts; the rules of the installation are kept when one is loaded)
            var snapCard = AddCardSection(panel, Lx("Στιγμιότυπα διάταξης", "Layout snapshots"));
            var snaps = host.SnapshotNames().ToArray();
            var snapPick = new FlatComboBox { Width = 300, Margin = new Padding(0, 0, 0, 8) };
            snapPick.SetItems(snaps.Length > 0 ? snaps : new[] { Lx("(κανένα)", "(none)") }, snaps.Length > 0 ? snaps[0] : Lx("(κανένα)", "(none)"));
            snapCard.Controls.Add(snapPick);
            var snapRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            HoverButton SnapBtn(string text, Action act) { var b = new HoverButton { Text = text, Width = 190, Height = 32, FlatStyle = FlatStyle.Flat, ForeColor = UiTheme.TextPrimary, BackColor = UiTheme.SurfaceHover, Cursor = Cursors.Hand, Margin = new Padding(0, 0, 8, 0) }; b.FlatAppearance.BorderSize = 0; b.Click += (_, _) => act(); return b; }
            snapRow.Controls.Add(SnapBtn(LocalizationManager.T("DCX.ProfileApply"), () => { if (snaps.Length > 0 && snapPick.SelectedIndex >= 0) { host.LoadSnapshot(snaps[snapPick.SelectedIndex]); ShowDeskContainers(); } }));
            snapRow.Controls.Add(SnapBtn(LocalizationManager.T("DCX.ProfileSave"), () =>
            {
                using var f = new MotionDesk.UI.AppDialog { Text = LocalizationManager.T("DCX.ProfileName"), StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(360, 110), FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false };
                var box = new TextBox { Location = new Point(14, 14), Width = 332 };
                var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(266, 62), Size = new Size(80, 30) };
                f.Controls.AddRange(new Control[] { box, ok }); f.AcceptButton = ok;
                StyleStudioDialog(f);
                if (f.ShowDialog(this) != DialogResult.OK || box.Text.Trim().Length == 0) return;
                host.SaveSnapshot(box.Text.Trim()); ShowDeskContainers();
            }));
            snapCard.Controls.Add(snapRow);
            var display = AddCardSection(panel, Lx("Διάταξη ανά συνδυασμό οθονών", "Layout by monitor arrangement"));
            string noLayout = Lx("Χωρίς αυτόματη αλλαγή", "No automatic change");
            var layoutNames = new[] { noLayout }.Concat(snaps).ToArray();
            FxCombo(display, Lx("Τρέχουσες οθόνες", "Current monitors"), layoutNames, host.LayoutForCurrentMonitors() ?? noLayout,
                selection => host.BindLayoutToCurrentMonitors(selection == noLayout ? null : selection));
            var preview = AddCardSection(panel, Lx("Προσωρινή εμφάνιση containers", "Container preview"));
            AddText(preview, Lx("Ctrl+Alt+Shift+Space για εμφάνιση πάνω από τα παράθυρα. Esc για κλείσιμο.",
                "Ctrl+Alt+Shift+Space to show containers above windows. Esc closes the preview."));
            preview.Controls.Add(SettingsView.Checkbox(Lx("Σκούρο φόντο στην προεπισκόπηση", "Dim background during preview"),
                AppSettings.Load().ContainerPeekDim, enabled => { var settings = AppSettings.Load(); settings.ContainerPeekDim = enabled; settings.Save(); }));
        }
    }
}
