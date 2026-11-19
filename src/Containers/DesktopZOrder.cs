using System.Runtime.InteropServices;
using System.Text;

namespace MotionDesk.Containers
{
    /// <summary>
    /// Z-order of the container windows (phase 6 part B): directly ABOVE the desktop (Progman / the WorkerW that holds the icons) and BELOW every normal window, so a container
    /// behaves like part of the desktop: apps cover it, "show desktop" (Win+D) shows it with the icons, and it is never a topmost window over your work. The plan is a pure
    /// function over the z-order chain (tested); <see cref="Place"/> applies it, and the host re-applies it on a timer and after an Explorer restart.
    /// </summary>
    internal static class DesktopZOrder
    {
        public static readonly IntPtr HwndTop = IntPtr.Zero;

        /// <summary>
        /// <paramref name="chainTopToBottom"/>: top-level windows from the top of the z-order to the bottom. <paramref name="desktopWindows"/>: Progman / WorkerW. Returns the window
        /// our window must be inserted AFTER (so that it ends up just above the highest desktop window), or null when it must go to the very top.
        /// </summary>
        public static IntPtr? InsertAfter(IReadOnlyList<IntPtr> chainTopToBottom, ISet<IntPtr> desktopWindows, IntPtr self)
        {
            IntPtr? before = null;
            foreach (var h in chainTopToBottom)
            {
                if (h == self) continue;
                if (desktopWindows.Contains(h)) return before;      // the first (highest) desktop window: stay right above it
                before = h;
            }
            return null;        // no desktop window found: leave the window where it is (caller keeps the current position)
        }

        public static List<IntPtr> Chain()
        {
            var list = new List<IntPtr>();
            for (var h = GetTopWindow(IntPtr.Zero); h != IntPtr.Zero; h = GetWindow(h, 2 /*GW_HWNDNEXT*/)) list.Add(h);
            return list;
        }

        public static HashSet<IntPtr> DesktopWindows()
        {
            var set = new HashSet<IntPtr>(); var sb = new StringBuilder(64);
            foreach (var h in Chain())
            {
                sb.Clear(); GetClassName(h, sb, 64); var c = sb.ToString();
                if (c == "Progman" || c == "WorkerW") set.Add(h);
            }
            return set;
        }

        /// <summary>
        /// Pure: is <paramref name="self"/> already "just above the desktop"? Windows of ours (other containers, their glass panes) between it and the desktop do not count: before 2.6.0 only a desktop window
        /// DIRECTLY below counted as "in place", so with two containers (or with glass on: the pane sits right below the container) every container was re-inserted every second, the panes
        /// were re-seated by their own timer, and the windows flickered / swapped places all the time.
        /// </summary>
        public static bool IsInPlace(IReadOnlyList<IntPtr> chainTopToBottom, ISet<IntPtr> desktopWindows, IntPtr self, Func<IntPtr, bool> isOurs)
        {
            int i = -1;
            for (int k = 0; k < chainTopToBottom.Count; k++) if (chainTopToBottom[k] == self) { i = k; break; }
            if (i < 0) return false;
            for (int j = i + 1; j < chainTopToBottom.Count; j++)
            {
                var h = chainTopToBottom[j];
                if (desktopWindows.Contains(h)) return true;
                if (!isOurs(h)) return false;
            }
            return false;
        }

        /// <summary>Moves <paramref name="hwnd"/> to just above the desktop. Returns false when no desktop window exists (Explorer is restarting): the caller tries again later.</summary>
        public static bool Place(IntPtr hwnd, ISet<IntPtr>? siblings = null)
        {
            var desktop = DesktopWindows();
            if (desktop.Count == 0) return false;
            var chain = Chain();
            // already in place? (cheap check: the window right below us in the chain is a desktop window)
            var sb = new StringBuilder(32);
            bool Ours(IntPtr h) { if (siblings != null && siblings.Contains(h)) return true; sb.Clear(); GetClassName(h, sb, 32); return sb.ToString() == "OpsisGlassPane"; }
            if (IsInPlace(chain, desktop, hwnd, Ours)) return true;
            var after = InsertAfter(chain, desktop, hwnd);
            const uint Flags = 0x0001 | 0x0002 | 0x0010 | 0x0200 | 0x0040;   // NOSIZE | NOMOVE | NOACTIVATE | NOOWNERZORDER | SHOWWINDOW
            return SetWindowPos(hwnd, after ?? HwndTop, 0, 0, 0, 0, Flags);
        }

        [DllImport("user32.dll")] private static extern IntPtr GetTopWindow(IntPtr h);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr h, uint cmd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    }
}
