namespace MotionDesk.UI;

internal sealed class StudioAdvancedSection : FlowLayoutPanel
{
    private readonly Button header;
    private readonly FlowLayoutPanel body;
    private readonly string title = OpsisStrings.T("ui.advanced");
    private bool userExpanded;
    private bool searchExpanded;

    public StudioAdvancedSection()
    {
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        FlowDirection = FlowDirection.TopDown;
        WrapContents = false;
        Margin = new Padding(0, 5, 0, 7);
        header = MainWindow.StudioButton("", Toggle, width: 204);
        header.TextAlign = ContentAlignment.MiddleLeft;
        header.Padding = new Padding(9, 0, 5, 0);
        header.Margin = new Padding(0, 0, 0, 4);
        header.AccessibleName = title;
        body = new FlowLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, Margin = new Padding(0, 4, 0, 0), Visible = false
        };
        Controls.Add(header);
        Controls.Add(body);
        RefreshExpansion();
    }

    internal bool Expanded => userExpanded || searchExpanded;

    internal bool ContainsMatch(string query) => PageSearch.Matches(PageSearch.TextOf(body), query);

    internal void AddControl(Control control) => body.Controls.Add(control);

    internal void SearchExpansion(bool expanded)
    {
        if (searchExpanded == expanded) return;
        searchExpanded = expanded;
        RefreshExpansion();
    }

    internal bool VerifyHeaderButton()
    {
        bool original = Expanded;
        int initialHeight = Height;
        header.PerformClick();
        bool shown = Expanded != original && body.Visible == Expanded;
        Parent?.PerformLayout();
        bool resized = original ? Height < initialHeight : Height > initialHeight;
        header.PerformClick();
        return shown && resized && Expanded == original && body.Visible == original;
    }

    private void Toggle()
    {
        userExpanded = !Expanded;
        searchExpanded = false;
        RefreshExpansion();
    }

    private void RefreshExpansion()
    {
        SuspendLayout();
        body.Visible = Expanded;
        header.Text = (Expanded ? "▾  " : "▸  ") + title;
        header.AccessibleDescription = Expanded
            ? MainWindow.StudioText("Expanded", "Ανοιχτό") : MainWindow.StudioText("Collapsed", "Κλειστό");
        ResumeLayout(performLayout: true);
    }
}
