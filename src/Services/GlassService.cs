using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using OpsisGlass = Opsis.Glass;

namespace MotionDesk.Services
{
    /// <summary>
    /// 2.6.0: Opsis' use of the GPU glass layer (glass/Opsis.Glass). One <see cref="OpsisGlass.GlassManager"/> for the whole app, created the first time a window asks for it,
    /// fed with what the rest of Opsis knows (performance tier, live wallpaper, lock / remote / fullscreen / battery saver, safe mode). If the GPU part cannot start
    /// the windows keep their old DWM blur look (nothing breaks). Off by default (setting <c>UseGpuGlass</c>) until it has been tested on real machines.
    /// </summary>
    internal static class GlassService
    {
        private static readonly object Gate = new();
        private static OpsisGlass.GlassManager? _manager;
        private static System.Threading.Timer? _timer;
        private static OpsisGlass.GlassConditions? _last;
        private static bool _failed;
        private static SynchronizationContext? _ui;
        private static int _refreshQueued, _policyInvalidated;
        private static readonly HashSet<object> SurfaceOwners = new();
        private static bool _settingsSubscribed;
        private static volatile bool _policyDemand;
        private static long? _emptySince;
        private const long IdleReleaseMs = 30_000;

        internal static bool ActivityDemand => _policyDemand || _manager != null;
        internal static bool NeedsPolicyMonitor(int eligibleSurfaces, GpuGlassMode mode, bool failed) =>
            eligibleSurfaces > 0 && !failed && mode is GpuGlassMode.Auto or GpuGlassMode.Always;
        internal static bool MayRetireManager(int panes, bool visibleGpuDemand, long idleMs, bool held) =>
            panes == 0 && !visibleGpuDemand && !held && idleMs >= IdleReleaseMs;

        // Called by existing UI surface owners, never by a new worker. Demand
        // is independent of temporary fullscreen/lock/load policy decisions.
        internal static void SetSurfaceDemand(object owner, bool needed)
        {
            lock (Gate)
            {
                bool changed = needed ? SurfaceOwners.Add(owner) : SurfaceOwners.Remove(owner);
                if (!changed) return;
                ReconcileMonitorLocked();
            }
            QueueRefresh();
        }

        private static void OnSettingsSaved()
        {
            Interlocked.Exchange(ref _policyInvalidated, 1);
            QueueRefresh();
        }

        private static void ReconcileMonitorLocked()
        {
            var s = AppSettings.Peek();
            bool demand = NeedsPolicyMonitor(SurfaceOwners.Count, GlassPolicy.ParseMode(s.GpuGlassMode, s.UseGpuGlass), _failed);
            if (_policyDemand != demand)
            {
                _policyDemand = demand;
                AppActivity.RequestRefresh();
            }
            bool observeSettings = SurfaceOwners.Count != 0 || _manager != null;
            if (observeSettings && !_settingsSubscribed)
            {
                _ui ??= SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
                AppSettings.SettingsSaved += OnSettingsSaved;
                _settingsSubscribed = true;
            }
            else if (!observeSettings && _settingsSubscribed)
            {
                AppSettings.SettingsSaved -= OnSettingsSaved;
                _settingsSubscribed = false;
            }
            if ((demand || _manager != null) && _timer == null)
            {
                _ui ??= SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
                AppActivity.FullscreenChanged += OnActivityChanged;
                PerformanceGovernor.EffectiveChanged += OnTierChanged;
                _timer = new System.Threading.Timer(_ => QueueRefresh(), null, 2000, 2000);
            }
            else if (!demand && _manager == null && _timer != null)
            {
                AppActivity.FullscreenChanged -= OnActivityChanged;
                PerformanceGovernor.EffectiveChanged -= OnTierChanged;
                _timer.Dispose(); _timer = null;
            }
            if (!observeSettings && _timer == null) _ui = null;
        }
        /// <summary>Diagnostics: while true Refresh() does not touch the conditions (a test holds them).</summary>
        internal static volatile bool Hold;

