namespace Opsis.Lockscreen;

public enum HelperOp { Apply, Restore, DisableFeature }

/// <summary>
/// The whole command line of the elevated helper: exactly ONE word from a fixed list and nothing else. Paths, SIDs and values are never taken from arguments
/// (they are derived from the data folder and the current user), so there is nothing to inject.
/// </summary>
public static class HelperCommand
{
    public static bool TryParse(string[] args, out HelperOp op)
    {
        op = default;
        if (args.Length != 1) return false;
        switch (args[0])
        {
            case "apply": op = HelperOp.Apply; return true;
            case "restore": op = HelperOp.Restore; return true;
            case "disable-feature": op = HelperOp.DisableFeature; return true;
            default: return false;
        }
    }

    public static OpResult Run(HelperOp op, LockscreenOperations ops) => op switch
    {
        HelperOp.Apply => ops.Apply(),
        HelperOp.Restore => ops.Restore(),
        _ => ops.DisableFeature(),
    };

    /// <summary>The data folder of the invoking user: OPSIS_APPDATA when set (tests), otherwise %AppData%\Opsis.</summary>
    public static string ResolveAppData()
    {
        string? iso = Environment.GetEnvironmentVariable("OPSIS_APPDATA");
        return !string.IsNullOrWhiteSpace(iso) ? iso : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Opsis");
    }
}
