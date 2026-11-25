using MotionDesk.Services;

namespace MotionDesk.UI;

internal sealed class ProfileMonitorMappingDialog : Form
{
    internal Dictionary<string,string> Mapping { get; } = new(StringComparer.OrdinalIgnoreCase);
    internal ProfileMonitorMappingDialog(IReadOnlyList<ProfileMonitor> imported)
    {
        Text = MainWindow.StudioText("Import profile — map monitors", "Εισαγωγή προφίλ — αντιστοίχιση οθονών");
        StartPosition = FormStartPosition.CenterParent; ClientSize=new Size(700, Math.Clamp(190+imported.Count*56,280,700));
        BackColor=UiTheme.PageBackground; ForeColor=UiTheme.TextPrimary;
        var connected=MonitorTopologyService.Current();
        var layout=new TableLayoutPanel{ Dock=DockStyle.Fill,AutoScroll=true,ColumnCount=2,RowCount=0,Padding=new Padding(15) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
        var choices=new List<(string Key,ComboBox Select)>();
        foreach (var source in imported)
        {
            var label=new Label { Text=$"{source.DeviceName}  ({source.Width}×{source.Height})",AutoSize=true,ForeColor=UiTheme.TextPrimary };
            var combo=new ComboBox { DropDownStyle=ComboBoxStyle.DropDownList,Width=280 };
            foreach(var screen in connected) combo.Items.Add($"{screen.DeviceName}  {screen.Bounds.Width}×{screen.Bounds.Height}");
            var match=MonitorTopologyService.Match(source.Id,source.DeviceName,source.Width,source.Height);
            combo.SelectedIndex=Math.Max(0,Array.FindIndex(connected.ToArray(),x=>x.Id==match?.Id));
            layout.Controls.Add(label,0,layout.RowCount);layout.Controls.Add(combo,1,layout.RowCount);
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,52));layout.RowCount++;
            choices.Add((source.Id,combo));
        }
        var info=new Label { Text=MainWindow.StudioText("Only the profile copy will be imported. Current workspace remains unchanged.",
            "Θα εισαχθεί μόνο αντίγραφο του προφίλ. Η τρέχουσα διάταξη δεν θα αλλάξει."), AutoSize=true };
        layout.Controls.Add(info,0,layout.RowCount);layout.SetColumnSpan(info,2);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,44));layout.RowCount++;
        var done=new Button { Text=MainWindow.StudioText("Import", "Εισαγωγή"),Width=120,Height=34 };
        var cancel=new Button { Text=MainWindow.StudioText("Cancel", "Ακύρωση"),Width=120,Height=34 };
        var buttons=new FlowLayoutPanel { Dock=DockStyle.Bottom,Height=52,FlowDirection=FlowDirection.RightToLeft,Padding=new Padding(8) };
        buttons.Controls.Add(done);buttons.Controls.Add(cancel);
        done.Click+=(_,_)=> {
            Mapping.Clear();foreach(var (key,picker) in choices) {
                if(picker.SelectedIndex<0 || picker.SelectedIndex>=connected.Count) return;
                Mapping[key]=connected[picker.SelectedIndex].Id;
            }
            DialogResult=DialogResult.OK;
        };
        cancel.Click+=(_,_)=>DialogResult=DialogResult.Cancel;
        Controls.Add(layout); Controls.Add(buttons);AcceptButton=done;CancelButton=cancel;
    }
}
