using System.Text.Json;
using AstroCore.Earth;
using GlobeCore.Geo;
using GlobeCore.Logging;
using GlobeCore.Regions;

namespace GlobeCore.Earth;

/// <summary>
/// Earth's political boundaries from Natural Earth (1:50m): reads a release's three files (admin-0 countries, admin-0
/// land boundary lines, admin-1 states and provinces) into <see cref="RawBoundaryData"/>, mapping Natural Earth's
/// properties to <see cref="Region"/>s (id ISO_A3_EH, or ADM0_A3 where that is -99; names NAME, then NAME_LONG,
/// FORMAL_EN, ADMIN; label LABEL_Y/LABEL_X) and admin-1 features to subdivisions (iso_3166_2 in adm0_a3). The bundled
/// set is Natural Earth 5.1.2, embedded in this pack; Assets/README.md records the files and the rules format.
/// </summary>
public static class NaturalEarthBoundaries
{
    public const string BundledTag = "v5.1.2";
    public static readonly DateOnly BundledDate = new(2022, 5, 13);

    /// <summary>The release files a boundary set is made of (the names in the release's <c>geojson/</c> folder).</summary>
    public const string CountriesFile = EarthAssets.CountriesFile;
    public const string LinesFile = EarthAssets.LandBoundariesFile;
    public const string Admin1File = "ne_50m_admin_1_states_provinces.geojson";

    /// <summary>The three files to download for a release, in the order above.</summary>
    public static IReadOnlyList<string> ReleaseFiles { get; } = [CountriesFile, LinesFile, Admin1File];

    // Names the Natural Earth file does not carry, in current official or common use.
    private static readonly Dictionary<string, string> BuiltInAliases = new()
    {
        ["Türkiye"] = "TUR", ["Republic of Türkiye"] = "TUR", ["Czechia"] = "CZE", ["Cabo Verde"] = "CPV",
        ["Timor-Leste"] = "TLS", ["Burma"] = "MMR", ["Ivory Coast"] = "CIV", ["Swaziland"] = "SWZ",
    };

    /// <summary>
    /// TRANSITIONAL. The Crimea rule (Crimea and Sevastopol as part of Ukraine, UN General Assembly resolution 68/262;
    /// EarthRelief3D ruling R15), built in so that the parameterless <see cref="CountryIndex.LoadNaturalEarth()"/> and
    /// <see cref="NaturalEarthLines.LandBoundaries()"/> keep their results until the apps supply their own
    /// <c>boundary-rules.json</c> (Plan 2b, Tasks 3 and 4). Then those callers move to the overloads that take the app's
    /// rules, and this default goes. New code should not depend on it.
    /// </summary>
    internal const string TransitionalDefaultRulesJson = """
        {
          "rules": [
            {
              "kind": "moveAdmin1",
              "name": "moveAdmin1 UA-43",
              "codes": ["UA-43", "UA-40"],
              "from": "RUS",
              "to": "UKR",
              "expectParts": 1,
              "expectLines": 1,
              "description": "Crimea shown as part of Ukraine (UN General Assembly resolution 68/262)",
              "reason": "UN practice (owner's decision Q3, EarthRelief3D ruling R15): UN General Assembly resolution 68/262 of 27 March 2014, Territorial integrity of Ukraine. Natural Earth's default admin-0 file draws Crimea and Sevastopol in Russia; their admin-1 polygons (UA-43, UA-40) move to Ukraine and the Perekop line, now internal, goes."
            }
          ]
        }
        """;

