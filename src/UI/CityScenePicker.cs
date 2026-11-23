using System.Globalization;
using MotionDesk.Services;
using MotionDesk.Widgets;

namespace MotionDesk.UI;

internal sealed class CityScenePicker : MotionDesk.UI.AppDialog
{
    private sealed record Option(string Id, string Title, string SearchText, bool IsIsland = false)
    {
        public override string ToString() => Title;
    }

    internal string? SelectedId { get; private set; }

    internal CityScenePicker(string current)
    {
        Text = MainWindow.StudioText("Greek landscapes", "Ελληνικά τοπία");
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(490, 440);
        MinimumSize = new Size(420, 320);
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        BackColor = UiTheme.PageBackground;
        ForeColor = UiTheme.TextPrimary;

        var options = new List<Option>
        {
            new(LandmarkCatalog.Auto, LocalizationManager.T("Wallpaper.LandmarkAuto"), "auto αυτόματο"),
            new(LandmarkCatalog.Off, LocalizationManager.T("Wallpaper.LandmarkOff"), "off κανένα"),
            new(LandmarkCatalog.MountainLake, Describe(LandmarkCatalog.MountainLake), "lake mountain forest λίμνη βουνό δάσος")
        };
        bool greek = MainWindow.StudioGreek;
        options.AddRange(LandmarkCatalog.Scenes
            .OrderBy(city => greek ? city.Name : city.NameEn, StringComparer.Create(CultureInfo.GetCultureInfo(greek ? "el-GR" : "en-US"), true))
            .Select(city => new Option(city.Id, Describe(city), city.Name + " " + city.NameEn, city.ManualOnly)));
        options.AddRange(LandmarkCatalog.Landmarks
            .Select(item => new Option(item.Id, greek ? item.NameEl : item.Name, item.Name + " " + item.NameEl)));

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), RowCount = 4, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var search = new TextBox
        {
            Dock = DockStyle.Fill, BackColor = UiTheme.Surface, ForeColor = UiTheme.TextPrimary,
            PlaceholderText = MainWindow.StudioText("Search for a landscape", "Αναζήτηση τοπίου"),
            AccessibleName = MainWindow.StudioText("Search for a landscape", "Αναζήτηση τοπίου"), Font = UiTheme.FontBody
        };
        var islands = new CheckBox
        {
            Text = MainWindow.StudioText("Islands only", "Μόνο νησιά"), AutoSize = true, Font = UiTheme.FontBody
        };
        var results = new ListBox
        {
            Dock = DockStyle.Fill, BackColor = UiTheme.Surface, ForeColor = UiTheme.TextPrimary,
            IntegralHeight = false, Font = UiTheme.FontBody, Margin = new Padding(3, 10, 3, 9)
        };
        var use = MainWindow.StudioButton(MainWindow.StudioText("Use landscape", "Επιλογή τοπίου"), Accept);
        var cancel = MainWindow.StudioButton(LocalizationManager.T("Common.Close"), () => DialogResult = DialogResult.Cancel, 94);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        actions.Controls.Add(use);
        actions.Controls.Add(cancel);
        layout.Controls.Add(search, 0, 0);
        layout.Controls.Add(islands, 0, 1);
        layout.Controls.Add(results, 0, 2);
        layout.Controls.Add(actions, 0, 3);
        Controls.Add(layout);
        AcceptButton = use;
        CancelButton = cancel;

        results.DoubleClick += (_, _) => Accept();
        results.SelectedIndexChanged += (_, _) => use.Enabled = results.SelectedItem is Option;
        search.TextChanged += (_, _) => Filter(results.SelectedItem is Option item ? item.Id : current);
        islands.CheckedChanged += (_, _) => Filter(results.SelectedItem is Option item ? item.Id : current);
        search.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Down || results.Items.Count == 0) return;
            results.Focus();
            e.SuppressKeyPress = true;
        };
        MainWindow.StyleStudioDialog(this);
        Filter(current);

        void Accept()
        {
            if (results.SelectedItem is not Option item) return;
            SelectedId = item.Id;
            DialogResult = DialogResult.OK;
        }

        void Filter(string selected)
        {
            var term = search.Text.Trim();
            var compare = CultureInfo.GetCultureInfo("el-GR").CompareInfo;
            const CompareOptions flags = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
            var filtered = options.Where(item => (!islands.Checked || item.IsIsland) && (term.Length == 0 ||
                compare.IndexOf(item.SearchText, term, flags) >= 0 || compare.IndexOf(item.Title, term, flags) >= 0)).ToArray();
            results.BeginUpdate();
            results.Items.Clear();
            results.Items.AddRange(filtered);
            results.SelectedIndex = filtered.Length == 0 ? -1 : Math.Max(0, Array.FindIndex(filtered, item => item.Id == selected));
            results.EndUpdate();
            use.Enabled = results.SelectedItem is Option;
        }
    }

    internal static string Describe(string id)
    {
        if (id == LandmarkCatalog.MountainLake) return MainWindow.StudioText("Mountain lake · photorealistic", "Ορεινή λίμνη · φωτορεαλιστική");
        if (id is LandmarkCatalog.Auto or LandmarkCatalog.Off)
            return LocalizationManager.T(id == LandmarkCatalog.Auto ? "Wallpaper.LandmarkAuto" : "Wallpaper.LandmarkOff");
        bool greek = MainWindow.StudioGreek;
        var city = LandmarkCatalog.Scenes.FirstOrDefault(item => item.Id == id);
        if (city != null) return Describe(city);
        var landmark = LandmarkCatalog.Landmarks.FirstOrDefault(item => item.Id == id);
        return landmark == null ? LocalizationManager.T("Wallpaper.LandmarkAuto") : greek ? landmark.NameEl : landmark.Name;
    }

    private static string Describe(LandmarkCatalog.CityScene city)
    {
        var kind = city.ManualOnly ? MainWindow.StudioText("island", "νησί")
            : city.File == null ? MainWindow.StudioText("illustration", "εικονογράφηση")
            : MainWindow.StudioText("photo", "φωτογραφία");
        return $"{(MainWindow.StudioGreek ? city.Name : city.NameEn)} · {kind}";
    }
}
