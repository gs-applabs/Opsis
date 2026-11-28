using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using MotionDesk.Services;

namespace MotionDesk.Widgets
{
    // Message-only παράθυρο που καταγράφει παγκόσμιες συντομεύσεις πληκτρολογίου (RegisterHotKey)
    // και σηκώνει ένα .NET event, ώστε να μη χρειάζεται να "κρύβουμε" hotkey λογική μέσα σε φόρμες UI.
    public sealed class HotkeyManager : NativeWindow, IDisposable
    {
        private const int WM_HOTKEY = 0x0312;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_SHIFT = 0x0004;

        private int _nextId = 9000;
        private bool _disposed;
        private readonly System.Collections.Generic.Dictionary<int, Action> _handlers = new();

        public HotkeyManager()
        {
            // Handoff Φάση 5: πραγματικό message-only window (HWND_MESSAGE, -3) αντί για ένα απλό
            // αόρατο top-level παράθυρο — δεν εμφανίζεται πουθενά σε window enumeration/Z-order, ίδιο
            // μοτίβο με το ήδη υπάρχον ExplorerRestartWatcher (WallpaperInterop.cs).
            CreateHandle(new CreateParams { Parent = new IntPtr(-3) });
        }

        public bool RegisterCtrlAlt(char key, Action onPressed) => Register(MOD_CONTROL | MOD_ALT, (uint)key, onPressed);

        // Υπερφόρτωση για πλήκτρα χωρίς απλή χαρακτήρα-αναπαράσταση (βελάκια κ.λπ.) — το Keys enum
        // έχει ήδη τους σωστούς Win32 virtual-key κωδικούς, απλή μετατροπή.
        public bool RegisterCtrlAlt(Keys key, Action onPressed) => Register(MOD_CONTROL | MOD_ALT, (uint)key, onPressed);

        // ΣΗΜΑΝΤΙΚΟ (βρέθηκε κατά τη δοκιμή του keyboard zone navigation): το απλό Ctrl+Alt+βελάκι
        // είναι ΗΔΗ δεσμευμένο συστημικά σε πολλά μηχανήματα από τον οδηγό γραφικών Intel/NVIDIA/AMD
        // (περιστροφή οθόνης) — το δικό μας RegisterHotKey απέτυχε σιωπηλά (ERROR_HOTKEY_ALREADY_
        // REGISTERED) χωρίς το combo να κάνει ΤΙΠΟΤΑ, επιβεβαιωμένο με αυτοτελή δοκιμή RegisterHotKey
        // σε αυτό το μηχάνημα. Το Ctrl+Alt+Shift+βελάκι είναι πολύ λιγότερο πιθανό να συγκρουστεί.
        public bool RegisterCtrlAltShift(Keys key, Action onPressed) => Register(MOD_CONTROL | MOD_ALT | MOD_SHIFT, (uint)key, onPressed);

        // Χωρίς Alt: χρησιμοποιείται για "μετακίνηση εστίασης" (ZoneSnapEngine.FocusAdjacentZone) —
        // δεν συγκρούεται με το Ctrl+Alt+βελάκι των οδηγών γραφικών (βλ. σχόλιο παραπάνω) αφού δεν
        // περιλαμβάνει καθόλου Alt, ούτε με το Ctrl+Alt+Shift+βελάκι ("μετακίνηση παραθύρου") αφού
        // εδώ δεν πατιέται Alt.
        public bool RegisterCtrlShift(Keys key, Action onPressed) => Register(MOD_CONTROL | MOD_SHIFT, (uint)key, onPressed);

        // Μόνο Ctrl (π.χ. Ctrl+` για το SpotSearch των DeskContainers).
        public bool RegisterCtrl(Keys key, Action onPressed) => Register(MOD_CONTROL, (uint)key, onPressed);

