using System.IO.Pipes;
using System.Text.Json.Nodes;
using Microsoft.Win32;
using Opsis.Containers;

namespace MotionDesk.Services
{
    /// <summary>
    /// Desktop right-click entries (plan 0B.3), opt-in and per-user (HKCU, no elevation): "New DeskContainer" and "Next animated wallpaper".
    /// Basis (Microsoft Learn, "Creating Shortcut Menu Handlers" / "Add a File Explorer context menu command to a packaged desktop app"): static verbs under
    /// DesktopBackground\Shell are the documented classic mechanism and appear in the classic menu ("Show more options" / Shift+right-click on Windows 11); the NEW Windows 11
    /// top-level menu needs an IExplorerCommand COM handler registered with package identity (sparse package + code signing), which is not done here.
    /// So the entries may be one step deeper on Windows 11 - the same actions stay available from the tray menu and the DeskStrip, and the wallpaper keys.
    /// </summary>
    public static class DesktopMenu
    {
        private const string Root = @"Software\Classes\DesktopBackground\Shell\";
        private static readonly (string Key, string Title, string Action)[] Verbs =
        {
            ("OpsisNewContainer", "Opsis: New DeskContainer", "new-container"),
            ("OpsisNextWallpaper", "Opsis: Next animated wallpaper", "next-video"),
        };

        public static string ExePath => Environment.ProcessPath ?? "";

        public static void Register(string exe)
        {
            foreach (var (key, title, action) in Verbs)
            {
                using var k = Registry.CurrentUser.CreateSubKey(Root + key);
                k.SetValue("MUIVerb", title); k.SetValue("Icon", "\"" + exe + "\"");
                using var c = k.CreateSubKey("command"); c.SetValue("", "\"" + exe + "\" --desktop-action " + action);
            }
        }

        public static void Unregister()
        {
            foreach (var (key, _, _) in Verbs) { try { Registry.CurrentUser.DeleteSubKeyTree(Root + key, false); } catch (Exception) { } }
        }

        public static bool IsRegistered() { try { using var k = Registry.CurrentUser.OpenSubKey(Root + Verbs[0].Key); return k != null; } catch (Exception) { return false; } }

        public static void Apply(bool enabled) { if (enabled) Register(ExePath); else Unregister(); }

        // ---- control channel: a second `Opsis.exe --desktop-action <x>` process tells the running instance what to do ----
        public static string PipeName => "opsis-control-v1-" + Environment.UserName.ToLowerInvariant();

        public static readonly HashSet<string> Allowed = new() { "new-container", "next-video", "prev-video", "toggle-video", "ping" };

        /// <summary>Client side (the verb process). Returns false when no Opsis instance answered.</summary>
        public static bool Send(string action)
        {
            try { var r = PipeClient.Send(PipeName, action, null, 2000); return r["ok"]?.GetValue<bool>() == true; }
            catch (Exception) { return false; }
        }

        /// <summary>Server side, in the running instance: only the whitelisted actions are accepted, and only from this user (the pipe has a per-user ACL).</summary>
        public sealed class Api : IContainersApi
        {
            private readonly System.Threading.SynchronizationContext? _ui = System.Threading.SynchronizationContext.Current;
            public JsonNode? Handle(string cmd, JsonObject args)
            {
                if (!Allowed.Contains(cmd)) throw new PipeException("unknown-command", "not allowed");
                if (cmd != "ping") _ui?.Post(_ => Run(cmd), null);
                return new JsonObject { ["accepted"] = cmd };
            }

            private static void Run(string cmd)
            {
                try
                {
                    switch (cmd)
                    {
                        case "next-video": MotionDesk.Widgets.WallpaperHostEngine.Instance.StepVideo(true); break;
                        case "prev-video": MotionDesk.Widgets.WallpaperHostEngine.Instance.StepVideo(false); break;
                        case "toggle-video": MotionDesk.Widgets.WallpaperHostEngine.Instance.TogglePause(); break;
                        case "new-container":
                            if (MotionDesk.Containers.ContainerHost.Current is { } host)
                            {
                                var p = System.Windows.Forms.Cursor.Position;
                                host.Service.Handle("create", new JsonObject { ["title"] = "New container", ["x"] = p.X, ["y"] = p.Y, ["width"] = 320, ["height"] = 240 });
                            }
                            else ContainersBridge.Command("new-container");
                            break;
                    }
                }
                catch (Exception ex) { RotatingLogger.Write("desktop action '" + cmd + "' failed: " + ex.Message); }
            }
        }
    }
}
