using System.Text.Json;

namespace GlobeCore.Earth;

/// <summary>
/// Shows Crimea and Sevastopol as part of Ukraine, as the United Nations does (UN General Assembly resolution 68/262,
/// 27 March 2014, "Territorial integrity of Ukraine"; the UN's humanitarian maps and country units do the same).
/// Natural Earth 5.1.2's default admin-0 file draws them inside Russia (de facto control). Its point-of-view variants
/// exist only at 1:10m and change many other countries as well, so GlobeCore keeps the 1:50m default file unmodified
/// and applies this fix-up when it reads it. Assets/README.md records the method and the files.
/// <para>
/// The regions are Natural Earth's own 1:50m admin-1 polygons (Crimea UA-43, Sevastopol UA-40), extracted unchanged from
/// <c>ne_50m_admin_1_states_provinces.geojson</c> (tag v5.1.2) into <see cref="RegionsFile"/>. Their union is exactly one
/// part of Russia's admin-0 multipolygon: every vertex of that part is one of theirs. That part is dropped from Russia
/// and the two admin-1 polygons are added to Ukraine. On the land-boundary lines, the line that is the regions' own
/// boundary (the Perekop isthmus, a "Disputed" line in Natural Earth) is dropped: it is an internal boundary of Ukraine.
/// </para>
/// </summary>
internal static class CrimeaFixup
{
    public const string RegionsFile = "ne_50m_admin_1_crimea_sevastopol.geojson";
    public const string From = "RUS";
    public const string To = "UKR";

    private static readonly Lazy<Regions> Loaded = new(Load);

    /// <summary>The two admin-1 polygons as GeoJSON rings ([lon, lat]), and every vertex of their outer rings.</summary>
    public sealed record Regions(IReadOnlyList<IReadOnlyList<IReadOnlyList<(double Lon, double Lat)>>> Polygons,
        HashSet<(double Lon, double Lat)> Vertices);

    public static Regions Get() => Loaded.Value;

    /// <summary>True when every vertex of the part's outer ring is a vertex of the regions (the part is Crimea).</summary>
    public static bool IsRegionsPart(IReadOnlyList<IReadOnlyList<(double Lon, double Lat)>> rings) =>
        rings.Count > 0 && rings[0].Count > 0 && rings[0].All(Get().Vertices.Contains);

    /// <summary>True when every point of the line is a vertex of the regions (the line is their own boundary).</summary>
    public static bool IsRegionsLine(IReadOnlyList<AstroCore.Earth.GeoPosition> line) =>
        line.Count > 0 && line.All(p => Get().Vertices.Contains((p.LongitudeDeg, p.LatitudeDeg)));

    private static Regions Load()
    {
        using var s = EarthAssets.Open(RegionsFile);
        using var doc = JsonDocument.Parse(s);
        var polygons = new List<IReadOnlyList<IReadOnlyList<(double Lon, double Lat)>>>();
        var vertices = new HashSet<(double Lon, double Lat)>();
        foreach (var f in doc.RootElement.GetProperty("features").EnumerateArray())
        {
            var g = f.GetProperty("geometry");
            if (g.GetProperty("type").GetString() != "Polygon")
                throw new InvalidDataException($"{RegionsFile}: expected Polygon geometries");
            var rings = new List<IReadOnlyList<(double Lon, double Lat)>>();
            foreach (var ring in g.GetProperty("coordinates").EnumerateArray())
                rings.Add(ring.EnumerateArray().Select(pt => (pt[0].GetDouble(), pt[1].GetDouble())).ToList());
            polygons.Add(rings);
            foreach (var v in rings[0]) vertices.Add(v);
        }
        if (polygons.Count != 2) throw new InvalidDataException($"{RegionsFile}: expected Crimea and Sevastopol, found {polygons.Count} features");
        return new Regions(polygons, vertices);
    }
}
