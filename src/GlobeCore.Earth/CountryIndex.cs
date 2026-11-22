using System.Globalization;
using System.Text;
using System.Text.Json;
using AstroCore.Earth;
using GlobeCore.Geo;

namespace GlobeCore.Earth;

/// <summary>
/// Natural Earth 1:50m admin-0 countries: point-in-country lookup and name matching. Instances are immutable;
/// <see cref="WithUnits"/> and <see cref="WithAliases"/> return new ones.
/// </summary>
public sealed class CountryIndex
{
    private sealed record Entry(Country Country, IReadOnlyList<string> Names, (double MinLat, double MinLon, double MaxLat, double MaxLon)[] Boxes);

    // Names the Natural Earth file does not carry, in current official or common use.
    private static readonly Dictionary<string, string> BuiltInAliases = new()
    {
        ["Türkiye"] = "TUR", ["Republic of Türkiye"] = "TUR", ["Czechia"] = "CZE", ["Cabo Verde"] = "CPV",
        ["Timor-Leste"] = "TLS", ["Burma"] = "MMR", ["Ivory Coast"] = "CIV", ["Swaziland"] = "SWZ",
    };

    private readonly IReadOnlyList<Entry> _entries;
    private readonly Dictionary<string, Entry> _byIso3;
    private readonly Dictionary<string, Entry> _byName;
    private readonly IReadOnlyList<(string Key, string Iso3)> _aliases;

    private CountryIndex(IReadOnlyList<Entry> entries, IReadOnlyList<(string Key, string Iso3)> aliases)
    {
        _entries = entries;
        _aliases = aliases;
        All = entries.Select(e => e.Country).ToList();
        _byIso3 = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in entries) _byIso3.TryAdd(e.Country.Iso3, e);

