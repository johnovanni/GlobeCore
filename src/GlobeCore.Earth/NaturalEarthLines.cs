using AstroCore.Earth;
using GlobeCore.Geo;
using GlobeCore.Regions;

namespace GlobeCore.Earth;

/// <summary>Natural Earth 1:50m lines, read from the embedded GeoJSON (each line a list of points, [lon, lat] read as lat/lon).</summary>
public static class NaturalEarthLines
{
    /// <summary>Coastlines: physical, so bundled only (never updated).</summary>
    public static List<IReadOnlyList<GeoPosition>> Coastlines() => Read(EarthAssets.CoastlineFile);

    /// <summary>Country borders on land from the bundled set with the app's rules file applied (see <see cref="BoundaryRules.Parse"/>).</summary>
    /// <exception cref="BoundaryRuleException">The rules are malformed or do not match the bundled files.</exception>
    public static List<IReadOnlyList<GeoPosition>> LandBoundaries(Stream rulesJson) =>
        LandBoundaries(NaturalEarthBoundaries.LoadBundled(BoundaryRules.Parse(rulesJson)));

    /// <summary>The boundary lines of a set (e.g. <see cref="CurrentBoundaries.Set"/>), as a new list.</summary>
    public static List<IReadOnlyList<GeoPosition>> LandBoundaries(BoundarySet set)
    {
        ArgumentNullException.ThrowIfNull(set);
        return set.Lines.Select(l => l.Points).ToList();
    }

    private static List<IReadOnlyList<GeoPosition>> Read(string name)
    {
        using var s = EarthAssets.Open(name);
        return GeoJsonLines.Read(s).ToList();
    }
}
