using System.Security.Principal;
using System.Text.Json;
using Microsoft.Win32;
using Opsis.Lockscreen;

// OpsisLockHelper.exe apply | restore | disable-feature
// Started with UAC by Opsis (or by the uninstaller for "restore"). The only input is ONE command word; the staged GIF is read from the invoking user's data folder,
// everything it writes (backup, last result) goes to an admin-only folder under ProgramData. No UI, no network, no other arguments.
internal static class Program
{
    private static int Main(string[] args)
    {
        if (!HelperCommand.TryParse(args, out var op)) return 2;
        string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        string backup = LockscreenPaths.BackupRoot(programData);
        OpResult res;
        try
        {
            using var id = WindowsIdentity.GetCurrent();
            string sid = id.User!.Value;
            SecureFolder.Ensure(Path.GetDirectoryName(backup)!);
            SecureFolder.Ensure(backup);
            string data = LockscreenPaths.DataRoot(HelperCommand.ResolveAppData());
            var ops = new LockscreenOperations(data, backup, LockscreenPaths.DefaultCacheRoot(sid, programData), sid, new SystemAclTool(), new RegistryFeatureStore(Registry.LocalMachine));
            res = HelperCommand.Run(op, ops);
        }
        catch (Exception ex) { res = OpResult.Fail("Failed", ex.Message); }
        try { File.WriteAllText(LockscreenPaths.ResultPath(backup), JsonSerializer.Serialize(new { op = op.ToString(), res.Ok, res.Code, res.Message, res.Details, utc = DateTime.UtcNow })); }
        catch (Exception) { }
        return res.Ok ? 0 : 1;
    }
}
