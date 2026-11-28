using System;
using System.IO;

namespace MotionDesk.Services
{
    // Κεντρικό logging με rotation (v2.0 handoff, Φάση 1 §S) — 5 αρχεία × 1MB στο
    // %AppData%\Opsis\logs\. Ξεχωριστό από το ήδη υπάρχον crash.log (Program.cs) το οποίο μένει ως
    // έχει (ήδη γνωστό, διαβάζεται από τον χρήστη/προηγούμενες συνεδρίες) — αυτό εδώ είναι η νέα,
    // γενικότερη υποδομή που ζήτησε ρητά το §S, συμπεριλαμβανομένου handler για
    // TaskScheduler.UnobservedTaskException που δεν υπήρχε καθόλου πριν.
    public static class RotatingLogger
    {
        private const long MaxBytesPerFile = 1 * 1024 * 1024;
        private const int MaxFiles = 5;
        private static readonly object Gate = new();
        // Only explicitly recurring diagnostics use throttling. Crash, safety,
        // security, user actions and recovery/save failures keep normal logging.
        internal enum RecurringSource { Activity, Glass, Experience, WallpaperAccent, ContainerTimer }
        internal sealed class RepeatBudget
        {
            private bool _written;
            private long _lastAt;
            private int _suppressed;
            internal bool Accept(long now, out int suppressed)
            {
                suppressed = 0;
                if (_written && now - _lastAt < 60_000)
                {
                    if (_suppressed < int.MaxValue) _suppressed++;
                    return false;
                }
                suppressed = _suppressed;
                _suppressed = 0; _lastAt = now; _written = true;
                return true;
            }
        }
        private static readonly RepeatBudget[] RepeatBudgets =
            Enum.GetValues<RecurringSource>().Select(_ => new RepeatBudget()).ToArray();

        internal static void WriteRecurring(RecurringSource source, string message)
        {
            try
            {
                lock (Gate)
                {
                    if (!RepeatBudgets[(int)source].Accept(Environment.TickCount64, out int suppressed)) return;
                    if (message.Length > 4096) message = message[..4096] + " [truncated]";
                    if (suppressed != 0) message += $" [suppressed {suppressed} recurring messages since previous entry]";
                    Write(message);
                }
            }
            catch (Exception) { /* diagnostics must not break a timer */ }
        }
        private static string LogDir => Path.Combine(AppPaths.AppDataFolder, "logs");
        private static string CurrentPath => Path.Combine(LogDir, "opsis.log");

        public static void Write(string message)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(LogDir);
                    RotateIfNeeded();
                    File.AppendAllText(CurrentPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}");
                }
            }
            catch (Exception) { /* diagnostics must never break the app */ }
        }

        public static void WriteException(string context, Exception ex) => Write($"{context}: {ex}");

        private static void RotateIfNeeded()
        {
            var info = new FileInfo(CurrentPath);
            if (!info.Exists || info.Length < MaxBytesPerFile) return;

            // opsis.log.4 -> deleted, opsis.log.3 -> .4, ..., opsis.log -> .1
            string oldest = Path.Combine(LogDir, $"opsis.log.{MaxFiles - 1}");
            if (File.Exists(oldest)) File.Delete(oldest);
            for (int i = MaxFiles - 2; i >= 1; i--)
            {
                string src = Path.Combine(LogDir, $"opsis.log.{i}");
                string dst = Path.Combine(LogDir, $"opsis.log.{i + 1}");
                if (File.Exists(src)) File.Move(src, dst);
            }
            File.Move(CurrentPath, Path.Combine(LogDir, "opsis.log.1"));
        }

        // Εγγράφεται ΜΙΑ φορά στο Main(), πριν από οτιδήποτε άλλο — πιάνει κατηγορίες σφαλμάτων που
        // το ήδη υπάρχον Application.ThreadException/AppDomain.UnhandledException ΔΕΝ πιάνουν: ένα
        // Task που αποτυγχάνει χωρίς ποτέ να γίνει awaited/παρατηρηθεί το exception του.
        public static void HookUnobservedTaskExceptions()
        {
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                WriteException("UnobservedTaskException", e.Exception);
                e.SetObserved();
            };
        }
    }
}
