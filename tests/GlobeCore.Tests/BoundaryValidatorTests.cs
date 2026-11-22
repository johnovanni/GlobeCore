using System.IO;
using AstroCore.Earth;
using GlobeCore.Regions;
using static GlobeCore.Tests.RegionTestData;

namespace GlobeCore.Tests;

public class BoundaryValidatorTests
{
    private static readonly IReadOnlyList<(GeoPosition Point, string ExpectedId)> Probes =
        [(new GeoPosition(5, 5), "ALPHA"), (new GeoPosition(1, 21.5), "BETA")];

    private static BoundaryValidator Validator(Func<RegionIndex, IReadOnlyList<string>>? names = null, int bundledCount = 3) =>
        new(Probes, names ?? (_ => []), bundledCount);

    private static readonly BoundarySet Moved = Rules(MovePeninsula).Apply(Sample());

    [Fact]
    public void ValidSetPasses()
    {
        var result = Validator().Validate(Moved);
        Assert.True(result.IsValid, result.Reason);
        Assert.Empty(result.Problems);
        Assert.Equal("", result.Reason);
    }

    [Fact]
    public void AProbeThatLandsElsewhereFails()
    {
        var unmoved = BoundaryRules.None.Apply(Sample());   // the peninsula is still Alpha's
        var result = Validator().Validate(unmoved);
        Assert.False(result.IsValid);
        Assert.Contains("expected BETA, found ALPHA", result.Reason);

        var sea = new BoundaryValidator([(new GeoPosition(-50, -50), "ALPHA")], _ => [], 3).Validate(Moved);
        Assert.Contains("expected ALPHA, found none", sea.Reason);
    }

    [Fact]
    public void ARegionCountOutOfRangeFails()
    {
        Assert.True(Validator(bundledCount: 3).Validate(Moved).IsValid);
        var result = Validator(bundledCount: 30).Validate(Moved);   // 3 regions where 30 were bundled
        Assert.False(result.IsValid);
        Assert.Contains("3 regions", result.Reason);
        Assert.Contains("30", result.Reason);
        // ±10% is inclusive: 100 bundled allows 90 to 110.
        Assert.True(BoundaryValidator.CountInRange(90, 100));
        Assert.True(BoundaryValidator.CountInRange(110, 100));
        Assert.False(BoundaryValidator.CountInRange(89, 100));
        Assert.False(BoundaryValidator.CountInRange(111, 100));
    }

    [Fact]
    public void AnUnresolvedNameFails()
    {
        string[] sourceNames = ["Alpha Land", "Beta", "Gamma Republic"];
        var result = Validator(idx => [.. sourceNames.Where(n => idx.Resolve(n) is null)]).Validate(Moved);
        Assert.False(result.IsValid);
        Assert.Contains("Gamma Republic", result.Reason);
        Assert.DoesNotContain("Alpha Land", result.Reason);
    }

    [Fact]
    public void RulesThatNoLongerMatchFailValidation()
    {
        var reshaped = Sample() with { Subdivisions = [] };
        var result = Validator().Validate(reshaped, Rules(MovePeninsula), out var set);
        Assert.False(result.IsValid);
        Assert.Null(set);
        Assert.Contains("no longer matches", result.Reason);

        var ok = Validator().Validate(Sample(), Rules(MovePeninsula), out var good);
        Assert.True(ok.IsValid, ok.Reason);
        Assert.NotNull(good);
    }

    [Fact]
    public void ASetThatCannotBeBuiltIsAProblemNotAnException()
    {
        // A vertex at latitude 95° parses, but splitting the polygons at ±180° refuses it (DateLine: ArgumentException).
        var sample = Sample();
        var broken = sample with
        {
            Regions = [.. sample.Regions.Select(r => r.Id == "GAMMA" ? r with { Parts = [new RawPolygon([[(50, 50), (60, 95), (60, 60), (50, 50)]])] } : r)],
        };
        var result = Validator().Validate(broken, Rules(MovePeninsula), out var set);
        Assert.False(result.IsValid);
        Assert.Null(set);
        Assert.StartsWith("the set could not be built: ", result.Reason);

        // The name hook's own failures were already problems; the probes and count checks never throw on a built set.
        var hook = new BoundaryValidator(Probes, _ => throw new InvalidOperationException("boom"), 3).Validate(Sample(), Rules(MovePeninsula), out _);
        Assert.Contains("boom", hook.Reason);
    }

    [Fact]
    public void ProbesFileIsRead()
    {
        var probes = BoundaryValidator.ReadProbes(Json("""
            {"probes": [{"name": "Khartoum", "lat": 15.5, "lon": 32.53, "expect": "SDN"},
                        {"name": "Fiji east", "lat": -16.85, "lon": -179.95, "expect": "FJI"}]}
            """));
        Assert.Equal(2, probes.Count);
        Assert.Equal((15.5, 32.53, "SDN"), (probes[0].Point.LatitudeDeg, probes[0].Point.LongitudeDeg, probes[0].ExpectedId));
        Assert.Throws<InvalidDataException>(() => BoundaryValidator.ReadProbes(Json("""{"probes": [{"lat": 1, "lon": 2}]}""")));
        Assert.Throws<InvalidDataException>(() => BoundaryValidator.ReadProbes(Json("""{"probes": [{"lat": 91, "lon": 2, "expect": "X"}]}""")));
    }
}
