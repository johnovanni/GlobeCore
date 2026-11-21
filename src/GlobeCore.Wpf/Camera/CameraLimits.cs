namespace GlobeCore.Wpf.Camera;

/// <summary>Zoom limits of a body's camera, in body equatorial radii.</summary>
/// <param name="MinDistance">Closest the camera may get to the body's centre.</param>
/// <param name="MaxDistance">Farthest the camera may get from the body's centre.</param>
/// <param name="ZoomPerNotch">Distance factor per mouse-wheel notch.</param>
public sealed record CameraLimits(double MinDistance, double MaxDistance, double ZoomPerNotch)
{
    /// <summary>The limits EarthWatch3D has always used.</summary>
    public static readonly CameraLimits EarthDefault = new(1.047, 20.0, 1.15);
}
