using AstroCore.Earth;
using GlobeCore.Geo;

namespace GlobeCore.Earth;

/// <summary>Natural Earth 1:50m lines, read from the embedded GeoJSON (each line a list of points, [lon, lat] read as lat/lon).</summary>
public static class NaturalEarthLines
{
    public static List<IReadOnlyList<GeoPosition>> Coastlines() => Read(EarthAssets.CoastlineFile);

    /// <summary>Country borders on land.</summary>
    public static List<IReadOnlyList<GeoPosition>> LandBoundaries() => Read(EarthAssets.LandBoundariesFile);

    private static List<IReadOnlyList<GeoPosition>> Read(string name)
    {
        using var s = EarthAssets.Open(name);
        return GeoJsonLines.Read(s).ToList();
    }
}
