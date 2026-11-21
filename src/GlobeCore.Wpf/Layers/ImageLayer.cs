using GlobeCore.Imaging;

namespace GlobeCore.Wpf.Layers;

/// <summary>
/// A layer drawn into the globe texture. At each compose the host reads <see cref="Current"/> (on the UI thread) and
/// blends it in its <see cref="IImageLayer.Pass"/> and <see cref="IImageLayer.Order"/>, on a background thread: the
/// instance returned must not change afterwards (build a new one instead, then <see cref="Invalidate"/>).
/// </summary>
public abstract class ImageLayer(string id, string title, int zOrder) : GlobeLayer(id, title, zOrder)
{
    /// <summary>What to blend now, or null for nothing (not loaded yet, or nothing to show).</summary>
    public abstract IImageLayer? Current { get; }

    /// <summary>
    /// The image has changed: asks the host for a recompose, throttled to one every 250 ms with a trailing compose,
    /// as a display setting change does. Showing or hiding the layer does this too.
    /// </summary>
    protected void Invalidate() => RaiseInvalidated(immediate: false);

    /// <summary>
    /// The image has changed: composes at once (or right after the compose running now), not throttled. For a layer
    /// that has just finished building its image in the background.
    /// </summary>
    protected void InvalidateNow() => RaiseInvalidated(immediate: true);

    /// <summary>
    /// Awaited on the UI thread at the start of each compose while the layer is visible, before <see cref="Current"/> is
    /// read: a layer built the first time it is shown (<see cref="LazyLayer{T}"/>) builds here, so that compose includes it.
    /// </summary>
    protected internal virtual Task PrepareForComposeAsync() => Task.CompletedTask;

    /// <summary>
    /// Called on the UI thread after each compose is on the globe. <paramref name="used"/> is the instance that was
    /// blended (null if the layer was hidden or had nothing); <paramref name="composed"/> is false when the compose failed
    /// and the plain base image is shown instead.
    /// </summary>
    protected internal virtual void OnComposeDisplayed(IImageLayer? used, bool composed) { }
}
