using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using MotionDesk.Services;

namespace MotionDesk.Widgets;

// Cache variants are adapted from LockscreenGif's CacheLayout.cs (MIT).
// Attribution and the upstream license are in THIRD_PARTY_NOTICES.md.
internal static class AnimatedLockScreen
{
    private const int MaxGifBytes = 32 * 1024 * 1024;
    private const int MaxTargets = 32;
    private const uint OpenExisting = 3;
    private const uint BackupSemantics = 0x02000000;
    private const uint OpenReparsePoint = 0x00200000;
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint ReadAttributes = 0x80;

    internal static int ApplyFromUi(string gifPath)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var launch = new ProcessStartInfo(Environment.ProcessPath!)
        {
            Verb = "runas",
            UseShellExecute = true
        };
        launch.ArgumentList.Add("--apply-lockscreen-gif");
        launch.ArgumentList.Add(gifPath);
        launch.ArgumentList.Add(identity.User!.Value);
        try
        {
            using var process = Process.Start(launch);
            if (process == null) return 1;
            process.WaitForExit();
            return process.ExitCode;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { return 2; }
        catch (Win32Exception ex)
        {
            RotatingLogger.Write("Lockscreen GIF launch: " + ex.Message);
            return 1;
        }
    }

    internal static int ApplyElevated(string gifPath, string userSid)
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            if (!string.Equals(identity.User?.Value, userSid, StringComparison.Ordinal)
                || !new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
                return 1;

            var gif = ReadGif(gifPath);
            using var token = EnableBackupRestorePrivileges();
            var root = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            if (!Path.IsPathFullyQualified(root) || root.StartsWith(@"\\", StringComparison.Ordinal)) return 1;
            root = Path.Combine(root, "Microsoft", "Windows", "SystemData", identity.User!.Value, "ReadOnly");
            using var directory = OpenDirectoryTree(root);
            var targets = new List<string>();
            foreach (var folder in ReadEntries(directory).Where(x => x.IsDirectory && IsFolderName(x.Name)))
            {
                var path = Path.Combine(root, folder.Name);
                using var active = OpenChecked(path, GenericRead, directory: true);
                targets.AddRange(ReadEntries(active).Where(x => !x.IsDirectory && IsImageName(x.Name))
                    .Select(entry => Path.Combine(path, entry.Name)));
                if (targets.Count > MaxTargets) throw new IOException("Too many lock-screen cache entries.");
            }
            if (targets.Count == 0) return 3;

            WriteAll(targets, gif);
            return 0;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode is 2 or 3) { return 3; }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException
            or Win32Exception)
        {
            RotatingLogger.Write("Lockscreen GIF apply: " + ex.Message);
            return 1;
        }
    }

    private static byte[] ReadGif(string path)
    {
        using var source = File.OpenRead(path);
        if (source.Length is < 14 or > MaxGifBytes) throw new IOException("Invalid GIF size.");
        var bytes = new byte[(int)source.Length];
        source.ReadExactly(bytes);
        ValidateGif(bytes);
        return bytes;
    }

    // Adapted from LockscreenGif's GifInspector. Parse blocks without decompressing pixels
    // or passing user-controlled media to a native decoder in the elevated process.
    internal static void ValidateGif(byte[] bytes)
    {
        using var memory = new MemoryStream(bytes, writable: false);
        using var reader = new BinaryReader(memory);
        var signature = reader.ReadBytes(6).AsSpan();
        if (!signature.SequenceEqual("GIF87a"u8) && !signature.SequenceEqual("GIF89a"u8))
            throw new InvalidDataException("Invalid GIF signature.");

        int canvasWidth = reader.ReadUInt16(), canvasHeight = reader.ReadUInt16();
        if (canvasWidth == 0 || canvasHeight == 0 || (long)canvasWidth * canvasHeight > 16_000_000)
            throw new InvalidDataException("Invalid GIF dimensions.");

        var packed = reader.ReadByte();
        Skip(2);
        SkipPalette(packed);
        int frames = 0;
        while (memory.Position < memory.Length)
        {
            switch (reader.ReadByte())
            {
                case 0x3b when frames >= 2:
                    return;
                case 0x21:
                    reader.ReadByte();
                    SkipSubBlocks();
                    break;
                case 0x2c:
                    int x = reader.ReadUInt16(), y = reader.ReadUInt16();
                    int w = reader.ReadUInt16(), h = reader.ReadUInt16();
                    if (w == 0 || h == 0 || x + w > canvasWidth || y + h > canvasHeight)
                        throw new InvalidDataException("Invalid GIF frame geometry.");
                    SkipPalette(reader.ReadByte());
                    if (reader.ReadByte() is < 2 or > 8 || !SkipSubBlocks())
                        throw new InvalidDataException("Invalid GIF frame data.");
                    frames++;
                    break;
                default:
                    throw new InvalidDataException("Expected an animated GIF.");
            }
        }

        throw new InvalidDataException("The GIF has no trailer.");

        void Skip(int count)
        {
            if (count > memory.Length - memory.Position) throw new EndOfStreamException();
            memory.Position += count;
        }

        void SkipPalette(byte flags)
        {
            if ((flags & 0x80) != 0) Skip(3 * (2 << (flags & 7)));
        }

        bool SkipSubBlocks()
        {
            byte count = reader.ReadByte();
            bool hasData = count != 0;
            while (count != 0)
            {
                Skip(count);
                count = reader.ReadByte();
            }
            return hasData;
        }
    }

    private static void WriteAll(List<string> paths, byte[] gif)
    {
        var files = new List<(FileStream Stream, byte[] Original)>();
        int touched = 0;
        long originalBytes = 0;
        try
        {
            foreach (var path in paths)
            {
                using var parent = OpenDirectoryTree(Path.GetDirectoryName(path)!);
                var handle = OpenChecked(path, GenericRead | GenericWrite, directory: false);
                FileStream? stream = null;
                try
                {
                    stream = new FileStream(handle, FileAccess.ReadWrite);
                    originalBytes += stream.Length;
                    if (originalBytes > 64 * 1024 * 1024) throw new IOException("Lock-screen cache exceeds the rollback limit.");
                    var original = new byte[(int)stream.Length];
                    stream.ReadExactly(original);
                    files.Add((stream, original));
                }
                catch
                {
                    if (stream == null) handle.Dispose();
                    else stream.Dispose();
                    throw;
                }
            }
            foreach (var file in files)
            {
                touched++;
                WriteImage(file.Stream, gif);
            }
        }
        catch
        {
            for (int i = 0; i < touched; i++)
            {
                try { WriteImage(files[i].Stream, files[i].Original); }
                catch (IOException ex) { RotatingLogger.Write("Lockscreen GIF rollback: " + ex.Message); }
            }
            throw;
        }
        finally
        {
            foreach (var file in files) file.Stream.Dispose();
        }
    }

    private static void WriteImage(FileStream stream, byte[] bytes)
    {
        stream.Position = 0;
        stream.Write(bytes);
        stream.SetLength(bytes.Length);
        stream.Flush(flushToDisk: true);
    }

    private static bool IsFolderName(string name) => name.StartsWith("LockScreen_", StringComparison.OrdinalIgnoreCase)
        && name.Length > 11 && name.AsSpan(11).IndexOfAnyExcept("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789") < 0;

    internal static bool IsImageName(string name) =>
        name.Equals("LockScreen.jpg", StringComparison.OrdinalIgnoreCase)
        || (name.StartsWith("LockScreen___", StringComparison.OrdinalIgnoreCase)
            && name.EndsWith("_notdimmed.jpg", StringComparison.OrdinalIgnoreCase)
            && name.Length > 27 && name.AsSpan(13, name.Length - 27).IndexOfAnyExcept("0123456789_") < 0);

    private static SafeFileHandle OpenDirectoryTree(string directory)
    {
        var checkedPaths = new Stack<string>();
        for (string? part = directory; part != null; part = Path.GetDirectoryName(part)) checkedPaths.Push(part);
        while (checkedPaths.Count > 1)
        {
            using var ancestor = OpenChecked(checkedPaths.Pop(), ReadAttributes, directory: true);
        }
        return OpenChecked(checkedPaths.Pop(), GenericRead, directory: true);
    }

    private static SafeFileHandle OpenChecked(string path, uint access, bool directory)
    {
        var handle = CreateFile(path, access, directory ? 7u : 1u, IntPtr.Zero,
            OpenExisting, BackupSemantics | OpenReparsePoint, IntPtr.Zero);
        try
        {
            if (handle.IsInvalid || !GetFileInformationByHandle(handle, out var info)) throw LastError();
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0
                || (info.Attributes.HasFlag(FileAttributes.Directory) != directory)
                || (!directory && info.NumberOfLinks != 1))
                throw new IOException("Unexpected lock-screen cache file type.");
            return handle;
        }
        catch { handle.Dispose(); throw; }
    }

    private static List<(string Name, bool IsDirectory)> ReadEntries(SafeFileHandle directory)
    {
        const int bufferSize = 65536;
        const int fileNameOffset = 104; // FILE_ID_BOTH_DIR_INFO.FileName
        const int fileIdBothDirectoryInfo = 10;
        const int errorNoMoreFiles = 18;
        var entries = new List<(string, bool)>();
        var buffer = Marshal.AllocHGlobal(bufferSize);
        try
        {
            while (GetFileInformationByHandleEx(directory, fileIdBothDirectoryInfo, buffer, bufferSize))
            {
                for (int offset = 0; ;)
                {
                    var record = buffer + offset;
                    int next = Marshal.ReadInt32(record);
                    int attributes = Marshal.ReadInt32(record, 56);
                    int nameBytes = Marshal.ReadInt32(record, 60);
                    if (nameBytes <= 0 || nameBytes % 2 != 0 || nameBytes > bufferSize - offset - fileNameOffset)
                        throw new IOException("Invalid directory record.");
                    if ((attributes & (int)FileAttributes.ReparsePoint) == 0)
                        entries.Add((Marshal.PtrToStringUni(record + fileNameOffset, nameBytes / 2)!,
                            (attributes & (int)FileAttributes.Directory) != 0));
                    if (entries.Count > 512) throw new IOException("Too many cache directory entries.");
                    if (next == 0) break;
                    if (next < fileNameOffset || offset + next > bufferSize - fileNameOffset)
                        throw new IOException("Invalid directory record offset.");
                    offset += next;
                }
            }
            if (Marshal.GetLastWin32Error() != errorNoMoreFiles) throw LastError();
            return entries;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static SafeAccessTokenHandle EnableBackupRestorePrivileges()
    {
        if (!OpenProcessToken(GetCurrentProcess(), 0x28, out var token)) throw LastError();
        try
        {
            foreach (var privilege in new[] { "SeBackupPrivilege", "SeRestorePrivilege" })
            {
                if (!LookupPrivilegeValue(null, privilege, out var luid)) throw LastError();
                var state = new TokenPrivileges { Count = 1, Luid = luid, Attributes = 2 };
                if (!AdjustTokenPrivileges(token, false, ref state, 0, IntPtr.Zero, IntPtr.Zero)
                    || Marshal.GetLastWin32Error() != 0) throw LastError();
            }
            return token;
        }
        catch { token.Dispose(); throw; }
    }

    private static Win32Exception LastError() => new(Marshal.GetLastWin32Error());

    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        public FileAttributes Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME Creation, Access, Write;
        public uint VolumeSerial, SizeHigh, SizeLow, NumberOfLinks, IndexHigh, IndexLow;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid { public uint Low; public int High; }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenPrivileges { public uint Count; public Luid Luid; public uint Attributes; }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security,
        uint creation, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation information);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int infoClass, IntPtr buffer, int length);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out SafeAccessTokenHandle token);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LookupPrivilegeValue(string? system, string name, out Luid luid);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AdjustTokenPrivileges(SafeAccessTokenHandle token, bool disable, ref TokenPrivileges state,
        int size, IntPtr previous, IntPtr returnLength);
}
