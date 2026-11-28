using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32;
using MotionDesk.Widgets;

namespace MotionDesk.Services;

/// <summary>Local data-only shell assets, shared by IconAtlas and DeskCursors.
/// No DLLs, external mods, system-file writes, or auto-application.</summary>
internal static class ShellAssetPack
{
    internal sealed record Slot(string Key, string Name, string Suffix);
    internal sealed record Asset(string Role, string Source, string Sha256);
    internal sealed record Value(bool Present, string Text, int Kind);
    internal sealed record Change(string Role, Value Original, Value Applied);
    internal sealed record Backup(bool Cursor, Change[] Changes);
    private static readonly object Gate = new();
    private const int FileLimit = 8 * 1024 * 1024;
    private static string Root => Path.Combine(AppPaths.AppDataFolder, "shell-assets");
    private static string Journal(bool cursor) => Path.Combine(Root, cursor ? "cursor-rollback.json" : "icon-rollback.json");
    private static Slot Resolve(string role, bool cursor) => cursor ? DeskCursorsEngine.PackSlot(role) : IconAtlasEngine.PackSlot(role);

    internal static void CheckWritable()
    {
        if (SafeModeService.IsSafeMode || WindowsUpdateGuard.RequiresReview || WindowsUpdateGuard.RuntimeHold)
            throw new InvalidOperationException("Windows compatibility review required.");
    }

