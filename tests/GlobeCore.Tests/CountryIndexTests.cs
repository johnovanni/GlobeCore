using AstroCore.Earth;
using GlobeCore.Earth;
using GlobeCore.Geo;

namespace GlobeCore.Tests;

public class CountryIndexTests
{
    private static readonly CountryIndex Index = CountryIndex.LoadNaturalEarth();

    [Fact]
    public void FindsSudanAtKhartoum() =>
        Assert.Equal("SDN", Index.Find(new GeoPosition(15.5, 32.53))?.Iso3);

    [Fact]
    // The brief's points (-17.0, 179.9) and (-16.9, -179.9) are sea in the 50m file (the islands' tips are just short of them),
    // so these are points inside the same two island pieces either side of the meridian.
    public void FindsFijiBothSidesOfTheDateLine()
    {
        Assert.Equal("FJI", Index.Find(new GeoPosition(-16.93, 179.95))?.Iso3);
        Assert.Equal("FJI", Index.Find(new GeoPosition(-16.85, -179.95))?.Iso3);
    }

    [Fact]
    public void OceanIsNull() => Assert.Null(Index.Find(new GeoPosition(0, -140)));

    [Fact]
    public void NaNPointIsNull() => Assert.Null(Index.Find(new GeoPosition(double.NaN, double.NaN)));

    [Fact]
    public void HolesAreRespected()
    {
        // Lesotho is a hole in South Africa.
        Assert.Equal("LSO", Index.Find(new GeoPosition(-29.6, 28.2))?.Iso3);
        Assert.Equal("ZAF", Index.Find(new GeoPosition(-30.5, 25.0))?.Iso3);
    }

    [Fact]
    public void MultiPolygonIslandsAreFound() =>
        Assert.Equal("USA", Index.Find(new GeoPosition(21.3, -157.85))?.Iso3);   // Honolulu

    [Fact]
    public void ResolvesVariants()
    {
        Assert.Equal("COD", Index.Resolve("Democratic Republic of the Congo")?.Iso3);
        Assert.Equal("CIV", Index.Resolve("Côte d'Ivoire")?.Iso3);
        Assert.Equal("CIV", Index.Resolve("Cote d'Ivoire")?.Iso3);
        Assert.Equal("CIV", Index.Resolve("Côte d’Ivoire")?.Iso3);
        Assert.Equal("TUR", Index.Resolve("Türkiye")?.Iso3);
        Assert.Equal("KOR", Index.Resolve("Republic of Korea")?.Iso3);
        Assert.Null(Index.Resolve("Atlantis"));
        Assert.Null(Index.Resolve("  "));
    }

    [Fact]
    public void ShortNameBeatsLongerNameOfAnotherUnit() =>
        Assert.Equal("KOR", Index.Resolve("South Korea")?.Iso3);

    [Fact]
    public void AliasesResolve()
    {
        Assert.Null(Index.Resolve("Palestine, State of"));
        var withAlias = Index.WithAliases(new Dictionary<string, string> { ["Palestine, State of"] = "PSE" });
        Assert.Equal("PSE", withAlias.Resolve("palestine state of")?.Iso3);
        Assert.Null(Index.Resolve("Palestine, State of"));   // the original is unchanged
    }

    [Fact]
    public void ByIso3AndAll()
    {
        Assert.Equal("Sudan", Index.ByIso3("SDN")?.Name);
        Assert.Equal("Sudan", Index.ByIso3("sdn")?.Name);
        Assert.Null(Index.ByIso3("XXX"));
        Assert.True(Index.All.Count > 200);
        Assert.All(Index.All, c => Assert.Equal(3, c.Iso3.Length));
        Assert.DoesNotContain(Index.All, c => c.Iso3 == "-99");
    }

    [Fact]
    public void UnitsMergeSomalilandIntoSomalia()
    {
        var hargeisa = new GeoPosition(9.56, 44.06);
        Assert.Equal("SOL", Index.Find(hargeisa)?.Iso3);
        var merged = Index.WithUnits(new Dictionary<string, string> { ["SOL"] = "SOM" });
        Assert.Equal("SOM", merged.Find(hargeisa)?.Iso3);
        Assert.Null(merged.ByIso3("SOL"));
        Assert.Equal("SOM", merged.Resolve("Somaliland")?.Iso3);
        Assert.Equal(Index.All.Count - 1, merged.All.Count);
    }
}
