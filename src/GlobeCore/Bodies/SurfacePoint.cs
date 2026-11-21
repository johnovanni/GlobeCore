using AstroCore.Earth;

namespace GlobeCore.Bodies;

/// <summary>A point on a body's surface: planetocentric latitude and longitude in degrees (east-positive unless the body says otherwise).</summary>
public readonly record struct SurfacePoint(double LatitudeDeg, double LongitudeDeg)
{
    public static implicit operator SurfacePoint(GeoPosition p) => new(p.LatitudeDeg, p.LongitudeDeg);
    public static implicit operator GeoPosition(SurfacePoint p) => new(p.LatitudeDeg, p.LongitudeDeg);
}
