using MotionDesk.Services;

namespace MotionDesk.UI;

public sealed partial class MainWindow
{
    private sealed record DesktopChoice(Guid Id, string Label)
    {
        public override string ToString() => Label;
    }

    private void BuildWorkspaceCard(Control panel)
    {
        var card = AddCardSection(panel, StudioText("Virtual desktops", "Εικονικές επιφάνειες"));
        AddText(card, StudioText("Set the wallpaper on the current Windows desktop, then save it to that workspace.",
            "Ρύθμισε το wallpaper στην τρέχουσα επιφάνεια των Windows και αποθήκευσέ το για αυτήν."));
        var status = new Label { AutoSize = true, Font = UiTheme.FontBody, ForeColor = UiTheme.TextSecondary };
        var name = new TextBox { Width = 285, MaxLength = 80,
            PlaceholderText = StudioText("Workspace name", "Όνομα workspace") };
        var desktops = new ComboBox { Width = 285, DropDownStyle = ComboBoxStyle.DropDownList };
        Guid displayed = Guid.Empty;
        void Update()
        {
            Guid current = VirtualWorkspaces.CurrentDesktop();
            var ids = VirtualWorkspaces.DesktopIds();
            int index = Array.IndexOf(ids, current);
            status.Text = current == Guid.Empty ? StudioText("Open a window on the active desktop.", "Άνοιξε ένα παράθυρο στην ενεργή επιφάνεια.") :
                (VirtualWorkspaces.Name(current) ?? StudioText("Desktop", "Επιφάνεια") + (index >= 0 ? $" {index + 1}" : "")) +
                (VirtualWorkspaces.HasWallpaper(current) ? StudioText(" · saved wallpaper", " · αποθηκευμένο wallpaper") : "");
            if (current != displayed) { name.Text = VirtualWorkspaces.Name(current) ?? ""; displayed = current; }
            Guid selection = (desktops.SelectedItem as DesktopChoice)?.Id ?? current;
            if (desktops.DroppedDown) return;
            var known = ids.Select((id, i) => new DesktopChoice(id, VirtualWorkspaces.Name(id) ??
                $"{StudioText("Desktop", "Επιφάνεια")} {i + 1}")).ToArray();
            if (!known.SequenceEqual(desktops.Items.Cast<DesktopChoice>()))
            {
                desktops.Items.Clear();
                desktops.Items.AddRange(known);
                if (known.Length > 0) desktops.SelectedIndex = Math.Max(0, Array.FindIndex(known, item => item.Id == selection));
            }
        }
        bool observing = false;
        void ObserveVisible()
        {
            bool wanted = card.Visible && !card.IsDisposed;
            if (wanted == observing) return;
            observing = wanted;
            if (wanted) { VirtualWorkspaces.BeginObservation(); Update(); }
            else VirtualWorkspaces.EndObservation();
        }
        VirtualWorkspaces.Changed += Update;
        card.VisibleChanged += (_, _) => ObserveVisible();
        card.Disposed += (_, _) =>
        {
            VirtualWorkspaces.Changed -= Update;
            if (observing) { observing = false; VirtualWorkspaces.EndObservation(); }
        };
        ObserveVisible();
        Update();
        card.Controls.Add(status);
        card.Controls.Add(name);
        card.Controls.Add(desktops);
        Action Save(Action action) => () => ApplySetting(card.Text, action);
        var wallpapers = new FlowLayoutPanel { AutoSize = true, WrapContents = true };
        wallpapers.Controls.Add(StudioButton(StudioText("Save for this workspace", "Αποθήκευση για αυτήν την επιφάνεια"), Save(() =>
        {
            if (!VirtualWorkspaces.BindCurrent(name.Text))
                throw new InvalidOperationException(StudioText("Windows did not identify the active desktop.", "Τα Windows δεν έδωσαν την ενεργή επιφάνεια."));
        }), 257, true));
        wallpapers.Controls.Add(StudioButton(StudioText("Save as default", "Αποθήκευση ως προεπιλογή"), Save(VirtualWorkspaces.SaveDefault), 205));
        wallpapers.Controls.Add(StudioButton(StudioText("Remove assignment", "Κατάργηση αντιστοίχισης"), Save(() => VirtualWorkspaces.Forget(VirtualWorkspaces.CurrentDesktop())), 205));
        card.Controls.Add(wallpapers);
        var navigation = new FlowLayoutPanel { AutoSize = true, WrapContents = true };
        navigation.Controls.Add(StudioButton(StudioText("Switch to selected", "Μετάβαση στην επιλεγμένη"), () =>
        {
            if (desktops.SelectedItem is DesktopChoice choice) VirtualWorkspaces.SwitchTo(choice.Id);
        }, 195));
        navigation.Controls.Add(StudioButton(StudioText("Task View", "Προβολή εργασιών"), VirtualWorkspaces.OpenTaskView, 155));
        navigation.Controls.Add(StudioButton(StudioText("New desktop", "Νέα επιφάνεια"), VirtualWorkspaces.CreateDesktop, 155));
        navigation.Controls.Add(StudioButton(StudioText("Close current", "Κλείσιμο τρέχουσας"), VirtualWorkspaces.CloseDesktop, 155));
        navigation.Controls.Add(StudioButton("←", () => VirtualWorkspaces.Switch(false), 47));
        navigation.Controls.Add(StudioButton("→", () => VirtualWorkspaces.Switch(true), 47));
        card.Controls.Add(navigation);
    }
}
