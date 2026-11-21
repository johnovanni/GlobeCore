using GlobeCore.Bodies;

namespace GlobeCore.Geo;

public static class GeoMath
{
    /// <summary>Great-circle distance (haversine) in kilometres on a sphere of radius <paramref name="radiusKm"/>.</summary>
    public static double DistanceKm(SurfacePoint a, SurfacePoint b, double radiusKm)
    {
        double lat1 = a.LatitudeDeg * Math.PI / 180, lat2 = b.LatitudeDeg * Math.PI / 180;
        double dLat = lat2 - lat1, dLon = (b.LongitudeDeg - a.LongitudeDeg) * Math.PI / 180;
        double h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                 + Math.Cos(lat1) * Math.Cos(lat2) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * radiusKm * Math.Asin(Math.Min(1.0, Math.Sqrt(h)));
    }
}
