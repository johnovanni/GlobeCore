using System.Globalization;
using AstroCore.Earth;
using GlobeCore.Bodies;

namespace GlobeCore.Geo;

public static class GeoFormat
{
    /// <summary>"12.3°S 45.0°W": one decimal, invariant culture; zero counts as N/E.</summary>
    public static string LatLon(GeoPosition p)
    {
        double lat = Math.Round(p.LatitudeDeg, 1, MidpointRounding.AwayFromZero);
        double lon = Math.Round(p.LongitudeDeg, 1, MidpointRounding.AwayFromZero);
        return string.Create(CultureInfo.InvariantCulture,
            $"{Math.Abs(lat):0.0}°{(lat >= 0 ? 'N' : 'S')} {Math.Abs(lon):0.0}°{(lon >= 0 ? 'E' : 'W')}");
    }

    /// <summary>
    /// <paramref name="p"/> (planetocentric, east-positive, as GlobeCore always stores it) shown in a body's longitude
    /// convention. <see cref="LongitudeConvention.EastPositive"/>: as <see cref="LatLon(GeoPosition)"/>.
    /// <see cref="LongitudeConvention.WestPositive"/> (Mars): the west longitude 0–360°W, "12.3°S 315.0°W" for 45°E, one
    /// decimal, where 360.0°W after rounding is 0.0°W. The latitude is shown as it is (planetocentric) either way.
    /// </summary>
    public static string LatLon(GeoPosition p, LongitudeConvention convention)
    {
        if (convention == LongitudeConvention.EastPositive) return LatLon(p);
        double lat = Math.Round(p.LatitudeDeg, 1, MidpointRounding.AwayFromZero);
        double west = (-p.LongitudeDeg % 360.0 + 360.0) % 360.0;   // [0, 360)
        west = Math.Round(west, 1, MidpointRounding.AwayFromZero);
        if (west >= 360.0) west = 0.0;
        return string.Create(CultureInfo.InvariantCulture, $"{Math.Abs(lat):0.0}°{(lat >= 0 ? 'N' : 'S')} {west:0.0}°W");
    }
}
