namespace MotionDesk.UI;

internal static class ThemeManager { public static object Current { get; } = new(); }
internal static partial class UiTheme
{
    internal static Color PageBackground { get; set; }
    internal static Color Surface { get; set; }
    internal static Color SurfaceHover { get; set; }
}

public sealed partial class MainWindow
{
    private sealed record WindowSkinColors(Color Accent, Color Surface, Color Hover, Color Muted, Color Text);
    private WindowSkinColors CurrentSkinColors { get; set; } = null!;
    private static WindowSkinColors ColorsFromTheme(object _) =>
        new(Color.FromArgb(0, 205, 238), UiTheme.Surface, UiTheme.SurfaceHover, Color.Gray, Color.White);

    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        using (var intro = new Label { Text = "A page introduction" })
        using (var card = new Panel { Tag = new SectionMark("Virtual desktops", null) })
        using (var body = new FlowLayoutPanel())
        using (var title = new Label { Text = "Virtual desktops" })
        using (var action = new Button { Text = "Save" })
        {
            card.Controls.Add(body);
            body.Controls.Add(title);
            body.Controls.Add(action);
            var section = PageCards.Split([intro, card], "Lead", out string? pageIntro).Single();
            Require(pageIntro == "A page introduction" && section.Title == "Virtual desktops" &&
                section.Main.SequenceEqual([action]), "Card titles and actions");
        }

        Directory.CreateDirectory("layout-check");
        foreach (bool dark in new[] { true, false })
        {
            var backdrop = dark ? Color.FromArgb(19, 19, 21) : Color.FromArgb(246, 246, 248);
            UiTheme.PageBackground = backdrop;
            UiTheme.Surface = dark ? Color.FromArgb(31, 31, 35) : Color.FromArgb(222, 226, 232);
            UiTheme.SurfaceHover = dark ? Color.FromArgb(46, 46, 54) : Color.FromArgb(208, 216, 225);
            using var form = new MainWindow
            {
                BackColor = backdrop,
                CurrentSkinColors = new(Color.FromArgb(0, 205, 238), UiTheme.Surface, UiTheme.SurfaceHover,
                    Color.Gray, dark ? Color.White : Color.Black),
                StartPosition = FormStartPosition.Manual, Location = new Point(20, 20), ClientSize = new Size(760, 250)
            };
            var grid = new FlowLayoutPanel { AutoSize = false, Bounds = new Rectangle(16, 16, 725, 200),
                WrapContents = true, BackColor = backdrop };
            form.Controls.Add(grid);
            var quick = new IconButton("Widgets", "Άνοιγμα γκαλερί widgets", 350);
            var longCaption = new IconButton("Save",
                "Αποθήκευση της διαμόρφωσης της επιφάνειας εργασίας ως νέο προφίλ με όλες τις ρυθμίσεις", 350);
            grid.Controls.Add(quick);
            grid.Controls.Add(longCaption);
            var desktop = new HoverButton(keyboard: true)
            {
                Text = "Αποθήκευση για αυτήν την επιφάνεια", Width = 260, Font = new Font("Segoe UI", 10f),
                BaseColor = form.CurrentSkinColors.Accent, BackColor = form.CurrentSkinColors.Accent,
                ForeColor = Color.Black, Margin = new Padding(0, 3, 8, 3)
            };
            desktop.Height = CaptionHeight(desktop.Text, desktop.Width, 32, desktop.Font);
            grid.Controls.Add(desktop);
            form.Show();
            form.Activate();
            Application.DoEvents();

            Require(quick.Width == 350 && longCaption.Left > quick.Left && longCaption.Top == quick.Top, "Two-column order");
            Require(longCaption.Height > quick.Height, "Long Greek caption gets more vertical space");
            Require(quick.Region == null && longCaption.Region == null && desktop.Region == null, "Owner-drawn corners have no native region clips");
            int clicks = 0;
            desktop.Click += (_, _) => clicks++;
            desktop.PerformClick();
            Require(clicks == 1, "Pill routes the click once");
            longCaption.Focus();
            using var bitmap = new Bitmap(form.ClientSize.Width, form.ClientSize.Height);
            form.DrawToBitmap(bitmap, form.ClientRectangle);
            bitmap.Save(Path.Combine("layout-check", dark ? "dark.png" : "light.png"));
        }
        Console.WriteLine("Card order, two-column rows, Greek caption height, corners and click routing verified.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
