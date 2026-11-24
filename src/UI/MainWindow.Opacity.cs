using MotionDesk.Services;

namespace MotionDesk.UI;

public sealed partial class MainWindow
{
    private readonly System.Windows.Forms.Timer _opacitySaveTimer = new() { Interval = 220 };
    private double? _pendingOpacity;
    private int? _micaBackdrop;

    private void ApplyWindowOpacity(double value, bool save = false)
    {
        double opacity = WindowOpacityPolicy.Normalize(value);
        if (opacity < 1)
        {
            // Remove the system backdrop before switching the form into layered mode.
            SetMainWindowBackdrop(false);
            if (!AllowTransparency) AllowTransparency = true;
        }
        if (Opacity != opacity) Opacity = opacity;
        if (opacity == 1 && AllowTransparency) AllowTransparency = false;
        RefreshMainWindowBackdrop();

        if (!save) return;
        _pendingOpacity = opacity;
        _opacitySaveTimer.Stop();
        _opacitySaveTimer.Start();
    }

    private void RefreshMainWindowBackdrop() => SetMainWindowBackdrop(WindowOpacityPolicy.UseMica(
        Opacity, Services.PerformanceProfile.ForFeature(Services.PerfFeature.Effects).BlurEffects, SystemInformation.HighContrast)
        && !AllowTransparency && !SafeModeService.IsSafeMode && Opsis.UI.GlassEnvironment.TransparencyEnabled);

    private void SetMainWindowBackdrop(bool enabled)
    {
        if (!IsHandleCreated) return;
        int material = !enabled ? 1 : AppSettings.Peek().ThemeMode == "SignatureGlass" ? 3 : 2;
        if (_micaBackdrop == material) return;
        if (GlassBlur.TryApplySystemBackdrop(Handle, material)) _micaBackdrop = material;
        else if (material == 3 && GlassBlur.TryApplySystemBackdrop(Handle, 2)) _micaBackdrop = 2;
        else _micaBackdrop = null;
    }

    private void SaveWindowOpacity()
    {
        _opacitySaveTimer.Stop();
        if (_pendingOpacity is not double opacity) return;
        try
        {
            var settings = AppSettings.Load();
            if (settings.WindowOpacity != opacity)
            {
                settings.WindowOpacity = opacity;
                settings.Save();
            }
            _pendingOpacity = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            RotatingLogger.Write($"Window opacity could not be saved: {ex.Message}");
        }
    }
}