        private static GlassLevel _level = GlassLevel.Solid; private static string _reason = ""; private static DateTime _decidedAt;
        private static HardwareInfo? _hw;
        internal static event Action? PolicyChanged;

        /// <summary>What is used right now and why (for the settings page).</summary>
        public static string Describe() { Decide(); return GlassPolicy.Describe(_level, _reason); }
        internal static GlassLevel Level { get { Decide(); return _level; } }

        private static void Decide()
        {
            if ((DateTime.UtcNow - _decidedAt).TotalMilliseconds < 800) return;
            _decidedAt = DateTime.UtcNow;
            var previous = _level;
            try
            {
                var s = AppSettings.Peek(); var act = AppActivity.Snapshot; _hw ??= HardwareInfo.Detect();
                var inp = new GlassInputs(_hw.RamMb, _hw.PhysicalCores, _hw.Adapters, _hw.OnBattery, Environment.OSVersion.Version.Build, SafeModeService.IsSafeMode, Opsis.UI.GlassEnvironment.TransparencyEnabled,
                    System.Windows.Forms.SystemInformation.HighContrast, act.Remote, act.Locked, act.Fullscreen, act.BatterySaver, _failed, PerformanceGovernor.EffectiveTier);
                (_level, _reason) = GlassPolicy.Decide(GlassPolicy.ParseMode(s.GpuGlassMode, s.UseGpuGlass), inp);
            }
            catch (Exception) { _level = GlassLevel.Solid; _reason = "could not read the computer's state"; }
            if (previous != _level) PolicyChanged?.Invoke();
        }

        /// <summary>True while the GPU glass may be used: the policy says Full or Frosted (no static default: the choice comes from the computer and follows the load live).</summary>
        internal static OpsisGlass.GlassManager? ExistingManager => _manager;

        public static bool Enabled { get { Decide(); return _level is GlassLevel.Full or GlassLevel.Frosted; } }

        /// <summary>The manager when glass is enabled and the GPU part is ready; null otherwise (the caller keeps its non-GPU look).</summary>
        public static OpsisGlass.GlassManager? Manager
        {
            get
            {
                if (!Enabled) return null;
                lock (Gate)
                {
                    if (_manager == null)
                    {
                        try
                        {
                            _manager = new OpsisGlass.GlassManager();
                            if (!_manager.IsGpuReady)
                            {
                                RotatingLogger.Write("GlassService: GPU glass unavailable, keeping the DWM look");
                                _failed = true;
                                RetireManagerLocked();
                                _decidedAt = DateTime.MinValue; Decide();
                                return null;
                            }
                            _emptySince = null;
                            ReconcileMonitorLocked();
                            AppActivity.RequestRefresh();
                            Refresh();
                        }
                        catch (Exception ex)
                        {
                            RotatingLogger.Write("GlassService: " + ex.Message);
                            _failed = true;
                            RetireManagerLocked();
                            _decidedAt = DateTime.MinValue; Decide();
                            return null;
                        }
                    }
                    return _manager;
                }
            }
        }

        private static void OnActivityChanged(bool _) => QueueRefresh();
        private static void OnTierChanged(HardwareTier _) => QueueRefresh();
        private static void QueueRefresh()
        {
            var ui = _ui;
            if (ui == null || Interlocked.Exchange(ref _refreshQueued, 1) != 0) return;
            try
            {
                ui.Post(_ =>
                {
                    Interlocked.Exchange(ref _refreshQueued, 0);
                    if (ReferenceEquals(_ui, ui)) Refresh();
                }, null);
            }
            catch (Exception ex) { Interlocked.Exchange(ref _refreshQueued, 0); System.Diagnostics.Debug.WriteLine(ex); }
        }

