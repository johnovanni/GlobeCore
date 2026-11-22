using AstroCore.Astronomy;
using AstroCore.Earth;

namespace GlobeCore.Bodies;

/// <summary>Reference ellipsoid of a body: equatorial and polar radii in km (each positive and finite).</summary>
/// <remarks>
/// GlobeCore draws a body in model units: equatorial radius 1 and polar radius <see cref="PolarRatio"/>, so the surface
/// is x² + y² + (z / PolarRatio)² = 1 (body-fixed axes: +X to (0°, 0°), +Y to (0°, 90°E), +Z to the north pole).
/// <see cref="ModelPoint(SurfacePoint)"/> and <see cref="SurfacePointOf"/> convert between that surface and planetocentric
/// latitude and longitude. A planetocentric latitude is the angle of the point's radius vector above the equator, so
/// each surface point lies on the radial line of its latitude and the conversion back needs no ratio.
/// </remarks>
public sealed record Ellipsoid
{
    public Ellipsoid(double EquatorialKm, double PolarKm)
    {
        if (!(double.IsFinite(EquatorialKm) && EquatorialKm > 0))
            throw new ArgumentOutOfRangeException(nameof(EquatorialKm), EquatorialKm, "The equatorial radius must be positive and finite.");
        if (!(double.IsFinite(PolarKm) && PolarKm > 0))
            throw new ArgumentOutOfRangeException(nameof(PolarKm), PolarKm, "The polar radius must be positive and finite.");
        this.EquatorialKm = EquatorialKm;
        this.PolarKm = PolarKm;
    }

    public double EquatorialKm { get; }
    public double PolarKm { get; }

    public void Deconstruct(out double EquatorialKm, out double PolarKm)
    {
        EquatorialKm = this.EquatorialKm;
        PolarKm = this.PolarKm;
    }

    public double PolarRatio => PolarKm / EquatorialKm;
    public double MeanRadiusKm => (2.0 * EquatorialKm + PolarKm) / 3.0;

    /// <summary>The point of this body's surface at <paramref name="p"/>, in model units (equatorial radius 1).</summary>
    public Vector3d ModelPoint(SurfacePoint p) => ModelPoint(p.LatitudeDeg, p.LongitudeDeg, PolarRatio);

    /// <summary>
    /// The point at planetocentric latitude <paramref name="latitudeDeg"/> and east longitude
    /// <paramref name="longitudeDeg"/> on the surface x² + y² + (z / <paramref name="polarRatio"/>)² = 1: the unit vector
    /// of that latitude and longitude, scaled to the ellipsoid's radius there, ρ = 1 / √(cos²φ + sin²φ / r²). At a ratio
    /// of exactly 1 it is the unit vector (cos φ cos λ, cos φ sin λ, sin φ), with degrees turned to radians as
    /// <c>deg * Math.PI / 180</c>, as EarthWatch3D's globe mesh always did (so the Earth's mesh is unchanged to the bit; it
    /// differs from <see cref="GeoPosition.ToUnitVector"/>, which multiplies by π/180, only by rounding).
    /// </summary>
    public static Vector3d ModelPoint(double latitudeDeg, double longitudeDeg, double polarRatio)
    {
        if (!(double.IsFinite(polarRatio) && polarRatio > 0))
            throw new ArgumentOutOfRangeException(nameof(polarRatio), polarRatio, "The polar ratio must be positive and finite.");
        double lat = latitudeDeg * Math.PI / 180, lon = longitudeDeg * Math.PI / 180;
        double cl = Math.Cos(lat), sl = Math.Sin(lat);
        if (polarRatio == 1.0) return new Vector3d(cl * Math.Cos(lon), cl * Math.Sin(lon), sl);
        double rho = 1.0 / Math.Sqrt(cl * cl + sl * sl / (polarRatio * polarRatio));
        return new Vector3d(rho * cl * Math.Cos(lon), rho * cl * Math.Sin(lon), rho * sl);
    }

    /// <summary>
    /// The planetocentric latitude and longitude of a model point (any point but the origin; its distance does not
    /// matter): <see cref="GeoPosition.FromVector"/>, longitude in (−180, 180].
    /// </summary>
    public static SurfacePoint SurfacePointOf(Vector3d modelPoint) => GeoPosition.FromVector(modelPoint);
}
