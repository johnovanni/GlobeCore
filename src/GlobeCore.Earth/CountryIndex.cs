using AstroCore.Earth;
using GlobeCore.Regions;

namespace GlobeCore.Earth;

/// <summary>
/// Earth's countries (Natural Earth 1:50m admin-0 units, by ISO3): point-in-country lookup and name matching. A facade
/// over the body-agnostic <see cref="RegionIndex"/> (<see cref="Regions"/>), giving each region as a
/// <see cref="Country"/>. Instances are immutable; <see cref="WithUnits"/> and <see cref="WithAliases"/> return new ones.
/// </summary>
public sealed class CountryIndex
{
    // One Country per Region (by reference), so Find, ByIso3, Resolve and All return the same instance. Read-only after construction.
    private readonly Dictionary<Region, Country> _countries = new(ReferenceEqualityComparer.Instance);

    private CountryIndex(RegionIndex regions)
    {
        Regions = regions;
        var all = new List<Country>(regions.All.Count);
        foreach (var r in regions.All)
        {
            var c = new Country(r.Id, r.Name, r.LabelPoint, r.Polygons);
            _countries[r] = c;
            all.Add(c);
        }
        All = all;
    }

    /// <summary>The index this facade wraps (ids are ISO3 codes).</summary>
    public RegionIndex Regions { get; }

    public IReadOnlyList<Country> All { get; }

    /// <summary>The countries of a boundary set (with whatever rules it was built with).</summary>
    public static CountryIndex From(BoundarySet set)
    {
        ArgumentNullException.ThrowIfNull(set);
        return new CountryIndex(set.Regions);
    }

    /// <summary>The countries of a region index whose ids are ISO3 codes.</summary>
    public static CountryIndex From(RegionIndex regions)
    {
        ArgumentNullException.ThrowIfNull(regions);
        return new CountryIndex(regions);
    }

    /// <summary>The bundled Natural Earth 5.1.2 countries with the app's rules file applied (see <see cref="BoundaryRules.Parse"/>).</summary>
    /// <exception cref="BoundaryRuleException">The rules are malformed or do not match the bundled files.</exception>
    public static CountryIndex LoadNaturalEarth(Stream rulesJson) =>
        From(NaturalEarthBoundaries.LoadBundled(BoundaryRules.Parse(rulesJson)));

    /// <summary>
    /// Merges each listed unit's polygons into another unit (keys and values are ISO3 codes, e.g. SOL to SOM) and drops the
    /// merged unit; its names still resolve, to the unit it joined. Unknown codes are ignored; a chain (A to B, B to C)
    /// is followed to its end.
    /// </summary>
    public CountryIndex WithUnits(IReadOnlyDictionary<string, string> mergeIntoIso3)
    {
        ArgumentNullException.ThrowIfNull(mergeIntoIso3);
        return new CountryIndex(Regions.WithMerges(mergeIntoIso3));
    }

    /// <summary>Adds names (any spelling) that resolve to an ISO3 code. They replace the file's own names and the built-in aliases.</summary>
    public CountryIndex WithAliases(IReadOnlyDictionary<string, string> aliasToIso3)
    {
        ArgumentNullException.ThrowIfNull(aliasToIso3);
        return new CountryIndex(Regions.WithAliases(aliasToIso3));
    }

    /// <summary>The country that contains the point (holes excluded), or null over the sea.</summary>
    public Country? Find(GeoPosition p) => Of(Regions.Find(p));

    public Country? ByIso3(string iso3) => Of(Regions.ById(iso3));

    /// <summary>Matches a name case- and accent-insensitively, ignoring punctuation such as apostrophes.</summary>
    public Country? Resolve(string name) => Of(Regions.Resolve(name));

    /// <summary>Lower case, accents dropped, apostrophes removed, other punctuation and spaces as a single space.</summary>
    internal static string Normalize(string text) => RegionIndex.Normalize(text);

    private Country? Of(Region? r) => r is null ? null : _countries[r];
}
