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
                v => { var a = AppSettings.Load(); a.WidgetsTintColor = v; a.Save(); ThemeManager.RaiseRepainted(); });
            col.Controls.Add(SettingsView.NumericRow(Lx("Απόχρωση (0 = πλήρως διάφανο)", "Tint (0 = fully transparent)"), Math.Clamp(AppSettings.Load().WidgetsTintPercent, 0, 100), 0, 100,
                v => { var a = AppSettings.Load(); a.WidgetsTintPercent = v; a.Save(); ThemeManager.NotifyChanged(); }, 0, "%"));
            return col;
        }
        // DeskContainers page: on/off, the containers (title bar colour / opacity / height / font, lock, delete) and the layout snapshots.
        private void BuildContainersSettings(Control panel)
        {
            var engine = AddCardSection(panel, Lx("DeskContainers (νέος πυρήνας)", "DeskContainers (new core)"));
            engine.Controls.Add(SettingsView.Checkbox(Lx("Ενεργοποίηση DeskContainers", "Enable DeskContainers"), AppSettings.Load().UseNativeContainers,
                v => { var a = AppSettings.Load(); a.UseNativeContainers = v; a.Save(); }, Lx("Τα containers ομαδοποιούν συντομεύσεις/αρχεία σε γυάλινα παράθυρα στην επιφάνεια εργασίας.", "Containers group shortcuts and files in glass windows on the desktop."), restart: true));
            var host = MotionDesk.Containers.ContainerHost.Current;
            if (host == null) { AddText(engine, Lx("Τα containers δεν τρέχουν αυτή τη στιγμή (απενεργοποιημένα ή ασφαλής λειτουργία).", "Containers are not running right now (disabled or safe mode).")); return; }
            AddText(engine, Lx("Συντομεύσεις: Ctrl+Alt+Shift+N νέο container, Ctrl+Alt+Shift+H απόκρυψη/εμφάνιση όλων.", "Shortcuts: Ctrl+Alt+Shift+N new container, Ctrl+Alt+Shift+H hide / show all."));

            AddIconButtonGrid(panel,
                ("Container", LocalizationManager.T("DeskZones.NewContainerBtn"), (_, _) => { host.NewContainerAtCursor(); ShowDeskContainers(); }),
                ("Close", LocalizationManager.T("DeskZones.CloseAllContainers"), (_, _) => host.ToggleAll()));

            // ---- one container at a time
            var list = host.Containers();
            var card = AddCardSection(panel, Lx("Container", "Container"));
            if (list.Count == 0) { AddText(card, LocalizationManager.T("DCX.NoContainers")); }
            else
            {
                var names = list.Select((c, n) => (string.IsNullOrWhiteSpace(c.Title) ? "Container" : c.Title) + "  (" + (n + 1) + ")").ToArray();
                int sel = 0;
                var holder = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0) };
                void Build()
                {
                    foreach (Control old in holder.Controls.Cast<Control>().ToList()) { holder.Controls.Remove(old); old.Dispose(); }
                    var c = host.Containers().ElementAtOrDefault(sel); if (c == null) return;
                    string id = c.Id; var tb = c.TitleBar;
                    void Style(string key, object value) { host.Service.Handle("set-title-style", new System.Text.Json.Nodes.JsonObject { ["id"] = id, [key] = System.Text.Json.Nodes.JsonValue.Create(value) }); host.Sync(); }
                    var colorRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 6) };
                    colorRow.Controls.Add(new Label { Text = Lx("Χρώμα τίτλου (#RRGGBB ή Auto):", "Title bar colour (#RRGGBB or Auto):"), AutoSize = true, ForeColor = UiTheme.TextSecondary, Font = UiTheme.FontBody, Padding = new Padding(0, 6, 10, 0) });
                    var colorBox = new TextBox { Width = 110, Text = tb.Color };
                    colorBox.Leave += (_, _) => { var t = colorBox.Text.Trim(); if (t.Equals("Auto", StringComparison.OrdinalIgnoreCase) || System.Text.RegularExpressions.Regex.IsMatch(t, "^#[0-9a-fA-F]{6}$")) Style("color", t); else colorBox.Text = tb.Color; };
                    colorRow.Controls.Add(colorBox); holder.Controls.Add(colorRow);
                    holder.Controls.Add(SettingsView.NumericRow(Lx("Διαφάνεια τίτλου", "Title bar opacity"), tb.Opacity, 0, 100, v => Style("opacity", v), 0, "%"));
                    holder.Controls.Add(SettingsView.NumericRow(Lx("Ύψος τίτλου", "Title bar height"), tb.Height, 16, 96, v => Style("height", v), 0, "px"));
                    holder.Controls.Add(SettingsView.NumericRow(Lx("Μέγεθος γραμματοσειράς τίτλου", "Title font size"), (int)tb.FontSize, 6, 36, v => Style("fontSize", (float)v), 0, "pt"));
                    holder.Controls.Add(SettingsView.Checkbox(Lx("Κλείδωμα (δεν μετακινείται/αλλάζει μέγεθος)", "Locked (cannot move or resize)"), c.Locked,
                        v => { host.Service.Update(d => { var x = d.Containers.FirstOrDefault(k => k.Id == id); if (x == null) return false; x.Locked = v; return true; }); host.Sync(); }));
                    holder.Controls.Add(SettingsView.Checkbox(Lx("Κρυμμένο", "Hidden"), c.Hidden, v => host.SetHidden(id, v)));
                    var del = new HoverButton { Text = LocalizationManager.T("DCX.DeleteContainer"), Width = 230, Height = 32, FlatStyle = FlatStyle.Flat, ForeColor = UiTheme.TextPrimary, BackColor = UiTheme.SurfaceHover, Cursor = Cursors.Hand, Margin = new Padding(0, 6, 0, 0) };
                    del.FlatAppearance.BorderSize = 0;
                    del.Click += (_, _) =>
                    {
                        string name = string.IsNullOrWhiteSpace(c.Title) ? c.Id : c.Title;
                        if (MessageBox.Show(this, string.Format(LocalizationManager.T("DCX.DeleteConfirm"), name), LocalizationManager.T("DCX.DeleteTitle"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                        host.Service.Handle("delete", new System.Text.Json.Nodes.JsonObject { ["id"] = id }); host.Sync(); ShowDeskContainers();
                    };
                    holder.Controls.Add(del);
                }
                var pick = new FlatComboBox { Width = 300, Margin = new Padding(0, 0, 0, 8) };
                pick.SetItems(names, names[0]);
                pick.SelectedIndexChanged += (_, _) => { sel = Math.Max(0, pick.SelectedIndex); Build(); };
                card.Controls.Add(pick); card.Controls.Add(holder); Build();
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
                using var f = new Form { Text = LocalizationManager.T("DCX.ProfileName"), StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(360, 110), FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false };
                var box = new TextBox { Location = new Point(14, 14), Width = 332 };
                var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(266, 62), Size = new Size(80, 30) };
                f.Controls.AddRange(new Control[] { box, ok }); f.AcceptButton = ok;
                if (f.ShowDialog(this) != DialogResult.OK || box.Text.Trim().Length == 0) return;
                host.SaveSnapshot(box.Text.Trim()); ShowDeskContainers();
            }));
            snapCard.Controls.Add(snapRow);
        }
    }
}
