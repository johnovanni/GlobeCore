using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using GlobeCore.Bodies;
using GlobeCore.Imaging;
using GlobeCore.Logging;
using GlobeCore.Wpf.Rendering;
using GlobeCore.Wpf.Viewport;
using HelixToolkit.Wpf;
#if PERF_LOG
using System.Diagnostics;
using GlobeCore.Wpf.Diagnostics;
#endif

namespace GlobeCore.Wpf.Layers;

/// <summary>
/// Owns a body's globe in a viewport and the layers drawn on it. The globe texture is the base image, then the visible
/// <see cref="ImageLayer"/>s' <see cref="ImageLayer.Current"/> images (by pass and order) and the
/// <see cref="Illumination"/>, composed on a background task; one compose runs at a time, requests made meanwhile collapse
/// into one follow-up compose, and a failed compose shows the plain base image (<see cref="ComposeOrPlain"/>).
/// It composes again when the display time has moved on by <see cref="RecomposeInterval"/>, and at most every
/// <see cref="RecomposeThrottle"/> for requests (with a trailing compose, so the globe ends on the last state).
/// Scene layers' visuals sit in the viewport after the globe; scene and overlay layers are redrawn on frames when the
/// camera, the viewport size or a layer has changed. Hover shows the top pickable layer's tooltip; a click raises
/// <see cref="Selected"/>. UI thread only. Dispose when the window closes.
/// </summary>
public sealed class LayerHost : IDisposable
{
    public static readonly TimeSpan RecomposeInterval = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan RecomposeThrottle = TimeSpan.FromMilliseconds(250);
    public static readonly TimeSpan HoverInterval = TimeSpan.FromMilliseconds(33);
    /// <summary>A left button released within this many pixels of where it went down is a click, not a drag.</summary>
    public const double ClickSlopPx = 4;
    private const double TipOffsetPx = 16;

    private readonly HelixViewport3D _viewport;
    private readonly Canvas _overlay;
    private readonly Body _body;
    private readonly IAppLog _log;
    private readonly List<GlobeLayer> _layers = [];   // sorted by ZOrder; equal ZOrders keep the order they were added in
    private readonly ModelVisual3D _globeVisual = new();

    // Kept between composes: the composer holds the base image; the target and bitmap are reused. The base image is
    // also kept here (no extra copy) as the fallback shown when a compose fails.
    private readonly GlobeImageComposer _composer;
    private readonly Bgra32Image _plain;
    private readonly Bgra32Image _composed;
    private readonly WriteableBitmap _bitmap;

    private readonly RecomposeSchedule _schedule = new(RecomposeInterval);
    private readonly Throttle _throttle = new(RecomposeThrottle);
    private readonly DispatcherTimer _trailing = new();
    private readonly TaskCompletionSource _firstCompose = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource? _loopDone;
    private bool _composing, _recomposePending, _closed;
    // Something the image shows changed since the last compose started.
    private bool _settingsDirty;
    private DateTime _displayTimeUtc;

    private bool _sceneDirty = true;
    // Programmatic camera moves (CameraAnimator) do not always raise CameraChanged, so compare the pose too.
    private (Point3D, Vector3D, Vector3D, double) _lastPose;

    // Left button went down at this point and has not moved far (a click, not a drag); null otherwise.
    private Point? _clickStart;
    private readonly Throttle _hoverThrottle = new(HoverInterval);
    private readonly DispatcherTimer _hoverTimer = new() { Interval = HoverInterval };
    private Point _hoverMouse;
    private readonly ToolTip _tip = new() { Placement = PlacementMode.Relative, StaysOpen = true };

#if PERF_LOG
    private int _composeCount;
#endif

