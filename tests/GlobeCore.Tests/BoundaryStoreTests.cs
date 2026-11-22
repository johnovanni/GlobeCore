using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using AstroCore.Earth;
using GlobeCore.Earth;
using GlobeCore.Regions;

namespace GlobeCore.Tests;

public sealed class BoundaryStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "GlobeCoreTests", "boundaries-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static readonly BoundaryRules Crimea = BoundaryRules.Parse(RegionTestData.Json("{\"rules\": [" + BoundaryRulesTests.CrimeaRule + "]}"));

    /// <summary>The bundled files, as a download of a later release would bring them (the admin-1 extract stands in for the full file).</summary>
    private static Dictionary<string, byte[]> BundledFiles()
    {
        static byte[] Read(string name)
        {
            using var s = EarthAssets.Open(name);
            using var m = new MemoryStream();
            s.CopyTo(m);
            return m.ToArray();
        }
        return new()
        {
            [NaturalEarthBoundaries.CountriesFile] = Read(EarthAssets.CountriesFile),
            [NaturalEarthBoundaries.LinesFile] = Read(EarthAssets.LandBoundariesFile),
            [NaturalEarthBoundaries.Admin1File] = Read(EarthAssets.CrimeaRegionsFile),
        };
    }

    private static BoundaryValidator Validator() =>
        new([(new GeoPosition(15.5, 32.53), "SDN"), (new GeoPosition(45.0, 34.1), "UKR")], _ => [], 242);

    [Fact]
    // Review focus 3: restarting offline after an update loads the stored validated set, not the bundled one.
    public void StoredSetLoadsBeforeBundled()
    {
        var store = new BoundaryStore(_dir);
        Assert.True(store.Save("v5.2.0", new DateOnly(2024, 3, 1), BundledFiles(), out var error), error?.ToString());

        var set = NaturalEarthBoundaries.Load(Crimea, store, Validator());
        Assert.Equal("v5.2.0", set.Tag);
        Assert.Equal("Natural Earth 5.2.0 (2024-03-01)", set.Edition);
        Assert.Equal("UKR", set.Regions.Find(new GeoPosition(45.0, 34.1))?.Id);   // the rules are applied to the stored set too
        Assert.Equal(["Crimea shown as part of Ukraine (UN General Assembly resolution 68/262)"], set.RuleDescriptions);

        Assert.True(store.TryLoadNewest(NaturalEarthBoundaries.Parse, Crimea, Validator(), out var direct, out var reason), reason);
        Assert.Equal("v5.2.0", direct!.Tag);
    }

    [Fact]
    public void ManifestRecordsTagDateAndSha256()
    {
        var files = BundledFiles();
        Assert.True(new BoundaryStore(_dir).Save("v5.2.0", new DateOnly(2024, 3, 1), files, out _));
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(_dir, "v5.2.0", "manifest.json")));
        var root = doc.RootElement;
        Assert.Equal("v5.2.0", root.GetProperty("tag").GetString());
        Assert.Equal("2024-03-01", root.GetProperty("date").GetString());
        foreach (var (name, bytes) in files)
        {
            var entry = root.GetProperty("files").GetProperty(name);
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), entry.GetProperty("sha256").GetString());
            Assert.Equal(bytes.Length, entry.GetProperty("bytes").GetInt64());
            Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(_dir, "v5.2.0", name)));
        }
        Assert.Empty(Directory.GetFiles(Path.Combine(_dir, "v5.2.0"), "*.tmp"));
    }

    [Fact]
    public void CorruptStoreFallsBackToBundled()
    {
        var store = new BoundaryStore(_dir);
        Assert.True(store.Save("v5.2.0", new DateOnly(2024, 3, 1), BundledFiles(), out _));
        var countries = Path.Combine(_dir, "v5.2.0", NaturalEarthBoundaries.CountriesFile);
        var bytes = File.ReadAllBytes(countries);
        bytes[1000] ^= 0x20;   // one flipped bit
        File.WriteAllBytes(countries, bytes);

        Assert.False(store.TryLoadNewest(NaturalEarthBoundaries.Parse, Crimea, Validator(), out var none, out var reason));
        Assert.Null(none);
        Assert.Contains("SHA-256", reason);
        var set = NaturalEarthBoundaries.Load(Crimea, store, Validator());
        Assert.Equal("v5.1.2", set.Tag);
        Assert.Equal("Natural Earth 5.1.2 (2022-05-13)", set.Edition);
        Assert.Equal("UKR", set.Regions.Find(new GeoPosition(45.0, 34.1))?.Id);
        Assert.True(File.Exists(countries));   // the stored set is kept, never deleted

        // A broken manifest, or a missing file, falls back too.
        File.WriteAllText(Path.Combine(_dir, "v5.2.0", "manifest.json"), "{ not json");
        Assert.Equal("v5.1.2", NaturalEarthBoundaries.Load(Crimea, store, Validator()).Tag);
        Assert.True(store.Save("v5.3.0", new DateOnly(2025, 1, 1), BundledFiles(), out _));
        File.Delete(Path.Combine(_dir, "v5.3.0", NaturalEarthBoundaries.LinesFile));
        Assert.Equal("v5.1.2", NaturalEarthBoundaries.Load(Crimea, store, Validator()).Tag);
    }

    [Fact]
    public void ANewerGoodSetIsPreferredAndABadNewestFallsBackToTheLastGood()
    {
        var store = new BoundaryStore(_dir);
        Assert.True(store.Save("v5.2.0", new DateOnly(2024, 3, 1), BundledFiles(), out _));
        Assert.True(store.Save("v5.10.0", new DateOnly(2025, 6, 1), BundledFiles(), out _));
        Assert.Equal(["v5.10.0", "v5.2.0"], store.StoredTags());   // version order, not text order
        Assert.Equal("v5.10.0", NaturalEarthBoundaries.Load(Crimea, store, Validator()).Tag);

        File.WriteAllText(Path.Combine(_dir, "v5.10.0", NaturalEarthBoundaries.LinesFile), "<html>rate limited</html>");
        Assert.Equal("v5.2.0", NaturalEarthBoundaries.Load(Crimea, store, Validator()).Tag);
    }

    [Fact]
    public void AStoredSetThatFailsValidationIsNotUsed()
    {
        var store = new BoundaryStore(_dir);
        Assert.True(store.Save("v5.2.0", new DateOnly(2024, 3, 1), BundledFiles(), out _));
        var strict = new BoundaryValidator([(new GeoPosition(45.0, 34.1), "RUS")], _ => [], 242);   // a probe the set fails
        Assert.False(store.TryLoadNewest(NaturalEarthBoundaries.Parse, Crimea, strict, out _, out var reason));
        Assert.Contains("v5.2.0", reason);
        Assert.Contains("expected RUS", reason);
    }

    [Fact]
    public void StoredSetsNoNewerThanTheBundledOneAreIgnored()
    {
        var store = new BoundaryStore(_dir);
        Assert.True(store.Save("v5.1.2", new DateOnly(2022, 5, 13), BundledFiles(), out _));
        Assert.True(store.Save("v5.0.0", new DateOnly(2021, 1, 1), BundledFiles(), out _));
        Assert.False(store.TryLoadNewest(NaturalEarthBoundaries.Parse, Crimea, Validator(), out _, out _, newerThanTag: "v5.1.2"));
        var set = NaturalEarthBoundaries.Load(Crimea, store, Validator());
        Assert.Equal("v5.1.2", set.Tag);
        Assert.DoesNotContain(set.Regions.All, r => r.Id == "-99");
    }

    [Fact]
    public void EmptyOrMissingStoreLoadsBundled()
    {
        var store = new BoundaryStore(Path.Combine(_dir, "never-created"));
        Assert.Empty(store.StoredTags());
        Assert.False(store.TryLoadNewest(NaturalEarthBoundaries.Parse, Crimea, Validator(), out _, out var reason));
        Assert.Contains("no stored", reason);
        Assert.Equal("v5.1.2", NaturalEarthBoundaries.Load(Crimea, store).Tag);
        Assert.Equal("v5.1.2", NaturalEarthBoundaries.Load(Crimea).Tag);
    }

    [Theory]
    [InlineData("..")]
    [InlineData("v5/../x")]
    [InlineData("")]
    [InlineData("v5.2.0\\x")]
    public void UnsafeTagsAndFileNamesAreRefused(string bad)
    {
        var store = new BoundaryStore(_dir);
        Assert.Throws<ArgumentException>(() => store.Save(bad, new DateOnly(2024, 1, 1), BundledFiles(), out _));
        Assert.Throws<ArgumentException>(() => store.Save("v5.2.0", new DateOnly(2024, 1, 1), new Dictionary<string, byte[]> { [bad] = [1] }, out _));
    }

    [Fact]
    public void TagsCompareAsVersions()
    {
        Assert.True(BoundaryStore.CompareTags("v5.10.0", "v5.2.0") > 0);
        Assert.True(BoundaryStore.CompareTags("v5.1.2", "v5.1.2") == 0);
        Assert.True(BoundaryStore.CompareTags("5.1.3", "v5.1.2") > 0);
        Assert.True(BoundaryStore.CompareTags("v5.1.2", "v6.0.0") < 0);
    }
}