        public static void Refresh()
        {
            if (Hold) return;
            try
            {
                if (Interlocked.Exchange(ref _policyInvalidated, 0) != 0) _decidedAt = DateTime.MinValue;
                Decide();
                lock (Gate) ReconcileMonitorLocked();
                var m = _manager;
                if (m == null) return;
                var act = AppActivity.Snapshot;
                var wp = WallpaperHostEngine_IsLive();
                var c = new OpsisGlass.GlassConditions
                {
                    Tier = Level switch { GlassLevel.Full => OpsisGlass.GlassTier.High, GlassLevel.Frosted => OpsisGlass.GlassTier.Balanced, _ => OpsisGlass.GlassTier.Low },     // Blur / Solid: the manager suspends and the windows keep their simple look
                    LiveBackdrop = wp,
                    Locked = act.Locked, RemoteSession = act.Remote, FullscreenApp = act.Fullscreen, BatterySaver = act.BatterySaver,
                    SafeMode = SafeModeService.IsSafeMode
                };
                if (_last != c)
                {
                    m.SetConditions(c);
                    _last = c;
                }
                // Stats comes from the actual shared renderer, not a second
                // pane registry. Keep a live pane and avoid churn on short gaps.
                lock (Gate)
                {
                    if (!ReferenceEquals(m, _manager)) return;
                    int panes = m.Stats.Panes;
                    bool visibleGpuDemand = _policyDemand && _level is GlassLevel.Full or GlassLevel.Frosted;
                    if (panes != 0 || visibleGpuDemand) _emptySince = null;
                    else
                    {
                        long now = Environment.TickCount64;
                        _emptySince ??= now;
                        if (MayRetireManager(panes, visibleGpuDemand, now - _emptySince.Value, Hold))
                            RetireManagerLocked();
                    }
                }
            }
            catch (Exception ex) { RotatingLogger.WriteRecurring(RotatingLogger.RecurringSource.Glass, "GlassService.Refresh: " + ex.Message); }
        }

        private static bool WallpaperHostEngine_IsLive()
        {
            try
            {
                return MotionDesk.Widgets.WallpaperHostEngine.IsEnabledIfCreated &&
                    MotionDesk.Widgets.WallpaperSettings.Load().Mode is not ("Desktop" or "Image");
            }
            catch (Exception) { return false; }
        }

        /// <summary>The look of a pane, from the user's Glass choices (Opsis.UI.GlassSettings) with the given corner radius.</summary>
        public static OpsisGlass.GlassStyle Style(int cornerRadius, DesktopSurface? surface = null)
        {
            if (surface is DesktopSurface target && DesktopMaterials.Get(target) is { UsesCustom: true } material)
                return new OpsisGlass.GlassStyle
                {
                    Tint = material.Color, TintAlpha = material.Opacity / 100f,
                    BlurSigma = material.Blur / 5f,
                    CornerRadius = material.Radius, Gloss = material.Refraction / 200f, Rim = material.Rim / 100f
                };
            var g = AppSettings.Peek().Glass;
            Color tint;
            try { tint = ColorTranslator.FromHtml(g.TintColor); } catch (Exception) { tint = Color.FromArgb(0x99, 0xCC, 0xFF); }
            return new OpsisGlass.GlassStyle
            {
                Tint = tint,
                TintAlpha = (float)(0.10 + 0.30 * Math.Clamp(g.Intensity, 0, 100) / 100.0),
                BlurSigma = Math.Clamp(g.Blur, 0, 40) / 5f,
                CornerRadius = cornerRadius,
                Gloss = Math.Clamp(g.Glossy, 0, 100) / 100f,
                Rim = Math.Clamp(g.RimLight, 0, 100) / 100f
            };
        }

        private static void RetireManagerLocked()
        {
            var manager = _manager; _manager = null; _last = null; _emptySince = null;
            try { manager?.Dispose(); }
            catch (Exception ex)
            {
                // A failed native release is not permission to allocate
                // another device in the same process.
                _failed = true;
                RotatingLogger.Write("GlassService disposal failed; restart required: " + ex.Message);
            }
            ReconcileMonitorLocked();
            AppActivity.RequestRefresh();
        }

