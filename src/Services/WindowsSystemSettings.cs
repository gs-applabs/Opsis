using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace MotionDesk.Services
{
    // Ζητήθηκε ρητά "ρύθμιση για διαφάνεια παραθύρων των Windows και γρήγορη εκκίνηση στον πίνακα
    // ελέγχου" — αυτά είναι οι ίδιες ρυθμίσεις που εμφανίζονται στα πραγματικά Windows Settings
    // (Personalization > Colors > "Transparency effects" και Control Panel > Power Options >
    // "Choose what the power buttons do" > "Turn on fast startup"), όχι δικές μας ρυθμίσεις της
    // εφαρμογής — διαβάζονται/γράφονται απευθείας στο μητρώο των Windows αντί να αποθηκεύονται
    // στο AppSettings.json, ώστε να παραμένουν συγχρονισμένες με την πραγματική κατάσταση του
    // συστήματος ακόμα κι αν αλλάξουν από αλλού (π.χ. τα ίδια τα Windows Settings).
    public static class WindowsSystemSettings
    {
        [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        private static readonly SemaphoreSlim ExplorerRestart = new(1, 1);

        public static async Task RestartExplorerAsync()
        {
            if (!await ExplorerRestart.WaitAsync(0)) return;
            try
            {
                IntPtr shell = GetShellWindow();
                if (shell == IntPtr.Zero || GetWindowThreadProcessId(shell, out uint pid) == 0)
                    throw new InvalidOperationException(LocalizationManager.T("Win.ExplorerUnavailable"));
                using var explorer = Process.GetProcessById((int)pid);
                using var current = Process.GetCurrentProcess();
                if (!string.Equals(explorer.ProcessName, "explorer", StringComparison.OrdinalIgnoreCase) ||
                    explorer.SessionId != current.SessionId || GetShellWindow() != shell)
                    throw new InvalidOperationException(LocalizationManager.T("Win.ExplorerUnavailable"));

                explorer.Kill();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await explorer.WaitForExitAsync(timeout.Token);
                await Task.Delay(1500);
                if (GetShellWindow() == IntPtr.Zero)
                {
                    string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
                    using var replacement = Process.Start(new ProcessStartInfo(path) { UseShellExecute = false });
                }
            }
            finally
            {
                ExplorerRestart.Release();
            }
        }

        // HKCU — καμία αυξημένη άδεια δεν χρειάζεται, ισχύει άμεσα χωρίς restart.
        public static bool GetTransparencyEffectsEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                var value = key?.GetValue("EnableTransparency");
                return value is not int i || i != 0;
            }
            catch { return true; }
        }

        public static bool SetTransparencyEffectsEnabled(bool enabled)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                key?.SetValue("EnableTransparency", enabled ? 1 : 0, RegistryValueKind.DWord);
                // Η εγγραφή στο μητρώο από μόνη της δεν αρκεί — DWM/Explorer/taskbar διαβάζουν αυτή
                // την τιμή μόνο όταν λάβουν το ίδιο broadcast που στέλνει το πραγματικό Windows Settings
                // όταν αλλάζει αυτός ο διακόπτης, αλλιώς η αλλαγή "πιάνει" μόνο μετά από logoff/restart.
                BroadcastSettingChange("ImmersiveColorSet");
                // Συμπλήρωμα του registry+broadcast: ζητά απευθείας από τον DWM να κάνει blur-behind
                // την ίδια την Taskbar (ίδια τεχνική με το TranslucentTB, χωρίς καμία μορφή injection —
                // βλ. GlassBlur.SetTaskbarTranslucency) — πιο αξιόπιστα ορατό από το να περιμένουμε τον
                // Explorer να εφαρμόσει μόνος του το registry/broadcast.
                MotionDesk.UI.GlassBlur.SetTaskbarTranslucency(enabled);
                return true;
            }
            catch { return false; }
        }

        private const int HWND_BROADCAST = 0xFFFF;
        private const uint WM_SETTINGCHANGE = 0x001A;

        // ΔΙΟΡΘΩΣΗ πραγματικού bug ("κολλάει η εφαρμογή όταν κάνω κλικ στο checkbox διαφάνειας"):
        // το SendMessageTimeout μπλοκάρει το thread του καλούντος μέχρι ΚΑΘΕ top-level παράθυρο στο
        // σύστημα να απαντήσει ή να λήξει το timeout (ήταν 2000ms/παράθυρο) — με δεκάδες ανοιχτά
        // παράθυρα (browser, IDE, κ.λπ.) αυτό μπλόκαρε ορατά το UI thread του Opsis για
        // δευτερόλεπτα. Το SendNotifyMessage στέλνει το ίδιο broadcast αλλά επιστρέφει αμέσως χωρίς
        // να περιμένει απάντηση — ο ενδεδειγμένος, μη-μπλοκαριστικός τρόπος για broadcast ρυθμίσεων.
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SendNotifyMessage(IntPtr hWnd, uint msg, UIntPtr wParam, string lParam);

        private static void BroadcastSettingChange(string setting)
        {
            try { SendNotifyMessage((IntPtr)HWND_BROADCAST, WM_SETTINGCHANGE, UIntPtr.Zero, setting); }
            catch { }
        }

        // HKLM — χρειάζεται αυξημένα δικαιώματα (admin)· αν η εφαρμογή δεν τρέχει elevated η
        // εγγραφή θα αποτύχει με UnauthorizedAccessException/SecurityException, το οποίο
        // πιάνεται εδώ και αναφέρεται στον καλούντα ως false (η σελίδα Ρυθμίσεων δείχνει
        // κατάλληλο μήνυμα αντί να αποτυγχάνει σιωπηλά ή να ρίχνει exception στο UI thread).
        public static bool GetFastStartupEnabled()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Power");
                var value = key?.GetValue("HiberbootEnabled");
                return value is not int i || i != 0;
            }
            catch { return true; }
        }

        public static bool TrySetFastStartupEnabled(bool enabled)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Power", writable: true);
                if (key == null) return false;
                key.SetValue("HiberbootEnabled", enabled ? 1 : 0, RegistryValueKind.DWord);
                return true;
            }
            catch { return false; }
        }
    }
}
