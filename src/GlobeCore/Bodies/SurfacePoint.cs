using AstroCore.Earth;

namespace GlobeCore.Bodies;

/// <summary>
/// A point on a body's surface: planetocentric latitude and longitude in degrees. The longitude is always east-positive,
/// normalised to (−180, 180], for every body; a body's <see cref="LongitudeConvention"/> changes only how it is shown.
/// </summary>
public readonly record struct SurfacePoint(double LatitudeDeg, double LongitudeDeg)
{
    public static implicit operator SurfacePoint(GeoPosition p) => new(p.LatitudeDeg, p.LongitudeDeg);
    public static implicit operator GeoPosition(SurfacePoint p) => new(p.LatitudeDeg, p.LongitudeDeg);
}
