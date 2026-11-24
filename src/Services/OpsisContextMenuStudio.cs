using System;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace MotionDesk.Services;

/// <summary>
/// HKCU-only Explorer context menu integration. Never touches legacy shell DLLs,
/// global context handlers, protected Windows files or third-party keys.
/// An explicit Opsis owner marker prevents deletion of a user/vendor menu.
/// </summary>
public static class OpsisContextMenuStudio
{
    private const string Owner = "gs-applabs/Opsis:context-menu:v1";
    private const string MenuKey = "OpsisDesktopStudio";
    private static readonly string[] Roots =
    {
        @"Software\Classes\DesktopBackground\Shell",
        @"Software\Classes\Directory\Background\shell"
    };
    private const string OwnerValue = "OpsisOwner";

    public static bool IsEnabled()
    {
        return Roots.All(root =>
        {
            using var key = Registry.CurrentUser.OpenSubKey(root + "\\" + MenuKey);
            return string.Equals(key?.GetValue(OwnerValue) as string, Owner, StringComparison.Ordinal);
        });
    }

    /// <summary>Do not allow an arbitrary user-supplied executable or command line.</summary>
    private static string VerifyOpsisExe(string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate) || !Path.IsPathFullyQualified(candidate) ||
            !string.Equals(Path.GetFileName(candidate), "Opsis.exe", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(candidate) || candidate.Contains('"'))
            throw new InvalidOperationException("A trusted installed Opsis.exe is required for shell integration.");
        return Path.GetFullPath(candidate);
    }

    public static string DefaultExecutable => Path.Combine(AppContext.BaseDirectory, "Opsis.exe");

    /// <summary>
    /// Applies two launcher entries. If another product owns either key,
    /// abort without touching any existing value.
    /// </summary>
    public static void Enable(string? opsisExe = null)
    {
        var exe = VerifyOpsisExe(opsisExe ?? DefaultExecutable);
        foreach (var root in Roots)
        {
            using var existing = Registry.CurrentUser.OpenSubKey(root + "\\" + MenuKey);
            if (existing != null && !string.Equals(existing.GetValue(OwnerValue) as string, Owner, StringComparison.Ordinal))
                throw new InvalidOperationException("A different context menu entry already uses the Opsis key.");
        }
        foreach (var root in Roots)
        {
            var name = root + "\\" + MenuKey;
            using var key = Registry.CurrentUser.CreateSubKey(name)
                ?? throw new IOException("Unable to open current user context menu key.");
            key.SetValue(OwnerValue, Owner, RegistryValueKind.String);
            key.SetValue("MUIVerb", "Opsis Desktop Studio", RegistryValueKind.String);
            key.SetValue("Icon", exe + ",0", RegistryValueKind.String);
            // Standard shell menu for the current user, not a context menu handler DLL.
            using var command = key.CreateSubKey("command")
                ?? throw new IOException("Unable to create launcher command.");
            command.SetValue("", "\"" + exe + "\"", RegistryValueKind.String);
        }
    }

    /// <summary>Remove only entries created by this version of Opsis.</summary>
    public static void Disable()
    {
        foreach (var root in Roots)
        {
            var path = root + "\\" + MenuKey;
            using (var key = Registry.CurrentUser.OpenSubKey(path))
            {
                if (key == null) continue;
                if (!string.Equals(key.GetValue(OwnerValue) as string, Owner, StringComparison.Ordinal))
                    throw new InvalidOperationException("Context menu entry is not owned by Opsis.");
            }
            Registry.CurrentUser.DeleteSubKeyTree(path, false);
        }
    }

    public static string Describe()
    {
        return IsEnabled()
            ? "Opsis launch commands are registered for the desktop and Explorer folder background (HKCU)."
            : "Opsis context menu commands are not fully registered. No Windows system files are modified.";
    }
}
