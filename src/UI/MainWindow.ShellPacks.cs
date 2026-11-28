using MotionDesk.Services;

namespace MotionDesk.UI;

public sealed partial class MainWindow
{
    private void ApplyShellPackWithConfirmation(string manifest, bool cursor)
    {
        try
        {
            var assets = ShellAssetPack.Preview(manifest, cursor);
            string summary = string.Join(Environment.NewLine, assets.Select(a => a.Role + " → " + Path.GetFileName(a.Source)));
            if (MessageBox.Show(this, summary + Environment.NewLine + StudioText("Apply these roles? Original registry values will be retained.", "Εφαρμογή αυτών των ρόλων; Θα διατηρηθούν οι αρχικές τιμές."),
                "Opsis", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            ShellAssetPack.Apply(manifest, cursor, assets);
            ShowPersonalization();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message + Environment.NewLine + "If partially applied, use Restore role pack.", "Opsis", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private void AddShellPackControls(Control card, bool cursor)
    {
        AddText(card, StudioText("Import a local declarative role manifest and its sibling image files. Assets are copied into Opsis storage; the original Windows values can be restored.",
            "Εισαγωγή τοπικού manifest ρόλων με τα αντίστοιχα αρχεία εικόνων. Τα αρχεία αντιγράφονται στον χώρο του OPSIS και οι αρχικές τιμές Windows μπορούν να επανέλθουν."));
        AddButtonGrid(card,
            (StudioText("Apply role pack", "Εφαρμογή πακέτου ρόλων"), (_, _) =>
            {
                using var picker = new OpenFileDialog { Filter = "Opsis role manifest (*.json)|*.json", CheckFileExists = true };
                if (picker.ShowDialog(this) != DialogResult.OK) return;
                ApplyShellPackWithConfirmation(picker.FileName, cursor);
            }),
            (StudioText("Restore role pack", "Επαναφορά πακέτου ρόλων"), (_, _) =>
            {
                if (MessageBox.Show(this, StudioText("Restore the values saved before applying the pack?", "Επαναφορά των τιμών πριν την εφαρμογή του πακέτου;"),
                    "Opsis", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                try { ShellAssetPack.Restore(cursor); ShowPersonalization(); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Opsis", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            }),
            (StudioText("Manifest format", "Μορφή manifest"), (_, _) =>
            {
                string sample = cursor
                    ? """{"schema":1,"kind":"cursor-pack","roles":{"Arrow":"arrow.cur","Hand":"hand.ani"}}"""
                    : """{"schema":1,"kind":"icon-pack","roles":{"ThisPC":"computer.ico","RecycleBinEmpty":"empty.ico","RecycleBinFull":"full.ico"}}""";
                ShowTextDialog("Opsis", StudioText("Local pack manifest", "Τοπικό manifest πακέτου"), sample +
                    Environment.NewLine + StudioText("Save as JSON beside the image files; only supported roles are accepted.", "Αποθήκευση ως JSON δίπλα στα αρχεία εικόνας· γίνονται δεκτοί μόνο οι υποστηριζόμενοι ρόλοι."));
            }));
    }
}