        public static void Shutdown()
        {
            lock (Gate)
            {
                SurfaceOwners.Clear();
                Hold = false;
                RetireManagerLocked();
                _ui = null;
                Interlocked.Exchange(ref _refreshQueued, 0);
                Interlocked.Exchange(ref _policyInvalidated, 0);
            }
        }
    }

    /// <summary>
    /// Keeps an Opsis.Glass pane directly below a see-through form (a widget in "Transparent" mode with the colour key): add / move / resize / show / hide / remove follow the form.
    /// <c>wants</c> decides when the pane should exist (setting on, form see-through); it is re-evaluated on theme and setting changes.
    /// </summary>
    internal sealed class GlassPaneBinder : IDisposable
    {
        private readonly Form _f; private readonly int _radius; private readonly DesktopSurface? _surface; private readonly Func<bool> _wants; private int _id = -1; private bool _disposed;
        // a widget changes its z-order without any event of ours (TopMost, BringToFront, another topmost window): re-seat the pane below it twice a second
        private readonly System.Windows.Forms.Timer _reseat = new() { Interval = 500 };
        private OpsisGlass.GlassStyle? _shownStyle;
        private Rectangle _shownBounds;
        private bool _shownVisible;
        private int _syncQueued;
        private readonly bool _trackPolicyDemand;
        private readonly Func<int?>? _cornerOverride;
        private readonly Func<OpsisGlass.GlassStyle, OpsisGlass.GlassStyle>? _tintOverride;
        private readonly System.Text.StringBuilder _nextClass = new(64);
#if SIGNATURE_COMPOSITION_PREVIEW
        private Opsis.UI.Composition.CompositionPaneBinding? _signature;
        private bool _signatureFramePending;
        private long _signatureNextFrame;
        private Rectangle _signatureLastFrameBounds;
        private async void RequestSignatureFrame()
        {
            if (_signature is not { Active: true } binding || _signatureFramePending || Environment.TickCount64 < _signatureNextFrame) return;
            _signatureFramePending = true;
            _signatureNextFrame = Environment.TickCount64 + (SignatureRenderer().AdaptiveTier == Opsis.UI.GlassTier.High && GlassService.Level == GlassLevel.Full ? 500 : 2000);
            var bounds = _f.Bounds;
            try
            {
                using var frame = await MotionDesk.Widgets.WallpaperHostEngine.Instance.CaptureSignatureCropAsync(bounds);
                if (_disposed || _f.IsDisposed || _f.Bounds != bounds || _signature != binding || !binding.Active) return;
                binding.SubmitBackdrop(frame);
                _signatureLastFrameBounds = bounds;
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("Signature frame: " + ex.Message); }
            finally { _signatureFramePending = false; }
        }
        // A single native device per UI thread; the old desktop renderer remains
        // available to the independent DeskStrip path during this preview.
        [ThreadStatic] private static Opsis.UI.Composition.CompositionGlassRenderer? _signatureRenderer;
        internal static Opsis.UI.Composition.CompositionDiagnostics? SignatureDiagnostics => _signatureRenderer?.Diagnostics;
        private static Opsis.UI.Composition.CompositionGlassRenderer SignatureRenderer()
        {
            if (_signatureRenderer != null) return _signatureRenderer;
            var renderer = new Opsis.UI.Composition.CompositionGlassRenderer();
            _signatureRenderer = renderer;
            int owner = Environment.CurrentManagedThreadId;
            EventHandler? cleanup = null;
            cleanup = (_, _) =>
            {
                if (Environment.CurrentManagedThreadId != owner) return;
                Application.ThreadExit -= cleanup;
                renderer.Dispose();
                _signatureRenderer = null;
            };
            Application.ThreadExit += cleanup;
            return renderer;
        }
        private Opsis.UI.GlassState SignatureState()
        {
            var level = GlassService.Level;
            var tier = level == GlassLevel.Full ? Opsis.UI.GlassTier.High :
                level == GlassLevel.Frosted ? Opsis.UI.GlassTier.Medium : Opsis.UI.GlassTier.Low;
            bool active = (level is GlassLevel.Full or GlassLevel.Frosted) && !AppSettings.Peek().Glass.ReducedEffects;
            return new Opsis.UI.GlassState(active, tier, Opsis.UI.GlassOffReason.None,
                Opsis.UI.BackdropKind.None, active ? 14 : 0, _radius, .30, .85, 0, .80);
        }
        private Opsis.UI.Composition.CompositionGlassStyle SignatureStyle(Opsis.UI.GlassState state)
        {
            var look = GlassService.Style(_radius, _surface);
            if (_cornerOverride?.Invoke() is int radius) look = look with { CornerRadius = radius };
            if (_tintOverride != null) look = _tintOverride(look);
            var material = AppSettings.Peek().Glass;
            var pointer = _f.PointToClient(Cursor.Position);
            bool hover = _f.ClientRectangle.Contains(pointer) && !SystemInformation.HighContrast && !material.ReducedEffects;
            return new Opsis.UI.Composition.CompositionGlassStyle(look.Tint, look.TintAlpha,
                look.CornerRadius, look.Gloss, look.Rim)
            {
                Blur = state.Tier == Opsis.UI.GlassTier.High ? look.BlurSigma : look.BlurSigma * .5f,
                Refraction = state.Tier == Opsis.UI.GlassTier.High ? look.Gloss : 0,
                Dispersion = Math.Clamp(material.Dispersion, 0, 100) / 100f,
                Microtexture = Math.Clamp(material.Microtexture, 0, 100) / 100f,
                Depth = Math.Clamp(material.Shadow, 0, 100) / 100f,
                InteractiveLight = hover ? Math.Clamp(material.InteractiveLight, 0, 100) / 100f : 0,
                LightX = Math.Clamp(pointer.X / (float)Math.Max(1, _f.ClientSize.Width), 0, 1),
                LightY = Math.Clamp(pointer.Y / (float)Math.Max(1, _f.ClientSize.Height), 0, 1)
            };
        }
#endif

