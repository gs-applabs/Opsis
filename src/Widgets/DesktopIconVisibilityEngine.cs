using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace MotionDesk.Widgets
{
    // Quick Hide parks desktop list entries off screen and journals their original coordinates.
    // Explorer keeps ownership of the desktop items and their underlying files.
    public sealed class DesktopIconVisibilityEngine
    {
        private static DesktopIconVisibilityEngine? _instance;
        public static DesktopIconVisibilityEngine Instance => _instance ??= new DesktopIconVisibilityEngine();

        // Resolve both the known folder and the desktop namespace item's localized label.
        internal static readonly Guid ThisPcId = new("0AC0837C-BBF8-452A-850D-79D08E667CA7");
        internal static readonly Guid ProfileId = new("5E6C858F-0E22-4760-9AFE-EA3317B67173");
        internal static readonly Guid ControlPanelId = new("82A74AEB-AEB4-465C-A014-D097EE346D63");
        internal static readonly Guid RecycleBinId = new("B7534046-3ECB-4C18-BE4E-64CD4CB7D6AC");

        internal static readonly string[] ThisPcFallback = { "This PC", "Αυτός ο υπολογιστής", "Ο Υπολογιστής μου", "Υπολογιστής μου", "Υπολογιστής", "My Computer", "Computer" };
        internal static readonly string[] ControlPanelFallback = { "Control Panel", "Πίνακας Ελέγχου" };
        internal static readonly string[] RecycleBinFallback = { "Recycle Bin", "Κάδος Ανακύκλωσης" };

        internal static HashSet<string> SpecialIconNames(Guid id)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Add(string? name) { if (!string.IsNullOrWhiteSpace(name)) names.Add(name.Trim()); }
            Add(ShellDisplayName(id));
            var (classId, fallback) =
                id == ThisPcId ? ("20D04FE0-3AEA-1069-A2D8-08002B30309D", ThisPcFallback)
                : id == ProfileId ? ("59031A47-3F72-44A7-89C5-5595FE6B30EE", new[] { Environment.UserName, "User’s Files", "User's Files", "Αρχεία χρήστη" })
                : id == ControlPanelId ? ("5399E694-6CE5-4D6C-8FCE-1D8870FDCBA0", ControlPanelFallback)
                : ("645FF040-5081-101B-9F08-00AA002F954E", RecycleBinFallback);
            Add(ShellDisplayNameByParsing("::{" + classId + "}"));
            if (id == ProfileId) Add(AccountDisplayName());
            foreach (string name in fallback) Add(name);
            return names;
        }

        internal static HashSet<string> BuildWhitelist(Services.AppSettings? settings = null)
        {
            var a = settings ?? Services.AppSettings.Load();
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (a.QuickHideKeepThisPc) set.UnionWith(SpecialIconNames(ThisPcId));
            if (a.QuickHideKeepUserFolder) set.UnionWith(SpecialIconNames(ProfileId));
            if (a.QuickHideKeepControlPanel) set.UnionWith(SpecialIconNames(ControlPanelId));
            if (a.QuickHideKeepRecycleBin) set.UnionWith(SpecialIconNames(RecycleBinId));
            foreach (var name in a.QuickHideExtraKept ?? []) if (!string.IsNullOrWhiteSpace(name)) set.Add(name.Trim());
            return set;
        }

        public static List<string> ListDesktopItemNames()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                WithDesktopList((process, list, count) =>
                {
                    for (int i = 0; i < count; i++)
                    {
                        string name = GetItemTextRemote(process, list, i).Trim();
                        if (name.Length > 0) names.Add(name);
                    }
                });
            }
            catch (Exception) { }
            if (names.Count == 0) names.UnionWith(LoadState().Positions.Select(x => x.Name));
            return names.OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        private static string? ShellDisplayName(Guid knownFolder)
        {
            IntPtr pidl = IntPtr.Zero;
            try
            {
                if (SHGetKnownFolderIDList(ref knownFolder, 0, IntPtr.Zero, out pidl) != 0 || pidl == IntPtr.Zero) return null;
                var info = new SHFILEINFOW();
                SHGetFileInfoW(pidl, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFOW>(), 0x000000008 /*SHGFI_PIDL*/ | 0x000000200 /*SHGFI_DISPLAYNAME*/);
                return string.IsNullOrWhiteSpace(info.szDisplayName) ? null : info.szDisplayName;
            }
            catch (Exception) { return null; }
            finally { if (pidl != IntPtr.Zero) Marshal.FreeCoTaskMem(pidl); }
        }

        private static string? ShellDisplayNameByParsing(string parsingName)
        {
            IntPtr pidl = IntPtr.Zero;
            try
            {
                if (SHParseDisplayName(parsingName, IntPtr.Zero, out pidl, 0, out _) != 0 || pidl == IntPtr.Zero) return null;
                var info = new SHFILEINFOW();
                SHGetFileInfoW(pidl, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFOW>(), 0x000000008 | 0x000000200);
                return string.IsNullOrWhiteSpace(info.szDisplayName) ? null : info.szDisplayName;
            }
            catch (Exception) { return null; }
            finally { if (pidl != IntPtr.Zero) Marshal.FreeCoTaskMem(pidl); }
        }

        private static string? AccountDisplayName()
        {
            try
            {
                var sb = new StringBuilder(256); uint n = (uint)sb.Capacity;
                return GetUserNameEx(3 /*NameDisplay*/, sb, ref n) ? sb.ToString() : null;
            }
            catch (Exception) { return null; }
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHParseDisplayName(string name, IntPtr bind, out IntPtr pidl, uint sfgaoIn, out uint sfgaoOut);
        [DllImport("secur32.dll", CharSet = CharSet.Unicode)] private static extern bool GetUserNameEx(int format, StringBuilder name, ref uint size);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEINFOW
        {
            public IntPtr hIcon; public int iIcon; public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
        }
        [DllImport("shell32.dll")] private static extern int SHGetKnownFolderIDList(ref Guid rfid, uint flags, IntPtr token, out IntPtr ppidl);
        [DllImport("shell32.dll", EntryPoint = "SHGetFileInfoW", CharSet = CharSet.Unicode)] private static extern IntPtr SHGetFileInfoW(IntPtr pidl, uint attrs, ref SHFILEINFOW info, uint cb, uint flags);

        private IntPtr _hookHandle = IntPtr.Zero;
        private LowLevelMouseProc? _hookProc;
        private uint _lastDownTime;
        private Point _lastDownPos;
        private bool _hidden;
        private bool _originalRecoveryDone;
        private System.Threading.SynchronizationContext? _uiContext;

        // ---- originals of icons that were dropped into a container (2.6.0): ONE mechanism (the same remote list-view calls as the quick-hide), its own state file ----
        // Hiding moves the desktop icon out of the screen and remembers where it was; restoring puts it back BY NAME. The state file lives only while icons are hidden, so every start of
        // Opsis (after an exit, a crash, a kill or an upgrade) and the command "Opsis.exe --restore-desktop-icons" can bring them back.
        private static string OriginalsStatePath => Path.Combine(Services.AppPaths.AppDataFolder, "desktop-originals-hidden.json");

        private static IEnumerable<string> IconNames(string path)
        {
            var n = Path.GetFileName(path.TrimEnd('\\', '/')); if (string.IsNullOrEmpty(n)) yield break;
            yield return n; var w = Path.GetFileNameWithoutExtension(n); if (!string.IsNullOrEmpty(w) && w != n) yield return w;
        }

        private static bool IsOnDesktop(string path)
        {
            try
            {
                var dir = Path.GetDirectoryName(Path.GetFullPath(path.TrimEnd('\\', '/'))) ?? "";
                return string.Equals(dir, Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), StringComparison.OrdinalIgnoreCase)
                    || string.Equals(dir, Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception) { return false; }
        }

        private static List<SavedIconPos> LoadOriginals() { try { return File.Exists(OriginalsStatePath) ? JsonSerializer.Deserialize<List<SavedIconPos>>(File.ReadAllText(OriginalsStatePath)) ?? new() : new(); } catch (Exception) { return new(); } }

        private static void WithDesktopList(Action<IntPtr, IntPtr, int> work)
        {
            IntPtr list = FindDesktopListView(); if (list == IntPtr.Zero) return;
            GetWindowThreadProcessId(list, out uint pid);
            IntPtr hp = OpenProcess(PROCESS_VM_OPERATION | PROCESS_VM_READ | PROCESS_VM_WRITE, false, pid); if (hp == IntPtr.Zero) return;
            try { work(hp, list, SendMessage(list, LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero).ToInt32()); } finally { CloseHandle(hp); }
        }

        [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hWnd, ref Point p);
        /// <summary>Screen position of the middle of the desktop icon of <paramref name="path"/> (for tests that use the real mouse), or null when it is not on the desktop right now.</summary>
        public static Point? DesktopIconCenter(string path)
        {
            Point? found = null; var want = IconNames(path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            try
            {
                WithDesktopList((hp, list, count) =>
                {
                    for (int i = 0; i < count; i++)
                    {
                        if (!want.Contains(GetItemTextRemote(hp, list, i))) continue;
                        var pos = GetItemPositionRemote(hp, list, i); var p = new Point(pos.X + 38, pos.Y + 32);
                        if (ClientToScreen(list, ref p)) found = p; return;
                    }
                });
            }
            catch (Exception) { }
            return found;
        }

        /// <summary>True when the point is over the desktop icon list (not over another window).</summary>
        public static bool IsDesktopListAt(Point screenPos) { try { var l = FindDesktopListView(); return l != IntPtr.Zero && WindowFromPoint(screenPos) == l; } catch (Exception) { return false; } }

        /// <summary>Full paths of the desktop icons that are selected right now (rubber-band or Ctrl+click), matched by name in the user and the public desktop folders.</summary>
        public static List<string> SelectedDesktopPaths()
        {
            var names = new List<string>();
            try { WithDesktopList((hp, list, count) => { for (int i = 0; i < count; i++) if ((SendMessage(list, 0x102C, (IntPtr)i, (IntPtr)2).ToInt32() & 2) != 0) names.Add(GetItemTextRemote(hp, list, i)); }); } catch (Exception) { }
            var dirs = new[] { Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory) };
            var result = new List<string>();
            foreach (var n in names)
            {
                foreach (var d in dirs)
                {
                    try { var hit = Directory.EnumerateFileSystemEntries(d).FirstOrDefault(f => string.Equals(Path.GetFileName(f), n, StringComparison.OrdinalIgnoreCase) || string.Equals(Path.GetFileNameWithoutExtension(f), n, StringComparison.OrdinalIgnoreCase)); if (hit != null) { result.Add(hit); break; } } catch (Exception) { }
                }
            }
            return result;
        }

        /// <summary>Hides the desktop icons of the given files (only files that really are on the desktop). Returns how many were hidden.</summary>
        public static int HideOriginals(IEnumerable<string> paths)
        {
            var want = paths.Where(IsOnDesktop).SelectMany(IconNames).ToHashSet(StringComparer.OrdinalIgnoreCase); if (want.Count == 0) return 0;
            int hidden = 0;
            try
            {
                var saved = LoadOriginals();
                WithDesktopList((hp, list, count) =>
                {
                    for (int i = 0; i < count; i++)
                    {
                        var name = GetItemTextRemote(hp, list, i);
                        if (!want.Contains(name) || saved.Any(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))) continue;
                        var pos = GetItemPositionRemote(hp, list, i);
                        if (pos.X <= -5000) continue;                                         // already hidden by the quick-hide: do not overwrite its saved place
                        saved.Add(new SavedIconPos { Index = i, Name = name, X = pos.X, Y = pos.Y });
                        SetItemPositionRemote(list, i, -10000, -10000); hidden++;
                    }
                });
                if (hidden > 0) { Directory.CreateDirectory(Path.GetDirectoryName(OriginalsStatePath)!); MotionDesk.Services.AtomicFile.WriteAllText(OriginalsStatePath, JsonSerializer.Serialize(saved)); }
            }
            catch (Exception) { }
            return hidden;
        }

        /// <summary>Puts hidden originals back (all of them, or only those of <paramref name="onlyPath"/>). Safe to call at any time; returns how many came back.</summary>
        public static int RestoreOriginals(string? onlyPath = null) =>
            RestoreOriginalsByName(onlyPath == null ? null : IconNames(onlyPath).ToHashSet(StringComparer.OrdinalIgnoreCase));

        internal static int RestoreOriginalsForPaths(IEnumerable<string> paths) =>
            RestoreOriginalsByName(paths.Where(IsOnDesktop).SelectMany(IconNames).ToHashSet(StringComparer.OrdinalIgnoreCase));

        private static int RestoreOriginalsByName(HashSet<string>? only)
        {
            if (only is { Count: 0 }) return 0;
            var saved = LoadOriginals(); if (saved.Count == 0) return 0;
            int back = 0;
            try
            {
                WithDesktopList((hp, list, count) =>
                {
                    for (int i = 0; i < count; i++)
                    {
                        var name = GetItemTextRemote(hp, list, i);
                        var sv = saved.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                        if (sv == null || (only != null && !only.Contains(name))) continue;
                        if (SetItemPositionRemote(list, i, sv.X, sv.Y)) { saved.Remove(sv); back++; }
                    }
                });
                if (saved.Count == 0) { try { File.Delete(OriginalsStatePath); } catch (Exception) { } }
                else MotionDesk.Services.AtomicFile.WriteAllText(OriginalsStatePath, JsonSerializer.Serialize(saved));
            }
            catch (Exception) { }
            return back;
        }

        private static string StateFilePath => Path.Combine(Services.AppPaths.AppDataFolder, "desktopicons_hidden_state.json");

        public void Start()
        {
            if (_hookHandle != IntPtr.Zero) return;
            _uiContext = System.Threading.SynchronizationContext.Current is System.Windows.Forms.WindowsFormsSynchronizationContext ui ? ui : null;
            try
            {
                _hookProc = HookProc;
                using var curModule = System.Diagnostics.Process.GetCurrentProcess().MainModule;
                IntPtr hMod = curModule != null ? GetModuleHandle(curModule.ModuleName) : IntPtr.Zero;
                _hookHandle = SetWindowsHookEx(WH_MOUSE_LL, _hookProc, hMod, 0);
            }
            catch { _hookHandle = IntPtr.Zero; }

            // Αν η εφαρμογή τερματίστηκε/κράσαρε ενώ τα εικονίδια ήταν κρυμμένα (το state file υπάρχει
            // ΜΟΝΟ όσο είναι κρυμμένα), τα εικονίδια έμεναν για πάντα εκτός οθόνης (-10000,-10000).
            // Τα επαναφέρουμε στην εκκίνηση.
            if (File.Exists(StateFilePath)) RestoreQuickHide();
            if (File.Exists(OriginalsStatePath) && !_originalRecoveryDone) RestoreOriginals();
            _originalRecoveryDone = true;            // originals hidden by a container that never got to give them back (crash, kill, upgrade)
        }

        /// <summary>Rescue / test helpers (2.6.0 beta). DumpPositions is read-only: one line "name TAB x TAB y" per desktop icon.</summary>
        public static List<string> DumpPositions()
        {
            var lines = new List<string>();
            IntPtr list = FindDesktopListView(); if (list == IntPtr.Zero) return lines;
            GetWindowThreadProcessId(list, out uint pid);
            IntPtr hp = OpenProcess(PROCESS_VM_OPERATION | PROCESS_VM_READ | PROCESS_VM_WRITE, false, pid); if (hp == IntPtr.Zero) return lines;
            try
            {
                int count = SendMessage(list, LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero).ToInt32();
                for (int i = 0; i < count; i++) { var p = GetItemPositionRemote(hp, list, i); lines.Add(GetItemTextRemote(hp, list, i) + "\t" + p.X + "\t" + p.Y); }
            }
            finally { CloseHandle(hp); }
            return lines;
        }

        // Snapshot BEFORE a bulk operation, never every animation frame. This is a separate undo
        // file from quick-hide and from hidden-originals recovery to avoid overwriting either.
        private static string BulkUndoPath => Path.Combine(Services.AppPaths.AppDataFolder, "desktop-icon-move-undo.json");
        public static int CapturePositionsForUndo()
        {
            var snapshot = new List<SavedIconPos>();
            try
            {
                WithDesktopList((process,list,count) =>
                {
                    if (HasAutoArrange(list)) return; // Explorer owns automatic layout.
                    for (int i=0;i<count;i++)
                    {
                        var name=GetItemTextRemote(process,list,i).Trim();
                        var point=ReadItemPosition(process,list,i);
                        if (name.Length==0 || point == null || IsParked(point.Value)) continue;
                        snapshot.Add(new SavedIconPos { Name=name,Index=i,X=point.Value.X,Y=point.Value.Y });
                    }
                });
                if(snapshot.Count < 2) return 0;
                Services.AtomicFile.WriteAllText(BulkUndoPath,JsonSerializer.Serialize(snapshot));
                return snapshot.Count;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return 0; }
        }

        public static int UndoLastBulkMove()
        {
            if(!File.Exists(BulkUndoPath)) return 0;
            List<SavedIconPos> saved;
            try { saved=JsonSerializer.Deserialize<List<SavedIconPos>>(Services.AtomicFile.ReadAllText(BulkUndoPath)) ?? new(); }
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or JsonException) { return 0; }
            if(saved.Count==0 || saved.Count>10000) return 0;
            var previous=saved.GroupBy(x=>x.Name,StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x=>x.Key,x=>x.ToList(),StringComparer.OrdinalIgnoreCase);
            var restored=0;
            try
            {
                WithDesktopList((hp,list,count)=>
                {
                    if(HasAutoArrange(list)) return;
                    for(int i=0;i<count;i++)
                    {
                        var name=GetItemTextRemote(hp,list,i).Trim();
                        if(!previous.TryGetValue(name,out var candidates) || candidates.Count==0) continue;
                        var old=candidates.Find(x=>x.Index==i) ?? candidates[0];
                        if(ReadItemPosition(hp,list,i) is not Point current || IsParked(current)) continue;
                        if(SetItemPositionRemote(list,i,old.X,old.Y)) { restored++; candidates.Remove(old); }
                    }
                });
                if(restored>0 && restored==saved.Count) File.Delete(BulkUndoPath);
            }
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException) { }
            return restored;
        }

        /// <summary>Test only: hides the icons exactly like the double-click gesture does (state file written), without any hook.</summary>
        public void HideForTest() { var l = FindDesktopListView(); if (l != IntPtr.Zero && !_hidden) ToggleDesktopIcons(l); }

        // Επαναφέρει τα κρυμμένα εικονίδια (αν υπάρχουν) — καλείται στην έξοδο και στην εκκίνηση.
        public void RestoreIfHidden()
        {
            RestoreQuickHide();
            try { RestoreOriginals(); } catch (Exception) { }
        }

        private void RestoreQuickHide()
        {
            if (!_hidden && !File.Exists(StateFilePath)) return;
            try
            {
                IntPtr list = FindDesktopListView();
                if (list == IntPtr.Zero) return;
                GetWindowThreadProcessId(list, out uint pid);
                IntPtr hProcess = OpenProcess(PROCESS_VM_OPERATION | PROCESS_VM_READ | PROCESS_VM_WRITE, false, pid);
                if (hProcess == IntPtr.Zero) return;
                try { RestorePositions(hProcess, list); }
                finally { CloseHandle(hProcess); }
                _hidden = false;
                MotionDesk.Containers.ContainerHost.Current?.SetAllHidden(false);
            }
            catch { }
        }

        private static IntPtr FindDesktopListView()
        {
            IntPtr defView = IntPtr.Zero;
            IntPtr progman = FindWindow("Progman", null);
            if (progman != IntPtr.Zero) defView = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (defView == IntPtr.Zero)
            {
                IntPtr w = IntPtr.Zero;
                while ((w = FindWindowEx(IntPtr.Zero, w, "WorkerW", null)) != IntPtr.Zero)
                {
                    defView = FindWindowEx(w, IntPtr.Zero, "SHELLDLL_DefView", null);
                    if (defView != IntPtr.Zero) break;
                }
            }
            return defView == IntPtr.Zero ? IntPtr.Zero : FindWindowEx(defView, IntPtr.Zero, "SysListView32", null);
        }

        private static void RestorePositions(IntPtr process, IntPtr list)
        {
            var session = LoadState();
            var saved = session.Positions.GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.OrderBy(x => x.Index).ToList(), StringComparer.OrdinalIgnoreCase);
            var originals = LoadOriginals().Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            bool incomplete = false;
            int count = SendMessage(list, LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero).ToInt32();
            for (int i = 0; i < count; i++)
            {
                string name = GetItemTextRemote(process, list, i).Trim();
                if (originals.Contains(name) || !saved.TryGetValue(name, out var positions) || positions.Count == 0) continue;
                if (ReadItemPosition(process, list, i) is not Point position) { incomplete = true; continue; }
                if (!IsParked(position)) continue;
                var previous = positions.Find(p => p.Index == i) ?? positions[0];
                if (SetItemPositionRemote(list, i, previous.X, previous.Y)) positions.Remove(previous);
                else incomplete = true;
            }
            if (incomplete) throw new IOException("Some desktop icon positions could not be restored.");
            if (session.RestoreAutoArrange) SetAutoArrange(list, true);
            File.Delete(StateFilePath);
        }

        private static bool IsParked(Point p) => p.X < -5000 && p.Y < -5000;

        public void ApplyKeepSettings()
        {
            if (!_hidden) return;
            try { WithDesktopList((process, list, count) => ApplyQuickHide(process, list, count, LoadState())); }
            catch (Exception) { }
        }

        private static void ApplyQuickHide(IntPtr process, IntPtr list, int count, QuickHideSession session)
        {
            var keep = BuildWhitelist();
            var originals = LoadOriginals().Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var savedByName = session.Positions.ToLookup(x => x.Name, StringComparer.OrdinalIgnoreCase);
            var changes = new List<(int Index, Point Target)>();
            for (int i = 0; i < count; i++)
            {
                string name = GetItemTextRemote(process, list, i).Trim();
                if (name.Length == 0 || originals.Contains(name) || ReadItemPosition(process, list, i) is not Point pos) continue;
                var previous = savedByName[name];
                var saved = previous.FirstOrDefault(x => x.Index == i);
                if (saved == null && previous.Count() == 1) saved = previous.First();
                if (keep.Contains(name))
                {
                    if (saved != null && IsParked(pos))
                        changes.Add((i, new Point(saved.X, saved.Y)));
                    continue;
                }
                if (IsParked(pos)) continue;
                if (saved == null || saved.Index != i || saved.X != pos.X || saved.Y != pos.Y)
                {
                    if (saved?.Index == i) session.Positions.Remove(saved);
                    session.Positions.Add(new SavedIconPos { Index = i, Name = name, X = pos.X, Y = pos.Y });
                }
                changes.Add((i, new Point(-10000, -10000)));
            }
            if (changes.Count > 0 && HasAutoArrange(list)) session.RestoreAutoArrange = true;
            // Persist recovery positions before changing Explorer, including the arrangement flag.
            SaveState(session);
            if (changes.Count > 0 && session.RestoreAutoArrange) SetAutoArrange(list, false);
            foreach (var change in changes) SetItemPositionRemote(list, change.Index, change.Target.X, change.Target.Y);
        }

        private const int WindowStyle = -16;
        private const int AutoArrangeStyle = 0x0100;

        private static bool HasAutoArrange(IntPtr list) => (GetWindowLong(list, WindowStyle) & AutoArrangeStyle) != 0;

        private static void SetAutoArrange(IntPtr list, bool enabled)
        {
            int style = GetWindowLong(list, WindowStyle);
            int next = enabled ? style | AutoArrangeStyle : style & ~AutoArrangeStyle;
            if (next != style) SetWindowLong(list, WindowStyle, next);
        }

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

        public void Stop()
        {
            RestoreQuickHide();
            if (_hookHandle != IntPtr.Zero) { UnhookWindowsHookEx(_hookHandle); _hookHandle = IntPtr.Zero; }
            _hookProc = null;
            _uiContext = null;
        }

        private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0 && wParam.ToInt64() == WM_LBUTTONDOWN)
                {
                    var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                    uint now = data.time;
                    var pos = new Point(data.pt.X, data.pt.Y);
                    bool isDoubleClick = (now - _lastDownTime) <= GetDoubleClickTime()
                        && Math.Abs(pos.X - _lastDownPos.X) <= GetSystemMetrics(SM_CXDOUBLECLK)
                        && Math.Abs(pos.Y - _lastDownPos.Y) <= GetSystemMetrics(SM_CYDOUBLECLK);
                    _lastDownTime = now;
                    _lastDownPos = pos;

                    if (isDoubleClick)
                    {
                        _lastDownTime = 0; // "καταναλώνει" το ζευγάρι — ένα τρίτο συνεχόμενο κλικ δεν μετράει ξανά ως 2ο μισό
                        if (_uiContext is { } ui)
                            ui.Post(_ => { if (_hookHandle != IntPtr.Zero) HandlePossibleDesktopDoubleClick(pos); }, null);
                        else HandlePossibleDesktopDoubleClick(pos);
                    }
                }
            }
            catch { /* ένας κακός χειρισμός εδώ δεν πρέπει ΠΟΤΕ να ρίξει το global hook / την εφαρμογή */ }
            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        private void HandlePossibleDesktopDoubleClick(Point screenPos)
        {
            IntPtr hwnd = WindowFromPoint(screenPos);
            if (hwnd == IntPtr.Zero || hwnd != FindDesktopListView()) return;

            var client = screenPos;
            ScreenToClient(hwnd, ref client);
            if (!IsEmptyAreaHit(hwnd, client)) return;

            ToggleDesktopIcons(hwnd);
        }

        private void ToggleDesktopIcons(IntPtr hwndList)
        {
            int count = SendMessage(hwndList, LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero).ToInt32();
            if (count <= 0) return;

            GetWindowThreadProcessId(hwndList, out uint pid);
            IntPtr hProcess = OpenProcess(PROCESS_VM_OPERATION | PROCESS_VM_READ | PROCESS_VM_WRITE, false, pid);
            if (hProcess == IntPtr.Zero) return;

            try
            {
                if (!_hidden)
                {
                    if (File.Exists(StateFilePath)) RestorePositions(hProcess, hwndList);
                    ApplyQuickHide(hProcess, hwndList, count, new QuickHideSession());
                    _hidden = true;
                }
                else
                {
                    RestorePositions(hProcess, hwndList);
                    _hidden = false;
                }

                // Ίδια χειρονομία, ίδια συμπεριφορά με τα πραγματικά Fences: το quick-hide της
                // επιφάνειας εργασίας κρύβει/ξαναδείχνει επίσης όλα τα DeskContainers, εκτός από
                // όσα ο χρήστης έχει σημειώσει ρητά "Exclude from quick-hide".
                MotionDesk.Containers.ContainerHost.Current?.SetAllHidden(_hidden);
            }
            catch { }
            finally { CloseHandle(hProcess); }
        }

        private static bool IsEmptyAreaHit(IntPtr hwndList, Point clientPoint)
        {
            GetWindowThreadProcessId(hwndList, out uint pid);
            IntPtr hProcess = OpenProcess(PROCESS_VM_OPERATION | PROCESS_VM_READ | PROCESS_VM_WRITE, false, pid);
            if (hProcess == IntPtr.Zero) return false;
            IntPtr remoteHit = IntPtr.Zero;
            try
            {
                remoteHit = VirtualAllocEx(hProcess, IntPtr.Zero, (uint)Marshal.SizeOf<LVHITTESTINFO>(), MEM_COMMIT, PAGE_READWRITE);
                if (remoteHit == IntPtr.Zero) return false;
                var hit = new LVHITTESTINFO { pt = new POINT { X = clientPoint.X, Y = clientPoint.Y } };
                if (!WriteProcessMemory(hProcess, remoteHit, ref hit, Marshal.SizeOf<LVHITTESTINFO>(), out _)) return false;
                SendMessage(hwndList, LVM_HITTEST, IntPtr.Zero, remoteHit);
                var buf = new byte[Marshal.SizeOf<LVHITTESTINFO>()];
                if (!ReadProcessMemory(hProcess, remoteHit, buf, buf.Length, out _)) return false;
                int iItem = BitConverter.ToInt32(buf, 12); // pt(8) + flags(4) = offset 12
                return iItem < 0; // κανένα item κάτω από το σημείο -> κενή περιοχή
            }
            catch { return false; }
            finally
            {
                if (remoteHit != IntPtr.Zero) VirtualFreeEx(hProcess, remoteHit, 0, MEM_RELEASE);
                CloseHandle(hProcess);
            }
        }

        private static string GetItemTextRemote(IntPtr hProcess, IntPtr hwndList, int index)
        {
            const int bufChars = 260;
            int bufBytes = bufChars * 2;
            IntPtr remoteText = IntPtr.Zero, remoteItem = IntPtr.Zero;
            try
            {
                remoteText = VirtualAllocEx(hProcess, IntPtr.Zero, (uint)bufBytes, MEM_COMMIT, PAGE_READWRITE);
                remoteItem = VirtualAllocEx(hProcess, IntPtr.Zero, (uint)Marshal.SizeOf<LVITEM>(), MEM_COMMIT, PAGE_READWRITE);
                if (remoteText == IntPtr.Zero || remoteItem == IntPtr.Zero) return string.Empty;

                var item = new LVITEM { mask = LVIF_TEXT, iItem = index, iSubItem = 0, pszText = remoteText, cchTextMax = bufChars };
                if (!WriteProcessMemory(hProcess, remoteItem, ref item, Marshal.SizeOf<LVITEM>(), out _)) return string.Empty;
                SendMessage(hwndList, LVM_GETITEMTEXT, (IntPtr)index, remoteItem);

                var localBuf = new byte[bufBytes];
                if (!ReadProcessMemory(hProcess, remoteText, localBuf, bufBytes, out _)) return string.Empty;
                string text = Encoding.Unicode.GetString(localBuf);
                int nullIdx = text.IndexOf('\0');
                return nullIdx >= 0 ? text[..nullIdx] : text;
            }
            catch { return string.Empty; }
            finally
            {
                if (remoteText != IntPtr.Zero) VirtualFreeEx(hProcess, remoteText, 0, MEM_RELEASE);
                if (remoteItem != IntPtr.Zero) VirtualFreeEx(hProcess, remoteItem, 0, MEM_RELEASE);
            }
        }

        private static Point GetItemPositionRemote(IntPtr hProcess, IntPtr hwndList, int index) =>
            ReadItemPosition(hProcess, hwndList, index) ?? Point.Empty;

        private static Point? ReadItemPosition(IntPtr hProcess, IntPtr hwndList, int index)
        {
            IntPtr remotePt = IntPtr.Zero;
            try
            {
                remotePt = VirtualAllocEx(hProcess, IntPtr.Zero, (uint)Marshal.SizeOf<POINT>(), MEM_COMMIT, PAGE_READWRITE);
                if (remotePt == IntPtr.Zero) return null;
                if (SendMessage(hwndList, LVM_GETITEMPOSITION, (IntPtr)index, remotePt) == IntPtr.Zero) return null;
                var buf = new byte[Marshal.SizeOf<POINT>()];
                if (!ReadProcessMemory(hProcess, remotePt, buf, buf.Length, out _)) return null;
                return new Point(BitConverter.ToInt32(buf, 0), BitConverter.ToInt32(buf, 4));
            }
            catch { return null; }
            finally { if (remotePt != IntPtr.Zero) VirtualFreeEx(hProcess, remotePt, 0, MEM_RELEASE); }
        }

        private static bool SetItemPositionRemote(IntPtr hwndList, int index, int x, int y)
        {
            IntPtr lParam = (IntPtr)(((y & 0xFFFF) << 16) | (x & 0xFFFF));
            return SendMessage(hwndList, LVM_SETITEMPOSITION, (IntPtr)index, lParam) != IntPtr.Zero;
        }

        private static void SaveState(QuickHideSession session)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StateFilePath)!);
            MotionDesk.Services.AtomicFile.WriteAllText(StateFilePath, JsonSerializer.Serialize(session));
        }

        private static QuickHideSession LoadState()
        {
            try
            {
                if (File.Exists(StateFilePath))
                {
                    var data = File.ReadAllText(StateFilePath);
                    if (data.AsSpan().TrimStart().StartsWith("["))
                        return new QuickHideSession { Positions = JsonSerializer.Deserialize<List<SavedIconPos>>(data) ?? [] };
                    return JsonSerializer.Deserialize<QuickHideSession>(data) ?? new();
                }
            }
            catch (JsonException) { }
            catch (IOException) { }
            return new();
        }

        private sealed class QuickHideSession
        {
            public List<SavedIconPos> Positions { get; set; } = [];
            public bool RestoreAutoArrange { get; set; }
        }

        private sealed class SavedIconPos
        {
            public int Index { get; set; }
            public string Name { get; set; } = "";
            public int X { get; set; }
            public int Y { get; set; }
        }

        // ---- Win32 interop ----

        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        private const int WH_MOUSE_LL = 14;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int SM_CXDOUBLECLK = 36;
        private const int SM_CYDOUBLECLK = 37;

        private const uint LVM_GETITEMCOUNT = 0x1004;      // LVM_FIRST + 4
        private const uint LVM_GETITEMTEXT = 0x1073;       // LVM_GETITEMTEXTW = LVM_FIRST + 115
        private const uint LVM_GETITEMPOSITION = 0x1010;   // LVM_FIRST + 16
        private const uint LVM_SETITEMPOSITION = 0x100F;   // LVM_FIRST + 15
        private const uint LVM_HITTEST = 0x1012;           // LVM_FIRST + 18
        private const uint LVIF_TEXT = 0x0001;

        private const uint MEM_COMMIT = 0x1000;
        private const uint MEM_RELEASE = 0x8000;
        private const uint PAGE_READWRITE = 0x04;
        private const uint PROCESS_VM_OPERATION = 0x0008;
        private const uint PROCESS_VM_READ = 0x0010;
        private const uint PROCESS_VM_WRITE = 0x0020;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData; public uint flags; public uint time; public IntPtr dwExtraInfo; }

        [StructLayout(LayoutKind.Sequential)]
        private struct LVITEM
        {
            public uint mask;
            public int iItem;
            public int iSubItem;
            public uint state;
            public uint stateMask;
            public IntPtr pszText;
            public int cchTextMax;
            public int iImage;
            public IntPtr lParam;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct LVHITTESTINFO { public POINT pt; public uint flags; public int iItem; public int iSubItem; public int iGroup; }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string? lpszClass, string? lpszWindow);
        [DllImport("user32.dll")] private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll")] private static extern IntPtr GetModuleHandle(string? lpModuleName);
        [DllImport("user32.dll")] private static extern uint GetDoubleClickTime();
        [DllImport("user32.dll")] private static extern int GetSystemMetrics(int nIndex);
        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point p);
        [DllImport("user32.dll")] private static extern bool ScreenToClient(IntPtr hWnd, ref Point lpPoint);
        [DllImport("user32.dll")] private static extern void GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr hObject);
        [DllImport("kernel32.dll")] private static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint flAllocationType, uint flProtect);
        [DllImport("kernel32.dll")] private static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint dwFreeType);
        [DllImport("kernel32.dll")] private static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, ref LVITEM buffer, int nSize, out IntPtr lpNumberOfBytesWritten);
        [DllImport("kernel32.dll")] private static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, ref LVHITTESTINFO buffer, int nSize, out IntPtr lpNumberOfBytesWritten);
        [DllImport("kernel32.dll")] private static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, int dwSize, out IntPtr lpNumberOfBytesRead);
    }
}
