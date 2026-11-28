using System;
using System.Windows.Forms;
using MotionDesk.Widgets;

namespace MotionDesk
{
    static class Program
    {
        [System.Runtime.InteropServices.DllImport("kernel32.dll")] private static extern bool AttachConsole(int pid);

        [STAThread]
        static void Main(string[] args)
        {
            if (Services.OpsisScreensaverHost.IsScreenSaverProcess)
            {
                Services.OpsisScreensaverHost.Run(args);
                return;
            }
            if (args.Length == 0 && Services.ElevationGuard.RelaunchNotElevated()) return;      // an elevated Opsis cannot receive drops from Explorer (UIPI): start again with normal rights
            // Ξ•Ξ»Ξ¬Ο‡ΞΉΟƒΟ„ΞΏΟ‚ elevated "helper" branch β€” ΞΊΞ±ΞΌΞ―Ξ± ΞΈΟ…ΟΞ―Ξ΄Ξ± UI, ΞΌΟΞ½ΞΏ ΞΌΞ―Ξ± ΞµΞ³Ξ³ΟΞ±Ο†Ξ® HKLM ΞΊΞ±ΞΉ
            // Ξ­ΞΎΞΏΞ΄ΞΏΟ‚. Ξ•Ξ½ΞµΟΞ³ΞΏΟ€ΞΏΞΉΞµΞ―Ο„Ξ±ΞΉ ΞΞΞΞ ΟΟ„Ξ±Ξ½ Ο„ΞΏ ΞΊΟΟΞΉΞΏ (ΞΌΞ·-elevated) UI ΞΎΞµΞΊΞΉΞ½Ξ®ΟƒΞµΞΉ ΞΎΞ±Ξ½Ξ¬ Ο„Ξ·Ξ½ Ξ―Ξ΄ΞΉΞ±
            // exe ΞΌΞµ Verb="runas" (Ξ²Ξ». LockScreenEngine.TrySetLockScreenImageElevated) β€” Ξ­Ο„ΟƒΞΉ Ξ΄ΞµΞ½
            // Ο‡ΟΞµΞΉΞ¬Ξ¶ΞµΟ„Ξ±ΞΉ ΞΏΞ»ΟΞΊΞ»Ξ·ΟΞ· Ξ· ΞµΟ†Ξ±ΟΞΌΞΏΞ³Ξ® Ξ½Ξ± Ο„ΟΞ­Ο‡ΞµΞΉ elevated Ξ³ΞΉΞ± ΞΌΞ―Ξ± ΞΌΟΞ½ΞΏ ΞµΞ½Ξ­ΟΞ³ΞµΞΉΞ±.
            if (args.Length == 2 && args[0] == "--set-lockscreen")
            {
                try { LockScreenEngine.WriteLockScreenRegistry(args[1]); } catch (Exception) { }
                return;
            }

            // ΞΞ΄ΞΉΞΏ ΞµΞ»Ξ¬Ο‡ΞΉΟƒΟ„ΞΏ elevated helper branch, Ξ³ΞΉΞ± Ο„Ξ·Ξ½ Ξ±Ξ½Ο„Ξ―ΟƒΟ„ΟΞΏΟ†Ξ· ΞµΞ½Ξ­ΟΞ³ΞµΞΉΞ±: Ξ΄ΞΉΞ±Ξ³ΟΞ±Ο†Ξ® Ο„Ο‰Ξ½ Ο„ΞΉΞΌΟΞ½
            // ΟΟƒΟ„Ξµ Ο„Ξ± Windows Ξ½Ξ± ΞΎΞ±Ξ½Ξ±Ξ΄ΞµΞ―Ο‡Ξ½ΞΏΟ…Ξ½ Ο„Ξ·Ξ½ Ο€ΟΞΏΞµΟ€ΞΉΞ»ΞµΞ³ΞΌΞ­Ξ½Ξ·/Ξ΄ΞΉΞΊΞ® Ο„ΞΏΟ…Ο‚ ΞµΟ€ΞΉΞ»ΞµΞ³ΞΌΞ­Ξ½Ξ· ΞΏΞΈΟΞ½Ξ·
            // ΞΊΞ»ΞµΞΉΞ΄ΟΞΌΞ±Ο„ΞΏΟ‚ Ξ±Ξ½Ο„Ξ― Ξ±Ο…Ο„Ξ® Ο€ΞΏΟ… ΟΟΞΉΟƒΞµ Ο„ΞΏ Opsis (Ξ²Ξ». LockScreenEngine.TryResetLockScreenElevated).
            if (args.Length == 1 && args[0] == "--reset-lockscreen")
            {
                try { LockScreenEngine.ClearLockScreenRegistry(); } catch (Exception) { }
                return;
            }

            // Headless self-test (v2.0 handoff, Ξ¦Ξ¬ΟƒΞ· 1/9) β€” Ξ”Ξ•Ξ Ο€ΞµΟΞ½Ξ¬ΞµΞΉ Ξ±Ο€Ο Ο„ΞΏ single-instance Mutex
            // ΞΏΟΟ„Ξµ Ξ±ΟΟ‡ΞΉΞΊΞΏΟ€ΞΏΞΉΞµΞ― UI/tray, ΟΟƒΟ„Ξµ Ξ½Ξ± ΞΌΟ€ΞΏΟΞµΞ― Ξ½Ξ± Ο„ΟΞ­ΞΎΞµΞΉ Ξ±Ο€Ο Ξ­Ξ½Ξ± build script Ξ±ΞΊΟΞΌΞ± ΞΊΞΉ Ξ±Ξ½
            // ΞΌΞΉΞ± ΞΊΞ±Ξ½ΞΏΞ½ΞΉΞΊΞ® ΞµΞΊΟ„Ξ­Ξ»ΞµΟƒΞ· Ο„ΞΏΟ… Opsis ΞµΞ―Ξ½Ξ±ΞΉ Ξ®Ξ΄Ξ· Ξ±Ξ½ΞΏΞΉΟ‡Ο„Ξ®.
            if (args.Length == 1 && args[0] == "--run-selftest")
            {
                Environment.Exit(Services.SelfTestRunner.Run());
                return;
            }

            // Ξ”ΞΉΞ±Ξ³Ξ½Ο‰ΟƒΟ„ΞΉΞΊΞ¬ ΞµΟ€ΞΉΟ†Ξ¬Ξ½ΞµΞΉΞ±Ο‚ ΞµΟΞ³Ξ±ΟƒΞ―Ξ±Ο‚/wallpaper/ΞΊΞ±ΞΉΟΞΏΟ (Ξ²Ξ». DesktopDiagnostics).
            if (args.Length >= 1 && args[0] == "--diag")
            {
                ApplicationConfiguration.Initialize();
                Environment.Exit(Services.DesktopDiagnostics.Run(args.Length > 1 && args[1] == "--show-desktop"));
                return;
            }

            // 2.6.0: read-only check of the "pause the wallpaper when the desktop is fully covered" decision on THIS machine (no window is created or moved):
            // prints which monitors are covered right now and what AppActivity says after its two-sample hysteresis.
            // 2.6.0 control channel of the new DeskContainers core (design doc phase 4/5): `Opsis.exe --containers <cmd> [json-args]` talks to the named pipe;
            // `Opsis.exe --containers-serve <data-file>` serves the pipe from that data file until a key is pressed (used for tests before the engine exists).
            // Rescue: puts the desktop icons back from the state file of THIS data folder (OPSIS_APPDATA or the normal AppData\Opsis), then exits. Safe to run any time, also when nothing is hidden.
            if (args.Length == 1 && args[0] == "--restore-desktop-icons")
            {
                AttachConsole(-1);
                Widgets.DesktopIconVisibilityEngine.Instance.RestoreIfHidden();
                Console.WriteLine("restore-desktop-icons: done (data folder: " + Services.AppPaths.AppDataFolder + ")");
                Environment.Exit(0);
                return;
            }
            if (args.Length == 2 && args[0] == "--test-icons-dump")
            {
                System.IO.File.WriteAllLines(args[1], Widgets.DesktopIconVisibilityEngine.DumpPositions());
                Environment.Exit(0);
                return;
            }
            // test of the originals mechanism on the real desktop: hides the icon of ONE existing desktop file, then either dies (crash) or gives it back; prints only counts
            if (args.Length == 3 && args[0] == "--test-originals")
            {
                var file = System.IO.Directory.EnumerateFiles(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)).FirstOrDefault(f => (System.IO.File.GetAttributes(f) & System.IO.FileAttributes.Hidden) == 0);
                if (file == null) { System.IO.File.WriteAllText(args[2], "no file on the desktop to test with"); Environment.Exit(4); return; }
                int n = Widgets.DesktopIconVisibilityEngine.HideOriginals(new[] { file });
                System.IO.File.WriteAllText(args[2], "hidden=" + n);
                if (args[1] == "die") System.Diagnostics.Process.GetCurrentProcess().Kill();
                n = Widgets.DesktopIconVisibilityEngine.RestoreOriginals();
                System.IO.File.AppendAllText(args[2], " restored=" + n);
                Environment.Exit(0);
                return;
            }
            if (args.Length == 1 && args[0] == "--test-icons-hide-and-die")
            {
                Widgets.DesktopIconVisibilityEngine.Instance.HideForTest();
                System.Diagnostics.Process.GetCurrentProcess().Kill();   // simulates a crash / kill: no Stop(), no cleanup
                return;
            }
            if (args.Length >= 2 && args[0] == "--containers")
            {
                AttachConsole(-1);   // Opsis.exe is a Windows-subsystem program: show the answer in the console that started it
                var pipe = Services.DesktopMenu.ContainerPipeName;
                try
                {
                    var json = args.Length > 2 ? System.Text.Json.Nodes.JsonNode.Parse(args[2]) : null;
                    Console.WriteLine(Opsis.Containers.PipeClient.Send(pipe, args[1], json).ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                    Environment.Exit(0);
                }
                catch (Exception ex) { Console.Error.WriteLine("containers: " + ex.Message); Environment.Exit(2); }
                return;
            }
            if (args.Length == 2 && args[0] == "--containers-serve")
            {
                AttachConsole(-1);
                var store = new Opsis.Containers.ContainersStore(args[1]);
                var mons = System.Windows.Forms.Screen.AllScreens.Select(s => new Opsis.Containers.MonitorInfo(s.DeviceName, s.Bounds, s.Primary)).ToList();
                using var server = new Opsis.Containers.PipeServer(Services.DesktopMenu.ContainerPipeName, new Opsis.Containers.ContainersService(store, () => mons));
                server.Start();
                Console.WriteLine("serving " + Services.DesktopMenu.ContainerPipeName + " - press Enter to stop");
                Console.ReadLine();
                Environment.Exit(0);
                return;
            }

            if (args.Length == 2 && args[0] == "--test-deskstrip-glass")
            {
                ApplicationConfiguration.Initialize();
                Environment.Exit(Services.DesktopDiagnostics.TestDeskStripGlass(args[1]));
                return;
            }

            if (args.Length == 2 && args[0] == "--test-widget-glass")
            {
                ApplicationConfiguration.Initialize();
                Environment.Exit(Services.DesktopDiagnostics.TestWidgetGlass(args[1]));
                return;
            }

            if (args.Length == 2 && args[0] == "--test-container-window")
            {
                ApplicationConfiguration.Initialize();
                Environment.Exit(Services.DesktopDiagnostics.TestContainerWindow(args[1]));
                return;
            }

            if (args.Length == 3 && args[0] == "--shot-help")
            {
                ApplicationConfiguration.Initialize();
                UI.MainWindow.SaveHelpPreview(args[1], int.Parse(args[2])); return;
            }
            if (args.Length == 2 && args[0] == "--test-container-shell")
            {
                ApplicationConfiguration.Initialize();
                Environment.Exit(Services.DesktopDiagnostics.TestContainerShellEvents(args[1]));
            }
            if (args.Length == 2 && args[0] == "--test-container-dnd")
            {
                ApplicationConfiguration.Initialize();
                Environment.Exit(Services.DesktopDiagnostics.TestContainerDragDrop(args[1]));
            }
            if (args.Length == 2 && args[0] == "--test-container-host")
            {
                ApplicationConfiguration.Initialize();
                Environment.Exit(Services.DesktopDiagnostics.TestContainerHost(args[1]));
                return;
            }

            string? desktopAction = null;
            if (args.Length == 3 && args[0] == "--setup-desktop-menu" && args[1] is "on" or "off")
            {
                try { Services.DesktopMenu.ApplyInstallerChoice(args[1] == "on", args[2] == "greek"); }
                catch (Exception ex)
                {
                    Services.RotatingLogger.Write("Explorer menu registration failed: " + ex.Message);
                    Environment.ExitCode = 1;
                }
                return;
            }
            if (args.Length > 0 && args[0] == "--desktop-action")
            {
                if (args.Length != 2 || !Services.DesktopMenu.Allowed.Contains(args[1])) return;
                if (Services.DesktopMenu.Send(args[1]) || args[1] == "ping" || args[1] == "stop-wallpaper") return;
                desktopAction = args[1];
            }
            if (args.Length == 2 && args[0] == "--test-container-zorder")
            {
                ApplicationConfiguration.Initialize();
                Environment.Exit(Services.DesktopDiagnostics.TestContainerZOrder(args[1]));
                return;
            }
            if (args.Length == 3 && args[0] == "--test-container-budget")
            {
                ApplicationConfiguration.Initialize();
                Environment.Exit(Services.DesktopDiagnostics.TestContainerBudget(args[1], int.Parse(args[2])));
                return;
            }
            if (args.Length == 3 && args[0] == "--shot-html")
            {
                ApplicationConfiguration.Initialize();
                Environment.Exit(Services.DesktopDiagnostics.ShotHtml(args[1], args[2]));
                return;
            }
            if (args.Length == 2 && args[0] == "--test-covered")
            {
                ApplicationConfiguration.Initialize();
                Environment.Exit(Services.DesktopDiagnostics.TestCovered(args[1]));
                return;
            }

            if (args.Length == 2 && args[0] == "--test-transparency")
            {
                ApplicationConfiguration.Initialize();
                Environment.Exit(Services.DesktopDiagnostics.TestTransparency(args[1]));
                return;
            }
            if (args.Length == 2 && args[0] == "--ui-audit")
            {
                ApplicationConfiguration.Initialize();
                Environment.SetEnvironmentVariable("OPSIS_E2E", "1");
                Environment.Exit(Services.UiAudit.Run(args[1]));
                return;
            }
            if (args.Length == 2 && args[0] == "--test-plugin-widget")
            {
                ApplicationConfiguration.Initialize();
                Environment.Exit(Services.DesktopDiagnostics.TestPluginWidget(args[1]));
                return;
            }
            if (args.Length == 3 && args[0] == "--test-widgets")
            {
                ApplicationConfiguration.Initialize();
                Environment.Exit(Services.DesktopDiagnostics.TestWidgets(args[1], args[2]));
                return;
            }
            if (args.Length == 3 && args[0] == "--test-deskstrip")
            {
                ApplicationConfiguration.Initialize();
                Environment.Exit(Services.DesktopDiagnostics.TestDeskStrip(args[1], args[2]));
                return;
            }
            if (args.Length == 2 && args[0] == "--test-wallpaper-icons")
            {
                ApplicationConfiguration.Initialize();
                Environment.Exit(Services.DesktopDiagnostics.TestWallpaperIcons(args[1]));
                return;
            }
            if (args.Length == 3 && args[0] == "--test-audio-detect")        // --test-audio-detect <seconds> <logfile>: polls the other-app audio detector
            {
                using var det = new Services.OtherAudioDetector();
                var lines = new System.Collections.Generic.List<string>();
                var end = DateTime.UtcNow.AddSeconds(int.Parse(args[1]));
                while (DateTime.UtcNow < end) { lines.Add(DateTime.Now.ToString("HH:mm:ss.fff") + " otherAppPlaying=" + det.IsOtherAppPlaying()); System.Threading.Thread.Sleep(300); }
                System.IO.File.WriteAllLines(args[2], lines);
                Environment.Exit(0);
                return;
            }
            if (args.Length >= 1 && args[0] == "--test-iconhide")
            {
                ApplicationConfiguration.Initialize();
                Environment.Exit(Services.DesktopDiagnostics.TestIconHide(args.Length > 1 ? args[1] : ""));
                return;
            }
            if (args.Length == 2 && args[0] == "--render-sysmon")
            {
                ApplicationConfiguration.Initialize();
                Environment.Exit(Services.DesktopDiagnostics.RenderSysmon(args[1]));
                return;
            }

            // Single-instance: Ξ΄ΟΞΏ Ο„Ξ±Ο…Ο„ΟΟ‡ΟΞΏΞ½ΞµΟ‚ ΞµΞΊΟ„ΞµΞ»Ξ­ΟƒΞµΞΉΟ‚ ΞΈΞ± Ο€Ξ¬Ξ»ΞµΟ…Ξ±Ξ½ Ξ³ΞΉΞ± Ο„Ξ± Ξ―Ξ΄ΞΉΞ± global hotkeys,
            // ΞΈΞ± Ξ­Ο†Ο„ΞΉΞ±Ο‡Ξ½Ξ±Ξ½ Ξ΄ΞΉΟ€Ξ»Ξ¬ wallpaper windows/tray icons ΞΊΞ±ΞΉ ΞΈΞ± Ξ­Ξ³ΟΞ±Ο†Ξ±Ξ½ Ο„Ξ± Ξ―Ξ΄ΞΉΞ± config Ξ±ΟΟ‡ΞµΞ―Ξ±.
            // An isolated data folder (OPSIS_APPDATA, used by automated tests) gets its own mutex, so a test run never collides with the user's real instance.
            string isolatedData = Environment.GetEnvironmentVariable("OPSIS_APPDATA") ?? "";
            // String.GetHashCode is randomized per process; use a stable digest of the normalized data path.
            string mutexName = isolatedData.Length == 0 ? @"Local\Opsis.SingleInstance" :
                @"Local\Opsis.SingleInstance." + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(isolatedData)).ToUpperInvariant())));
            using var singleInstance = new System.Threading.Mutex(true, mutexName, out bool isFirst);
            if (!isFirst) { if (desktopAction != null) Services.DesktopMenu.Send(desktopAction); return; }

            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => ReportError(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => ReportError(e.ExceptionObject as Exception);
            Services.RotatingLogger.HookUnobservedTaskExceptions();

            // Safe mode (v2.0 handoff, Ξ¦Ξ¬ΟƒΞ· 1 Β§S) β€” Ο€ΟΞ­Ο€ΞµΞΉ Ξ½Ξ± Ξ±ΞΎΞΉΞΏΞ»ΞΏΞ³Ξ·ΞΈΞµΞ― ΞΞ£Ξ Ξ¤Ξ Ξ”Ξ¥ΞΞ‘Ξ¤ΞΞ
            // Ξ½Ο‰ΟΞ―Ο„ΞµΟΞ±, Ο€ΟΞΉΞ½ Ξ±ΟΟ‡ΞΉΞΊΞΏΟ€ΞΏΞΉΞ·ΞΈΞµΞ― ΞΏΟ„ΞΉΞ΄Ξ®Ο€ΞΏΟ„Ξµ Ξ²Ξ±ΟΟ (wallpaper, plugins). Ξ¤ΞΏ
            // TrayApplicationContext Ξ΄ΞΉΞ±Ξ²Ξ¬Ξ¶ΞµΞΉ Ο„ΞΏ Services.SafeModeService.IsSafeMode Ο€Ξ±ΟΞ±ΞΊΞ¬Ο„Ο‰.
            Services.SafeModeService.EvaluateAtStartup();
            Services.RotatingLogger.Write($"Startup: SafeMode={Services.SafeModeService.IsSafeMode} (shift={Services.SafeModeService.TriggeredByShift}, failedStartups={Services.SafeModeService.ConsecutiveFailedStartups})");
            Services.RotatingLogger.Write($"Hardware: tier={Services.HardwareProfileService.CurrentTier}, cores={Environment.ProcessorCount}");
            try { Services.RotatingLogger.Write("Hardware detail: " + Services.HardwareInfo.Detect()); } catch (Exception) { }
            Services.RuntimeSessionPolicy.SetAllowed(Services.RuntimeSessionPolicy.StartupMask(Services.AppSettings.Peek().ReducedStartup));
            Services.PerformanceGovernor.Start();
            Services.PluginUiStyle.Publish();
            Services.PerformanceGovernor.EffectiveChanged += t =>
            {
                Services.RotatingLogger.Write("Performance: effective tier is now " + t);
                try { MotionDesk.Widgets.WallpaperHostEngine.QueuePerfIfCreated(); } catch (Exception) { }
            };

            // Ξ¤ΞΏ .NET 5+ Ξ΄ΞµΞ½ Ο€ΞµΟΞΉΞ»Ξ±ΞΌΞ²Ξ¬Ξ½ΞµΞΉ Ο€Ξ»Ξ­ΞΏΞ½ Ο„ΞΉΟ‚ legacy ΞΊΟ‰Ξ΄ΞΉΞΊΞΏΟƒΞµΞ»Ξ―Ξ΄ΞµΟ‚ (Ο€.Ο‡. 737 Ξ³ΞΉΞ± ΞµΞ»Ξ»Ξ·Ξ½ΞΉΞΊΞ¬
            // OEM/console) by default β€” Ο‡Ο‰ΟΞ―Ο‚ Ξ±Ο…Ο„Ξ® Ο„Ξ·Ξ½ ΞµΞ³Ξ³ΟΞ±Ο†Ξ®, Encoding.GetEncoding(737) Ο€ΞµΟ„Ξ¬ΞµΞΉ
            // NotSupportedException. Ξ§ΟΞµΞΉΞ¬Ξ¶ΞµΟ„Ξ±ΞΉ Ξ³ΞΉΞ± ΟƒΟ‰ΟƒΟ„Ξ® Ξ±Ξ½Ξ¬Ξ³Ξ½Ο‰ΟƒΞ· Ο„Ξ·Ο‚ ΞµΞΎΟΞ΄ΞΏΟ… ΞΊΞΏΞ½ΟƒΟΞ»Ξ±Ο‚ ΞµΟΞ³Ξ±Ξ»ΞµΞ―Ο‰Ξ½
            // ΟƒΞ±Ξ½ Ο„ΞΏ powercfg (SystemControlService.GetPowerPlans) ΟƒΞµ ΞΌΞ·-Ξ±Ξ³Ξ³Ξ»ΞΉΞΊΞ¬ Windows.
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

            // Sync Ξ· Taskbar translucency (GlassBlur.SetTaskbarTranslucency) ΞΌΞµ Ο„Ξ·Ξ½ Ο„ΟΞ­Ο‡ΞΏΟ…ΟƒΞ± Ο„ΞΉΞΌΞ® Ο„ΞΏΟ…
            // Windows "Ξ•Ο†Ξ­ Ξ΄ΞΉΞ±Ο†Ξ¬Ξ½ΞµΞΉΞ±Ο‚" ΟƒΟ„Ξ·Ξ½ ΞµΞΊΞΊΞ―Ξ½Ξ·ΟƒΞ· β€” Ο‡Ο‰ΟΞ―Ο‚ Ξ±Ο…Ο„Ο, Ο„ΞΏ blur Ο„Ξ·Ο‚ Taskbar ΞΈΞ± ΞµΟ†Ξ±ΟΞΌΞΏΞ¶ΟΟ„Ξ±Ξ½
            // ΞΌΟΞ½ΞΏ Ξ±Ο†ΞΏΟ ΞΏ Ο‡ΟΞ®ΟƒΟ„Ξ·Ο‚ ΟƒΞ²Ξ®ΟƒΞµΞΉ/ΞΎΞ±Ξ½Ξ±Ο„ΟƒΞµΞΊΞ¬ΟΞµΞΉ Ο„ΞΏ checkbox ΟƒΟ„ΞΉΟ‚ Ξ΅Ο…ΞΈΞΌΞ―ΟƒΞµΞΉΟ‚ ΞΌΞ―Ξ± Ο†ΞΏΟΞ¬.
            if (Services.RuntimeSessionPolicy.IsPermitted(Services.SessionBackgroundService.AppearanceAutomatic))
                try { MotionDesk.UI.GlassBlur.SetTaskbarTranslucency(Services.WindowsSystemSettings.GetTransparencyEffectsEnabled()); } catch (Exception) { }

            ApplicationConfiguration.Initialize();
            MotionDesk.UI.SecondaryWindowStyler.Install();
            Application.Run(new TrayApplicationContext(desktopAction));
        }

        private static DateTime _lastErrorDialog = DateTime.MinValue;

        internal static bool IsTransient(Exception ex)
        {
            for (var e = ex; e != null; e = e.InnerException)
            {
                if (e is InvalidOperationException && e.Message.Contains("modified", StringComparison.OrdinalIgnoreCase)) return true;      // "Collection was modified; enumeration operation may not execute."
                if (e is ObjectDisposedException) return true;
            }
            return false;
        }

        private static void ReportError(Exception? ex)
        {
            if (ex == null) return;
            try
            {
                var logPath = System.IO.Path.Combine(Services.AppPaths.AppDataFolder, "crash.log");
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(logPath)!);
                System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:O}] {ex}\n\n");
            }
            catch (Exception) { }
            Services.RotatingLogger.WriteException("UnhandledException", ex);

            // A transient race (a list changed by another thread while the tray menu or a widget was reading it) is logged and retried by the next refresh; it must never
            // pop up a dialog in the user's face. Same for a window that closed while a timer ticked.
            if (IsTransient(ex)) return;

            // ΞΞ½Ξ± exception ΞΌΞ­ΟƒΞ± ΟƒΞµ timer tick ΞµΟ€Ξ±Ξ½Ξ±Ξ»Ξ±ΞΌΞ²Ξ¬Ξ½ΞµΟ„Ξ±ΞΉ ΞΊΞ¬ΞΈΞµ tick β€” Ο‡Ο‰ΟΞ―Ο‚ ΟΟΞΉΞΏ, ΞΏ Ο‡ΟΞ®ΟƒΟ„Ξ·Ο‚ Ξ­Ξ²Ξ»ΞµΟ€Ξµ
            // Ξ±Ο„Ξ­Ξ»ΞµΞΉΟ‰Ο„Ξ· Ξ±Ξ»Ξ»Ξ·Ξ»ΞΏΟ…Ο‡Ξ―Ξ± modal Ξ΄ΞΉΞ±Ξ»ΟΞ³Ο‰Ξ½. ΞΞ½Ξ±Ο‚ Ξ΄ΞΉΞ¬Ξ»ΞΏΞ³ΞΏΟ‚ Ξ±Ξ½Ξ¬ 30s Ξ±ΟΞΊΞµΞ― (ΟΞ»Ξ± ΞΊΞ±Ο„Ξ±Ξ³ΟΞ¬Ο†ΞΏΞ½Ο„Ξ±ΞΉ ΟƒΟ„ΞΏ crash.log).
            if ((DateTime.Now - _lastErrorDialog).TotalSeconds < 30) return;
            _lastErrorDialog = DateTime.Now;

            MessageBox.Show(string.Format(MotionDesk.Services.LocalizationManager.T("Dialog.UnhandledErrorFormat"), ex.Message), "Opsis",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}

