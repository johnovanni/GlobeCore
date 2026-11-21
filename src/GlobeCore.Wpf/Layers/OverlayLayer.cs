using System.Windows.Controls;
using GlobeCore.Wpf.Rendering;

namespace GlobeCore.Wpf.Layers;

/// <summary>
/// A layer of 2D screen-space content (labels, edge arrows) on the overlay canvas over the viewport. The host calls
/// <see cref="Render"/> on frames when the scene is dirty, after the scene layers' updates, in
/// <see cref="GlobeLayer.ZOrder"/> order.
/// </summary>
public abstract class OverlayLayer(string id, string title, int zOrder) : GlobeLayer(id, title, zOrder)
{
    public abstract void Render(Canvas overlay, Projector projector);

    /// <summary>Removes or hides what the layer drew: called when it is hidden, and every frame while the window is minimized.</summary>
    protected internal virtual void Clear(Canvas overlay) { }

    /// <summary>Asks for <see cref="Render"/> (and every scene layer's Update) on the next frame.</summary>
    protected void Invalidate() => RaiseInvalidated(immediate: false);
}
