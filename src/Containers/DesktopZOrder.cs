using System.Runtime.InteropServices;
using System.Text;

namespace MotionDesk.Containers
{
    /// <summary>
    /// Z-order of the container windows (phase 6 part B): directly ABOVE the desktop (Progman / the WorkerW that holds the icons) and BELOW every normal window, so a container
    /// behaves like part of the desktop: apps cover it, "show desktop" (Win+D) shows it with the icons, and it is never a topmost window over your work. The plan is a pure
    /// function over the z-order chain (tested); <see cref="PlaceGroup"/> applies it, and the host re-applies it on a timer and after an Explorer restart.
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

        public static HashSet<IntPtr> DesktopWindows() => DesktopWindows(Chain());

        private static HashSet<IntPtr> DesktopWindows(IReadOnlyList<IntPtr> chain)
        {
            var shell = GetShellWindow();
            if (shell == IntPtr.Zero || GetWindowThreadProcessId(shell, out var shellPid) == 0) return [];
            var set = new HashSet<IntPtr>();
            var name = new StringBuilder(64);
            foreach (var handle in chain)
            {
                name.Clear();
                GetClassName(handle, name, name.Capacity);
                if (name.ToString() is not ("Progman" or "WorkerW") || !IsWindowVisible(handle)) continue;
                if (GetWindowThreadProcessId(handle, out var pid) != 0 && pid == shellPid &&
                    FindWindowEx(handle, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
                    set.Add(handle);
            }
            // Wallpaper WorkerW windows are not anchors. While the icon view is unavailable,
            // keep the real shell as our fallback and wait entirely if Explorer is restarting.
            if (set.Count == 0 && IsWindowVisible(shell) && chain.Contains(shell)) set.Add(shell);
            return set;
        }

        internal static IntPtr[] PlanBand(IReadOnlyList<IntPtr> chain, ISet<IntPtr> desktop, ISet<IntPtr> containers,
            ISet<IntPtr> glassPanes, out IntPtr after)
        {
            after = HwndTop;
            int anchor = -1;
            var band = new List<IntPtr>();
            for (int i = 0; i < chain.Count; i++)
            {
                if (anchor < 0 && desktop.Contains(chain[i])) anchor = i;
                if (!containers.Contains(chain[i])) continue;
                band.Add(chain[i]);
                if (i + 1 < chain.Count && glassPanes.Contains(chain[i + 1])) band.Add(chain[i + 1]);
            }
            if (anchor < 0 || band.Count == 0) return [];

            var movable = band.ToHashSet();
            int iAbove = anchor - 1;
            while (iAbove >= 0 && (movable.Contains(chain[iAbove]) || glassPanes.Contains(chain[iAbove]))) iAbove--;
            int alreadyInBand = 0;
            for (int i = iAbove + 1; i < anchor; i++)
                if (containers.Contains(chain[i])) alreadyInBand++;
            if (alreadyInBand == band.Count(containers.Contains)) return [];

            if (iAbove >= 0) after = chain[iAbove];
            return band.ToArray();
        }

        public static void PlaceGroup(ISet<IntPtr> containers)
        {
            if (containers.Count == 0) return;
            var chain = Chain();
            var desktop = DesktopWindows(chain);
            if (desktop.Count == 0) return;
            var glass = new HashSet<IntPtr>();
            var name = new StringBuilder(64);
            foreach (var handle in chain)
            {
                name.Clear();
                GetClassName(handle, name, name.Capacity);
                switch (name.ToString())
                {
                    case "OpsisGlassPane":
                        GetWindowThreadProcessId(handle, out uint pid);
                        if (pid == Environment.ProcessId) glass.Add(handle);
                        break;
                }
            }
            var band = PlanBand(chain, desktop, containers, glass, out var after);
            if (band.Length == 0) return;
            if (after != HwndTop && (GetWindowLong(after, -20) & 8) != 0)
                after = new IntPtr(-2); // HWND_NOTOPMOST: remain below the topmost band
            var placement = BeginDeferWindowPos(band.Length);
            const uint flags = 0x0001 | 0x0002 | 0x0010 | 0x0200; // preserve size, position, activation and visibility
            foreach (var handle in band)
            {
                if (placement == IntPtr.Zero) return;
                placement = DeferWindowPos(placement, handle, after, 0, 0, 0, 0, flags);
                after = handle;
            }
            if (placement != IntPtr.Zero) EndDeferWindowPos(placement);
        }

        [DllImport("user32.dll")] private static extern IntPtr GetTopWindow(IntPtr h);
        [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr h, int index);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string? title);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr h, uint cmd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll")] private static extern IntPtr BeginDeferWindowPos(int count);
        [DllImport("user32.dll")] private static extern IntPtr DeferWindowPos(IntPtr batch, IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] private static extern bool EndDeferWindowPos(IntPtr batch);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    }
}
