using AstroCore.Astronomy;
using AstroCore.Bodies;

namespace GlobeCore.Bodies;

/// <summary>
/// A globe body. <paramref name="HeliocentricPosition"/> maps jdTT to heliocentric J2000 ecliptic AU; null means the Earth.
/// </summary>
public sealed record Body(
    string Id,
    string Name,
    Ellipsoid Shape,
    IBodyRotation Rotation,
    Func<double, Vector3d>? HeliocentricPosition,
    LongitudeConvention LongitudeConvention,
    Illumination Illumination)
{
    /// <summary>The point on the body directly beneath the Sun at <paramref name="utc"/>.</summary>
    public SurfacePoint SubSolarPoint(DateTime utc) => BodySubPoints.SubSolarPoint(HeliocentricPosition, Rotation, utc);
}