        public GlassPaneBinder(Form form, int cornerRadius, Func<bool> wants, DesktopSurface? surface = null, Func<int?>? cornerOverride = null,
            Func<OpsisGlass.GlassStyle, OpsisGlass.GlassStyle>? tintOverride = null, bool trackPolicyDemand = true)
        {
            _f = form; _radius = cornerRadius; _wants = wants; _surface = surface; _cornerOverride = cornerOverride; _tintOverride = tintOverride;
            _trackPolicyDemand = trackPolicyDemand;
            form.HandleCreated += HandleCreated;
            form.LocationChanged += GeometryChanged;
            form.SizeChanged += GeometryChanged;
            form.VisibleChanged += HandleCreated;
            form.Disposed += FormDisposed;
            MotionDesk.UI.ThemeManager.Changed += OnTheme;
            MotionDesk.UI.ThemeManager.Repainted += OnTheme;
            GlassService.PolicyChanged += OnPolicyChanged;
            form.HandleDestroyed += HandleDestroyed;
            _reseat.Tick += (_, _) =>
            {
#if SIGNATURE_COMPOSITION_PREVIEW
                if (!_disposed && _f.Visible) Sync();
#else
                if (_id >= 0 && !_disposed && _f.Visible && !PaneIsBelow())
                    GlassService.ExistingManager?.PlaceBelow(_id, _f.Handle);
#endif
            };
        }

        private void HandleCreated(object? sender, EventArgs e) => Sync();
        private void GeometryChanged(object? sender, EventArgs e) => QueueSync();
        private void FormDisposed(object? sender, EventArgs e) => Dispose();
        private void HandleDestroyed(object? sender, EventArgs e)
        {
            Interlocked.Exchange(ref _syncQueued, 0);
            if (_trackPolicyDemand) GlassService.SetSurfaceDemand(this, false);
            Remove();
        }

        private void QueueSync()
        {
            if (_disposed || !_f.IsHandleCreated || Interlocked.Exchange(ref _syncQueued, 1) != 0) return;
            try { _f.BeginInvoke(() => { Interlocked.Exchange(ref _syncQueued, 0); Sync(); }); }
            catch (InvalidOperationException) { Interlocked.Exchange(ref _syncQueued, 0); }
        }

