using MotionDesk.Services;
using MotionDesk.Widgets;

namespace MotionDesk.UI;

public sealed partial class MainWindow
{
    private void BuildDesktopSnappingCard(Control panel)
    {
        string title = LocalizationManager.T("Settings.SectionSnapping");
        var card = AddCardSection(panel, title);
        var current = AppSettings.Load();
        card.Controls.Add(SettingsView.Checkbox(LocalizationManager.T("Settings.EnableGridSnap"), current.GridSnap, value =>
        {
            WidgetSnapEngine.EnableGridSnap = value;
            SaveAppSettings();
        }));
        card.Controls.Add(SettingsView.NumericRow(LocalizationManager.T("Settings.SnapThreshold"), current.SnapThreshold, 5, 50, value =>
        {
            WidgetSnapEngine.SnapThreshold = value;
            SaveAppSettings();
        }));
        card.Controls.Add(SettingsView.NumericRow(LocalizationManager.T("Settings.GridSize"), current.GridSize, 5, 100, value =>
        {
            WidgetSnapEngine.GridSize = value;
            SaveAppSettings();
        }));
        card.Controls.Add(StudioButton(OpsisStrings.T("ui.reset"), () =>
        {
            if (MessageBox.Show(this, LocalizationManager.T("Settings.ResetConfirm"), title,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            var defaults = new AppSettings();
            WidgetSnapEngine.EnableGridSnap = defaults.GridSnap;
            WidgetSnapEngine.SnapThreshold = defaults.SnapThreshold;
            WidgetSnapEngine.GridSize = defaults.GridSize;
            SaveAppSettings();
            ShowDeskZones();
        }));
    }
}