    private static void RequireUnlinkedLocal(string path)
    {
        if (!Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal) ||
            new DriveInfo(Path.GetPathRoot(path)!).DriveType is not (DriveType.Fixed or DriveType.Removable))
            throw new InvalidDataException("Select a local file.");
        for (string? item = Path.GetFullPath(path); item != null; item = Path.GetDirectoryName(item))
            if (PackagePaths.IsLink(item)) throw new InvalidDataException("Linked paths are not supported.");
    }

    private static byte[] ReadBounded(string path, int limit)
    {
        RequireUnlinkedLocal(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length is <= 0 || stream.Length > limit) throw new InvalidDataException("Asset size exceeds quota.");
        var data = new byte[(int)stream.Length];
        stream.ReadExactly(data);
        if (stream.ReadByte() != -1) throw new InvalidDataException("Asset changed during reading.");
        return data;
    }

    internal static bool ValidImageDirectory(ReadOnlySpan<byte> data, bool cursor)
    {
        if (data.Length < 22 || BinaryPrimitives.ReadUInt16LittleEndian(data) != 0 ||
            BinaryPrimitives.ReadUInt16LittleEndian(data[2..]) != (cursor ? 2 : 1)) return false;
        int count = BinaryPrimitives.ReadUInt16LittleEndian(data[4..]);
        if (count is < 1 or > 64 || data.Length < 6 + 16 * count) return false;
        for (int i = 0; i < count; i++)
        {
            var entry = data.Slice(6 + 16 * i, 16);
            uint bytes = BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]);
            uint offset = BinaryPrimitives.ReadUInt32LittleEndian(entry[12..]);
            if (bytes == 0 || offset < 6 + 16 * count || (ulong)offset + bytes > (ulong)data.Length) return false;
            if (cursor && (BinaryPrimitives.ReadUInt16LittleEndian(entry[4..]) >= (entry[0] == 0 ? 256 : entry[0]) ||
                           BinaryPrimitives.ReadUInt16LittleEndian(entry[6..]) >= (entry[1] == 0 ? 256 : entry[1]))) return false;
        }
        return true;
    }

    // Bounded standard RIFF ACON icon-frame format; structural validation only.
    internal static bool ValidAnimatedCursor(ReadOnlySpan<byte> data)
    {
        if (data.Length < 12 || data.Length > FileLimit ||
            !data[..4].SequenceEqual("RIFF"u8) || !data.Slice(8, 4).SequenceEqual("ACON"u8) ||
            (ulong)BinaryPrimitives.ReadUInt32LittleEndian(data[4..]) + 8 != (ulong)data.Length) return false;
        int offset = 12, chunks = 0, frames = 0;
        uint declaredFrames = 0, steps = 0, flags = 0;
        bool header = false, frameList = false, sequence = false, rate = false;
        int sequenceOffset = 0, sequenceLength = 0, rateOffset = 0, rateLength = 0;
        while (offset < data.Length)
        {
            int payloadOffset = offset + 8;
            if (++chunks > 256 || !TryChunk(data, ref offset, out var id, out var payload)) return false;
            if (id.SequenceEqual("anih"u8))
            {
                if (header || payload.Length != 36 || BinaryPrimitives.ReadUInt32LittleEndian(payload) != 36) return false;
                header = true;
                declaredFrames = BinaryPrimitives.ReadUInt32LittleEndian(payload[4..]);
                steps = BinaryPrimitives.ReadUInt32LittleEndian(payload[8..]);
                flags = BinaryPrimitives.ReadUInt32LittleEndian(payload[32..]);
                if (declaredFrames is < 1 or > 128 || steps is < 1 or > 1024 ||
                    (flags & 1) == 0 || (flags & ~3u) != 0 ||
                    BinaryPrimitives.ReadUInt32LittleEndian(payload[28..]) is < 1 or > 3600) return false;
            }
            else if (id.SequenceEqual("seq "u8))
            {
                if (sequence || payload.Length == 0 || payload.Length > 4096 || payload.Length % 4 != 0) return false;
                sequence = true; sequenceOffset = payloadOffset; sequenceLength = payload.Length;
            }
            else if (id.SequenceEqual("rate"u8))
            {
                if (rate || payload.Length == 0 || payload.Length > 4096 || payload.Length % 4 != 0) return false;
                rate = true; rateOffset = payloadOffset; rateLength = payload.Length;
            }
            else if (id.SequenceEqual("LIST"u8))
            {
                if (payload.Length < 4) return false;
                if (payload[..4].SequenceEqual("INFO"u8)) continue;
                if (!payload[..4].SequenceEqual("fram"u8) || frameList) return false;
                frameList = true;
                int inner = 4;
                while (inner < payload.Length)
                {
                    if (++frames > 128 || !TryChunk(payload, ref inner, out var frameId, out var frame) ||
                        !frameId.SequenceEqual("icon"u8) ||
                        (!ValidImageDirectory(frame, true) && !ValidImageDirectory(frame, false))) return false;
                }
            }
            else return false;
        }
        var sequenceData = data.Slice(sequenceOffset, sequenceLength);
        var rateData = data.Slice(rateOffset, rateLength);
        if (!header || !frameList || frames != declaredFrames ||
            sequence != ((flags & 2) != 0) || (!sequence && steps != declaredFrames) ||
            (sequence && sequenceData.Length != steps * 4) || (rate && rateData.Length != steps * 4)) return false;
        for (int i = 0; i < sequenceData.Length; i += 4)
            if (BinaryPrimitives.ReadUInt32LittleEndian(sequenceData[i..]) >= declaredFrames) return false;
        for (int i = 0; i < rateData.Length; i += 4)
            if (BinaryPrimitives.ReadUInt32LittleEndian(rateData[i..]) is < 1 or > 3600) return false;
        return true;
    }

    private static bool TryChunk(ReadOnlySpan<byte> data, ref int offset,
        out ReadOnlySpan<byte> id, out ReadOnlySpan<byte> payload)
    {
        id = default; payload = default;
        if (offset < 0 || data.Length - offset < 8) return false;
        uint length = BinaryPrimitives.ReadUInt32LittleEndian(data[(offset + 4)..]);
        if ((ulong)length + (length & 1u) > (ulong)(data.Length - offset - 8)) return false;
        id = data.Slice(offset, 4);
        payload = data.Slice(offset + 8, (int)length);
        offset += 8 + (int)length + (int)(length & 1u);
        return true;
    }

    private static byte[] ReadAsset(string path, bool cursor, bool allowAnimated = false)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        byte[] data = ReadBounded(path, FileLimit);
        ValidateAssetBytes(data, extension, cursor, allowAnimated);
        return data;
    }

    internal static void ValidateAssetBytes(byte[] data, string extension, bool cursor, bool allowAnimated = false)
    {
        if (data.Length is <= 0 or > FileLimit) throw new InvalidDataException("Asset size exceeds quota.");
        if (cursor && allowAnimated && extension == ".ani")
        {
            if (!ValidAnimatedCursor(data))
                throw new InvalidDataException("Invalid animated cursor.");
            return;
        }
        if (extension != (cursor ? ".cur" : ".ico") || !ValidImageDirectory(data, cursor))
            throw new InvalidDataException("Invalid icon/cursor directory.");
        if (!cursor)
        {
            using var input = new MemoryStream(data, writable: false);
            using var image = new Icon(input);
            using var bitmap = image.ToBitmap();
        }
    }
    internal static void ValidateLocalAsset(string path, bool cursor, bool allowAnimated = false) => _ = ReadAsset(path, cursor, allowAnimated);

    internal static IReadOnlyDictionary<string,string> ParseRoles(byte[] data, bool cursor)
    {
        if (data.Length is <= 0 or > 16384) throw new InvalidDataException("Invalid shell manifest size.");
        using var doc = JsonDocument.Parse(data);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("schema", out var schema) ||
            schema.ValueKind != JsonValueKind.Number || !schema.TryGetInt32(out int version) || version != 1 ||
            !root.TryGetProperty("kind", out var kind) || kind.ValueKind != JsonValueKind.String ||
            kind.GetString() != (cursor ? "cursor-pack" : "icon-pack") ||
            !root.TryGetProperty("roles", out var roles) || roles.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Unsupported shell asset manifest.");
        var items = new Dictionary<string,string>(StringComparer.Ordinal);
        var fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in root.EnumerateObject())
            if (!fields.Add(field.Name)) throw new InvalidDataException("Duplicate pack field.");
        foreach (var role in roles.EnumerateObject())
        {
            _ = Resolve(role.Name, cursor);
            if (items.ContainsKey(role.Name) || items.Count >= 6 || role.Value.ValueKind != JsonValueKind.String)
                throw new InvalidDataException("Invalid or duplicate pack role.");
            string name = role.Value.GetString()!;
            if (name.Length is < 1 or > 128 || name != Path.GetFileName(name) ||
                name.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('.' or '_' or '-' or ' ')))
                throw new InvalidDataException("Pack assets must be sibling files with simple filenames.");
            string stem = name.Split('.')[0].TrimEnd().ToUpperInvariant();
            if (stem is "CON" or "PRN" or "AUX" or "NUL" ||
                stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && char.IsAsciiDigit(stem[3]) ||
                (cursor ? Path.GetExtension(name).ToLowerInvariant() is not (".cur" or ".ani") :
                    !Path.GetExtension(name).Equals(".ico", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Unsupported or reserved asset filename.");
            items.Add(role.Name, name);
        }
        if (items.Count == 0) throw new InvalidDataException("Empty pack.");
        return items;
    }

    internal static Asset[] Preview(string manifestPath, bool cursor)
    {
        if (!Path.GetExtension(manifestPath).Equals(".json", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Expected a JSON pack manifest.");
        var roles = ParseRoles(ReadBounded(manifestPath, 16384), cursor);
        string folder = Path.GetDirectoryName(manifestPath)!;
        return roles.Select(role =>
        {
            string path = Path.Combine(folder, role.Value);
            return new Asset(role.Key, path, Convert.ToHexString(SHA256.HashData(ReadAsset(path, cursor, allowAnimated: cursor))));
        }).ToArray();
    }

    private static Value Read(Slot slot)
    {
        using var key = Registry.CurrentUser.OpenSubKey(slot.Key);
        object? raw = key?.GetValue(slot.Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (raw == null) return new(false, "", 0);
        var kind = key!.GetValueKind(slot.Name);
        if (raw is not string text || kind is not (RegistryValueKind.String or RegistryValueKind.ExpandString) || text.Length > 4096)
            throw new InvalidDataException("Unsupported existing registry value: " + slot.Name);
        return new(true, text, (int)kind);
    }
    private static void Write(Slot slot, Value value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(slot.Key);
        if (value.Present) key.SetValue(slot.Name, value.Text, (RegistryValueKind)value.Kind);
        else key.DeleteValue(slot.Name, false);
        if (Read(slot) != value) throw new IOException("Registry readback failed.");
    }

    private static string Store(byte[] data, string extension)
    {
        string path = Path.Combine(Root, Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant() + extension);
        if (!File.Exists(path))
        {
            if (Directory.EnumerateFiles(Root).Take(256).Count() >= 256)
                throw new InvalidDataException("Shell asset storage quota reached.");
            using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            output.Write(data); output.Flush(true);
        }
        if (!ReadBounded(path, FileLimit).AsSpan().SequenceEqual(data)) throw new InvalidDataException("Stored asset checksum mismatch.");
        return path;
    }
    private static void Refresh(bool cursor)
    {
        if (cursor) DeskCursorsEngine.RefreshCurrent(); else IconAtlasEngine.RefreshShellIcons();
    }
    internal static void Apply(string manifest, bool cursor, IReadOnlyList<Asset> approved)
    {
        lock (Gate)
        {
            CheckWritable();
            var items = Preview(manifest, cursor);
            if (!items.SequenceEqual(approved)) throw new InvalidDataException("Pack changed after preview.");
            Directory.CreateDirectory(Root);
            RequireUnlinkedLocal(Root);
            if (File.Exists(Journal(cursor))) throw new InvalidOperationException("Restore the previous pack before applying another.");
            var changes = new List<Change>();
            foreach (var item in items)
            {
                var slot = Resolve(item.Role, cursor);
                var data = ReadAsset(item.Source, cursor, allowAnimated: cursor);
                if (Convert.ToHexString(SHA256.HashData(data)) != item.Sha256) throw new InvalidDataException("Pack asset changed after preview.");
                string stored = Store(data, Path.GetExtension(item.Source).ToLowerInvariant());
                var next = new Value(true, stored + slot.Suffix, (int)RegistryValueKind.ExpandString);
                var prior = Read(slot);
                if (prior != next) changes.Add(new(item.Role, prior, next));
            }
            if (changes.Count == 0) return;
            // Keep an exact registry-value journal before the first change.
            AtomicFile.WriteAllText(Journal(cursor), JsonSerializer.Serialize(new Backup(cursor, changes.ToArray())));
            try
            {
                foreach (var change in changes)
                {
                    CheckWritable();
                    var slot = Resolve(change.Role, cursor);
                    if (Read(slot) != change.Original) throw new IOException("Registry changed during pack application.");
                    Write(slot, change.Applied);
                }
            }
            finally { Refresh(cursor); }
        }
    }

    private static bool ValidValue(Value? value) => value != null && value.Text != null &&
        value.Text.Length <= 4096 && (value.Present ? value.Kind == (int)RegistryValueKind.String ||
        value.Kind == (int)RegistryValueKind.ExpandString : value.Kind == 0 && value.Text.Length == 0);

    internal static void Restore(bool cursor)
    {
        lock (Gate)
        {
            // Explicit restore allowed even if compatibility review is active.
            string path = Journal(cursor);
            var backup = JsonSerializer.Deserialize<Backup>(ReadBounded(path, 65536)) ??
                throw new InvalidDataException("Invalid pack backup.");
            if (backup.Cursor != cursor || backup.Changes is not { Length: >= 1 and <= 6 } ||
                backup.Changes.Select(x => x.Role).Distinct(StringComparer.Ordinal).Count() != backup.Changes.Length)
                throw new InvalidDataException("Invalid pack backup.");
            // A partially applied pack is also recoverable. Never overwrite an
            // unrelated manual change; validate every slot before writing any.
            foreach (var change in backup.Changes)
            {
                var slot = Resolve(change.Role, cursor);
                if (!ValidValue(change.Original) || !ValidValue(change.Applied))
                    throw new InvalidDataException("Invalid backup value.");
                var current = Read(slot);
                if (current != change.Original && current != change.Applied)
                    throw new InvalidOperationException("A role was modified outside the pack; no settings overwritten.");
            }
            try
            {
                foreach (var change in backup.Changes.Reverse())
                {
                    var slot = Resolve(change.Role, cursor);
                    var current = Read(slot);
                    if (current != change.Original && current != change.Applied) throw new IOException("Registry changed during restore.");
                    if (current != change.Original) Write(slot, change.Original);
                }
                File.Delete(path);
            }
            finally { Refresh(cursor); }
        }
    }
}