        private bool PaneIsBelow()
        {
            var adjacent = GetWindow(_f.Handle, 2);
            if (adjacent == IntPtr.Zero) return false;
            _nextClass.Clear();
            GetClassName(adjacent, _nextClass, _nextClass.Capacity);
            return _nextClass.ToString() == "OpsisGlassPane" && GetWindowRect(adjacent, out var rect) &&
                _shownBounds == Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, uint command);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder result, int length);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect result);

        private void OnPolicyChanged() => QueueSync();

        private void OnTheme() { if (_disposed || _f.IsDisposed) return; if (_f.InvokeRequired) { try { _f.BeginInvoke(new Action(Sync)); } catch (InvalidOperationException) { } } else Sync(); }

        public void Sync()
        {
            if (_disposed || _f.IsDisposed || _f.Disposing || !_f.IsHandleCreated) return;
            bool eligible = _f.Visible && _wants();
            if (_trackPolicyDemand) GlassService.SetSurfaceDemand(this, eligible);
            if (!eligible || !GlassService.Enabled) { Remove(); return; }
#if SIGNATURE_COMPOSITION_PREVIEW
            // Reuse the existing reseat timer for policy and z-order changes;
            // no per-frame timer and no additional legacy pane for this form.
            if (!_reseat.Enabled) _reseat.Start();
            if (_signature == null)
            {
                _signature = new Opsis.UI.Composition.CompositionPaneBinding(_f, SignatureRenderer(),
                    SignatureState, SignatureStyle, _wants);
                _signature.ActiveChanged += _ => { if (!_disposed && !_f.Disposing && _f.IsHandleCreated) PaneChanged?.Invoke(); };
            }
            if (_signature.Sync())
            {
                if (_signatureLastFrameBounds != _f.Bounds) { _signature.SubmitBackdrop(null); _signatureLastFrameBounds = _f.Bounds; }
                RequestSignatureFrame();
            }
            return;
#else
            var m = GlassService.Manager;
            if (m == null) { Remove(); return; }
            var style = GlassService.Style(_radius, _surface);
            if (_cornerOverride?.Invoke() is int radius) style = style with { CornerRadius = radius };
            if (_tintOverride != null) style = _tintOverride(style);
            var bounds = _f.Bounds;
            bool created = _id < 0;
            if (created)
            {
                _id = m.AddPane(bounds, style, clickThrough: true);
                if (_id < 0) return;
            }
            else
            {
                if (_shownBounds.Location != bounds.Location) m.MovePane(_id, bounds.Location);
                if (_shownBounds.Size != bounds.Size) m.ResizePane(_id, bounds.Size);
                if (style != _shownStyle) m.SetStyle(_id, style);
            }
            _shownBounds = bounds;
            if (created || !PaneIsBelow()) m.PlaceBelow(_id, _f.Handle);
            if (created || !_shownVisible) m.SetPaneVisible(_id, true);
            _shownStyle = style;
            _shownVisible = true;
            if (!_reseat.Enabled) _reseat.Start();
            if (created) PaneChanged?.Invoke();
#endif
        }

        internal bool HasPane =>
#if SIGNATURE_COMPOSITION_PREVIEW
            _signature?.Active == true;
#else
            _id >= 0;
