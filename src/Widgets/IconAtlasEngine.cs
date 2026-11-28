using System;
using System.IO;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Collections.Generic;
using MotionDesk.Services;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace MotionDesk.Widgets
{
    // "IconAtlas" — αλλαγή εικονιδίων συστήματος (This PC, Κάδος Ανακύκλωσης άδειος/γεμάτος,
    // εικονίδιο ανά φάκελο) μέσω επίσημων registry mechanisms — ό,τι ακριβώς χρησιμοποιεί το ίδιο
    // το Explorer/Personalization panel, καμία τροποποίηση αρχείων συστήματος. Επαληθεύτηκε έναντι
    // πραγματικού registry state πριν γραφτεί (CLSID paths, μορφή τιμών "path,index").
    internal static class IconAtlasEngine
    {
        private const string ThisPcClsid = "{20D04FE0-3AEA-1069-A2D8-08002B30309D}";
        private const string RecycleBinClsid = "{645FF040-5081-101B-9F08-00AA002F954E}";
        // Επέκταση κάλυψης depot (ζητήθηκε ρητά v1.5.0): τα υπόλοιπα τυπικά εικονίδια που τα ίδια
        // τα Windows επιτρέπουν να αλλάξουν μέσω Personalization/registry — δεν υπάρχει δημόσιο
        // API για "όλα τα εικονίδια των Windows" (άπειρο, απροσδιόριστο σύνολο), μόνο αυτό το
        // συγκεκριμένο, γνωστό σύνολο CLSID-based εικονιδίων επιφάνειας εργασίας.
        private const string NetworkClsid = "{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}";
        private const string ControlPanelClsid = "{21EC2020-3AEA-1069-A2DD-08002B30309D}";
        private const string UsersFilesClsid = "{59031A47-3F72-44A7-89C5-5595FE6B30EE}";

        public static string? GetThisPcIcon() => GetClsidIcon(ThisPcClsid, null);
        public static string? GetRecycleBinEmptyIcon() => GetClsidIcon(RecycleBinClsid, "empty");
        public static string? GetRecycleBinFullIcon() => GetClsidIcon(RecycleBinClsid, "full");
        public static string? GetNetworkIcon() => GetClsidIcon(NetworkClsid, null);
        public static string? GetControlPanelIcon() => GetClsidIcon(ControlPanelClsid, null);
        public static string? GetUsersFilesIcon() => GetClsidIcon(UsersFilesClsid, null);

        public static void SetThisPcIcon(string? icoPath) => SetClsidIcon(ThisPcClsid, null, icoPath);
        public static void SetRecycleBinEmptyIcon(string? icoPath) => SetClsidIcon(RecycleBinClsid, "empty", icoPath);
        public static void SetRecycleBinFullIcon(string? icoPath) => SetClsidIcon(RecycleBinClsid, "full", icoPath);
        public static void SetNetworkIcon(string? icoPath) => SetClsidIcon(NetworkClsid, null, icoPath);
        public static void SetControlPanelIcon(string? icoPath) => SetClsidIcon(ControlPanelClsid, null, icoPath);
        public static void SetUsersFilesIcon(string? icoPath) => SetClsidIcon(UsersFilesClsid, null, icoPath);

        private static string? GetClsidIcon(string clsid, string? namedValue)
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                $@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\CLSID\{clsid}\DefaultIcon");
            var raw = key?.GetValue(namedValue ?? string.Empty) as string;
            if (string.IsNullOrEmpty(raw)) return null;
            int comma = raw.LastIndexOf(',');
            return comma > 0 ? raw[..comma] : raw;
        }

        private static void SetClsidIcon(string clsid, string? namedValue, string? icoPath)
        {
            using var key = Registry.CurrentUser.CreateSubKey(
                $@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\CLSID\{clsid}\DefaultIcon");
            if (key == null) return;
            string valueName = namedValue ?? string.Empty;
            if (string.IsNullOrEmpty(icoPath)) key.DeleteValue(valueName, false);
            else key.SetValue(valueName, $"{icoPath},0", RegistryValueKind.ExpandString);
            RefreshShellIcons();
        }

        // Γενικό εικονίδιο ενός συγκεκριμένου φακέλου μέσω desktop.ini — ίδια τεχνική με αυτή που
        // χρησιμοποιεί το ίδιο το Explorer όταν ο χρήστης αλλάζει εικονίδιο φακέλου από το UI του
        // ("Ιδιότητες" → "Προσαρμογή"). Το ReadOnly attribute στον ΦΑΚΕΛΟ (όχι στα περιεχόμενά του)
        // είναι το επίσημο σήμα προς το Explorer "αυτός ο φάκελος έχει custom desktop.ini".
        private static readonly object FolderGate = new();
        private const int IniLimit = 65536;
        private sealed class FolderOriginal
        {
            public string Folder { get; set; } = "";
            public byte[]? Original { get; set; }
            public int IniAttributes { get; set; }
            public bool FolderReadOnly { get; set; }
            public string? LastHash { get; set; }
            public string? PendingHash { get; set; }
        }

        private static string? Digest(byte[]? bytes) =>
            bytes == null ? null : Convert.ToHexString(SHA256.HashData(bytes));

        private static void RequireUnlinked(string path)
        {
            for (string? p = Path.GetFullPath(path); p != null; p = Path.GetDirectoryName(p))
            {
                try
                {
                    if ((File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0)
                        throw new IOException("Folder icons cannot modify linked paths.");
                }
                catch (FileNotFoundException) when (p == path) { }
                catch (DirectoryNotFoundException) when (p == path) { }
            }
        }

        private static string FolderPath(string folderPath)
        {
            if (!Path.IsPathFullyQualified(folderPath) || folderPath.StartsWith(@"\\", StringComparison.Ordinal))
                throw new IOException("Choose a local folder.");
            var folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folderPath));
            if (!Directory.Exists(folder) || Path.GetDirectoryName(folder) == null ||
                new DriveInfo(Path.GetPathRoot(folder)!).DriveType != DriveType.Fixed)
                throw new IOException("Choose an existing folder on a local fixed drive, not a drive root.");
            foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) })
            {
                if (string.IsNullOrEmpty(root)) continue;
                var relative = Path.GetRelativePath(root, folder);
                if (relative == "." || (!Path.IsPathRooted(relative) && relative != ".." &&
                    !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
                    throw new IOException("System and application folders are not supported.");
            }
            RequireUnlinked(folder);
            return folder;
        }

        private static byte[]? ReadIni(string path)
        {
            RequireUnlinked(path);
            if (!File.Exists(path)) return null;
            using var stream = File.OpenRead(path);
            if (stream.Length > IniLimit) throw new InvalidDataException("desktop.ini exceeds the safety limit.");
            var bytes = new byte[(int)stream.Length];
            stream.ReadExactly(bytes);
            return bytes;
        }

        // Preserve other sections and keys, including InfoTip and localized names.
        // Unsupported legacy encodings fail closed instead of silently corrupting them.
        internal static byte[] MergeFolderIconIni(byte[]? original, string iconPath)
        {
            if (string.IsNullOrWhiteSpace(iconPath) || iconPath.Contains('\r') || iconPath.Contains('\n') ||
                iconPath.Contains('\0')) throw new ArgumentException("Invalid icon path.");
            string text = "";
            if (original is { Length: > 0 })
            {
                if (original.Length >= 2 && original[0] == 255 && original[1] == 254)
                    text = new UnicodeEncoding(false, true, true).GetString(original, 2, original.Length - 2);
                else if (original.Length >= 2 && original[0] == 254 && original[1] == 255)
                    text = new UnicodeEncoding(true, true, true).GetString(original, 2, original.Length - 2);
                else
                {
                    int offset = original.Length >= 3 && original[0] == 239 && original[1] == 187 && original[2] == 191 ? 3 : 0;
                    text = new UTF8Encoding(false, true).GetString(original, offset, original.Length - offset);
                }
                if (text.Contains('\0')) throw new InvalidDataException("Unsupported desktop.ini encoding.");
            }
            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').ToList();
            bool shell = false, found = false;
            var result = new List<string>();
            foreach (var line in lines)
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                {
                    shell = string.Equals(trimmed, "[.ShellClassInfo]", StringComparison.OrdinalIgnoreCase);
                    if (shell && found) throw new InvalidDataException("Duplicate ShellClassInfo section.");
                    result.Add(line);
                    if (shell) { found = true; result.Add("IconResource=" + iconPath + ",0"); }
                    continue;
                }
                var equals = trimmed.IndexOf('=');
                if (shell && equals >= 0)
                {
                    var key = trimmed[..equals].Trim();
                    if (new[] { "IconResource", "IconFile", "IconIndex" }.Contains(key, StringComparer.OrdinalIgnoreCase))
                        continue;
                }
                result.Add(line);
            }
            if (!found)
            {
                result.Add("[.ShellClassInfo]");
                result.Add("IconResource=" + iconPath + ",0");
            }
            var encoding = new UnicodeEncoding(false, true, true);
            return encoding.GetPreamble().Concat(encoding.GetBytes(string.Join("\r\n", result) + "\r\n")).ToArray();
        }

        private static void WriteIni(string path, byte[]? data, FileAttributes attributes)
        {
            RequireUnlinked(path);
            if (data == null)
            {
                if (File.Exists(path)) { File.SetAttributes(path, FileAttributes.Normal); File.Delete(path); }
                return;
            }
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            FileAttributes? prior = File.Exists(path) ? File.GetAttributes(path) : null;
            bool swapped = false;
            try
            {
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { stream.Write(data); stream.Flush(true); }
                if (prior.HasValue) File.SetAttributes(path, FileAttributes.Normal);
                File.Move(temp, path, overwrite: true);
                swapped = true;
                File.SetAttributes(path, attributes);
            }
            finally
            {
                if (!swapped && prior.HasValue && File.Exists(path)) File.SetAttributes(path, prior.Value);
                if (File.Exists(temp)) File.Delete(temp);
            }
        }

        public static void SetFolderIcon(string folderPath, string? icoPath)
        {
            if (SafeModeService.IsSafeMode || WindowsUpdateGuard.RequiresReview || WindowsUpdateGuard.RuntimeHold)
                throw new InvalidOperationException("Windows compatibility review is required.");
            lock (FolderGate)
            {
                string folder = FolderPath(folderPath);
                string ini = Path.Combine(folder, "desktop.ini");
                byte[]? current = ReadIni(ini);
                string root = Path.Combine(AppPaths.AppDataFolder, "folder-icons");
                Directory.CreateDirectory(root);
                RequireUnlinked(root);
                string journal = Path.Combine(root, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(folder.ToUpperInvariant()))) + ".json");
                RequireUnlinked(journal);
                FolderOriginal? backup = null;
                if (File.Exists(journal))
                {
                    if (new FileInfo(journal).Length > 128 * 1024) throw new InvalidDataException("Invalid folder icon backup.");
                    backup = JsonSerializer.Deserialize<FolderOriginal>(File.ReadAllText(journal))
                        ?? throw new InvalidDataException("Invalid folder icon backup.");
                    if (!string.Equals(backup.Folder, folder, StringComparison.OrdinalIgnoreCase) || backup.Original?.Length > IniLimit)
                        throw new InvalidDataException("Folder icon backup does not match.");
                    var hash = Digest(current);
                    if (hash != Digest(backup.Original) && hash != backup.LastHash && hash != backup.PendingHash)
                        throw new IOException("desktop.ini changed outside Opsis. No settings were overwritten.");
                }
                if (string.IsNullOrEmpty(icoPath))
                {
                    if (backup == null) throw new InvalidOperationException("No Opsis backup exists for this folder.");
                    WriteIni(ini, backup.Original, (FileAttributes)backup.IniAttributes);
                    var attrs = File.GetAttributes(folder);
                    File.SetAttributes(folder, backup.FolderReadOnly ? attrs | FileAttributes.ReadOnly : attrs & ~FileAttributes.ReadOnly);
                    File.Delete(journal);
                    RefreshShellIcons();
                    return;
                }
                if (!Path.IsPathFullyQualified(icoPath) || !File.Exists(icoPath)) throw new IOException("Choose an existing icon.");
                RequireUnlinked(icoPath);
                backup ??= new FolderOriginal { Folder = folder, Original = current,
                    IniAttributes = (int)(current == null ? FileAttributes.Normal : File.GetAttributes(ini)),
                    FolderReadOnly = (File.GetAttributes(folder) & FileAttributes.ReadOnly) != 0 };
                var updated = MergeFolderIconIni(current, icoPath);
                if (updated.Length > IniLimit) throw new InvalidDataException("desktop.ini exceeds the safety limit.");
                backup.PendingHash = Digest(updated);
                // Persist original and intended hash BEFORE touching the folder.
                AtomicFile.WriteAllText(journal, JsonSerializer.Serialize(backup));
                WriteIni(ini, updated, FileAttributes.Hidden | FileAttributes.System | FileAttributes.Archive);
                File.SetAttributes(folder, File.GetAttributes(folder) | FileAttributes.ReadOnly);
                backup.LastHash = backup.PendingHash;
                backup.PendingHash = null;
                AtomicFile.WriteAllText(journal, JsonSerializer.Serialize(backup));
                RefreshShellIcons();
            }
        }

        // Generate an original multi-resolution ICO locally; no download or
        // new renderer service. Windows Explorer uses the same folder owner.
        public static void SetFolderColor(string folderPath, Color color)
        {
            string root = Path.Combine(AppPaths.AppDataFolder, "folder-icons");
            Directory.CreateDirectory(root);
            RequireUnlinked(root);
            string path = Path.Combine(root, $"folder-{color.R:X2}{color.G:X2}{color.B:X2}.ico");
            RequireUnlinked(path);
            if (!File.Exists(path))
            {
                byte[] bytes = CreateFolderColorIcon(color);
                string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    { stream.Write(bytes); stream.Flush(true); }
                    try { File.Move(temp, path, overwrite: false); }
                    catch (IOException) when (File.Exists(path)) { }
                }
                finally { if (File.Exists(temp)) File.Delete(temp); }
            }
            SetFolderIcon(folderPath, path);
        }

        internal static byte[] CreateFolderColorIcon(Color color)
        {
            int[] sizes = { 16, 24, 32, 48, 64, 128, 256 };
            var images = new List<byte[]>();
            foreach (int size in sizes)
            {
                using var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(Color.Transparent);
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    graphics.ScaleTransform(size / 256f, size / 256f);
                    using var back = new SolidBrush(ControlPaintDark(color));
                    using var front = new LinearGradientBrush(new Rectangle(12, 72, 232, 150),
                        Color.FromArgb(255, Math.Min(255, color.R + 32), Math.Min(255, color.G + 32), Math.Min(255, color.B + 32)),
                        Color.FromArgb(color.R, color.G, color.B), LinearGradientMode.Vertical);
                    graphics.FillPolygon(back, new Point[] { new(13, 48), new(90, 48), new(112, 71), new(235, 71), new(235, 216), new(13, 216) });
                    graphics.FillPolygon(front, new Point[] { new(12, 87), new(244, 87), new(226, 220), new(22, 220) });
                }
                using var png = new MemoryStream();
                bitmap.Save(png, ImageFormat.Png);
                images.Add(png.ToArray());
            }
            using var output = new MemoryStream();
            using var writer = new BinaryWriter(output);
            writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)sizes.Length);
            int offset = 6 + sizes.Length * 16;
            for (int i = 0; i < sizes.Length; i++)
            {
                byte dimension = (byte)(sizes[i] == 256 ? 0 : sizes[i]);
                writer.Write(dimension); writer.Write(dimension); writer.Write((byte)0); writer.Write((byte)0);
                writer.Write((ushort)1); writer.Write((ushort)32);
                writer.Write(images[i].Length); writer.Write(offset); offset += images[i].Length;
            }
            foreach (var image in images) writer.Write(image);
            writer.Flush(); return output.ToArray();
        }

        private static Color ControlPaintDark(Color c) =>
            Color.FromArgb(c.R * 3 / 4, c.G * 3 / 4, c.B * 3 / 4);

        // "Depot" icon packs: στο web υπάρχουν έτοιμα σετ εικονιδίων Κάδου Ανακύκλωσης που
        // καλύπτουν και τις δύο καταστάσεις (π.χ. sdushantha/recycle-bin-themes στο GitHub),
        // ακολουθώντας τη σύμβαση ονοματοδοσίας "όνομα-empty.ico" / "όνομα-full.ico" (με "-", "_"
        // ή κενό ως διαχωριστικό) — ζητήθηκε ρητά να λαμβάνεται αυτό υπόψη στις ρυθμίσεις του
        // IconAtlas. Σαρώνει έναν φάκελο που επιλέγει ο χρήστης και ταιριάζει αυτόματα το ζευγάρι.
        public sealed class DepotImportResult
        {
            public string? EmptyIconPath { get; set; }
            public string? FullIconPath { get; set; }
            public string? SingleIconPath { get; set; }
        }

        public static DepotImportResult ScanDepotFolder(string folderPath)
        {
            var result = new DepotImportResult();
            if (!Directory.Exists(folderPath)) return result;

            var icoFiles = Directory.GetFiles(folderPath, "*.ico", SearchOption.TopDirectoryOnly);
            string? FindByKeyword(string keyword) => icoFiles.FirstOrDefault(f =>
            {
                string name = Path.GetFileNameWithoutExtension(f);
                return name.EndsWith("-" + keyword, StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith("_" + keyword, StringComparison.OrdinalIgnoreCase)
                    || name.Contains(keyword, StringComparison.OrdinalIgnoreCase);
            });

            result.EmptyIconPath = FindByKeyword("empty");
            result.FullIconPath = FindByKeyword("full");
            if (result.EmptyIconPath == null && result.FullIconPath == null && icoFiles.Length == 1)
                result.SingleIconPath = icoFiles[0];
            return result;
        }

        public static void ApplyDepotToRecycleBin(DepotImportResult depot)
        {
            if (depot.EmptyIconPath != null) SetRecycleBinEmptyIcon(depot.EmptyIconPath);
            if (depot.FullIconPath != null) SetRecycleBinFullIcon(depot.FullIconPath);
        }

        [DllImport("shell32.dll")]
        private static extern void SHChangeNotify(int wEventId, int uFlags, IntPtr dwItem1, IntPtr dwItem2);
        private const int SHCNE_ASSOCCHANGED = 0x08000000;
        private const int SHCNF_IDLIST = 0x0000;

        public static void RefreshShellIcons() => SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
    }
}
