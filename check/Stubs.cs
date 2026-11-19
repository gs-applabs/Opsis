namespace MotionDesk.Services
{
    public static class LocalizationManager { public static string CurrentLanguage => "el"; }
    public static class RotatingLogger { public static void Write(string message) => Console.WriteLine(message); }
    public sealed class AppSettings
    {
        private static readonly AppSettings Instance = new();
        public static AppSettings Load() => Instance;
        public bool DesktopMenuEnabled { get; set; }
        public void Save() { }
    }
}
namespace MotionDesk.Containers
{
    public sealed class ContainerHost
    {
        public static ContainerHost Current { get; } = new();
        public int Count { get; private set; }
        public void NewContainerAtCursor(string kind = "Data", string? title = null) => Count++;
    }
}
namespace MotionDesk.Widgets
{
    public sealed class WallpaperHostEngine
    {
        public static WallpaperHostEngine Instance { get; } = new();
        public readonly List<string> Calls = new();
        public void StepWallpaper(bool forward) => Calls.Add(forward ? "next" : "previous");
        public void PlayOrPauseWallpaper() => Calls.Add("pause");
        public void StopWallpaper() => Calls.Add("stop");
        public void StepVideo(bool forward) => Calls.Add(forward ? "next-video" : "prev-video");
        public void TogglePause() => Calls.Add("pause-video");
    }
}
namespace Opsis.Containers
{
    using System.Text.Json.Nodes;
    public interface IContainersApi { JsonNode? Handle(string cmd, JsonObject args); }
    public sealed class PipeException(string code, string description) : System.Exception(code + " " + description);
    public static class PipeClient
    {
        public static JsonObject Send(string pipe, string action, JsonObject? body, int timeout) => new() { ["ok"] = true };
    }
}
