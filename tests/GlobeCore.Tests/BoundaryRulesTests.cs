using AstroCore.Earth;
using GlobeCore.Earth;
using GlobeCore.Geo;
using GlobeCore.Regions;
using static GlobeCore.Tests.RegionTestData;

namespace GlobeCore.Tests;

public class BoundaryRulesTests
{
    // The rule an Earth app supplies for ruling R15 (owner's Q3, UN practice), as in the README's example.
    internal const string CrimeaRule = """
        {"kind": "moveAdmin1", "name": "moveAdmin1 UA-43", "codes": ["UA-43", "UA-40"], "from": "RUS", "to": "UKR",
         "expectParts": 1, "expectLines": 1,
         "description": "Crimea shown as part of Ukraine (UN General Assembly resolution 68/262)",
         "reason": "UN General Assembly resolution 68/262 (27 March 2014), Territorial integrity of Ukraine"}
        """;

    private static readonly RawBoundaryData Bundled = NaturalEarthBoundaries.ReadBundled();

    [Fact]
    // Same results as the CrimeaFixup code it replaces (68f9dc5): the one RUS part made of the admin-1 vertices moves
    // to UKR as Natural Earth's two admin-1 polygons, and the Perekop line (all 15 points on them) is dropped.
    public void CrimeaRuleMovesAdmin1AndDropsPerekop()
    {
        var set = Rules(CrimeaRule).Apply(Bundled);
        var plain = BoundaryRules.None.Apply(Bundled);

        var ukr = set.Regions.ById("UKR")!;
        var rus = set.Regions.ById("RUS")!;
        Assert.Equal(plain.Regions.ById("UKR")!.Polygons.Count + 2, ukr.Polygons.Count);
        Assert.Equal(4, ukr.Polygons.Count);
        Assert.Equal(plain.Regions.ById("RUS")!.Polygons.Count - 1, rus.Polygons.Count);
        // The added polygons are the admin-1 polygons, unchanged, after Ukraine's own parts.
        var admin1 = Bundled.Subdivisions.Where(s => s.Code is "UA-43" or "UA-40").SelectMany(s => s.Parts).SelectMany(p => DateLine.SplitPolygon(p.Rings)).ToList();
        Assert.Equal(admin1, ukr.Polygons.Skip(2));
        Assert.DoesNotContain(rus.Polygons, p => p.Rings[0].All(q => q.LatitudeDeg is > 44.3 and < 46.3 && q.LongitudeDeg is > 32.4 and < 36.7));

        Assert.Equal("UKR", set.Regions.Find(new GeoPosition(45.0, 34.1))?.Id);   // Simferopol area
        Assert.Equal("UKR", set.Regions.Find(new GeoPosition(44.6, 33.5))?.Id);   // Sevastopol
        Assert.Equal("RUS", plain.Regions.Find(new GeoPosition(45.0, 34.1))?.Id); // the file itself draws it in Russia
        Assert.Equal("RUS", set.Regions.Find(new GeoPosition(45.04, 38.98))?.Id); // Krasnodar

        Assert.Equal(Bundled.Lines.Count - 1, set.Lines.Count);
        Assert.Equal(Bundled.Lines.Count, plain.Lines.Count);
        Assert.DoesNotContain(set.Lines, l => l.Points.Any(p => p.LatitudeDeg is > 45.7 and < 46.2 && p.LongitudeDeg is > 33.5 and < 35.1));
        Assert.Contains(set.Lines, l => l.Points.Any(p => p.LatitudeDeg is > 44.9 and < 45.7 && p.LongitudeDeg is > 36.5 and < 36.8));   // Kerch Strait stays

        Assert.Equal(["Crimea shown as part of Ukraine (UN General Assembly resolution 68/262)"], set.RuleDescriptions);
        Assert.Equal("Natural Earth 5.1.2 (2022-05-13)", set.Edition);
        Assert.Equal("v5.1.2", set.Tag);
        Assert.Equal(new DateOnly(2022, 5, 13), set.EditionDate);
    }

    [Fact]
    // Review focus 2: a new edition that reshapes Crimea is rejected, never half-applied.
    public void MismatchedRuleRejectsTheSet()
    {
        // One vertex of Russia's Crimea part moved: the part is no longer made of the admin-1 vertices.
        var regions = Bundled.Regions.Select(r => r.Id != "RUS" ? r : r with
        {
            Parts = [.. r.Parts.Select(p => p.Rings[0].All(q => q.Lat is > 44.3 and < 46.3 && q.Lon is > 32.4 and < 36.7)
                ? new RawPolygon([[(p.Rings[0][0].Lon + 0.001, p.Rings[0][0].Lat), .. p.Rings[0].Skip(1)]]) : p)],
        }).ToList();
        var reshaped = Bundled with { Regions = regions };
        var ex = Assert.Throws<BoundaryRuleException>(() => Rules(CrimeaRule).Apply(reshaped));
        Assert.Contains("rule moveAdmin1 UA-43 no longer matches", ex.Message);
        Assert.Equal("moveAdmin1 UA-43", ex.RuleName);

        // The admin-1 polygons missing from the set.
        var noAdmin1 = Bundled with { Subdivisions = [.. Bundled.Subdivisions.Where(s => s.Code != "UA-40")] };
        Assert.Contains("rule moveAdmin1 UA-43 no longer matches", Assert.Throws<BoundaryRuleException>(() => Rules(CrimeaRule).Apply(noAdmin1)).Message);

        // The Perekop line reshaped: the expected line count no longer matches.
        var lines = Bundled.Lines.Where(l => !l.Any(p => p.LatitudeDeg is > 45.7 and < 46.2 && p.LongitudeDeg is > 33.5 and < 35.1)).ToList();
        Assert.Equal(Bundled.Lines.Count - 1, lines.Count);
        Assert.Throws<BoundaryRuleException>(() => Rules(CrimeaRule).Apply(Bundled with { Lines = lines }));

        // The receiving region gone.
        Assert.Throws<BoundaryRuleException>(() => Rules(CrimeaRule).Apply(Bundled with { Regions = [.. Bundled.Regions.Where(r => r.Id != "UKR")] }));
    }