#endif
        internal event Action? PaneChanged;

        private void Remove()
        {
            _reseat.Stop();
#if SIGNATURE_COMPOSITION_PREVIEW
            _signature?.Dispose(); _signature = null;
#endif
            if (_id < 0) return;
            GlassService.ExistingManager?.RemovePane(_id);
            _id = -1;
            _shownVisible = false;
            _shownStyle = null;
            _shownBounds = Rectangle.Empty;
            if (!_disposed && _f.IsHandleCreated && !_f.Disposing) PaneChanged?.Invoke();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _f.HandleCreated -= HandleCreated;
            _f.LocationChanged -= GeometryChanged;
            _f.SizeChanged -= GeometryChanged;
            _f.VisibleChanged -= HandleCreated;
            _f.Disposed -= FormDisposed;
            _f.HandleDestroyed -= HandleDestroyed;
            MotionDesk.UI.ThemeManager.Changed -= OnTheme;
            MotionDesk.UI.ThemeManager.Repainted -= OnTheme;
            GlassService.PolicyChanged -= OnPolicyChanged;
            if (_trackPolicyDemand) GlassService.SetSurfaceDemand(this, false);
            Remove();
            _reseat.Dispose();
            PaneChanged = null;
        }
    }

    /// <summary>
    /// A 32-bit premultiplied-alpha surface for a layered window (UpdateLayeredWindow): the window draws into <see cref="Begin"/>'s Graphics and calls <see cref="Present"/>.
    /// One DIB section reused for every frame; re-created only when the size changes.
    /// </summary>
    internal sealed class LayeredSurface : IDisposable
    {
        private IntPtr _memDc, _dib, _bits, _previousBitmap;
        private Bitmap? _bmp;
        private int _w, _h;

        public Graphics Begin(int w, int h)
        {
            if (w != _w || h != _h || _bmp == null) Recreate(w, h);
            var g = Graphics.FromImage(_bmp!);
            g.Clear(Color.FromArgb(1, 0, 0, 0));   // alpha 1: invisible, but the window still receives the mouse everywhere (alpha 0 would be click-through)
            return g;
        }

        public bool Present(IntPtr hwnd, Point location)
        {
            if (_bmp == null) return false;
            var size = new SIZE { cx = _w, cy = _h }; var src = new POINT(); var dst = new POINT { x = location.X, y = location.Y };
            var blend = new BLEND { BlendOp = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
            var screen = GetDC(IntPtr.Zero);
            bool updated = UpdateLayeredWindow(hwnd, screen, ref dst, ref size, _memDc, ref src, 0, ref blend, 2);
            ReleaseDC(IntPtr.Zero, screen);
            return updated;
        }

        private void Recreate(int w, int h)
        {
            Free();
            _w = w; _h = h;
            var screen = GetDC(IntPtr.Zero);
            _memDc = CreateCompatibleDC(screen);
            var bi = new BITMAPINFOHEADER { biSize = 40, biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
            _dib = CreateDIBSection(_memDc, ref bi, 0, out _bits, IntPtr.Zero, 0);
            _previousBitmap = SelectObject(_memDc, _dib);
            ReleaseDC(IntPtr.Zero, screen);
            _bmp = new Bitmap(w, h, w * 4, PixelFormat.Format32bppPArgb, _bits);
        }

        private void Free()
        {
            _bmp?.Dispose();
            _bmp = null;
            if (_memDc != IntPtr.Zero)
            {
                if (_previousBitmap != IntPtr.Zero && _previousBitmap != new IntPtr(-1)) SelectObject(_memDc, _previousBitmap);
                DeleteDC(_memDc);
            }
            if (_dib != IntPtr.Zero) DeleteObject(_dib);
            _dib = _memDc = _bits = _previousBitmap = IntPtr.Zero;
        }
        public void Dispose() => Free();

        [StructLayout(LayoutKind.Sequential)] private struct POINT { public int x, y; }
        [StructLayout(LayoutKind.Sequential)] private struct SIZE { public int cx, cy; }
        [StructLayout(LayoutKind.Sequential)] private struct BLEND { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
        [StructLayout(LayoutKind.Sequential)] private struct BITMAPINFOHEADER { public int biSize, biWidth, biHeight; public short biPlanes, biBitCount; public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant; }
        [DllImport("user32.dll")] private static extern bool UpdateLayeredWindow(IntPtr h, IntPtr dst, ref POINT pd, ref SIZE ps, IntPtr src, ref POINT psrc, int key, ref BLEND b, int flags);
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFOHEADER bi, uint usage, out IntPtr bits, IntPtr section, uint offset);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr o);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    }
}