    /// <param name="overlay">The canvas over the viewport that overlay layers draw on.</param>
    /// <param name="body">The body: its shape gives the globe mesh; a self-luminous body is never night-shaded.</param>
    /// <param name="baseImage">The body's base texture (equirectangular, straight BGRA); every layer image must be its size.</param>
    public LayerHost(HelixViewport3D viewport, Canvas overlay, Body body, Bgra32Image baseImage, IAppLog log)
    {
        _viewport = viewport ?? throw new ArgumentNullException(nameof(viewport));
        _overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));
        _body = body ?? throw new ArgumentNullException(nameof(body));
        ArgumentNullException.ThrowIfNull(baseImage);
        _log = log ?? throw new ArgumentNullException(nameof(log));
        // Nothing need await the first compose (only diagnostics do), so observe a fault here: otherwise it would
        // reach TaskScheduler.UnobservedTaskException and a crash handler would show a second message for it.
        _firstCompose.Task.ContinueWith(static t => _ = t.Exception, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

        int w = baseImage.Width, h = baseImage.Height;
        _composer = new GlobeImageComposer(baseImage);
        _plain = baseImage;
        _composed = Bgra32Image.Create(w, h);
        _bitmap = ImageConversions.CreateBitmap(w, h);
        _viewport.Children.Add(_globeVisual);   // the mesh is set by the first compose

        _tip.PlacementTarget = _viewport;
        _trailing.Tick += OnTrailingTick;
        _hoverTimer.Tick += OnHoverTimerTick;
        _viewport.CameraChanged += OnCameraChanged;
        _viewport.SizeChanged += OnViewportSizeChanged;
        _viewport.MouseMove += OnViewportMouseMove;
        _viewport.MouseLeave += OnViewportMouseLeave;
        // Also for handled events: the app's own double-click handling marks its second press handled.
        _viewport.AddHandler(UIElement.PreviewMouseDownEvent, new MouseButtonEventHandler(OnPreviewMouseDown), handledEventsToo: true);
        _viewport.AddHandler(UIElement.PreviewMouseUpEvent, new MouseButtonEventHandler(OnPreviewMouseUp), handledEventsToo: true);
        CompositionTarget.Rendering += OnRendering;
    }

    /// <summary>The layers, in z-order.</summary>
    public IReadOnlyList<GlobeLayer> Layers => _layers;

    public Body Body => _body;

    /// <summary>How the globe is lit at the next compose; null means no night shade and no day/night line.</summary>
    public IlluminationState? Illumination { get; set; }

    /// <summary>
    /// If set, called on the UI thread at the start of every compose with the display time it composes; the result
    /// becomes <see cref="Illumination"/>. So the light always matches the time composed (sub-solar point and settings
    /// read then, as one value).
    /// </summary>
    public Func<DateTime, IlluminationState?>? IlluminationAt { get; set; }

    /// <summary>
    /// The time the globe shows. Setting it composes again (throttled) once the time composed last is
    /// <see cref="RecomposeInterval"/> or more away, or is later than this one; nothing while the window is minimized
    /// (see <see cref="RecomposeIfStale"/>). It does not redraw scene or overlay layers: they invalidate themselves.
    /// </summary>
    public DateTime DisplayTimeUtc
    {
        get => _displayTimeUtc;
        set
        {
            _displayTimeUtc = value;
            if (_closed || IsMinimized) return;   // composed on restore instead
            if (_schedule.IsDue(value)) RequestThrottled();
        }
    }

    /// <summary>The display time of the composed image now on the globe (default before the first compose).</summary>
    public DateTime ComposedUtc { get; private set; }

    /// <summary>True when no compose is running, queued, or owed (a throttled request waiting for its trailing compose).</summary>
    public bool IsComposeIdle => !_composing && !_recomposePending && !_settingsDirty && !_trailing.IsEnabled;

    /// <summary>Raised once, on the UI thread, when the first composed image is on the globe.</summary>
    public event EventHandler? FirstComposeDisplayed;

    /// <summary>
    /// Completes when the first composed image is on the globe; faults if it could not be shown; cancelled if the host is
    /// disposed first. For diagnostics that wait for the globe.
    /// </summary>
    public Task WhenFirstComposeDisplayed => _firstCompose.Task;

    /// <summary>Raised on the UI thread each time a composed image is put on the globe (<see cref="ComposedUtc"/> is its time).</summary>
    public event EventHandler? Composed;

    /// <summary>A left click hit something: the top pickable layer's result.</summary>
    public event EventHandler<PickResult>? Selected;

    /// <summary>A left click hit nothing pickable.</summary>
    public event EventHandler? SelectionCleared;

    /// <summary>Raised from the frame loop when the camera's pose has changed (however it was moved).</summary>
    public event EventHandler? CameraPoseChanged;

    /// <summary>Other overlay elements (cards, panels): no hover picking while the mouse is over one of them.</summary>
    public IList<UIElement> HoverBlockers { get; } = [];

    /// <summary>The hover tooltip; set its <see cref="FrameworkElement.Style"/> to match the app (GlobeCore's dark card style by default).</summary>
    public ToolTip HoverTip => _tip;

    /// <summary>The composed texture (tests).</summary>
    internal Bgra32Image ComposedImage => _composed;

    private bool IsMinimized => Window.GetWindow(_viewport)?.WindowState == WindowState.Minimized;

    // ---------------------------------------------------------------- layers

    public void Add(GlobeLayer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        ObjectDisposedException.ThrowIf(_closed, this);
        if (_layers.Contains(layer)) throw new ArgumentException($"Layer {layer.Id} is already added.", nameof(layer));
        if (_layers.Any(l => l.Id == layer.Id)) throw new ArgumentException($"A layer with id {layer.Id} is already added.", nameof(layer));
        _layers.Insert(_layers.FindLastIndex(l => l.ZOrder <= layer.ZOrder) + 1, layer);
        layer.PropertyChanged += OnLayerPropertyChanged;
        layer.Invalidated += OnLayerInvalidated;
        if (layer is SceneLayer scene && scene.IsVisible) AttachRoot(scene);
        _sceneDirty = true;
    }

    private void OnLayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_closed || e.PropertyName != nameof(GlobeLayer.IsVisible) || sender is not GlobeLayer layer) return;
        switch (layer)
        {
            case ImageLayer:
                RequestRecompose();
                break;
            case SceneLayer scene:
                if (scene.IsVisible) AttachRoot(scene);
                else _viewport.Children.Remove(scene.Root);
                _sceneDirty = true;
                break;
            case OverlayLayer overlay:
                if (!overlay.IsVisible) overlay.Clear(_overlay);
                _sceneDirty = true;
                break;
        }
    }

    private void OnLayerInvalidated(GlobeLayer layer, bool immediate)
    {
        if (_closed) return;
        if (layer is not ImageLayer) { _sceneDirty = true; return; }
        if (!immediate) { RequestRecompose(); return; }
        _settingsDirty = true;
        Recompose();
    }

    /// <summary>Puts the layer's root in the viewport after the globe and after the roots of the layers before it.</summary>
    private void AttachRoot(SceneLayer scene)
    {
        if (_viewport.Children.Contains(scene.Root)) return;
        int index = _viewport.Children.IndexOf(_globeVisual) + 1;
        foreach (var l in _layers)
        {
            if (ReferenceEquals(l, scene)) break;
            if (l is SceneLayer before && _viewport.Children.IndexOf(before.Root) is >= 0 and var i) index = Math.Max(index, i + 1);
        }
        _viewport.Children.Insert(index, scene.Root);
    }

    // ---------------------------------------------------------------- globe image

    /// <summary>
    /// Something the globe image shows has changed (a display setting, a layer): composes again, at most every
    /// <see cref="RecomposeThrottle"/>, with a trailing compose so the globe ends on the final state. While the window
    /// is minimized it is only noted, and composed by <see cref="RecomposeIfStale"/> on restore.
    /// </summary>
    public void RequestRecompose()
    {
        if (_closed) return;
        _settingsDirty = true;
        if (IsMinimized) return;   // composed on restore instead
        RequestThrottled();   // throttled: a slider changes a setting many times a second
    }

    /// <summary>
    /// Composes now, not throttled (or, if a compose is running, once more right after it). Completes when the last
    /// compose of the run is on the globe.
    /// </summary>
    public Task RecomposeNowAsync()
    {
        if (_closed) return Task.CompletedTask;
        if (_composing) { _recomposePending = true; return _loopDone!.Task; }
        _ = ComposeLoopAsync();
        return _loopDone!.Task;
    }

    /// <summary>Composes now if a change is waiting or the display time has moved on (for example when the window is restored).</summary>
    public void RecomposeIfStale()
    {
        if (_closed) return;
        if (_settingsDirty || _schedule.IsDue(_displayTimeUtc)) Recompose();
    }

    /// <summary>Redraws the scene and overlay layers on the next frame (for example after the overlay's free area changed).</summary>
    public void RequestSceneUpdate() => _sceneDirty = true;

    private void Recompose() => _ = RecomposeNowAsync();

    private async Task ComposeLoopAsync()
    {
        _composing = true;
        var done = _loopDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            do
            {
                _recomposePending = false;
                _settingsDirty = false;
                var now = _displayTimeUtc;
                _schedule.MarkComposed(now);
                if (IlluminationAt is { } at) Illumination = at(now);
                // A layer built the first time it is shown builds here; switching a layer off just leaves it out.
                var imageLayers = _layers.OfType<ImageLayer>().ToArray();
                foreach (var l in imageLayers)
                    if (l.IsVisible) await l.PrepareForComposeAsync();
                if (_closed) return;
                var used = new IImageLayer?[imageLayers.Length];
                var images = new List<IImageLayer>(imageLayers.Length);
                for (int i = 0; i < imageLayers.Length; i++)
                {
                    used[i] = imageLayers[i].IsVisible ? imageLayers[i].Current : null;
                    if (used[i] is { } image) images.Add(image);
                }
                var light = _body.Illumination is SelfLuminous ? null : Illumination;
                var composer = _composer;
                var target = _composed;
                var plain = _plain;
                var log = _log;
#if PERF_LOG
                var sw = Stopwatch.StartNew();
#endif
                bool composed = await Task.Run(() => ComposeOrPlain(composer, images, light, target, plain, log));
#if PERF_LOG
                long composeMs = sw.ElapsedMilliseconds;
#endif
                if (_closed) return;
                ImageConversions.CopyInto(target, _bitmap);
                _globeVisual.Content ??= new GeometryModel3D(BodyMesh.Create(_body.Shape.PolarRatio), new DiffuseMaterial(new ImageBrush(_bitmap)));
                for (int i = 0; i < imageLayers.Length; i++) imageLayers[i].OnComposeDisplayed(used[i], composed);
#if PERF_LOG
                var sub = light?.SubSolarPoint ?? default;
                PerfLog.Write(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                    $"compose #{++_composeCount} utc={now:yyyy-MM-ddTHH:mm:ss.fffZ} subsolar lat={sub.LatitudeDeg:F3} lon={sub.LongitudeDeg:F3} compose={composeMs} ms total={sw.ElapsedMilliseconds} ms {PerfLog.Memory()}"));
#endif
                ComposedUtc = now;
                if (_firstCompose.TrySetResult()) FirstComposeDisplayed?.Invoke(this, EventArgs.Empty);
                Composed?.Invoke(this, EventArgs.Empty);
            } while (_recomposePending && !_closed);
        }
        catch (OperationCanceledException) when (_closed) { }   // a layer's rasterizer was shut down by the window closing
        catch (Exception ex)
        {
            // ComposeOrPlain already handles a failed compose; this is anything else (copying to the bitmap, say).
            _log.Error("Globe image could not be shown", ex);
            _firstCompose.TrySetException(ex);
        }
        finally
        {
            _composing = false;
            done.TrySetResult();
        }
    }

    /// <summary>
    /// Composes <paramref name="layers"/> into <paramref name="target"/>. If that throws (low memory, or a layer of the
    /// wrong size), logs it, fills <paramref name="target"/> with <paramref name="plain"/>, the base image with no shading,
    /// and returns false. The schedule was marked before the compose, so the next scheduled compose (within a minute) retries.
    /// </summary>
    public static bool ComposeOrPlain(GlobeImageComposer composer, IReadOnlyList<IImageLayer> layers, IlluminationState? light,
                                      Bgra32Image target, Bgra32Image plain, IAppLog log)
    {
        try
        {
            composer.ComposeInto(layers, light, target);
            return true;
        }
        catch (Exception ex)
        {
            log.Error("Globe compose failed; showing the plain image until the next compose", ex);
            Buffer.BlockCopy(plain.Pixels, 0, target.Pixels, 0, Math.Min(plain.Pixels.Length, target.Pixels.Length));
            return false;
        }
    }

    // Requests change the image many times a second (scrubbing, sliders): compose at most every 250 ms, and let a
    // one-shot timer make the last request after the gap so the globe ends on the final state.
    private void RequestThrottled()
    {
        if (_closed) return;
        if (_throttle.TryAcquire(DateTime.UtcNow))
        {
            _trailing.Stop();
            Recompose();
            return;
        }
        var wait = _throttle.NextAllowedUtc - DateTime.UtcNow;
        _trailing.Interval = wait > TimeSpan.FromMilliseconds(1) ? wait : TimeSpan.FromMilliseconds(1);
        _trailing.Stop();
        _trailing.Start();
    }

    private void OnTrailingTick(object? sender, EventArgs e)
    {
        _trailing.Stop();
        if (_closed || IsMinimized) return;
        if (_settingsDirty || _schedule.IsDue(_displayTimeUtc)) RequestThrottled();
    }

    // ---------------------------------------------------------------- scene and overlay layers

    private void OnCameraChanged(object? sender, RoutedEventArgs e) => _sceneDirty = true;
    private void OnViewportSizeChanged(object sender, SizeChangedEventArgs e) => _sceneDirty = true;

    private void OnRendering(object? sender, EventArgs e)
    {
        if (_closed) return;
        if (IsMinimized)
        {
            foreach (var l in _layers) if (l is OverlayLayer o) o.Clear(_overlay);
            _sceneDirty = true;
            return;
        }
        if (_viewport.Camera is not PerspectiveCamera camera) { _sceneDirty = false; return; }
        NoteCameraChange(camera);
        var ctx = new SceneContext(camera, new Projector(camera, _viewport.ActualWidth, _viewport.ActualHeight), _displayTimeUtc);
        foreach (var l in _layers.ToArray()) if (l is SceneLayer s && s.IsVisible) s.OnFrame(ctx);
        if (!_sceneDirty) return;
        _sceneDirty = false;
        foreach (var l in _layers.ToArray())
        {
            if (!l.IsVisible) continue;
            if (l is SceneLayer s) s.Update(ctx);
            else if (l is OverlayLayer o) o.Render(_overlay, ctx.Projector);
        }
    }

    private void NoteCameraChange(PerspectiveCamera c)
    {
        var pose = (c.Position, c.LookDirection, c.UpDirection, c.FieldOfView);
        if (pose == _lastPose) return;
        _lastPose = pose;
        _sceneDirty = true;
        CameraPoseChanged?.Invoke(this, EventArgs.Empty);
    }

    // ---------------------------------------------------------------- picking, hover and click

    /// <summary>What is under <paramref name="screen"/> (viewport coordinates): the first hit among visible pickable layers, highest z-order first.</summary>
    public PickResult? Pick(Point screen, bool hover)
    {
        if (_viewport.Camera is not PerspectiveCamera camera) return null;
        var projector = new Projector(camera, _viewport.ActualWidth, _viewport.ActualHeight);
        for (int i = _layers.Count - 1; i >= 0; i--)
            if (_layers[i] is IPickable p && _layers[i].IsVisible && p.HitTest(screen, projector, hover) is { } hit)
                return hit;
        return null;
    }

    private void OnViewportMouseMove(object sender, MouseEventArgs e)
    {
        _hoverTimer.Stop();
        if (e.LeftButton == MouseButtonState.Pressed || IsOverHoverBlocker()) { HideTip(); return; }
        _hoverMouse = e.GetPosition(_viewport);
        // About 30 Hz, with a trailing pick so the last position is always handled.
        if (_hoverThrottle.TryAcquire(DateTime.UtcNow)) UpdateHover(_hoverMouse);
        else _hoverTimer.Start();
    }

    private void OnHoverTimerTick(object? sender, EventArgs e)
    {
        _hoverTimer.Stop();
        _hoverThrottle.TryAcquire(DateTime.UtcNow);
        if (!IsOverHoverBlocker()) UpdateHover(_hoverMouse);
    }

    private bool IsOverHoverBlocker()
    {
        foreach (var b in HoverBlockers) if (b.IsMouseOver) return true;
        return false;
    }

    private void UpdateHover(Point mouse)
    {
        if (_closed || _viewport.Camera is not PerspectiveCamera || Mouse.LeftButton == MouseButtonState.Pressed) { HideTip(); return; }
        string? text = Pick(mouse, hover: true)?.Tooltip;
        if (string.IsNullOrEmpty(text)) { HideTip(); return; }
        _tip.Style ??= DarkToolTipStyle();
        if (!Equals(_tip.Content, text)) _tip.Content = text;
        _tip.HorizontalOffset = mouse.X + TipOffsetPx;
        _tip.VerticalOffset = mouse.Y + TipOffsetPx;
        _tip.IsOpen = true;
    }

    private void OnViewportMouseLeave(object sender, MouseEventArgs e)
    {
        // A trailing hover tick still queued from the last move would otherwise reopen the tooltip after the mouse left.
        _hoverTimer.Stop();
        HideTip();
    }

    private void HideTip()
    {
        if (_tip.IsOpen) _tip.IsOpen = false;
    }

    /// <summary>GlobeCore's dark tooltip (light text on dark), from Styles/CardStyles.xaml.</summary>
    public static Style DarkToolTipStyle() =>
        (Style)new ResourceDictionary { Source = new Uri("/GlobeCore.Wpf;component/Styles/CardStyles.xaml", UriKind.Relative) }[typeof(ToolTip)];

    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) _clickStart = e.ClickCount == 1 ? e.GetPosition(_viewport) : null;
    }

    /// <summary>A left click (released within 4 px of where it went down) selects what is under it, or clears the selection.</summary>
    private void OnPreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || _clickStart is not { } start) return;
        _clickStart = null;
        var end = e.GetPosition(_viewport);
        if (_closed || (end - start).Length >= ClickSlopPx || _viewport.Camera is not PerspectiveCamera) return;
        if (Pick(end, hover: false) is { } hit) Selected?.Invoke(this, hit);
        else SelectionCleared?.Invoke(this, EventArgs.Empty);
    }

    // ---------------------------------------------------------------- lifetime

    public void Dispose()
    {
        if (_closed) return;
        _closed = true;
        CompositionTarget.Rendering -= OnRendering;
        _trailing.Stop();
        _trailing.Tick -= OnTrailingTick;
        _hoverTimer.Stop();
        _hoverTimer.Tick -= OnHoverTimerTick;
        HideTip();
        _viewport.CameraChanged -= OnCameraChanged;
        _viewport.SizeChanged -= OnViewportSizeChanged;
        _viewport.MouseMove -= OnViewportMouseMove;
        _viewport.MouseLeave -= OnViewportMouseLeave;
        _viewport.RemoveHandler(UIElement.PreviewMouseDownEvent, new MouseButtonEventHandler(OnPreviewMouseDown));
        _viewport.RemoveHandler(UIElement.PreviewMouseUpEvent, new MouseButtonEventHandler(OnPreviewMouseUp));
        foreach (var l in _layers)
        {
            l.PropertyChanged -= OnLayerPropertyChanged;
            l.Invalidated -= OnLayerInvalidated;
        }
        _firstCompose.TrySetCanceled();
    }
}