        // Priority: NAME, then NAME_LONG, FORMAL_EN, ADMIN across all countries, so a short name is never shadowed by a longer one.
        _byName = new Dictionary<string, Entry>();
        for (int i = 0; i < 4; i++)
            foreach (var e in entries)
                if (i < e.Names.Count && Normalize(e.Names[i]) is { Length: > 0 } k) _byName.TryAdd(k, e);
        foreach (var (key, iso3) in aliases)
            if (key.Length > 0 && _byIso3.TryGetValue(iso3, out var target)) _byName[key] = target;
    }

    public IReadOnlyList<Country> All { get; }

    /// <summary>
    /// Reads the embedded Natural Earth file. Crimea and Sevastopol are part of Ukraine, as on UN maps (UN General
    /// Assembly resolution 68/262): <see cref="CrimeaFixup"/> moves them out of Russia's polygons as the file is read.
    /// </summary>
    public static CountryIndex LoadNaturalEarth()
    {
        using var s = EarthAssets.Open(EarthAssets.CountriesFile);
        using var doc = JsonDocument.Parse(s);
        var entries = new List<Entry>();
        int movedParts = 0, receivers = 0;
        foreach (var f in doc.RootElement.GetProperty("features").EnumerateArray())
        {
            var p = f.GetProperty("properties");
            string iso3 = Str(p, "ISO_A3_EH");
            if (iso3.Length == 0 || iso3 == "-99") iso3 = Str(p, "ADM0_A3");
            string name = Str(p, "NAME");
            var label = new GeoPosition(Num(p, "LABEL_Y"), Num(p, "LABEL_X"));
            var polygons = new List<GeoPolygon>();
            if (f.TryGetProperty("geometry", out var g) && g.ValueKind == JsonValueKind.Object)
            {
                string type = g.GetProperty("type").GetString() ?? "";
                var coords = g.GetProperty("coordinates");
                List<IReadOnlyList<IReadOnlyList<(double Lon, double Lat)>>> parts = type switch
                {
                    "Polygon" => [ReadRings(coords)],
                    "MultiPolygon" => [.. coords.EnumerateArray().Select(ReadRings)],
                    _ => [],
                };
                foreach (var rings in parts)
                {
                    if (iso3 == CrimeaFixup.From && CrimeaFixup.IsRegionsPart(rings)) { movedParts++; continue; }
                    polygons.AddRange(DateLine.SplitPolygon(rings));
                }
                if (iso3 == CrimeaFixup.To)
                {
                    receivers++;
                    foreach (var rings in CrimeaFixup.Get().Polygons) polygons.AddRange(DateLine.SplitPolygon(rings));
                }
            }
            var names = new[] { name, Str(p, "NAME_LONG"), Str(p, "FORMAL_EN"), Str(p, "ADMIN") };
            entries.Add(MakeEntry(new Country(iso3, name, label, polygons), names));
        }
        // The embedded files are fixed, so a mismatch is a packaging bug: fail loudly rather than show Crimea in Russia.
        if (movedParts != 1 || receivers != 1)
            throw new InvalidDataException($"Crimea fix-up: expected 1 {CrimeaFixup.From} part and 1 {CrimeaFixup.To} feature, found {movedParts} and {receivers}");
        var aliases = BuiltInAliases.Select(kv => (Normalize(kv.Key), kv.Value)).ToList();
        return new CountryIndex(entries, aliases);
    }

    /// <summary>
    /// Merges each listed unit's polygons into another unit (keys and values are ISO3 codes, e.g. SOL to SOM) and drops the
    /// merged unit; its names still resolve, to the unit it joined. Unknown codes are ignored; a chain (A to B, B to C)
    /// is followed to its end.
    /// </summary>
    public CountryIndex WithUnits(IReadOnlyDictionary<string, string> mergeIntoIso3)
    {
        ArgumentNullException.ThrowIfNull(mergeIntoIso3);
        var map = new Dictionary<string, string>(mergeIntoIso3, StringComparer.OrdinalIgnoreCase);
        string Final(string code)
        {
            for (int guard = 0; guard < 16 && map.TryGetValue(code, out var next) && !next.Equals(code, StringComparison.OrdinalIgnoreCase); guard++) code = next;
            return code;
        }
        var kept = new List<Entry>();
        var joining = new Dictionary<string, List<Entry>>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in _entries)
        {
            string target = Final(e.Country.Iso3);
            if (!target.Equals(e.Country.Iso3, StringComparison.OrdinalIgnoreCase) && _byIso3.ContainsKey(target))
            {
                if (!joining.TryGetValue(target, out var l)) joining[target] = l = [];
                l.Add(e);
            }
            else kept.Add(e);
        }
        for (int i = 0; i < kept.Count; i++)
        {
            if (!joining.TryGetValue(kept[i].Country.Iso3, out var merged)) continue;
            var polys = kept[i].Country.Polygons.Concat(merged.SelectMany(m => m.Country.Polygons)).ToList();
            kept[i] = MakeEntry(kept[i].Country with { Polygons = polys }, kept[i].Names);
        }
        var aliases = _aliases.ToList();
        foreach (var (target, merged) in joining)
            foreach (var m in merged)
                foreach (var n in m.Names) aliases.Add((Normalize(n), target));
        return new CountryIndex(kept, aliases);
    }

    /// <summary>Adds names (any spelling) that resolve to an ISO3 code. They replace the file's own names and the built-in aliases.</summary>
    public CountryIndex WithAliases(IReadOnlyDictionary<string, string> aliasToIso3)
    {
        ArgumentNullException.ThrowIfNull(aliasToIso3);
        var aliases = _aliases.Concat(aliasToIso3.Select(kv => (Normalize(kv.Key), kv.Value))).ToList();
        return new CountryIndex(_entries, aliases);
    }

    /// <summary>The country that contains the point (holes excluded), or null over the sea.</summary>
    public Country? Find(GeoPosition p)
    {
        double lat = p.LatitudeDeg, lon = p.LongitudeDeg;
        foreach (var e in _entries)
        {
            var polys = e.Country.Polygons;
            for (int i = 0; i < polys.Count; i++)
            {
                var b = e.Boxes[i];
                if (!(lat >= b.MinLat && lat <= b.MaxLat && lon >= b.MinLon && lon <= b.MaxLon)) continue;   // also rejects NaN
                if (polys[i].Contains(p)) return e.Country;
            }
        }
        return null;
    }

    public Country? ByIso3(string iso3) => iso3 is not null && _byIso3.TryGetValue(iso3.Trim(), out var e) ? e.Country : null;

    /// <summary>Matches a name case- and accent-insensitively, ignoring punctuation such as apostrophes.</summary>
    public Country? Resolve(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        return _byName.TryGetValue(Normalize(name), out var e) ? e.Country : null;
    }

    /// <summary>Lower case, accents dropped, apostrophes removed, other punctuation and spaces as a single space.</summary>
    internal static string Normalize(string text)
    {
        var sb = new StringBuilder(text.Length);
        bool space = false;
        foreach (char c in text.Normalize(NormalizationForm.FormD))
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(c);
            if (cat == UnicodeCategory.NonSpacingMark) continue;
            if (c is '\'' or '`' or '´' or 'ʼ' or 'ʻ' or 'ʹ'
                || cat is UnicodeCategory.InitialQuotePunctuation or UnicodeCategory.FinalQuotePunctuation) continue;
            if (char.IsLetterOrDigit(c))
            {
                if (space && sb.Length > 0) sb.Append(' ');
                space = false;
                sb.Append(char.ToLowerInvariant(c));
            }
            else space = true;
        }
        return sb.ToString();
    }

    private static Entry MakeEntry(Country c, IReadOnlyList<string> names) =>
        new(c, names, c.Polygons.Select(p => p.Bounds).ToArray());

    private static IReadOnlyList<IReadOnlyList<(double Lon, double Lat)>> ReadRings(JsonElement rings)
    {
        var list = new List<IReadOnlyList<(double Lon, double Lat)>>();
        foreach (var ring in rings.EnumerateArray())
            list.Add(ring.EnumerateArray().Select(pt => (pt[0].GetDouble(), pt[1].GetDouble())).ToList());
        return list;
    }

    private static string Str(JsonElement p, string key) =>
        p.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static double Num(JsonElement p, string key) =>
        p.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
}