    private static readonly Lazy<BoundaryRules> TransitionalRules = new(() =>
    {
        using var s = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(TransitionalDefaultRulesJson));
        return BoundaryRules.Parse(s);
    });

    private static readonly Lazy<BoundarySet> TransitionalSet = new(() => LoadBundled(TransitionalRules.Value));

    /// <summary>TRANSITIONAL: <see cref="TransitionalDefaultRulesJson"/>, parsed.</summary>
    internal static BoundaryRules TransitionalDefaultRules => TransitionalRules.Value;

    /// <summary>TRANSITIONAL: the bundled set with <see cref="TransitionalDefaultRules"/>, read once and shared.</summary>
    internal static BoundarySet TransitionalDefaultSet => TransitionalSet.Value;

    /// <summary>"Natural Earth 5.1.2 (2022-05-13)" from the tag and the release date.</summary>
    public static string EditionName(string tag, DateOnly date) =>
        $"Natural Earth {tag.TrimStart('v', 'V')} ({date:yyyy-MM-dd})";

    /// <summary>The bundled Natural Earth 5.1.2 files (the admin-1 file is the Crimea and Sevastopol extract), no rules applied.</summary>
    public static RawBoundaryData ReadBundled()
    {
        using var countries = EarthAssets.Open(EarthAssets.CountriesFile);
        using var lines = EarthAssets.Open(EarthAssets.LandBoundariesFile);
        using var admin1 = EarthAssets.Open(EarthAssets.CrimeaRegionsFile);
        return Parse(BundledTag, BundledDate, countries, lines, admin1);
    }

    /// <summary>The bundled set with the app's rules applied.</summary>
    /// <exception cref="BoundaryRuleException">A rule does not match the bundled files.</exception>
    public static BoundarySet LoadBundled(BoundaryRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return rules.Apply(ReadBundled());
    }

    /// <summary>
    /// The set to start with: the newest stored set newer than the bundled one that is whole, takes the rules and passes
    /// validation, otherwise the bundled set. With no validator, stored sets are checked only for the rules and the
    /// region count (±10% of the bundled set's).
    /// </summary>
    public static BoundarySet Load(BoundaryRules rules, BoundaryStore? store = null, BoundaryValidator? validator = null, IAppLog? log = null)
    {
        ArgumentNullException.ThrowIfNull(rules);
        BoundarySet? bundled = null;
        if (store is not null)
        {
            if (validator is null)
            {
                bundled = LoadBundled(rules);
                validator = new BoundaryValidator([], _ => [], bundled.Regions.All.Count);
            }
            if (store.TryLoadNewest(Parse, rules, validator, out var stored, out var reason, newerThanTag: BundledTag))
            {
                log?.Info($"Boundaries: {stored.Edition} from {store.CacheDir}");
                return stored;
            }
            log?.Info($"Boundaries: bundled {EditionName(BundledTag, BundledDate)} ({reason})");
        }
        return bundled ?? LoadBundled(rules);
    }

    /// <summary>
    /// Reads a stored or downloaded release: the files <see cref="CountriesFile"/>, <see cref="LinesFile"/> and
    /// <see cref="Admin1File"/> (or the bundled admin-1 extract, <see cref="EarthAssets.CrimeaRegionsFile"/>).
    /// </summary>
    /// <exception cref="InvalidDataException">A file is missing or is not the GeoJSON expected.</exception>
    public static RawBoundaryData Parse(StoredBoundaryFiles stored)
    {
        ArgumentNullException.ThrowIfNull(stored);
        byte[] Need(params string[] names)
        {
            foreach (var n in names)
                if (stored.Files.TryGetValue(n, out var b)) return b;
            throw new InvalidDataException($"boundary set {stored.Tag}: {names[0]} is missing");
        }
        var raw = Parse(stored.Tag, stored.Date,
            new MemoryStream(Need(CountriesFile)), new MemoryStream(Need(LinesFile)), new MemoryStream(Need(Admin1File, EarthAssets.CrimeaRegionsFile)));
        return raw with { Files = stored.Files };
    }

    /// <summary>Reads the three GeoJSON files of one release. The caller disposes the streams.</summary>
    /// <exception cref="InvalidDataException">A file is not valid GeoJSON of the expected shape.</exception>
    public static RawBoundaryData Parse(string tag, DateOnly date, Stream countries, Stream lines, Stream admin1)
    {
        try
        {
            var idOfAdm0 = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var regions = ReadCountries(countries, idOfAdm0);
            var boundaryLines = GeoJsonLines.Read(lines);
            var subdivisions = ReadAdmin1(admin1, idOfAdm0);
            if (regions.Count == 0) throw new InvalidDataException("no countries");
            return new RawBoundaryData(tag, EditionName(tag, date), date, regions, boundaryLines, subdivisions) { Aliases = BuiltInAliases };
        }
        catch (Exception ex) when (ex is not (InvalidDataException or OutOfMemoryException))
        {   // any parse failure of a downloaded file means the file is not usable
            throw new InvalidDataException($"Natural Earth {tag}: not the GeoJSON expected ({ex.Message})", ex);
        }
    }

    // The region id of a Natural Earth country feature: ISO_A3_EH, or ADM0_A3 where that is missing or -99.
    private static string RegionId(JsonElement p)
    {
        string iso3 = Str(p, "ISO_A3_EH");
        return iso3.Length == 0 || iso3 == "-99" ? Str(p, "ADM0_A3") : iso3;
    }

    private static List<RawRegion> ReadCountries(Stream s, Dictionary<string, string> idOfAdm0)
    {
        using var doc = JsonDocument.Parse(s);
        var regions = new List<RawRegion>();
        foreach (var f in doc.RootElement.GetProperty("features").EnumerateArray())
        {
            var p = f.GetProperty("properties");
            string iso3 = RegionId(p);
            // First feature wins when two countries share an ADM0_A3 (none do in 5.1.2).
            string adm0 = Str(p, "ADM0_A3");
            if (adm0.Length > 0) idOfAdm0.TryAdd(adm0, iso3);
            string[] other = [.. new[] { Str(p, "NAME_LONG"), Str(p, "FORMAL_EN"), Str(p, "ADMIN") }.Where(n => n.Length > 0)];
            var label = new GeoPosition(Num(p, "LABEL_Y"), Num(p, "LABEL_X"));
            regions.Add(new RawRegion(iso3, Str(p, "NAME"), other, label, ReadParts(f)));
        }
        return regions;
    }

    // A subdivision's parent is named by its feature's adm0_a3, which is ADM0_A3, not the region id: South Sudan is SDS
    // there and SSD (ISO_A3_EH) as a region. So the parent id goes through the countries file's own ADM0_A3 to id mapping.
    private static List<RawSubdivision> ReadAdmin1(Stream s, IReadOnlyDictionary<string, string> idOfAdm0)
    {
        using var doc = JsonDocument.Parse(s);
        var list = new List<RawSubdivision>();
        foreach (var f in doc.RootElement.GetProperty("features").EnumerateArray())
        {
            var p = f.GetProperty("properties");
            var parts = ReadParts(f);
            if (parts.Count == 0) continue;
            string adm0 = Str(p, "adm0_a3");
            list.Add(new RawSubdivision(Str(p, "iso_3166_2"), idOfAdm0.GetValueOrDefault(adm0, adm0), Str(p, "name"), parts));
        }
        return list;
    }

    /// <summary>A feature's Polygon or MultiPolygon as raw parts ([lon, lat] rings); none for other or null geometry.</summary>
    private static List<RawPolygon> ReadParts(JsonElement feature)
    {
        if (!feature.TryGetProperty("geometry", out var g) || g.ValueKind != JsonValueKind.Object) return [];
        var coords = g.GetProperty("coordinates");
        return (g.GetProperty("type").GetString() ?? "") switch
        {
            "Polygon" => [ReadRings(coords)],
            "MultiPolygon" => [.. coords.EnumerateArray().Select(ReadRings)],
            _ => [],
        };
    }

    private static RawPolygon ReadRings(JsonElement rings)
    {
        var list = new List<IReadOnlyList<(double Lon, double Lat)>>();
        foreach (var ring in rings.EnumerateArray())
            list.Add(ring.EnumerateArray().Select(pt => (pt[0].GetDouble(), pt[1].GetDouble())).ToList());
        return new RawPolygon(list);
    }

    private static string Str(JsonElement p, string key) =>
        p.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static double Num(JsonElement p, string key) =>
        p.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
}
