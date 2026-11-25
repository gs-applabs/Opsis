using MotionDesk.Services;

namespace MotionDesk.UI;

public sealed partial class MainWindow
{
    private void BuildDesktopMaterialCard(Control panel, DesktopSurface target)
    {
        var card = AddCardSection(panel, StudioText("Glass and transparency", "Glass και διαφάνεια"));
        var propertyRows = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown, WrapContents = false,
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Padding.Empty
        };
        var modes = new[] { "Default", "Clear", "Frosted", "Solid", "Custom" };
        var names = new[]
        {
            StudioText("App default", "Προεπιλογή εφαρμογής"), StudioText("Clear glass", "Διάφανο glass"),
            StudioText("Frosted glass", "Θαμπό glass"), StudioText("Solid", "Συμπαγές"),
            StudioText("Custom", "Προσαρμοσμένο")
        };
        bool refreshing = false;
        var material = DesktopMaterials.Get(target);
        FlatComboBox? picker = null;
        picker = FxCombo(card, StudioText("Material", "Υλικό"), names, names[Array.IndexOf(modes, material.Mode)], name =>
        {
            if (refreshing) return;
            var mode = modes[Array.IndexOf(names, name)];
            Save(mode == "Custom" ? material with { Mode = "Custom" } : DesktopMaterial.Preset(mode), rebuild: true);
        });
        card.Controls.Add(propertyRows);
        if (target == DesktopSurface.Widgets)
            AddText(card, StudioText("Glass presets switch the widgets to transparent mode.", "Τα προφίλ glass ενεργοποιούν τη διαφανή λειτουργία των widgets."));
        var status = new Label { AutoSize = true, MaximumSize = new Size(690, 0), ForeColor = UiTheme.TextSecondary };
        card.Controls.Add(StudioButton(StudioText("Import theme JSON", "Εισαγωγή θέματος JSON"), () =>
        {
            using var open = new OpenFileDialog { Filter = "Theme JSON (*.json)|*.json", CheckFileExists = true };
            if (open.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                Save(DesktopMaterial.ImportTheme(open.FileName, UiTheme.PageBackground.GetBrightness() < .5f), rebuild: true);
                status.Text = StudioText("Imported tint, opacity, blur, lens, rim and corners.",
                    "Εισήχθησαν απόχρωση, αδιαφάνεια, θάμπωμα, διάθλαση, περίγραμμα και γωνίες.");
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException)
            {
                status.Text = ex.Message;
            }
        }, width: 245));
        card.Controls.Add(status);
        BuildRows();

        void Save(DesktopMaterial next, bool rebuild)
        {
            material = next.Normalized();
            DesktopMaterials.Set(target, material);
            refreshing = true;
            try { picker?.SetItems(names, names[Array.IndexOf(modes, material.Mode)]); }
            finally { refreshing = false; }
            if (rebuild) BuildRows();
        }

        void BuildRows()
        {
            propertyRows.SuspendLayout();
            try
            {
                DisposeChildren(propertyRows);
                // the corner radius (curvature) is adjustable in EVERY look, also the default one (it no longer forces the Custom material)
                if (target == DesktopSurface.Containers)
                    propertyRows.Controls.Add(SettingsView.NumericRow(StudioText("Corner radius", "Ακτίνα γωνιών"), material.UsesCustom || material.RadiusSet ? material.Radius : 14, 0, 32,
                        number => Save(material with { Radius = number, RadiusSet = true }, rebuild: false)));
                if (!material.UsesCustom) return;
                propertyRows.Controls.Add(StudioButton(StudioText("Tint color", "Χρώμα απόχρωσης"), () =>
                {
                    using var color = new ColorDialog { Color = material.Color, FullOpen = true };
                    if (color.ShowDialog(this) != DialogResult.OK) return;
                    Save(material with { Tint = $"#{color.Color.R:X2}{color.Color.G:X2}{color.Color.B:X2}", Mode = EditMode() }, rebuild: false);
                }));
                Number("Opacity %", "Αδιαφάνεια %", material.Opacity, 100, value => material with { Opacity = value });
                Number("Blur", "Θάμπωμα", material.Blur, 60, value => material with { Blur = value });
                Number("Lens / gloss %", "Φακός / γυαλάδα %", material.Refraction, 200, value => material with { Refraction = value });
                Number("Rim light %", "Φωτεινό περίγραμμα %", material.Rim, 300, value => material with { Rim = value });
                if (target != DesktopSurface.Containers)
                    Number("Corner radius", "Ακτίνα γωνιών", material.Radius, 32, value => material with { Radius = value });
                if (target == DesktopSurface.Containers)
                    Number("Item font size (0 = per container)", "Μέγεθος γραμματοσειράς (0 = ανά container)",
                        material.ItemFontSize, 24, value => material with { ItemFontSize = value });
            }
            finally { propertyRows.ResumeLayout(); }
        }

        void Number(string en, string el, int value, int max, Func<int, DesktopMaterial> edit) =>
            propertyRows.Controls.Add(SettingsView.NumericRow(StudioText(en, el), value, 0, max,
                number => Save(edit(number) with { Mode = EditMode() }, rebuild: false)));
        string EditMode() => material.Mode == "Solid" ? "Solid" : "Custom";
    }
}
