using System.Security.AccessControl;
using System.Security.Principal;

namespace Opsis.Lockscreen;

/// <summary>
/// The backup lives under ProgramData, where every user may create folders: before the elevated helper trusts a folder there it must be owned by SYSTEM / Administrators
/// (a folder a user pre-created is refused). A new folder gets an explicit ACL: SYSTEM and Administrators full control, Users read only (the UI shows the status).
/// </summary>
public static class SecureFolder
{
    public static readonly string[] DefaultTrustedOwners = { "S-1-5-18", "S-1-5-32-544" };

    public static void Ensure(string dir, IEnumerable<string>? trustedOwners = null)
    {
        var trusted = new HashSet<string>(trustedOwners ?? DefaultTrustedOwners, StringComparer.OrdinalIgnoreCase);
        dir = Path.GetFullPath(dir);
        if (Directory.Exists(dir))
        {
            LockscreenPaths.RejectLinks(dir, dir);
            var owner = new DirectoryInfo(dir).GetAccessControl(AccessControlSections.Owner).GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
            if (owner == null || !trusted.Contains(owner.Value)) throw new UnauthorizedAccessException("The folder is not owned by the system or the administrators: " + dir);
            return;
        }
        Directory.CreateDirectory(dir);
        var sec = new DirectorySecurity();
        sec.SetAccessRuleProtection(true, false);
        var inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        sec.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier("S-1-5-18"), FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        sec.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier("S-1-5-32-544"), FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        sec.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier("S-1-5-32-545"), FileSystemRights.ReadAndExecute, inherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(dir).SetAccessControl(sec);
    }
}
