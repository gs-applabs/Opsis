using System.Text.Json;
using MotionDesk.Services;
using MotionDesk.Widgets;

namespace MotionDesk.UI;

public sealed partial class MainWindow
{
    private void BuildWidgetCatalog(Control panel)
    {
        var catalog = AddCardSection(panel, StudioText("Widget add-on catalog", "Κατάλογος πρόσθετων widgets"));
        var list = new ListBox { Width = 382, Height = 115, BackColor = UiTheme.Surface, ForeColor = UiTheme.TextPrimary };
        var details = new Label { AutoSize = true, MaximumSize = new Size(600, 0), ForeColor = UiTheme.TextSecondary };
        void Reload()
        {
            list.Items.Clear();
            list.Items.AddRange(WidgetCatalog.List());
            if (list.Items.Count > 0) list.SelectedIndex = 0;
        }
        list.SelectedIndexChanged += (_, _) => details.Text = list.SelectedItem is WidgetCatalog.Package item
            ? $"{item.Manifest.Description}  ·  {item.Manifest.Version}  ·  " + (item.BuiltIn ? StudioText("Bundled", "Ενσωματωμένο") : StudioText("Installed", "Εγκατεστημένο")) : "";
        catalog.Controls.Add(list);
        catalog.Controls.Add(details);
        var actions = new FlowLayoutPanel { AutoSize = true, WrapContents = true };
        actions.Controls.Add(StudioButton(StudioText("Open widget", "Άνοιγμα widget"), () =>
        {
            if (list.SelectedItem is not WidgetCatalog.Package package) return;
            var area = Screen.FromControl(this).WorkingArea;
            WidgetHostEngine.Instance.RequestWidget(package.Key, area.Left + 40, area.Top + 60, package.Manifest.Width, package.Manifest.Height);
        }, 165, true));
        actions.Controls.Add(StudioButton(StudioText("Install ZIP…", "Εγκατάσταση ZIP…"), () =>
        {
            using var file = new OpenFileDialog { Filter = "Widget ZIP (*.zip)|*.zip", CheckFileExists = true };
            if (file.ShowDialog(this) != DialogResult.OK) return;
            try { WidgetCatalog.Install(file.FileName); Reload(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
            { MessageBox.Show(this, ex.Message, catalog.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }, 175));
        actions.Controls.Add(StudioButton(StudioText("Remove widget", "Κατάργηση widget"), () =>
        {
            if (list.SelectedItem is not WidgetCatalog.Package { BuiltIn: false } package) return;
            ApplySetting(package.Manifest.Name, () =>
            {
                WidgetHostEngine.Instance.CloseWidget(package.Key);
                WidgetCatalog.Remove(package);
                Reload();
            });
        }, 165));
        actions.Controls.Add(StudioButton(StudioText("SDK guide", "Οδηγός SDK"), () =>
        {
            var guide = Path.Combine(AppContext.BaseDirectory, "widget-sdk", "README.md");
            if (File.Exists(guide)) ShowTextDialog("Opsis Widget SDK", StudioText("Package format and examples", "Μορφή πακέτου και παραδείγματα"), File.ReadAllText(guide));
        }, 145));
        catalog.Controls.Add(actions);
        Reload();
    }
}
