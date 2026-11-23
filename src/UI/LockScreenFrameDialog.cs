using System.Drawing.Imaging;
using MotionDesk.Services;

namespace MotionDesk.UI;

internal sealed class LockScreenFrameDialog : MotionDesk.UI.AppDialog
{
    private readonly Image previewImage;
    private readonly Image stillImage;
    private readonly PictureBox preview;
    private readonly NumericUpDown index;

    internal int SelectedFrame => (int)index.Value - 1;

    internal LockScreenFrameDialog(string path, int frames)
    {
        Text = MainWindow.StudioText("GIF frame for lock screen", "Καρέ GIF για κλείδωμα");
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimumSize = new Size(510, 360);
        ClientSize = new Size(700, 480);
        MinimizeBox = false;
        MaximizeBox = false;
        BackColor = UiTheme.PageBackground;
        ForeColor = UiTheme.TextPrimary;
        previewImage = Image.FromFile(path);
        stillImage = (Image)previewImage.Clone();

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(18) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label
        {
            AutoSize = true, Margin = new Padding(0, 0, 0, 12), Font = UiTheme.FontBody,
            Text = MainWindow.StudioText("Choose the still frame Windows will use.", "Διάλεξε το στατικό καρέ για τα Windows.")
        }, 0, 0);
        preview = new PictureBox { Dock = DockStyle.Fill, BackColor = UiTheme.Surface, SizeMode = PictureBoxSizeMode.Zoom };
        layout.Controls.Add(preview, 0, 1);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        actions.Controls.Add(new Label { AutoSize = true, Text = MainWindow.StudioText("Frame", "Καρέ"), Margin = new Padding(0, 10, 4, 0) });
        index = new NumericUpDown { Minimum = 1, Maximum = Math.Max(1, frames), Width = 65, Margin = new Padding(0, 7, 5, 0) };
        var play = new CheckBox { AutoSize = true, Text = MainWindow.StudioText("Animate preview", "Κινούμενη προεπισκόπηση"),
            Margin = new Padding(8, 10, 8, 0), Enabled = frames > 1 };
        index.ValueChanged += (_, _) => RefreshFrame();
        play.CheckedChanged += (_, _) => { index.Enabled = !play.Checked; RefreshFrame(); };
        actions.Controls.Add(index);
        actions.Controls.Add(play);
        var apply = MainWindow.StudioButton(MainWindow.StudioText("Use frame", "Χρήση καρέ"), () => DialogResult = DialogResult.OK, 106, true);
        var close = MainWindow.StudioButton(LocalizationManager.T("Common.Close"), () => DialogResult = DialogResult.Cancel, 90);
        layout.Controls.Add(actions, 0, 2);
        var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        footer.Controls.Add(apply);
        footer.Controls.Add(close);
        layout.Controls.Add(footer, 0, 3);
        Controls.Add(layout);
        AcceptButton = apply;
        CancelButton = close;
        MainWindow.StyleStudioDialog(this);
        RefreshFrame();

        void RefreshFrame()
        {
            if (play.Checked) preview.Image = previewImage;
            else
            {
                if (frames > 1) stillImage.SelectActiveFrame(FrameDimension.Time, SelectedFrame);
                double scale = Math.Min(1, 900d / Math.Max(stillImage.Width, stillImage.Height));
                var bitmap = new Bitmap(stillImage, new Size(Math.Max(1, (int)(stillImage.Width * scale)),
                    Math.Max(1, (int)(stillImage.Height * scale))));
                preview.Image = bitmap;
                stillFrame?.Dispose();
                stillFrame = bitmap;
            }
        }
    }

    private Bitmap? stillFrame;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (preview != null) preview.Image = null;
            stillFrame?.Dispose();
            previewImage?.Dispose();
            stillImage?.Dispose();
        }
        base.Dispose(disposing);
    }
}
