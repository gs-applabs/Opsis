using System.Runtime.InteropServices;

namespace MotionDesk.Services;

/// <summary>
/// Uses Windows' Authenticode chain and file-signature verification, not just
/// X509Certificate.CreateFromSignedFile (which does NOT verify a signature).
/// Revocation checks require a network request and are deliberately omitted;
/// the approval dialog says this clearly. Never equate "certificate present"
/// with "signature verified".
/// </summary>
internal static class AuthenticodeVerifier
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WintrustFileInfo
    {
        public uint StructSize;
        [MarshalAs(UnmanagedType.LPWStr)] public string FilePath;
        public IntPtr FileHandle;
        public IntPtr KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WintrustData
    {
        public uint StructSize;
        public IntPtr PolicyCallbackData;
        public IntPtr SIPClientData;
        public uint UIChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public IntPtr FileInfo;
        public uint StateAction;
        public IntPtr StateData;
        public IntPtr UrlReference;
        public uint ProviderFlags;
        public uint UIContext;
        public IntPtr SignatureSettings;
    }

    [DllImport("wintrust.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern uint WinVerifyTrust(IntPtr window, ref Guid action, ref WintrustData data);

    internal static string VerifyOffline(string path)
    {
        if (!OperatingSystem.IsWindows()) return "Verification requires Windows";
        IntPtr memory = IntPtr.Zero;
        try
        {
            var file = new WintrustFileInfo
            {
                StructSize = (uint)Marshal.SizeOf<WintrustFileInfo>(),
                FilePath = Path.GetFullPath(path),
                FileHandle = IntPtr.Zero,
                KnownSubject = IntPtr.Zero
            };
            memory = Marshal.AllocHGlobal(Marshal.SizeOf<WintrustFileInfo>());
            Marshal.StructureToPtr(file, memory, false);
            var data = new WintrustData
            {
                StructSize = (uint)Marshal.SizeOf<WintrustData>(),
                UIChoice = 2, // WTD_UI_NONE
                RevocationChecks = 0, // WTD_REVOKE_NONE
                UnionChoice = 1, // WTD_CHOICE_FILE
                FileInfo = memory,
                ProviderFlags = 0x1000 // WTD_CACHE_ONLY_URL_RETRIEVAL: no network during approval
            };
            var action = new Guid("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");
            uint result = WinVerifyTrust(IntPtr.Zero, ref action, ref data);
            return result switch
            {
                0 => "Verified Authenticode signature (offline; revocation not checked)",
                0x800B0100 => "Unsigned / Χωρίς υπογραφή",
                _ => $"Signature NOT verified by Windows (0x{result:X8})"
            };
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or
                                    UnauthorizedAccessException or IOException or ArgumentException)
        { return "Cannot verify signature: " + ex.GetType().Name; }
        finally
        {
            if (memory != IntPtr.Zero)
            {
                Marshal.DestroyStructure<WintrustFileInfo>(memory);
                Marshal.FreeHGlobal(memory);
            }
        }
    }
}
