using AstroCore.Earth;
using GlobeCore.Geo;

namespace GlobeCore.Earth;

/// <summary>Natural Earth 1:50m lines, read from the embedded GeoJSON (each line a list of points, [lon, lat] read as lat/lon).</summary>
public static class NaturalEarthLines
{
    public static List<IReadOnlyList<GeoPosition>> Coastlines() => Read(EarthAssets.CoastlineFile);

    /// <summary>
    /// Country borders on land. The line between Crimea and the rest of Ukraine (Perekop isthmus) is left out: Crimea is
    /// part of Ukraine, as on UN maps (see <see cref="CrimeaFixup"/>).
    /// </summary>
    public static List<IReadOnlyList<GeoPosition>> LandBoundaries()
    {
        var lines = Read(EarthAssets.LandBoundariesFile);
        int dropped = lines.RemoveAll(CrimeaFixup.IsRegionsLine);
        if (dropped != 1) throw new InvalidDataException($"Crimea fix-up: expected 1 land-boundary line around Crimea, found {dropped}");
        return lines;
    }

    private static List<IReadOnlyList<GeoPosition>> Read(string name)
    {
        using var s = EarthAssets.Open(name);
        return GeoJsonLines.Read(s).ToList();
    }
}
