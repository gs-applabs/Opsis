using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Win32;
using Opsis.Lockscreen;

namespace MotionDesk.Services
{
    /// <summary>
    /// Animated lock screen (GIF), app side. Opsis never runs elevated: the GIF is staged in %AppData%\Opsis\lockscreen and a separate minimal helper
    /// (OpsisLockHelper.exe, one command word) does the system-level work after the user's UAC approval. Opt-in (AppSettings.LockscreenGifConsent), backed up, restorable.
    /// Logs go to Opsis's own log; there is no telemetry of any kind.
    /// </summary>
    public static class LockscreenGifService
    {
        public static string DataRoot => LockscreenPaths.DataRoot(AppPaths.AppDataFolder);
        public static string BackupRoot => LockscreenPaths.BackupRoot(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));
        public static string HelperPath => Path.Combine(AppContext.BaseDirectory, "OpsisLockHelper.exe");
        /// <summary>Isolated test runs (OPSIS_APPDATA) must never elevate or touch the real system.</summary>
        public static bool Isolated => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPSIS_APPDATA"));

        public sealed record Status(LockScreenMode Mode, bool CachePresent, bool? FeatureDisabled, bool FeatureApplies, int Build, bool HasBackup, bool Applied, bool HelperPresent);

        public static Status GetStatus()
        {
            string sid = WindowsIdentity.GetCurrent().User?.Value ?? "S-1-5-18";
            string cache = LockscreenPaths.DefaultCacheRoot(sid, Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));
            int build = Environment.OSVersion.Version.Build;
            bool? disabled = null;
            try { disabled = new RegistryFeatureStore(Registry.LocalMachine).IsDisabled(); } catch (Exception) { }
            var st = LockscreenState.Read(BackupRoot);
            return new Status(Prerequisites.InferMode(), Prerequisites.CachePresent(cache), disabled, Prerequisites.FeatureApplies(build), build, st.HasBackup, st.Applied, File.Exists(HelperPath));
        }

        /// <summary>Copies the chosen GIF into the staging folder (the only file the helper will ever read). Throws on a non-GIF or an oversized file.</summary>
        public static void Stage(string gifPath)
        {
            var fi = new FileInfo(gifPath);
            if (!fi.Exists) throw new FileNotFoundException(gifPath);
            if (fi.Length == 0 || fi.Length > LockscreenPaths.MaxGifBytes) throw new InvalidDataException("The GIF is empty or larger than " + LockscreenPaths.MaxGifBytes / 1048576 + " MB.");
            var head = new byte[6];
            using (var s = fi.OpenRead()) { if (s.Read(head, 0, 6) < 6 || !LockscreenPaths.IsGif(head)) throw new InvalidDataException("The file is not a GIF."); }
            string dest = LockscreenPaths.StagingGif(DataRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(gifPath, dest, true);
            RotatingLogger.Write("Lockscreen GIF staged (" + fi.Length + " bytes)");
        }

        public sealed record HelperOutcome(bool Ok, bool Cancelled, string Code, string Message);

        /// <summary>Starts the helper with UAC and waits. op is one of: apply | restore | disable-feature.</summary>
        public static Task<HelperOutcome> RunHelperAsync(string op, bool? isolatedOverride = null) => Task.Run(() =>
        {
            if (isolatedOverride ?? Isolated) return new HelperOutcome(false, false, "Isolated", "Isolated test run: the elevated helper is never started.");
            if (!HelperCommand.TryParse(new[] { op }, out _)) return new HelperOutcome(false, false, "BadCommand", op);
            if (!File.Exists(HelperPath)) return new HelperOutcome(false, false, "NoHelper", "OpsisLockHelper.exe is missing.");
            try
            {
                string resultFile = LockscreenPaths.ResultPath(BackupRoot);
                DateTime before = File.Exists(resultFile) ? File.GetLastWriteTimeUtc(resultFile) : DateTime.MinValue;
                var psi = new ProcessStartInfo(HelperPath) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
                psi.ArgumentList.Add(op);
                using var p = Process.Start(psi) ?? throw new IOException("The helper did not start");
                if (!p.WaitForExit(120_000)) { try { p.Kill(true); } catch (Exception) { } return new HelperOutcome(false, false, "Timeout", "The helper timed out."); }
                string code = p.ExitCode == 0 ? "Ok" : "Failed", msg = "exit " + p.ExitCode;
                try
                {
                    if (File.Exists(resultFile) && File.GetLastWriteTimeUtc(resultFile) >= before)
                    {
                        using var doc = JsonDocument.Parse(File.ReadAllText(resultFile));
                        code = doc.RootElement.GetProperty("Code").GetString() ?? code; msg = doc.RootElement.GetProperty("Message").GetString() ?? msg;
                        if (doc.RootElement.TryGetProperty("Details", out var d)) foreach (var line in d.EnumerateArray()) RotatingLogger.Write("Lockscreen helper: " + line.GetString());
                    }
                }
                catch (Exception) { }
                RotatingLogger.Write("Lockscreen helper '" + op + "' -> " + code + ": " + msg);
                return new HelperOutcome(p.ExitCode == 0, false, code, msg);
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)       // the user declined the UAC prompt
            {
                RotatingLogger.Write("Lockscreen helper '" + op + "': UAC declined");
                return new HelperOutcome(false, true, "Cancelled", "Administrator approval was declined.");
            }
            catch (Exception ex)
            {
                RotatingLogger.WriteException("Lockscreen helper '" + op + "'", ex);
                return new HelperOutcome(false, false, "Failed", ex.Message);
            }
        });

        /// <summary>Opsis.exe --restore-lockscreen: nothing to restore = success; otherwise the helper's restore (with UAC). Runs without any window.</summary>
        public static int RestoreFromCommandLine()
        {
            try
            {
                if (!LockscreenState.Read(BackupRoot).HasBackup) return 0;
                var r = RunHelperAsync("restore").GetAwaiter().GetResult();
                return r.Ok ? 0 : r.Cancelled ? 3 : 1;
            }
            catch (Exception ex) { RotatingLogger.WriteException("--restore-lockscreen", ex); return 1; }
        }

        // ---- video to GIF (FFmpeg, user supplied) ----------------------------------------------------------------------------------------------------
        public static async Task<(bool Ok, string Message)> ConvertVideoAsync(string ffmpeg, FfmpegGif.Options o, string outGif)
        {
            if (!FfmpegGif.LooksLikeFfmpeg(ffmpeg)) return (false, "ffmpeg.exe");
            string tmp = Path.Combine(Path.GetTempPath(), "opsis-gif-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(tmp);
            try
            {
                string palette = Path.Combine(tmp, "palette.png");
                string temp = Path.Combine(tmp, "out.gif");
                var (c1, e1) = await RunFfmpegAsync(ffmpeg, FfmpegGif.PaletteArgs(o, palette));
                if (c1 != 0 || !File.Exists(palette)) return (false, e1);
                var (c2, e2) = await RunFfmpegAsync(ffmpeg, FfmpegGif.GifArgs(o, palette, temp));
                if (c2 != 0 || !File.Exists(temp)) return (false, e2);
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outGif))!);
                File.Copy(temp, outGif, true);
                RotatingLogger.Write("Video converted to GIF (" + new FileInfo(outGif).Length + " bytes)");
                return (true, "");
            }
            catch (Exception ex) { RotatingLogger.WriteException("Video to GIF", ex); return (false, ex.Message); }
            finally { try { Directory.Delete(tmp, true); } catch (Exception) { } }
        }

        public static async Task<bool> IsFfmpegAsync(string ffmpeg)
        {
            if (!FfmpegGif.LooksLikeFfmpeg(ffmpeg)) return false;
            var (code, text) = await RunFfmpegAsync(ffmpeg, new System.Collections.Generic.List<string> { "-version" }, true);
            return code == 0 && FfmpegGif.VersionOutputIsFfmpeg(text);
        }

        private static async Task<(int Code, string Text)> RunFfmpegAsync(string ffmpeg, System.Collections.Generic.List<string> args, bool stdout = false)
        {
            var psi = new ProcessStartInfo(ffmpeg) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi) ?? throw new IOException("ffmpeg did not start");
            var o = p.StandardOutput.ReadToEndAsync(); var e = p.StandardError.ReadToEndAsync();
            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromMinutes(5));
            try { await p.WaitForExitAsync(cts.Token); }
            catch (OperationCanceledException) { try { p.Kill(true); } catch (Exception) { } return (-1, "ffmpeg timed out"); }
            string text = stdout ? await o : await e;
            return (p.ExitCode, text.Length > 2000 ? text[..2000] : text);
        }
    }
}
