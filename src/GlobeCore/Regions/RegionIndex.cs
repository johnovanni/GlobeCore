using System.Globalization;
using System.Text;
using AstroCore.Earth;

namespace GlobeCore.Regions;

/// <summary>
/// Point-in-region lookup and name matching over a list of <see cref="Region"/>s, for any body. Instances are
/// immutable; <see cref="WithMerges"/> and <see cref="WithAliases"/> return new ones.
/// </summary>
public sealed class RegionIndex
{
    private sealed record Entry(Region Region, (double MinLat, double MinLon, double MaxLat, double MaxLon)[] Boxes);

    private readonly IReadOnlyList<Entry> _entries;
    private readonly Dictionary<string, Entry> _byId;
    private readonly Dictionary<string, Entry> _byName;
    private readonly IReadOnlyList<(string Key, string Id)> _aliases;

    /// <summary>
    /// Indexes the regions in order: <see cref="Find"/> returns the first that contains a point, and the first region
    /// with an id wins <see cref="ById"/>.
    /// </summary>
    /// <param name="aliases">Extra names (any spelling) for a region id. They replace the regions' own names; one for an unknown id is ignored.</param>
    public RegionIndex(IEnumerable<Region> regions, IReadOnlyDictionary<string, string>? aliases = null)
        : this([.. regions.Select(MakeEntry)], aliases?.Select(kv => (Normalize(kv.Key), kv.Value)).ToList() ?? [])
    {
    }

    private RegionIndex(IReadOnlyList<Entry> entries, IReadOnlyList<(string Key, string Id)> aliases)
    {
        _entries = entries;
        _aliases = aliases;
        All = entries.Select(e => e.Region).ToList();
        _byId = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in entries) _byId.TryAdd(e.Region.Id, e);

        // Priority: every region's Name, then each OtherNames position in turn, so a short name is never shadowed by a
        // longer one of another region.
        _byName = new Dictionary<string, Entry>();
        foreach (var e in entries)
            if (Normalize(e.Region.Name) is { Length: > 0 } k) _byName.TryAdd(k, e);
        int most = entries.Count == 0 ? 0 : entries.Max(e => e.Region.OtherNames.Count);
        for (int i = 0; i < most; i++)
            foreach (var e in entries)
                if (i < e.Region.OtherNames.Count && Normalize(e.Region.OtherNames[i]) is { Length: > 0 } k) _byName.TryAdd(k, e);
        foreach (var (key, id) in aliases)
            if (key.Length > 0 && _byId.TryGetValue(id, out var target)) _byName[key] = target;
    }

    /// <summary>The regions, in order.</summary>
    public IReadOnlyList<Region> All { get; }

    /// <summary>The region that contains the point (holes excluded), or null where there is none (or for NaN).</summary>
    public Region? Find(GeoPosition p)
    {
        double lat = p.LatitudeDeg, lon = p.LongitudeDeg;
        foreach (var e in _entries)
        {
            var polys = e.Region.Polygons;
            for (int i = 0; i < polys.Count; i++)
            {
                var b = e.Boxes[i];
                if (!(lat >= b.MinLat && lat <= b.MaxLat && lon >= b.MinLon && lon <= b.MaxLon)) continue;   // also rejects NaN
                if (polys[i].Contains(p)) return e.Region;
            }
        }
        return null;
    }

    /// <summary>The region with this id (case-insensitive, trimmed), or null.</summary>
    public Region? ById(string id) => id is not null && _byId.TryGetValue(id.Trim(), out var e) ? e.Region : null;

    /// <summary>Matches a name case- and accent-insensitively, ignoring punctuation such as apostrophes.</summary>
    public Region? Resolve(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        return _byName.TryGetValue(Normalize(name), out var e) ? e.Region : null;
    }

    /// <summary>
    /// Merges each listed region's polygons into another region (keys and values are ids, e.g. SOL to SOM) and drops
    /// the merged region; its names, and aliases that pointed at it, still resolve, to the region it joined. Unknown
    /// ids are ignored; a chain (A to B, B to C) is followed to its end.
    /// </summary>
    public RegionIndex WithMerges(IReadOnlyDictionary<string, string> mergeInto)
    {
        ArgumentNullException.ThrowIfNull(mergeInto);
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in mergeInto) map[k.Trim()] = v.Trim();
        string Final(string id)
        {
            for (int guard = 0; guard < 16 && map.TryGetValue(id, out var next) && !next.Equals(id, StringComparison.OrdinalIgnoreCase); guard++) id = next;
            return id;
        }
        var kept = new List<Entry>();
        var joining = new Dictionary<string, List<Entry>>(StringComparer.OrdinalIgnoreCase);
        var finalOf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in _entries)
        {
            string target = Final(e.Region.Id);
            if (!target.Equals(e.Region.Id, StringComparison.OrdinalIgnoreCase) && _byId.ContainsKey(target))
            {
                if (!joining.TryGetValue(target, out var l)) joining[target] = l = [];
                l.Add(e);
                finalOf[e.Region.Id] = target;
            }
            else kept.Add(e);
        }
        for (int i = 0; i < kept.Count; i++)
        {
            if (!joining.TryGetValue(kept[i].Region.Id, out var merged)) continue;
            var polys = kept[i].Region.Polygons.Concat(merged.SelectMany(m => m.Region.Polygons)).ToList();
            kept[i] = MakeEntry(kept[i].Region with { Polygons = polys });
        }
        var aliases = _aliases.Select(a => finalOf.TryGetValue(a.Id, out var t) ? (a.Key, t) : a).ToList();
        foreach (var (target, merged) in joining)
            foreach (var m in merged)
                foreach (var n in m.Region.OtherNames.Prepend(m.Region.Name)) aliases.Add((Normalize(n), target));
        return new RegionIndex(kept, aliases);
    }

    /// <summary>Adds names (any spelling) for region ids. They replace the regions' own names and earlier aliases; one for an unknown id is ignored.</summary>
    public RegionIndex WithAliases(IReadOnlyDictionary<string, string> aliasToId)
    {
        ArgumentNullException.ThrowIfNull(aliasToId);
        var aliases = _aliases.Concat(aliasToId.Select(kv => (Normalize(kv.Key), kv.Value))).ToList();
        return new RegionIndex(_entries, aliases);
    }

    /// <summary>Lower case, accents dropped, apostrophes removed, other punctuation and spaces as a single space.</summary>
    public static string Normalize(string text)
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

    private static Entry MakeEntry(Region r) => new(r, r.Polygons.Select(p => p.Bounds).ToArray());
}