        // Handoff Φάση 5 ("conflict handling: hotkey ήδη πιασμένο -> UI μήνυμα"): πριν, η επιστρεφόμενη
        // τιμή του RegisterHotKey αγνοούνταν εντελώς — μια σύγκρουση (π.χ. ο οδηγός γραφικών, βλ.
        // σχόλιο Ctrl+Alt+βελάκι παραπάνω) περνούσε τελείως απαρατήρητη. Τώρα η αποτυχία καταγράφεται
        // (RotatingLogger) ΚΑΙ επιστρέφεται στον caller, που μπορεί να την αγνοήσει (συμβατό προς τα
        // πίσω) ή να αντιδράσει. Σκόπιμα ΧΩΡΙΣ δικό της MessageBox εδώ — μια σιωπηλή εναλλακτική
        // συντόμευση (όπως ήδη έγινε για το Ctrl+Alt+βελάκι -> Ctrl+Alt+Shift+βελάκι) είναι λιγότερο
        // ενοχλητική από ένα διάλογο σε κάθε εκκίνηση σε μηχανήματα με γνωστή σύγκρουση οδηγού.
        private bool Register(uint modifiers, uint vk, Action onPressed)
        {
            if (_disposed || Handle == IntPtr.Zero) return false;
            int id = _nextId++;
            bool ok = RegisterHotKey(Handle, id, modifiers, vk);
            if (ok) { _handlers[id] = onPressed; }
            else
            {
                int err = Marshal.GetLastWin32Error();
                RotatingLogger.Write($"HotkeyManager: RegisterHotKey failed (already taken) for modifiers=0x{modifiers:X} vk=0x{vk:X} (Win32 error {err})");
                lock (FailedCombos)
                {
                    string label = Describe(modifiers, vk);
                    FailedCombos.Add(label);
                    if (!ConflictHistory.Contains(label, StringComparer.OrdinalIgnoreCase) && ConflictHistory.Count < 100)
                        ConflictHistory.Add(label);
                }
            }
            return ok;
        }

        // 2.4.6.1: οι συντομεύσεις που ΔΕΝ καταχωρήθηκαν (πιασμένες από άλλη εφαρμογή/οδηγό) — το tray τις
        // δείχνει μία φορά με balloon ώστε ο χρήστης να ξέρει ότι δεν θα δουλέψουν.
        public static readonly System.Collections.Generic.List<string> FailedCombos = new();
        private static readonly System.Collections.Generic.List<string> ConflictHistory = new();
        private static int _probeId = 20000;

        /// <summary>A snapshot of failed registrations, not cleared when the tray balloon is shown.</summary>
        public static string[] GetConflictCatalog()
        {
            lock (FailedCombos) return ConflictHistory.ToArray();
        }

        /// <summary>Probe the exact combination without installing a hotkey handler or stealing a key.</summary>
        public static (bool Available, int Win32Error) Probe(Keys key, bool ctrl, bool alt, bool shift)
        {
            uint modifiers = (ctrl ? MOD_CONTROL : 0u) | (alt ? MOD_ALT : 0u) | (shift ? MOD_SHIFT : 0u);
            if (modifiers == 0 || key is Keys.None or Keys.ControlKey or Keys.Menu or Keys.ShiftKey)
                return (false, 87);
            // RegisterHotKey(NULL, ...) registers for only the calling UI thread; unregister
            // immediately, regardless of the result. Nothing remains installed.
            int id = System.Threading.Interlocked.Increment(ref _probeId);
            bool ok = RegisterHotKey(IntPtr.Zero, id, modifiers, (uint)key);
            int error = ok ? 0 : Marshal.GetLastWin32Error();
            if (ok) UnregisterHotKey(IntPtr.Zero, id);
            return (ok, error);
        }

        public static string TakeFailureSummary()
        {
            lock (FailedCombos)
            {
                if (FailedCombos.Count == 0) return "";
                var s = string.Join(", ", FailedCombos);
                FailedCombos.Clear();
                return s;
            }
        }

        private static string Describe(uint modifiers, uint vk)
        {
            var parts = new System.Collections.Generic.List<string>();
            if ((modifiers & MOD_CONTROL) != 0) parts.Add("Ctrl");
            if ((modifiers & MOD_ALT) != 0) parts.Add("Alt");
            if ((modifiers & MOD_SHIFT) != 0) parts.Add("Shift");
            parts.Add(((Keys)vk).ToString());
            return string.Join("+", parts);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY && _handlers.TryGetValue(m.WParam.ToInt32(), out var handler))
            {
                if (!_disposed)
                {
                    try { handler.Invoke(); }
                    catch (Exception ex) { try { RotatingLogger.Write("Hotkey action failed: " + ex.Message); } catch { } }
                }
            }
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            var handle = Handle;
            try
            {
                if (handle != IntPtr.Zero)
                    foreach (var id in _handlers.Keys) UnregisterHotKey(handle, id);
            }
            finally
            {
                _handlers.Clear();
                if (Handle != IntPtr.Zero) DestroyHandle();
            }
        }

        [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    }
}
