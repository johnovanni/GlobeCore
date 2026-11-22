using System.IO;
using AstroCore.Earth;
using GlobeCore.Regions;

namespace GlobeCore.Tests;

/// <summary>Small body-agnostic boundary data (lon/lat squares) for the region and rule tests.</summary>
internal static class RegionTestData
{
    /// <summary>A closed square ring from (lon, lat) with the given side, counter-clockwise.</summary>
    public static List<(double Lon, double Lat)> Ring(double lon, double lat, double side) =>
        [(lon, lat), (lon + side, lat), (lon + side, lat + side), (lon, lat + side), (lon, lat)];

    public static RawPolygon Square(double lon, double lat, double side) => new([Ring(lon, lat, side)]);

    /// <summary>
    /// Three regions on an unnamed body: ALPHA (a big square and a small "peninsula" square at lon 20), BETA, and GAMMA.
    /// The subdivision ALPHA-P is exactly ALPHA's small square, split into two halves P-1 and P-2. Lines: one along the
    /// peninsula's own west edge (all its points are subdivision vertices) and one between ALPHA and BETA.
    /// </summary>
    public static RawBoundaryData Sample()
    {
        var regions = new List<RawRegion>
        {
            new("ALPHA", "Alpha", ["Alpha Land", "Republic of Alpha"], new GeoPosition(5, 5), [Square(0, 0, 10), Square(20, 0, 2)]),
            new("BETA", "Beta", ["Beta Land"], new GeoPosition(5, 35), [Square(30, 0, 10)]),
            new("GAMMA", "Gamma", [], new GeoPosition(55, 55), [Square(50, 50, 10)]),
        };
        // The peninsula [20,22]x[0,2] split at lon 21: every vertex of ALPHA's small square is one of theirs.
        var subdivisions = new List<RawSubdivision>
        {
            new("P-1", "ALPHA", "West half", [new RawPolygon([[(20, 0), (21, 0), (21, 2), (20, 2), (20, 0)]])]),
            new("P-2", "ALPHA", "East half", [new RawPolygon([[(21, 0), (22, 0), (22, 2), (21, 2), (21, 0)]])]),
            new("Q-1", "BETA", "Elsewhere", [Square(32, 2, 1)]),
        };
        var lines = new List<IReadOnlyList<GeoPosition>>
        {
            new List<GeoPosition> { new(0, 20), new(2, 20) },        // the peninsula's west edge (lat, lon)
            new List<GeoPosition> { new(0, 10), new(10, 10), new(10, 30) },   // not on the peninsula
        };
        return new RawBoundaryData("v1.0.0", "Test Atlas 1.0.0 (2020-01-02)", new DateOnly(2020, 1, 2), regions, lines, subdivisions);
    }

    public static Stream Json(string json) => new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));

    public static BoundaryRules Rules(string rulesArray) => BoundaryRules.Parse(Json("{\"rules\": [" + rulesArray + "]}"));

    /// <summary>The Earth apps' Crimea rule (<see cref="BoundaryRulesTests.CrimeaRule"/>), parsed.</summary>
    public static BoundaryRules CrimeaRules { get; } = Rules(BoundaryRulesTests.CrimeaRule);

    /// <summary>The Crimea rule as a rules file.</summary>
    public static Stream CrimeaRulesJson() => Json("{\"rules\": [" + BoundaryRulesTests.CrimeaRule + "]}");

    /// <summary>The bundled Natural Earth 5.1.2 set with the Crimea rule, read once.</summary>
    public static BoundarySet CrimeaSet => CrimeaSetLazy.Value;

    private static readonly Lazy<BoundarySet> CrimeaSetLazy = new(() => GlobeCore.Earth.NaturalEarthBoundaries.LoadBundled(CrimeaRules));

    public const string MovePeninsula = """
        {"kind": "moveAdmin1", "codes": ["P-1", "P-2"], "from": "ALPHA", "to": "BETA", "expectParts": 1, "expectLines": 1,
         "description": "Peninsula shown as part of Beta", "reason": "test ruling"}
        """;
}
