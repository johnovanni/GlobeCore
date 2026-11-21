using System.Windows;
using GlobeCore.Wpf.Rendering;

namespace GlobeCore.Wpf.Layers;

/// <summary>What is under the mouse: the layer that found it, the item itself, and the hover tooltip text.</summary>
public sealed record PickResult(string LayerId, object Item, string Tooltip);

/// <summary>A layer that can be hovered and clicked. The host asks visible ones in descending z-order; the first hit wins.</summary>
public interface IPickable
{
    /// <param name="screen">The mouse, in viewport coordinates.</param>
    /// <param name="hover">True for a hover (runs about 30 times a second: no disk reads), false for a click.</param>
    PickResult? HitTest(Point screen, Projector projector, bool hover);
}
