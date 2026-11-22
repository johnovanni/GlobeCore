using AstroCore.Earth;
using GlobeCore.Geo;
using GlobeCore.Regions;
using static GlobeCore.Tests.RegionTestData;

namespace GlobeCore.Tests;

/// <summary>The body-agnostic index under <c>CountryIndex</c>: lookups, name matching, merges and aliases.</summary>
public class RegionIndexTests
{
    private static Region R(string id, string name, IReadOnlyList<string> other, params GeoPolygon[] polys) =>
        new(id, name, other, new GeoPosition(0, 0), polys);

    private static GeoPolygon Poly(params List<(double Lon, double Lat)>[] rings) =>
        new(rings.Select(r => (IReadOnlyList<GeoPosition>)r.Select(p => new GeoPosition(p.Lat, p.Lon)).ToList()).ToList());

    private static readonly RegionIndex Index = new(
    [
        R("MARE-1", "Mare Imbrium", ["Sea of Showers"], Poly(Ring(0, 0, 10), Ring(4, 4, 2))),   // with a hole
        R("HOLE", "Inner Crater", [], Poly(Ring(4, 4, 2))),
        R("MARE-2", "Mare Crisium", ["Sea of Crises"], Poly(Ring(170, 0, 10))),
    ]);

    [Fact]
    public void FindRespectsHolesAndBoxes()
    {
        Assert.Equal("MARE-1", Index.Find(new GeoPosition(1, 1))?.Id);
        Assert.Equal("HOLE", Index.Find(new GeoPosition(5, 5))?.Id);
        Assert.Equal("MARE-2", Index.Find(new GeoPosition(5, 175))?.Id);
        Assert.Null(Index.Find(new GeoPosition(50, 50)));
        Assert.Null(Index.Find(new GeoPosition(double.NaN, 1)));
    }

    [Fact]
    public void ByIdIsCaseInsensitiveAndFirstWins()
    {
        var dup = new RegionIndex([R("X", "First", []), R("X", "Second", [])]);
        Assert.Equal("First", dup.ById("x")?.Name);
        Assert.Equal(2, dup.All.Count);
        Assert.Null(Index.ById("NOPE"));
        Assert.Null(Index.ById(null!));
    }

    [Fact]
    public void ResolveNormalisesAndPrefersPrimaryNames()
    {
        Assert.Equal("MARE-1", Index.Resolve("mare imbrium")?.Id);
        Assert.Equal("MARE-1", Index.Resolve("Sea of Showers")?.Id);
        Assert.Equal("MARE-2", Index.Resolve("  SEA-OF   crises ")?.Id);
        Assert.Null(Index.Resolve(""));
        Assert.Null(Index.Resolve("Oceanus Procellarum"));
        // A primary name beats another region's other name.
        var clash = new RegionIndex([R("A", "Alpha", ["Beta"]), R("B", "Beta", [])]);
        Assert.Equal("B", clash.Resolve("Beta")?.Id);
    }

    [Fact]
    public void NormalizeDropsAccentsAndApostrophes()
    {
        Assert.Equal("cote divoire", RegionIndex.Normalize("Côte d’Ivoire"));
        Assert.Equal("cote divoire", RegionIndex.Normalize("Cote d'Ivoire"));
        Assert.Equal("a b", RegionIndex.Normalize("  A--b. "));
    }

    [Fact]
    public void MergesFollowChainsAndKeepNames()
    {
        var idx = new RegionIndex([R("A", "Ay", [], Poly(Ring(0, 0, 1))), R("B", "Bee", [], Poly(Ring(5, 0, 1))), R("C", "Sea", [], Poly(Ring(10, 0, 1)))]);
        var merged = idx.WithMerges(new Dictionary<string, string> { ["a"] = "B", ["B"] = "C", ["ZZZ"] = "C" });
        Assert.Equal(["C"], merged.All.Select(r => r.Id));
        Assert.Equal(3, merged.ById("C")!.Polygons.Count);
        Assert.Equal("C", merged.Find(new GeoPosition(0.5, 0.5))?.Id);
        Assert.Equal("C", merged.Resolve("Ay")?.Id);
        Assert.Equal("C", merged.Resolve("Bee")?.Id);
        Assert.Equal(3, idx.All.Count);   // the original is unchanged
    }

    [Fact]
    public void AliasesOverrideAndRetargetOnMerge()
    {
        var idx = new RegionIndex([R("A", "Ay", []), R("B", "Bee", [])], new Dictionary<string, string> { ["Old Ay"] = "A" });
        Assert.Equal("A", idx.Resolve("old ay")?.Id);
        var aliased = idx.WithAliases(new Dictionary<string, string> { ["Bee"] = "A", ["Ghost"] = "NONE" });
        Assert.Equal("A", aliased.Resolve("Bee")?.Id);   // an alias replaces a region's own name
        Assert.Null(aliased.Resolve("Ghost"));          // an alias to an unknown id is ignored
        // An alias that pointed at a merged region follows it.
        Assert.Equal("B", idx.WithMerges(new Dictionary<string, string> { ["A"] = "B" }).Resolve("Old Ay")?.Id);
    }
}
