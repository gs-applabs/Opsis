using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using MotionDesk.Services;
using MotionDesk.Widgets;
using UiTheme = MotionDesk.UI.UiTheme;

namespace MotionDesk.UI
{
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

        // "Transparent" widgets: a colour/tint setting, and a text colour that follows the desktop behind them.
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
        private void BuildContainersSettings(Control panel)
        {
            var card = AddCardSection(panel, LocalizationManager.T("DCM.GroupTitle"));
            AddText(card, LocalizationManager.T("DeskContainers.Intro"));
            AddIconButtonGrid(card,
                ("Container", LocalizationManager.T("DeskContainers.ChooseFolder"), (_, _) => DeskContainers.Instance.ChooseFolder(this)));
        }
    }
}
