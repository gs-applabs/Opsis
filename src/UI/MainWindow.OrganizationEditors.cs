using System.Text.Json;
using MotionDesk.Containers;
using MotionDesk.Services;
using Opsis.Containers;

namespace MotionDesk.UI;

public sealed partial class MainWindow
{
    private sealed record RuleTarget(string Id, string Name)
    {
        public override string ToString() => Name;
    }

    private void EditSmartFolder(ContainerHost host, Container? container)
    {
        var original = container == null ? null : LayoutImport.SmartSpec(container);
        using var dialog = new MotionDesk.UI.AppDialog
        {
            Text = StudioText("Smart container", "Έξυπνο container"),
            StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(550, 480),
            AutoScroll = true, BackColor = UiTheme.PageBackground, ForeColor = UiTheme.TextPrimary, ShowInTaskbar = false
        };
        var form = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, AutoScroll = true, Padding = new Padding(14) };
        TextBox Field(string label, string value, bool multiline = false)
        {
            form.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(3, 8, 3, 2) });
            var field = new TextBox { Text = value, Width = 485, Multiline = multiline, Height = multiline ? 64 : 24 };
            form.Controls.Add(field);
            return field;
        }
        var title = Field(StudioText("Container name", "Όνομα container"), container?.Title ?? "");
        var sources = Field(StudioText("Local folders, one per line", "Τοπικοί φάκελοι, ένας ανά γραμμή"),
            string.Join(Environment.NewLine, original?.Folders ?? []), multiline: true);
        form.Controls.Add(StudioButton(StudioText("Add folder…", "Προσθήκη φακέλου…"), () =>
        {
            using var picker = new FolderBrowserDialog();
            if (picker.ShowDialog(dialog) != DialogResult.OK) return;
            sources.Lines = sources.Lines.Append(picker.SelectedPath).Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
        }, 180));
        var name = Field(StudioText("Words in filename", "Λέξεις στο όνομα του αρχείου"), original?.Name ?? "");
        var other = original?.Conditions.Where(condition => LayoutImport.String(condition, "cond") != "ext").ToArray() ?? [];
        var extensions = original?.Conditions.Where(condition => LayoutImport.String(condition, "cond") == "ext")
            .SelectMany(condition => LayoutImport.Array(condition, "value").Select(LayoutImport.AsString)) ?? [];
        var ext = Field(StudioText("Extensions separated by commas", "Καταλήξεις χωρισμένες με κόμμα"),
            string.Join(", ", extensions));
        var days = new NumericUpDown { Minimum = 0, Maximum = 3650, Value = Math.Clamp(original?.Days ?? 0, 0, 3650), Width = 90 };
        form.Controls.Add(new Label { Text = StudioText("Changed in the last N days (0 = any time)", "Τροποποιήθηκε τις τελευταίες Ν ημέρες (0 = οποτεδήποτε)"), AutoSize = true });
        form.Controls.Add(days);
        var limit = new NumericUpDown { Minimum = 1, Maximum = 500, Value = Math.Clamp(original?.Limit ?? 200, 1, 500), Width = 90 };
        form.Controls.Add(new Label { Text = StudioText("Maximum results", "Μέγιστος αριθμός αποτελεσμάτων"), AutoSize = true });
        form.Controls.Add(limit);
        var recursive = new CheckBox { Text = StudioText("Include subfolders", "Συμπερίληψη υποφακέλων"),
            Checked = original?.Recursive ?? false, AutoSize = true };
        var any = new CheckBox { Text = StudioText("Match any filter", "Αρκεί να ταιριάζει ένα φίλτρο"),
            Checked = original?.Any ?? false, AutoSize = true };
        form.Controls.Add(recursive);
        form.Controls.Add(any);
        var save = StudioButton(StudioText("Save", "Αποθήκευση"), () =>
        {
            var paths = sources.Lines.Select(line => line.Trim()).Where(line => line.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (title.Text.Trim().Length == 0 || paths.Length == 0 || paths.Any(path => !DesktopFiles.IsLocalPath(path) || !Directory.Exists(path)))
            {
                MessageBox.Show(dialog, StudioText("Enter a name and select existing local folders.", "Δώσε όνομα και διάλεξε υπάρχοντες τοπικούς φακέλους."), dialog.Text);
                return;
            }
            var suffixes = ext.Text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var conditions = suffixes.Length == 0 ? other : other.Append(JsonSerializer.SerializeToElement(new { cond = "ext", value = suffixes })).ToArray();
            var spec = new SmartFolderSpec(paths, recursive.Checked, any.Checked, name.Text.Trim(),
                days.Value == 0 ? null : (int)days.Value, (int)limit.Value, conditions);
            var id = container?.Id ?? host.NewContainerAtCursor("Data", title.Text.Trim());
            if (id.Length == 0) return;
            host.SetSmartFolder(id, title.Text.Trim(), spec);
            dialog.DialogResult = DialogResult.OK;
        }, emphasized: true);
        form.Controls.Add(save);
        dialog.Controls.Add(form);
        dialog.AcceptButton = save;
        StyleStudioDialog(dialog);
        if (dialog.ShowDialog(this) == DialogResult.OK) ShowDeskContainers();
    }

    private DesktopFileRule? EditFileRule(DesktopFileRule? rule, RuleTarget[] targets, Form owner)
    {
        using var dialog = new MotionDesk.UI.AppDialog
        {
            Text = StudioText("File rule", "Κανόνας αρχείων"),
            StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(615, 390),
            BackColor = UiTheme.PageBackground, ForeColor = UiTheme.TextPrimary, ShowInTaskbar = false
        };
        var form = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14),
            FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
        var name = new TextBox { Width = 535, Text = rule?.Name ?? "" };
        var target = new ComboBox { Width = 360, DropDownStyle = ComboBoxStyle.DropDownList };
        target.Items.AddRange(targets);
        target.SelectedIndex = Math.Max(0, Array.FindIndex(targets, item => item.Id == rule?.Target));
        var priority = new CheckBox { Text = StudioText("High priority", "Υψηλή προτεραιότητα"), Checked = rule?.Priority ?? false, AutoSize = true };
        var conditions = new TextBox { Multiline = true, Width = 550, Height = 145, ScrollBars = ScrollBars.Vertical,
            Text = rule == null ? "[{ \"cond\": \"ext\", \"value\": [\"pdf\"] }]" : JsonSerializer.Serialize(rule.Conditions, new JsonSerializerOptions { WriteIndented = true }) };
        form.Controls.Add(new Label { AutoSize = true, Text = StudioText("Rule name", "Όνομα κανόνα") });
        form.Controls.Add(name);
        form.Controls.Add(new Label { AutoSize = true, Text = StudioText("Destination", "Προορισμός") });
        form.Controls.Add(target);
        form.Controls.Add(priority);
        var insert = new ComboBox { Width = 360, DropDownStyle = ComboBoxStyle.DropDownList };
        var templates = new (string Label, string Condition)[]
        {
            (StudioText("File extension", "Κατάληξη αρχείου"), """{"cond":"ext","value":["pdf"]}"""),
            (StudioText("Filename contains", "Το όνομα περιέχει"), """{"cond":"name","value":{"op":"contains","value":"draft"}}"""),
            (StudioText("Category", "Κατηγορία"), """{"cond":"type","value":["images"]}"""),
            (StudioText("Exact filename", "Ακριβές όνομα"), """{"cond":"exactName","value":["report.pdf"]}"""),
            (StudioText("Wildcard", "Μοτίβο ονόματος"), """{"cond":"glob","value":"report-*.pdf"}"""),
            (StudioText("Shortcut target", "Στόχος συντόμευσης"), """{"cond":"shortcutTarget","value":{"op":"contains","value":"example.exe"}}"""),
            (StudioText("Folders only", "Μόνο φάκελοι"), """{"cond":"foldersOnly"}"""),
            (StudioText("Files only", "Μόνο αρχεία"), """{"cond":"filesOnly"}"""),
            (StudioText("Plain files", "Απλά αρχεία"), """{"cond":"plainFiles"}"""),
            (StudioText("Size in MB", "Μέγεθος σε MB"), """{"cond":"sizeMb","value":{"min":1,"max":20}}"""),
            (StudioText("Creation time", "Ώρα δημιουργίας"), """{"cond":"createdTime","value":{"from_min":540,"to_min":1020}}"""),
            (StudioText("Creation weekday", "Ημέρα δημιουργίας"), """{"cond":"createdWeekday","value":[0,1,2,3,4]}"""),
            (StudioText("Desktop origin", "Προέλευση επιφάνειας"), """{"cond":"origin","value":"userDesktop"}"""),
            (StudioText("Days without changes", "Ημέρες χωρίς αλλαγές"), """{"cond":"idleDays","value":{"min":30}}""")
        };
        form.Controls.Add(new Label { AutoSize = true, Text = StudioText("Conditions (all must match)", "Συνθήκες (πρέπει να ισχύουν όλες)") });
        insert.Items.Add(StudioText("Add a condition…", "Προσθήκη συνθήκης…"));
        insert.Items.AddRange(templates.Select(template => template.Label).ToArray());
        insert.SelectedIndex = 0;
        insert.SelectionChangeCommitted += (_, _) =>
        {
            if (insert.SelectedIndex < 1) return;
            try
            {
                var current = JsonSerializer.Deserialize<JsonElement[]>(conditions.Text) ?? [];
                var addition = JsonSerializer.Deserialize<JsonElement>(templates[insert.SelectedIndex - 1].Condition);
                conditions.Text = JsonSerializer.Serialize(current.Append(addition), new JsonSerializerOptions { WriteIndented = true });
            }
            catch (JsonException) { MessageBox.Show(dialog, StudioText("Correct the conditions JSON first.", "Διόρθωσε πρώτα το JSON των συνθηκών."), dialog.Text); }
            insert.SelectedIndex = 0;
        };
        form.Controls.Add(insert);
        form.Controls.Add(conditions);
        DesktopFileRule? result = null;
        var save = StudioButton(StudioText("Save", "Αποθήκευση"), () =>
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<JsonElement[]>(conditions.Text);
                if (name.Text.Trim().Length == 0 || target.SelectedItem is not RuleTarget destination ||
                    parsed is not { Length: > 0 } || !parsed.All(DesktopFiles.Supports))
                    throw new JsonException();
                result = new DesktopFileRule(name.Text.Trim(), destination.Id, parsed, priority.Checked, rule?.Enabled ?? true, Native: true);
                dialog.DialogResult = DialogResult.OK;
            }
            catch (JsonException)
            {
                MessageBox.Show(dialog, StudioText("Enter a name, destination and valid conditions.", "Συμπλήρωσε όνομα, προορισμό και έγκυρες συνθήκες."), dialog.Text);
            }
        }, emphasized: true);
        form.Controls.Add(save);
        dialog.Controls.Add(form);
        dialog.AcceptButton = save;
        StyleStudioDialog(dialog);
        return dialog.ShowDialog(owner) == DialogResult.OK ? result : null;
    }
}
