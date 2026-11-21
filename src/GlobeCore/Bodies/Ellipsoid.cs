namespace GlobeCore.Bodies;

/// <summary>Reference ellipsoid of a body: equatorial and polar radii in km.</summary>
public sealed record Ellipsoid(double EquatorialKm, double PolarKm)
{
    public double PolarRatio => PolarKm / EquatorialKm;
    public double MeanRadiusKm => (2.0 * EquatorialKm + PolarKm) / 3.0;
}
