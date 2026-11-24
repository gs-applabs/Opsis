using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using MotionDesk.Services;
using MotionDesk.Widgets;

namespace MotionDesk.UI;

public sealed partial class MainWindow
{
    /// <summary>All studios use the same page/theme, settings and recovery services.</summary>
    private void BuildEcosystemStudios(Panel panel)
    {
        var guard = AddCardSection(panel, StudioText("P3 — Windows Compatibility Guard", "P3 — Έλεγχος συμβατότητας Windows"));
        AddText(guard, StudioText(
            "Opsis remembers the approved Windows build and pauses risky shell activation after a feature update. Wallpapers and independent widgets remain usable. Windows-protected files are never overwritten.",
            "Το Opsis θυμάται την εγκεκριμένη έκδοση Windows και αναστέλλει επεμβατικές λειτουργίες Shell μετά από ενημέρωση. Τα ανεξάρτητα widgets και wallpapers συνεχίζουν. Δεν αντικαθίστανται προστατευμένα αρχεία Windows."));
        var guardStatus = new Label { AutoSize = true, ForeColor = UiTheme.TextPrimary, Margin = new Padding(8) };
        guard.Controls.Add(guardStatus);
        void RefreshGuard()
        {
            try
            {
                var state = WindowsUpdateGuard.Read();
                guardStatus.Text = (WindowsUpdateGuard.RequiresReview
                    ? StudioText("Action required: review Windows changes", "Απαιτείται έλεγχος αλλαγών Windows")
                    : StudioText("Compatibility baseline approved", "Εγκεκριμένη βάση συμβατότητας")) +
                    Environment.NewLine + "Current: " + OpsisRecoveryHub.WindowsFingerprint() +
                    Environment.NewLine + "Last approved: " + state.LastApprovedFingerprint;
            }
            catch (Exception ex) { guardStatus.Text = "Guard: " + ex.Message; }
        }
        RefreshGuard();
        AddButtonGrid(guard,
            (StudioText("Inspect Windows changes", "Έλεγχος αλλαγών Windows"), (_, _) =>
            {
                try
                {
                    MessageBox.Show(this, OpsisRecoveryHub.ReadLastUpdateImpact() + Environment.NewLine + Environment.NewLine +
                        "Current scan: " + OpsisRecoveryHub.InspectUpdateImpact(),
                        "Opsis Compatibility Guard", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    RefreshGuard();
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Opsis Guard", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }),
            (StudioText("Approve current build", "Έγκριση τρέχουσας έκδοσης"), (_, _) =>
            {
                if (MessageBox.Show(this,
                    StudioText("Only approve after checking Explorer, Taskbar, widgets and plugins. No system file will be modified. Proceed?",
                        "Ενέκρινε μόνο αφού ελέγξεις Explorer, Taskbar, widgets και plugins. Δεν αλλάζουν αρχεία συστήματος. Συνέχεια;"),
                    "Opsis Compatibility Guard", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                try { WindowsUpdateGuard.ApproveCurrentBuild(); RefreshGuard(); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Opsis Guard", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }),
            (StudioText("System Protection settings", "Ρυθμίσεις Προστασίας Συστήματος"), (_, _) =>
            {
                try { WindowsProtectionService.OpenProtectionSettings(); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Windows Protection", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }),
            (StudioText("Archive original Windows shell files", "Backup αυθεντικών αρχείων Shell"), async (_, _) =>
            {
                if (MessageBox.Show(this,
                    StudioText("Archive a small, verified set of original Windows shell binaries for audit? The copies must never be manually pasted over live system files.",
                        "Να αποθηκευτεί μικρό σύνολο αυθεντικών αρχείων Shell με ελέγχους SHA-256; Δεν πρέπει να αντιγραφούν χειροκίνητα πάνω σε ενεργά αρχεία Windows."),
                    "Opsis Recovery Hub", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                try
                {
                    var inventory = await System.Threading.Tasks.Task.Run(OpsisRecoveryHub.BackupMonitoredWindowsOriginals);
                    MessageBox.Show(this, "Archived " + inventory.Files.Count(x => x.Available) +
                        " files in " + OpsisRecoveryHub.Root + "\\WindowsOriginals" +
                        Environment.NewLine + "Use Windows DISM/SFC or System Restore for actual repair.",
                        "Opsis Recovery Hub", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Opsis Recovery Hub", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }),
            (StudioText("Create restore point", "Δημιουργία σημείου επαναφοράς"), async (_, _) =>
            {
                if (MessageBox.Show(this,
                    StudioText("Ask Windows to create a restore point? This may require UAC, enabled System Protection and may be limited to one per 24 hours.",
                        "Να ζητηθεί σημείο επαναφοράς Windows; Απαιτείται πιθανώς UAC και ενεργή Προστασία Συστήματος· ισχύουν χρονικοί περιορισμοί."),
                    "Windows System Restore", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                try
                {
                    var exitCode = await WindowsProtectionService.RequestSystemRestorePointAsync();
                    MessageBox.Show(this, exitCode == 0
                        ? StudioText("Windows accepted the restore point command.", "Τα Windows δέχθηκαν την εντολή σημείου επαναφοράς.")
                        : StudioText("Windows reported failure: ", "Τα Windows ανέφεραν αποτυχία: ") + exitCode,
                        "Windows System Restore", MessageBoxButtons.OK,
                        exitCode == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Windows System Restore", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }));

        var sceneCard = AddCardSection(panel, StudioText("P4 — Weather Scene Composer", "P4 — Συνθέτης σκηνών καιρού"));
        AddText(sceneCard, StudioText(
            "Combine existing weather, hour, wind and Greek landmark controls into reusable scenes. No extra renderer or background service.",
            "Συνδύασε καιρό, ώρα, άνεμο και ελληνικά τοπία σε επαναχρησιμοποιήσιμες σκηνές του υπάρχοντος renderer."));
        var sceneName = new TextBox { Width = 170, PlaceholderText = StudioText("Scene name", "Όνομα σκηνής") };
        var scenePicker = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
        var weather = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
        weather.Items.AddRange(new object[] { "Auto", "Clear", "PartlyCloudy", "Cloudy", "Drizzle", "Rain", "HeavyRain", "Thunderstorm", "Snow", "Fog" });
        var hour = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
        hour.Items.AddRange(new object[] { "Auto", "Dawn", "Day", "Dusk", "Night" });
        var wind = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130 };
        wind.Items.AddRange(new object[] { "Auto", "Calm", "LightBreeze", "FreshBreeze", "Strong", "Gale" });
        var landmark = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190 };
        landmark.Items.Add("Auto"); landmark.Items.Add("Off");
        foreach (var entry in LandmarkCatalog.Scenes) landmark.Items.Add(entry.Id);
        var currentWallpaper = WallpaperSettings.Load();
        weather.SelectedItem = currentWallpaper.WeatherSimulation;
        hour.SelectedItem = currentWallpaper.TimeSimulation;
        wind.SelectedItem = currentWallpaper.WindSimulation;
        landmark.SelectedItem = currentWallpaper.WeatherLandmarkId;
        if (weather.SelectedIndex < 0) weather.SelectedIndex = 0;
        if (hour.SelectedIndex < 0) hour.SelectedIndex = 0;
        if (wind.SelectedIndex < 0) wind.SelectedIndex = 0;
        if (landmark.SelectedIndex < 0) landmark.SelectedIndex = 0;
        var sceneRow = new FlowLayoutPanel { Width = 720, AutoSize = true, WrapContents = true };
        foreach (var control in new Control[] { sceneName, scenePicker, weather, hour, wind, landmark })
            sceneRow.Controls.Add(control);
        sceneCard.Controls.Add(sceneRow);
        var sceneSummary = new Label { Width = 720, AutoSize = true, ForeColor = UiTheme.TextSecondary };
        sceneCard.Controls.Add(sceneSummary);
        void RefreshScenes()
        {
            var current = scenePicker.SelectedItem?.ToString();
            scenePicker.Items.Clear();
            foreach (var s in WeatherSceneComposer.List()) scenePicker.Items.Add(s);
            if (current != null && scenePicker.Items.Contains(current)) scenePicker.SelectedItem = current;
            else if (scenePicker.Items.Count > 0) scenePicker.SelectedIndex = 0;
        }
        void ShowScenePreview()
        {
            sceneSummary.Text = $"Weather: {weather.SelectedItem}  |  Time: {hour.SelectedItem}  |  Wind: {wind.SelectedItem}  |  Landmark: {landmark.SelectedItem}";
        }
        foreach (var picker in new[] { weather, hour, wind, landmark })
            picker.SelectedIndexChanged += (_, _) => ShowScenePreview();
        ShowScenePreview();
        RefreshScenes();
        AddButtonGrid(sceneCard,
            (StudioText("Save scene", "Αποθήκευση σκηνής"), (_, _) =>
            {
                try
                {
                    var created = WeatherSceneComposer.Capture(sceneName.Text);
                    created.Weather = weather.SelectedItem?.ToString() ?? "Auto";
                    created.Time = hour.SelectedItem?.ToString() ?? "Auto";
                    created.Wind = wind.SelectedItem?.ToString() ?? "Auto";
                    created.Landmark = landmark.SelectedItem?.ToString() ?? "Auto";
                    WeatherSceneComposer.Save(created); RefreshScenes(); scenePicker.SelectedItem = created.Name;
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Weather Scene Composer", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }),
            (StudioText("Apply selected scene", "Εφαρμογή σκηνής"), (_, _) =>
            {
                if (scenePicker.SelectedItem is not string selected) return;
                if (MessageBox.Show(this, selected + "?", "Weather Scene Composer",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                try { WeatherSceneComposer.Apply(selected); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Weather Scene Composer", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }),
            (StudioText("Delete scene", "Διαγραφή σκηνής"), (_, _) =>
            {
                if (scenePicker.SelectedItem is not string selected) return;
                if (MessageBox.Show(this, selected + "?", "Weather Scene Composer",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                try { WeatherSceneComposer.Delete(selected); RefreshScenes(); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Weather Scene Composer", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }));

        AddButtonGrid(sceneCard,
            (StudioText("Export current theme", "Εξαγωγή τρέχοντος θέματος"), (_, _) =>
            {
                using var dialog = new SaveFileDialog
                {
                    Filter = "Opsis Theme (*.opsis-theme)|*.opsis-theme",
                    FileName = "OpsisTheme.opsis-theme"
                };
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    OpsisThemeBundle.ExportCurrent(System.IO.Path.GetFileNameWithoutExtension(dialog.FileName), dialog.FileName);
                    MessageBox.Show(this, StudioText("Theme exported. The bundle contains declarative settings only.",
                        "Το θέμα εξήχθη με ασφαλή αρχεία ρυθμίσεων, χωρίς εκτελέσιμο κώδικα."),
                        "Opsis Theme Bundle", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Opsis Theme Bundle", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }),
            (StudioText("Import theme bundle", "Εισαγωγή θέματος"), (_, _) =>
            {
                using var dialog = new OpenFileDialog
                {
                    Filter = "Opsis Theme (*.opsis-theme)|*.opsis-theme"
                };
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var name = OpsisThemeBundle.Import(dialog.FileName);
                    RefreshScenes();
                    MessageBox.Show(this,
                        StudioText("Imported without applying system changes: ", "Εισήχθη χωρίς αλλαγές συστήματος: ") + name +
                        Environment.NewLine + StudioText("Apply its scene and appearance profile separately after reviewing.",
                            "Εφάρμοσε τη σκηνή και το προφίλ εμφάνισης ξεχωριστά μετά από έλεγχο."),
                        "Opsis Theme Bundle", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Opsis Theme Bundle", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }));

        var widgetCard = AddCardSection(panel, StudioText("P4 — Widget Layout Studio", "P4 — Στούντιο σχεδίασης Widget"));
        AddText(widgetCard, StudioText(
            "Build reusable native desktop clocks, dates and labels. This initial safe designer uses the common Opsis theme without independent plugin services.",
            "Δημιούργησε επαναχρησιμοποιήσιμα ρολόγια, ημερομηνίες και ετικέτες. Χρησιμοποιείται το κοινό θέμα Opsis, χωρίς ξεχωριστές υπηρεσίες plugins."));
        var widgetName = new TextBox { Width = 150, PlaceholderText = StudioText("Widget name", "Όνομα widget") };
        var widgetBinding = new ComboBox { Width = 110, DropDownStyle = ComboBoxStyle.DropDownList };
        widgetBinding.Items.AddRange(new object[] { "Clock", "Date", "Text", "CPU", "RAM", "Battery" }); widgetBinding.SelectedIndex = 0;
        var widgetText = new TextBox { Width = 150, PlaceholderText = StudioText("Static text", "Στατικό κείμενο") };
        var widgetHex = new TextBox { Width = 86, Text = "#00D2FF" };
        var widgetFont = new NumericUpDown { Width = 68, Minimum = 12, Maximum = 80, Value = 30 };
        var widgetPicker = new ComboBox { Width = 150, DropDownStyle = ComboBoxStyle.DropDownList };
        var widgetRow = new FlowLayoutPanel { Width = 720, AutoSize = true, WrapContents = true };
        foreach (var part in new Control[] { widgetName, widgetBinding, widgetText, widgetHex, widgetFont, widgetPicker })
            widgetRow.Controls.Add(part);
        widgetCard.Controls.Add(widgetRow);
        var widgetInfo = new Label { AutoSize = true, ForeColor = UiTheme.TextSecondary,
            Text = StudioText("Widget preview is a native WinForms window.", "Η προεπισκόπηση widget είναι εγγενές παράθυρο WinForms.") };
        widgetCard.Controls.Add(widgetInfo);
        void RefreshWidgets()
        {
            var current = widgetPicker.SelectedItem?.ToString();
            widgetPicker.Items.Clear();
            foreach (var name in OpsisWidgetDesigner.List()) widgetPicker.Items.Add(name);
            if (current != null && widgetPicker.Items.Contains(current)) widgetPicker.SelectedItem = current;
            else if (widgetPicker.Items.Count > 0) widgetPicker.SelectedIndex = 0;
        }
        RefreshWidgets();
        AddButtonGrid(widgetCard,
            (StudioText("Save widget", "Αποθήκευση widget"), (_, _) =>
            {
                try
                {
                    var layout = new OpsisWidgetDesigner.Layout
                    {
                        Name = widgetName.Text.Trim(), Binding = widgetBinding.SelectedItem?.ToString() ?? "Clock",
                        Text = widgetText.Text, AccentHex = widgetHex.Text.Trim(),
                        FontSize = (int)widgetFont.Value
                    };
                    OpsisWidgetDesigner.Save(layout);
                    RefreshWidgets(); widgetPicker.SelectedItem = layout.Name;
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Opsis Widget Studio", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }),
            (StudioText("Visual editor", "Οπτικός επεξεργαστής"), (_, _) =>
            {
                if (widgetPicker.SelectedItem is not string selected) return;
                try
                {
                    using var editor = new OpsisWidgetVisualDesignerForm(OpsisWidgetDesigner.Read(selected));
                    editor.ShowDialog(this);
                    RefreshWidgets();
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Opsis Widget Studio", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }),
            (StudioText("Open widget", "Άνοιγμα widget"), (_, _) =>
            {
                if (widgetPicker.SelectedItem is not string selected) return;
                try
                {
                    var window = new OpsisCustomWidgetWindow(OpsisWidgetDesigner.Read(selected));
                    window.Show(this);
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Opsis Widget Studio", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }),
            (StudioText("Delete widget", "Διαγραφή widget"), (_, _) =>
            {
                if (widgetPicker.SelectedItem is not string selected) return;
                if (MessageBox.Show(this, selected + "?", "Opsis Widget Studio",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                try { OpsisWidgetDesigner.Delete(selected); RefreshWidgets(); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Opsis Widget Studio", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }));

        AddButtonGrid(widgetCard,
            (StudioText("Import Rainmeter INI", "Εισαγωγή Rainmeter INI"), (_, _) =>
            {
                using var chooser = new OpenFileDialog
                {
                    Filter = "Rainmeter skin (*.ini)|*.ini",
                    CheckFileExists = true
                };
                if (chooser.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var info = OpsisRainmeterSkinImporter.PreviewFile(chooser.FileName);
                    var message = StudioText(
                        "Only one built-in String/Time meter is supported. Script, plugin and dynamic expressions are rejected.",
                        "Υποστηρίζεται μία εγγενής ετικέτα String/Time. Scripts, plugins και δυναμικές εκφράσεις απορρίπτονται.") +
                        Environment.NewLine + info.Name + " · " + info.Binding + " · " + info.AccentHex +
                        Environment.NewLine + StudioText("Import this safe, data-only widget?",
                            "Να εισαχθεί ως ασφαλές widget δεδομένων;");
                    if (MessageBox.Show(this, message, "Opsis / Rainmeter", MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question) != DialogResult.Yes) return;
                    var name = OpsisRainmeterSkinImporter.Import(chooser.FileName);
                    RefreshWidgets();
                    widgetPicker.SelectedItem = name;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "Opsis Rainmeter Import",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }));

        var shellDesignerCard = AddCardSection(panel, StudioText("P3 — Unified Shell Studio", "P3 — Ενιαίο στούντιο Shell"));
        AddText(shellDesignerCard, StudioText(
            "One coherent entry point for native Start preferences, Opsis taskbar layout and supported Explorer options. Reversible profiles, no Windows binary injection.",
            "Ενιαίος χώρος για Start, σχεδίαση γραμμής εργασιών Opsis και υποστηριζόμενες επιλογές Explorer. Αναστρέψιμα προφίλ, χωρίς επεμβάσεις σε αρχεία Windows."));
        AddButtonGrid(shellDesignerCard,
            (StudioText("Scan Windhawk/Rainmeter conflicts", "Έλεγχος συμβατότητας mods"), (_, _) =>
            {
                try
                {
                    var results = OpsisModCompatibility.Scan();
                    var message = results.Count == 0
                        ? StudioText("No known competing shell tools detected in installed applications or processes.",
                            "Δεν εντοπίστηκαν γνωστά εργαλεία Shell στις εγκατεστημένες εφαρμογές ή διεργασίες.")
                        : string.Join(Environment.NewLine + Environment.NewLine,
                            results.Select(f => f.Product + " [" + f.Risk + "] — " + f.Area +
                                Environment.NewLine + f.Advice));
                    MessageBox.Show(this, message, "Opsis Shell Compatibility",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Opsis Compatibility",
                    MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }),
            (StudioText("Taskbar visual designer", "Οπτικός σχεδιαστής Taskbar"), (_, _) =>
            {
                try { using var designer = new OpsisTaskbarDesignerForm(); designer.ShowDialog(this); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Opsis Taskbar Studio", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }),
            (StudioText("Start Studio", "Στούντιο Έναρξης"), (_, _) =>
            {
                try { using var start = new StartStudioForm(); start.ShowDialog(this); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Opsis Start Studio", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }),
            (StudioText("Explorer visual studio", "Οπτικός σχεδιαστής Explorer"), (_, _) =>
            {
                try { using var explorer = new OpsisExplorerStudioForm(); explorer.ShowDialog(this); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Opsis Explorer Studio", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }),
            (StudioText("Windows Start settings", "Ρυθμίσεις Έναρξης Windows"), (_, _) =>
            {
                try { TaskbarViewProfiles.OpenWindowsStartSettings(); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Windows Start", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }));

        var screensaverCard = AddCardSection(panel, StudioText("P4 — Screensaver Studio", "P4 — Στούντιο προφύλαξης οθόνης"));
        AddText(screensaverCard, StudioText(
            "Create native Opsis fullscreen clock/image scenes. User-triggered preview only; Windows .scr registration remains a separate opt-in integration.",
            "Δημιουργία σκηνών ρολογιού και εικόνας πλήρους οθόνης. Προεπισκόπηση μόνο με επιλογή χρήστη· δεν αλλάζει η προφύλαξη οθόνης των Windows."));
        var saverMode = new ComboBox { Width = 152, DropDownStyle = ComboBoxStyle.DropDownList };
        saverMode.Items.AddRange(new object[] { "Clock", "StaticImage", "ClockAndImage" });
        var saverImage = new TextBox { Width = 320, ReadOnly = true };
        var saverChoose = new Button { Text = StudioText("Choose image", "Επιλογή εικόνας"), Width = 120 };
        var saverDim = new NumericUpDown { Minimum = 0, Maximum = 75, Increment = 5, Width = 68 };
        var saverSeconds = new CheckBox { Text = StudioText("Show seconds", "Εμφάνιση δευτερολέπτων"),
            AutoSize = true, ForeColor = UiTheme.TextPrimary };
        OpsisScreensaverStudio.Profile savedSaver;
        try { savedSaver = OpsisScreensaverStudio.Read(); }
        catch (Exception ex)
        {
            RotatingLogger.Write("Invalid screensaver configuration: " + ex.Message);
            savedSaver = new OpsisScreensaverStudio.Profile();
        }
        saverMode.SelectedItem = savedSaver.Mode;
        saverImage.Text = savedSaver.ImagePath;
        saverDim.Value = savedSaver.DimPercent;
        saverSeconds.Checked = savedSaver.ShowSeconds;
        saverChoose.Click += (_, _) =>
        {
            using var chooser = new OpenFileDialog
            {
                Filter = "Images (*.jpg;*.jpeg;*.png;*.webp;*.bmp)|*.jpg;*.jpeg;*.png;*.webp;*.bmp"
            };
            if (chooser.ShowDialog(this) == DialogResult.OK) saverImage.Text = chooser.FileName;
        };
        var saverRow = new FlowLayoutPanel { Width = 720, AutoSize = true, WrapContents = true };
        saverRow.Controls.Add(saverMode);
        saverRow.Controls.Add(saverImage);
        saverRow.Controls.Add(saverChoose);
        saverRow.Controls.Add(new Label { Text = StudioText("Dim %", "Σκοτείνιασμα %"),
            AutoSize = true, ForeColor = UiTheme.TextSecondary, Margin = new Padding(4, 8, 2, 0) });
        saverRow.Controls.Add(saverDim);
        saverRow.Controls.Add(saverSeconds);
        screensaverCard.Controls.Add(saverRow);
        OpsisScreensaverStudio.Profile BuildScreensaverProfile() => new()
        {
            Mode = saverMode.SelectedItem?.ToString() ?? "Clock",
            ImagePath = saverImage.Text,
            DimPercent = (int)saverDim.Value,
            ShowSeconds = saverSeconds.Checked
        };
        AddButtonGrid(screensaverCard,
            (StudioText("Save screensaver scene", "Αποθήκευση σκηνής"), (_, _) =>
            {
                try { OpsisScreensaverStudio.Save(BuildScreensaverProfile()); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Opsis Screensaver", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }),
            (StudioText("Fullscreen preview", "Προεπισκόπηση πλήρους οθόνης"), (_, _) =>
            {
                try
                {
                    using var preview = new OpsisScreensaverPreviewForm(BuildScreensaverProfile());
                    preview.ShowDialog(this);
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Opsis Screensaver", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }),
            (StudioText("Windows screensaver settings", "Προφύλαξη οθόνης Windows"), (_, _) =>
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("control.exe", "desk.cpl,,1")
                    { UseShellExecute = true }); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Windows Screensaver", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }));

        var menuCard = AddCardSection(panel, StudioText("P3 — Context Menu Studio", "P3 — Μενού δεξιού κλικ"));
        AddText(menuCard, StudioText(
            "An optional Opsis launcher on Windows desktop and Explorer folder background menus. HKCU only; no DLL extension, system-file edits or Explorer restart.",
            "Προαιρετική εντολή Opsis στο δεξί κλικ της επιφάνειας εργασίας και του Explorer. Μόνο HKCU, χωρίς DLL, αλλαγές αρχείων Windows ή επανεκκίνηση Explorer."));
        var contextStatus = new Label { AutoSize = true, ForeColor = UiTheme.TextSecondary,
            Text = OpsisContextMenuStudio.Describe() };
        menuCard.Controls.Add(contextStatus);
        AddButtonGrid(menuCard,
            (StudioText("Enable Opsis context menu", "Ενεργοποίηση μενού Opsis"), (_, _) =>
            {
                try
                {
                    if (MessageBox.Show(this, StudioText("Add Opsis Desktop Studio to your right-click menus?",
                        "Να προστεθεί το Opsis στο δεξί κλικ;"), "Opsis Context Menu",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                    OpsisContextMenuStudio.Enable();
                    contextStatus.Text = OpsisContextMenuStudio.Describe();
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Context Menu Studio", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }),
            (StudioText("Remove Opsis context menu", "Αφαίρεση μενού Opsis"), (_, _) =>
            {
                try
                {
                    OpsisContextMenuStudio.Disable();
                    contextStatus.Text = OpsisContextMenuStudio.Describe();
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Context Menu Studio", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }));

        var accentCard = AddCardSection(panel, StudioText("P4 — Wallpaper Dynamic Accent", "P4 — Δυναμικά χρώματα Wallpaper"));
        AddText(accentCard, StudioText(
            "Extract a balanced accent from a local image wallpaper, apply to the shared Opsis palette, without changing Windows system accent. Explicit user activation only.",
            "Εξαγωγή χρώματος από το ενεργό τοπικό wallpaper και εφαρμογή στην κοινή παλέτα Opsis, χωρίς αλλαγή χρωμάτων Windows. Μόνο με επιλογή χρήστη."));
        var accentValue = new Label { AutoSize = true, ForeColor = UiTheme.TextPrimary,
            Text = AppSettings.Load().WallpaperAccentEnabled
                ? AppSettings.Load().WallpaperAccentHex
                : StudioText("Disabled", "Ανενεργό") };
        accentCard.Controls.Add(accentValue);
        AddButtonGrid(accentCard,
            (StudioText("Preview image accent", "Προεπισκόπηση χρώματος"), (_, _) =>
            {
                try
                {
                    var hex = WallpaperAccentEngine.PreviewCurrent();
                    accentValue.Text = hex + StudioText(" (preview only)", " (μόνο προεπισκόπηση)");
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Opsis Dynamic Accent", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }),
            (StudioText("Apply image accent", "Εφαρμογή χρώματος"), (_, _) =>
            {
                try
                {
                    WallpaperAccentEngine.ApplyCurrent();
                    accentValue.Text = AppSettings.Load().WallpaperAccentHex;
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Opsis Dynamic Accent", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }),
            (StudioText("Restore default accent", "Επαναφορά χρώματος"), (_, _) =>
            {
                try
                {
                    WallpaperAccentEngine.Disable();
                    accentValue.Text = StudioText("Disabled", "Ανενεργό");
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Opsis Dynamic Accent", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }));

        var cursorCard = AddCardSection(panel, StudioText("P4 — Cursor FX Studio", "P4 — Εφέ δείκτη ποντικιού"));
        AddText(cursorCard, StudioText(
            "Optional halo and click ripple around the pointer. No mouse hooks or shell DLL injection. Pauses in fullscreen and on battery when configured.",
            "Προαιρετική φωτεινή άλω και κύκλος στο κλικ. Χωρίς hooks ή DLL injection, με παύση σε πλήρη οθόνη και λειτουργία μπαταρίας."));
        var cursorStyle = new ComboBox { Width = 160, DropDownStyle = ComboBoxStyle.DropDownList };
        cursorStyle.Items.AddRange(new object[] { "Halo", "Ripple", "Both" });
        cursorStyle.SelectedItem = AppSettings.Load().CursorFxStyle;
        if (cursorStyle.SelectedIndex < 0) cursorStyle.SelectedItem = "Both";
        var cursorIntensity = new NumericUpDown
        {
            Minimum = 20, Maximum = 100, Increment = 5, Width = 82,
            Value = Math.Clamp(AppSettings.Load().CursorFxIntensity, 20, 100)
        };
        var cursorRow = new FlowLayoutPanel { Width = 720, AutoSize = true };
        cursorRow.Controls.Add(new Label { Text = StudioText("Effect", "Εφέ"), AutoSize = true,
            Margin = new Padding(5, 9, 1, 0), ForeColor = UiTheme.TextPrimary });
        cursorRow.Controls.Add(cursorStyle);
        cursorRow.Controls.Add(new Label { Text = StudioText("Intensity", "Ένταση"), AutoSize = true,
            Margin = new Padding(5, 9, 1, 0), ForeColor = UiTheme.TextPrimary });
        cursorRow.Controls.Add(cursorIntensity);
        cursorCard.Controls.Add(cursorRow);
        AddButtonGrid(cursorCard,
            (StudioText("Enable Cursor FX", "Ενεργοποίηση εφέ"), (_, _) =>
            {
                try
                {
                    if (SafeModeService.IsSafeMode || WindowsUpdateGuard.RequiresReview)
                        throw new InvalidOperationException("Windows compatibility review is required.");
                    var s = AppSettings.Load();
                    s.CursorFxStyle = cursorStyle.SelectedItem?.ToString() ?? "Both";
                    s.CursorFxIntensity = (int)cursorIntensity.Value;
                    s.Save();
                    OpsisFeatureSwitcher.Set("cursor.effects", true);
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Opsis Cursor FX", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }),
            (StudioText("Disable Cursor FX", "Απενεργοποίηση εφέ"), (_, _) =>
            {
                try { OpsisFeatureSwitcher.Set("cursor.effects", false); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Opsis Cursor FX", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }),
            (StudioText("Apply effect settings", "Εφαρμογή ρυθμίσεων"), (_, _) =>
            {
                try
                {
                    var s = AppSettings.Load();
                    s.CursorFxStyle = cursorStyle.SelectedItem?.ToString() ?? "Both";
                    s.CursorFxIntensity = (int)cursorIntensity.Value;
                    s.Save();
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Opsis Cursor FX", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }));

        var windowCard = AddCardSection(panel, StudioText("P3 — Window Rules", "P3 — Κανόνες παραθύρων"));
        AddText(windowCard, StudioText(
            "Control position and always-on-top for explicitly named applications. Only the foreground window is inspected, no global hooks or DLL injection. All original positions are restored on exit.",
            "Έλεγξε θέση και παραμονή επάνω για συγκεκριμένες εφαρμογές. Ελέγχεται μόνο το ενεργό παράθυρο, χωρίς hooks ή DLL injection. Στην έξοδο γίνεται προσπάθεια επαναφοράς αρχικών θέσεων."));
        var windowEnabled = new CheckBox { AutoSize = true, ForeColor = UiTheme.TextPrimary,
            Text = StudioText("Enable Window Rules", "Ενεργοποίηση κανόνων παραθύρων"),
            Checked = AppSettings.Load().WindowRulesEnabled };
        windowEnabled.CheckedChanged += (_, _) =>
        {
            try
            {
                if (windowEnabled.Checked && (SafeModeService.IsSafeMode || WindowsUpdateGuard.RequiresReview))
                {
                    windowEnabled.Checked = false;
                    throw new InvalidOperationException("Windows compatibility review is required.");
                }
                var s = AppSettings.Load(); s.WindowRulesEnabled = windowEnabled.Checked; s.Save();
                if (windowEnabled.Checked)
                {
                    WindowRulesEngine.Instance.Start();
                }
                else { WindowRulesEngine.Instance.Stop(); WindowRulesEngine.Instance.Restore(); }
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Window Rules", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        };
        windowCard.Controls.Add(windowEnabled);
        var appName = new TextBox { Width = 145, PlaceholderText = "notepad" };
        NumericUpDown Numeric(int minimum, int maximum, int current, int width = 74) =>
            new() { Minimum = minimum, Maximum = maximum, Value = current, Width = width };
        var xPos = Numeric(-20000, 20000, 100);
        var yPos = Numeric(-20000, 20000, 100);
        var winWidth = Numeric(300, 10000, 960);
        var winHeight = Numeric(240, 10000, 640);
        var move = new CheckBox { Text = StudioText("Set bounds", "Θέση/μέγεθος"), AutoSize = true,
            ForeColor = UiTheme.TextPrimary, Checked = true };
        var topMost = new CheckBox { Text = "Always on top", AutoSize = true,
            ForeColor = UiTheme.TextPrimary };
        var windowForm = new FlowLayoutPanel { Width = 720, AutoSize = true, WrapContents = true };
        foreach (var part in new Control[] { appName, xPos, yPos, winWidth, winHeight, move, topMost })
            windowForm.Controls.Add(part);
        windowCard.Controls.Add(windowForm);
        var windowRulesList = new ListBox { Width = 700, Height = 110 };
        windowCard.Controls.Add(windowRulesList);
        void RefreshWindowRules()
        {
            windowRulesList.Items.Clear();
            foreach (var r in WindowRulesEngine.Read())
                windowRulesList.Items.Add((r.Enabled ? "● " : "○ ") + r.Process + " · " +
                    (r.MoveAndResize ? $"{r.X},{r.Y} {r.Width}×{r.Height}" : "No move") +
                    (r.AlwaysOnTop ? " · Always on top" : ""));
        }
        try { RefreshWindowRules(); }
        catch (Exception ex) { windowRulesList.Items.Add("Error: " + ex.Message); }
        AddButtonGrid(windowCard,
            (StudioText("Add rule", "Προσθήκη κανόνα"), (_, _) =>
            {
                try
                {
                    var list = WindowRulesEngine.Read().ToList();
                    list.Add(new WindowRulesEngine.Rule
                    {
                        Process = appName.Text.Trim().Replace(".exe", "", StringComparison.OrdinalIgnoreCase),
                        Enabled = false, MoveAndResize = move.Checked,
                        X = (int)xPos.Value, Y = (int)yPos.Value, Width = (int)winWidth.Value,
                        Height = (int)winHeight.Value, AlwaysOnTop = topMost.Checked
                    });
                    WindowRulesEngine.Save(list); RefreshWindowRules();
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Window Rules", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }),
            (StudioText("Enable / disable", "Ενεργοποίηση / απενεργοποίηση"), (_, _) =>
            {
                if (windowRulesList.SelectedIndex < 0) return;
                try
                {
                    var list = WindowRulesEngine.Read().ToList();
                    list[windowRulesList.SelectedIndex].Enabled = !list[windowRulesList.SelectedIndex].Enabled;
                    WindowRulesEngine.Instance.Restore();
                    WindowRulesEngine.Save(list); RefreshWindowRules();
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Window Rules", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }),
            (StudioText("Delete rule", "Διαγραφή κανόνα"), (_, _) =>
            {
                if (windowRulesList.SelectedIndex < 0) return;
                try
                {
                    var list = WindowRulesEngine.Read().ToList();
                    list.RemoveAt(windowRulesList.SelectedIndex);
                    WindowRulesEngine.Instance.Restore();
                    WindowRulesEngine.Save(list); RefreshWindowRules();
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Window Rules", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }),
            (StudioText("Restore window positions", "Επαναφορά θέσεων παραθύρων"), (_, _) =>
                WindowRulesEngine.Instance.Restore()));

        var automationCard = AddCardSection(panel, StudioText("P5 — Automation Studio", "P5 — Στούντιο αυτοματισμών"));
        AddText(automationCard, StudioText(
            "Local schedules only. Rules are disabled by default and never execute shell commands, arbitrary scripts or downloaded code.",
            "Τοπικοί αυτοματισμοί με ασφαλείς ενέργειες. Αρχικά ανενεργοί, χωρίς αυθαίρετα scripts ή εντολές συστήματος."));
        var enabledToggle = new CheckBox { AutoSize = true, Margin = new Padding(8),
            Text = StudioText("Enable scheduled automations", "Ενεργοποίηση προγραμματισμένων αυτοματισμών"),
            Checked = AppSettings.Load().OpsisAutomationsEnabled, ForeColor = UiTheme.TextPrimary };
        enabledToggle.CheckedChanged += (_, _) =>
        {
            try { var s = AppSettings.Load(); s.OpsisAutomationsEnabled = enabledToggle.Checked; s.Save(); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Automation Studio", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        };
        automationCard.Controls.Add(enabledToggle);
        var ruleName = new TextBox { Width = 160, PlaceholderText = StudioText("Rule name", "Όνομα κανόνα") };
        var trigger = new ComboBox { Width = 112, DropDownStyle = ComboBoxStyle.DropDownList };
        trigger.Items.AddRange(new object[] { "Startup", "DailyAt", "WeekdayAt" }); trigger.SelectedIndex = 1;
        var time = new DateTimePicker { Width = 110, Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm", ShowUpDown = true };
        var action = new ComboBox { Width = 150, DropDownStyle = ComboBoxStyle.DropDownList };
        action.Items.AddRange(new object[] { "Scene", "AppearancePreset", "ThemeMode", "HotCorners" });
        action.SelectedIndex = 2;
        var target = new ComboBox { Width = 170, DropDownStyle = ComboBoxStyle.DropDownList };
        void RefreshTargets()
        {
            target.Items.Clear();
            var values = action.SelectedItem?.ToString() switch
            {
                "Scene" => WeatherSceneComposer.List(),
                "AppearancePreset" => AppearancePresets.List(),
                "ThemeMode" => new[] { "Follow", "Light", "Dark" },
                "HotCorners" => new[] { "Off", "On" },
                _ => Array.Empty<string>()
            };
            foreach (var item in values) target.Items.Add(item);
            if (target.Items.Count > 0) target.SelectedIndex = 0;
        }
        action.SelectedIndexChanged += (_, _) => RefreshTargets();
        RefreshTargets();
        var ruleEditor = new FlowLayoutPanel { Width = 720, AutoSize = true, WrapContents = true };
        foreach (var control in new Control[] { ruleName, trigger, time, action, target })
            ruleEditor.Controls.Add(control);
        automationCard.Controls.Add(ruleEditor);
        var rulesBox = new ListBox { Width = 690, Height = 130 };
        automationCard.Controls.Add(rulesBox);
        void RefreshRules()
        {
            var configuration = OpsisAutomationStudio.Read();
            rulesBox.Items.Clear();
            foreach (var rule in configuration.Rules)
                rulesBox.Items.Add((rule.Enabled ? "● " : "○ ") + rule.Name + "  |  " +
                    rule.Trigger + " " + rule.Hour.ToString("00") + ":" + rule.Minute.ToString("00") +
                    "  → " + rule.Action + " / " + rule.Target);
        }
        try { RefreshRules(); }
        catch (Exception ex) { rulesBox.Items.Add("Error: " + ex.Message); }
        AddButtonGrid(automationCard,
            (StudioText("Add rule", "Προσθήκη κανόνα"), (_, _) =>
            {
                try
                {
                    var conf = OpsisAutomationStudio.Read();
                    conf.Rules.Add(new OpsisAutomationStudio.Rule
                    {
                        Name = ruleName.Text.Trim(), Enabled = false,
                        Trigger = trigger.SelectedItem?.ToString() ?? "DailyAt",
                        Hour = time.Value.Hour, Minute = time.Value.Minute,
                        Action = action.SelectedItem?.ToString() ?? "ThemeMode",
                        Target = target.SelectedItem?.ToString() ?? ""
                    });
                    OpsisAutomationStudio.Save(conf); RefreshRules();
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Automation Studio", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }),
            (StudioText("Enable / disable rule", "Ενεργοποίηση / απενεργοποίηση"), (_, _) =>
            {
                if (rulesBox.SelectedIndex < 0) return;
                try
                {
                    var conf = OpsisAutomationStudio.Read();
                    conf.Rules[rulesBox.SelectedIndex].Enabled = !conf.Rules[rulesBox.SelectedIndex].Enabled;
                    OpsisAutomationStudio.Save(conf); RefreshRules();
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Automation Studio", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }),
            (StudioText("Remove rule", "Διαγραφή κανόνα"), (_, _) =>
            {
                if (rulesBox.SelectedIndex < 0) return;
                try
                {
                    var conf = OpsisAutomationStudio.Read();
                    conf.Rules.RemoveAt(rulesBox.SelectedIndex);
                    OpsisAutomationStudio.Save(conf); RefreshRules();
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Automation Studio", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }));
    }
}