    [Fact]
    public void MoveAdmin1WorksOnAnyBody()
    {
        var raw = Sample();
        var set = Rules(MovePeninsula).Apply(raw);
        Assert.Equal("BETA", set.Regions.Find(new GeoPosition(1, 21.5))?.Id);
        Assert.Equal("ALPHA", set.Regions.Find(new GeoPosition(5, 5))?.Id);
        Assert.Single(set.Regions.ById("ALPHA")!.Polygons);
        Assert.Equal(3, set.Regions.ById("BETA")!.Polygons.Count);
        Assert.Single(set.Lines);
        Assert.Equal(["Peninsula shown as part of Beta"], set.RuleDescriptions);
        // Subdivisions of another parent do not match.
        Assert.Throws<BoundaryRuleException>(() => Rules(MovePeninsula.Replace("\"P-2\"", "\"Q-1\"")).Apply(raw));
    }

    [Fact]
    public void RuleWithoutReasonIsRejected()
    {
        var ex = Assert.Throws<BoundaryRuleException>(() => Rules("""{"kind": "mergeUnits", "units": ["SOL"], "into": "SOM"}"""));
        Assert.Contains("reason", ex.Message);
        Assert.Throws<BoundaryRuleException>(() => Rules("""{"kind": "mergeUnits", "units": ["SOL"], "into": "SOM", "reason": "  "}"""));
        Assert.Throws<BoundaryRuleException>(() => Rules("""{"kind": "alias", "names": {"X": "SOM"}, "reason": null}"""));
    }

    [Fact]
    public void MalformedRulesAreRejected()
    {
        Assert.Throws<BoundaryRuleException>(() => Rules("""{"kind": "teleport", "reason": "r"}"""));
        Assert.Throws<BoundaryRuleException>(() => Rules("""{"kind": "mergeUnits", "unit": ["SOL"], "into": "SOM", "reason": "typo in a key"}"""));
        Assert.Throws<BoundaryRuleException>(() => Rules("""{"kind": "moveAdmin1", "codes": [], "from": "A", "to": "B", "reason": "r"}"""));
        Assert.Throws<BoundaryRuleException>(() => Rules("""{"kind": "moveAdmin1", "codes": ["X"], "from": "A", "to": "A", "reason": "r"}"""));
        Assert.Throws<BoundaryRuleException>(() => BoundaryRules.Parse(Json("[]")));
        Assert.Throws<BoundaryRuleException>(() => BoundaryRules.Parse(Json("{\"rules\": [")));
        // Comments and trailing commas are allowed, so a rules file can explain itself.
        Assert.Single(BoundaryRules.Parse(Json("{ // UN view\n \"rules\": [" + MovePeninsula + ",], }")).Rules);
    }

    [Fact]
    public void MergeUnitsAndAliasesApply()
    {
        var rules = Rules("""
            {"kind": "mergeUnits", "units": ["GAMMA"], "into": "ALPHA", "reason": "the sources report Gamma with Alpha",
             "description": "Gamma counted with Alpha"},
            {"kind": "alias", "names": {"The Gamma Isles": "ALPHA", "Betaland": "BETA"}, "reason": "source spellings"}
            """);
        var set = rules.Apply(Sample());
        Assert.Equal(["ALPHA", "BETA"], set.Regions.All.Select(r => r.Id));
        Assert.Equal("ALPHA", set.Regions.Find(new GeoPosition(55, 55))?.Id);
        Assert.Equal("ALPHA", set.Regions.Resolve("Gamma")?.Id);
        Assert.Equal("ALPHA", set.Regions.Resolve("the gamma isles")?.Id);
        Assert.Equal("BETA", set.Regions.Resolve("Betaland")?.Id);
        Assert.Equal(["Gamma counted with Alpha"], rules.Descriptions);

        // Rules that name a region the set lacks reject it.
        Assert.Throws<BoundaryRuleException>(() => Rules("""{"kind": "mergeUnits", "units": ["OMEGA"], "into": "ALPHA", "reason": "r"}""").Apply(Sample()));
        Assert.Throws<BoundaryRuleException>(() => Rules("""{"kind": "alias", "names": {"Omega": "OMEGA"}, "reason": "r"}""").Apply(Sample()));
    }

    [Fact]
    public void SomalilandMergesAsInCountryResolver()
    {
        var rules = Rules("""{"kind": "mergeUnits", "units": ["SOL"], "into": "SOM", "reason": "C16"}""");
        var set = rules.Apply(Bundled);
        Assert.Equal("SOM", set.Regions.Find(new GeoPosition(9.56, 44.06))?.Id);   // Hargeisa
        Assert.Null(set.Regions.ById("SOL"));
        Assert.Equal("SOM", set.Regions.Resolve("Somaliland")?.Id);
    }

    [Fact]
    public void NoRulesLeavesTheFileAsItIs()
    {
        Assert.Empty(BoundaryRules.None.Rules);
        Assert.Empty(BoundaryRules.None.Descriptions);
        var set = BoundaryRules.None.Apply(Sample());
        Assert.Equal(3, set.Regions.All.Count);
        Assert.Equal(2, set.Lines.Count);
        Assert.Empty(set.RuleDescriptions);
    }
}
