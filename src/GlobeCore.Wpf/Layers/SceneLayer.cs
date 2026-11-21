using System.Windows.Media.Media3D;
using GlobeCore.Wpf.Rendering;

namespace GlobeCore.Wpf.Layers;

/// <summary>What a scene or overlay layer draws against: the camera, a projector for the viewport, and the display time.</summary>
public sealed record SceneContext(PerspectiveCamera Camera, Projector Projector, DateTime DisplayTimeUtc);

/// <summary>
/// A layer of 3D visuals above the surface (markers, rings, arcs) in the body frame, unit equatorial radius. The host
/// puts <see cref="Root"/> in the viewport after the globe, in <see cref="GlobeLayer.ZOrder"/> order, while the layer is
/// visible, and calls <see cref="Update"/> on frames when the scene is dirty (the camera moved, the viewport was resized,
/// or a layer called <see cref="Invalidate"/>).
/// </summary>
public abstract class SceneLayer : GlobeLayer
{
    protected SceneLayer(string id, string title, int zOrder, ModelVisual3D? root = null) : base(id, title, zOrder) =>
        Root = root ?? new ModelVisual3D();

    public ModelVisual3D Root { get; }

    public abstract void Update(SceneContext ctx);

    /// <summary>Called every frame while the layer is visible and the window is not minimized, before any <see cref="Update"/>.</summary>
    protected internal virtual void OnFrame(SceneContext ctx) { }

    /// <summary>Asks for <see cref="Update"/> (and every overlay layer's Render) on the next frame.</summary>
    protected void Invalidate() => RaiseInvalidated(immediate: false);
}
