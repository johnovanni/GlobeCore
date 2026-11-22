using System.Text.Json;
using GlobeCore.Geo;

namespace GlobeCore.Regions;

/// <summary>A rule that is malformed, has no reason, or does not match the boundary data exactly. The set is rejected.</summary>
public sealed class BoundaryRuleException(string ruleName, string message) : Exception(message)
{
    /// <summary>The rule's name, e.g. "moveAdmin1 UA-43" (or "#2" when a malformed rule has none).</summary>
    public string RuleName { get; } = ruleName;
}

/// <summary>One rule as read from the rules file.</summary>
/// <param name="Kind"><c>moveAdmin1</c>, <c>mergeUnits</c> or <c>alias</c>.</param>
/// <param name="Name">Used in errors and logs: the file's <c>name</c>, or the kind and its first code, unit or name.</param>
/// <param name="Reason">Why the app applies it (required).</param>
/// <param name="Description">Text for the screen (legend, tooltip), or null when the rule needs none.</param>
public sealed record BoundaryRule(string Kind, string Name, string Reason, string? Description);

/// <summary>
/// Adjustments an app makes to every boundary set it loads, bundled or downloaded, so its decisions survive updates.
/// They are data (a JSON file the app supplies; the format is in GlobeCore.Earth's Assets/README.md and on
/// <see cref="Parse"/>), and every rule must say why. If a rule does not match a set exactly, the whole set is
/// rejected (<see cref="BoundaryRuleException"/>), never half-applied.
/// </summary>
public sealed class BoundaryRules
{
    /// <summary>Areas within this fraction count as the same land in a <c>moveAdmin1</c> rule.</summary>
    public const double AreaTolerance = 0.01;

    private readonly IReadOnlyList<Step> _steps;

    private BoundaryRules(IReadOnlyList<Step> steps)
    {
        _steps = steps;
        Rules = steps.Select(s => s.Info).ToList();
        Descriptions = Rules.Where(r => r.Description is not null).Select(r => r.Description!).ToList();
    }

    /// <summary>No rules: the source's own view.</summary>
    public static BoundaryRules None { get; } = new([]);

    public IReadOnlyList<BoundaryRule> Rules { get; }

    /// <summary>The on-screen text of the rules that have a description, in file order.</summary>
    public IReadOnlyList<string> Descriptions { get; }

    /// <summary>
    /// Reads a rules file: <c>{"rules": [ ... ]}</c>, each rule an object with <c>kind</c>, <c>reason</c> (required, not
    /// blank), an optional <c>name</c>, and <c>description</c> (the text for the screen; required, not blank, for
    /// <c>moveAdmin1</c> and <c>mergeUnits</c>, which change the map; optional for <c>alias</c>), plus by kind:
    /// <list type="bullet">
    /// <item><c>moveAdmin1</c>: <c>codes</c> (subdivision codes), <c>from</c> and <c>to</c> (region ids), and optional
    /// <c>expectParts</c> (at least 1) and <c>expectLines</c> (0 or more), the exact numbers of parts and lines it must remove.</item>
    /// <item><c>mergeUnits</c>: <c>units</c> (region ids) and <c>into</c> (a region id).</item>
    /// <item><c>alias</c>: <c>names</c>, an object of name to region id.</item>
    /// </list>
    /// Comments and trailing commas are allowed; unknown kinds and keys (often a typo) are rejected.
    /// </summary>
    /// <exception cref="BoundaryRuleException">The file is not valid JSON, or a rule is malformed or has no reason.</exception>
    public static BoundaryRules Parse(Stream json)
    {
        ArgumentNullException.ThrowIfNull(json);
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        }
        catch (JsonException ex)
        {
            throw new BoundaryRuleException("rules", $"boundary rules: not valid JSON ({ex.Message})");
        }
        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("rules", out var list) || list.ValueKind != JsonValueKind.Array)
                throw new BoundaryRuleException("rules", "boundary rules: expected {\"rules\": [...]}");
            foreach (var p in root.EnumerateObject())
                if (p.Name != "rules") throw new BoundaryRuleException("rules", $"boundary rules: unknown key \"{p.Name}\"");
            var steps = new List<Step>();
            int n = 0;
            foreach (var r in list.EnumerateArray()) steps.Add(ReadRule(r, ++n));
            return new BoundaryRules(steps);
        }
    }

    /// <summary>
    /// Applies the rules to one edition: every <c>moveAdmin1</c> rule first, in file order, on the source geometry; then
    /// the polygons are split at ±180°; then <c>mergeUnits</c> and <c>alias</c> rules, in file order, on the index.
    /// </summary>
    /// <exception cref="BoundaryRuleException">A rule does not match the data exactly (message "rule &lt;name&gt; no longer matches: ...").</exception>
    public BoundarySet Apply(RawBoundaryData raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        var regions = raw.Regions.Select(r => new WorkingRegion(r, [.. r.Parts])).ToList();
        var lines = raw.Lines.ToList();
        foreach (var s in _steps.OfType<MoveAdmin1Step>()) s.Apply(regions, lines, raw.Subdivisions);

        var index = new RegionIndex(
            regions.Select(w => new Region(w.Raw.Id, w.Raw.Name, w.Raw.OtherNames, w.Raw.LabelPoint,
                w.Parts.SelectMany(p => DateLine.SplitPolygon(p.Rings)).ToList())),
            raw.Aliases);
        foreach (var s in _steps)
            if (s is IndexStep i) index = i.Apply(index);

        return new BoundarySet(raw.Edition, raw.EditionDate, index, lines.Select(l => new GeoLine(l)).ToList())
        {
            Tag = raw.Tag,
            RuleDescriptions = Descriptions,
        };
    }

    // ---- reading ----

    private static Step ReadRule(JsonElement r, int n)
    {
        string label = "#" + n;
        if (r.ValueKind != JsonValueKind.Object) throw new BoundaryRuleException(label, $"boundary rule {label}: expected an object");
        string kind = OptString(r, "kind", label) ?? throw new BoundaryRuleException(label, $"boundary rule {label}: missing \"kind\"");
        string[] allowed = kind switch
        {
            "moveAdmin1" => ["codes", "from", "to", "expectParts", "expectLines"],
            "mergeUnits" => ["units", "into"],
            "alias" => ["names"],
            _ => throw new BoundaryRuleException(label, $"boundary rule {label}: unknown kind \"{kind}\" (expected moveAdmin1, mergeUnits or alias)"),
        };
        foreach (var p in r.EnumerateObject())
            if (p.Name is not ("kind" or "name" or "reason" or "description") && !allowed.Contains(p.Name))
                throw new BoundaryRuleException(label, $"boundary rule {label} ({kind}): unknown key \"{p.Name}\"");

        string? description = OptString(r, "description", label);
        Step step;
        switch (kind)
        {
            case "moveAdmin1":
            {
                var codes = StringArray(r, "codes", label);
                string from = ReqString(r, "from", label), to = ReqString(r, "to", label);
                if (from.Equals(to, StringComparison.OrdinalIgnoreCase)) throw new BoundaryRuleException(label, $"boundary rule {label}: \"from\" and \"to\" are both {from}");
                int? parts = OptInt(r, "expectParts", 1, label), linesCount = OptInt(r, "expectLines", 0, label);
                var info = Info(r, kind, codes[0], description, label);
                step = new MoveAdmin1Step(info, codes, from, to, parts, linesCount);
                break;
            }
            case "mergeUnits":
            {
                var units = StringArray(r, "units", label);
                string into = ReqString(r, "into", label);
                if (units.Contains(into, StringComparer.OrdinalIgnoreCase)) throw new BoundaryRuleException(label, $"boundary rule {label}: {into} merges into itself");
                step = new MergeStep(Info(r, kind, units[0], description, label), units, into);
                break;
            }
            default:
            {
                if (!r.TryGetProperty("names", out var names) || names.ValueKind != JsonValueKind.Object)
                    throw new BoundaryRuleException(label, $"boundary rule {label}: \"names\" must be an object of name to id");
                var map = new Dictionary<string, string>();
                foreach (var p in names.EnumerateObject())
                {
                    if (p.Value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(p.Value.GetString()) || string.IsNullOrWhiteSpace(p.Name))
                        throw new BoundaryRuleException(label, $"boundary rule {label}: alias \"{p.Name}\" needs a region id");
                    map[p.Name] = p.Value.GetString()!.Trim();
                }
                if (map.Count == 0) throw new BoundaryRuleException(label, $"boundary rule {label}: \"names\" is empty");
                step = new AliasStep(Info(r, kind, map.Keys.First(), description, label), map);
                break;
            }
        }
        return step;
    }

    private static BoundaryRule Info(JsonElement r, string kind, string first, string? description, string label)
    {
        string name = OptString(r, "name", label) ?? $"{kind} {first}";
        string reason = OptString(r, "reason", label)
            ?? throw new BoundaryRuleException(name, $"boundary rule {name}: every rule needs a \"reason\"");
        // Ruling R3: a rule that changes what the map shows is described on screen. An alias changes only name matching.
        if (description is null && kind is "moveAdmin1" or "mergeUnits")
            throw new BoundaryRuleException(name, $"boundary rule {name}: a {kind} rule changes the map, so it needs a \"description\" for the screen");
        return new BoundaryRule(kind, name, reason, description);
    }

    /// <summary>A non-blank string, trimmed; null when the key is absent, null or blank.</summary>
    private static string? OptString(JsonElement r, string key, string label)
    {
        if (!r.TryGetProperty(key, out var v) || v.ValueKind == JsonValueKind.Null) return null;
        if (v.ValueKind != JsonValueKind.String) throw new BoundaryRuleException(label, $"boundary rule {label}: \"{key}\" must be a string");
        var s = v.GetString()!.Trim();
        return s.Length == 0 ? null : s;
    }

    private static string ReqString(JsonElement r, string key, string label) =>
        OptString(r, key, label) ?? throw new BoundaryRuleException(label, $"boundary rule {label}: missing \"{key}\"");

    private static int? OptInt(JsonElement r, string key, int min, string label)
    {
        if (!r.TryGetProperty(key, out var v) || v.ValueKind == JsonValueKind.Null) return null;
        if (v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out int i) || i < min)
            throw new BoundaryRuleException(label, $"boundary rule {label}: \"{key}\" must be a whole number of at least {min}");
        return i;
    }

    private static string[] StringArray(JsonElement r, string key, string label)
    {
        if (!r.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.Array)
            throw new BoundaryRuleException(label, $"boundary rule {label}: \"{key}\" must be a list");
        var list = new List<string>();
        foreach (var e in v.EnumerateArray())
        {
            if (e.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(e.GetString()))
                throw new BoundaryRuleException(label, $"boundary rule {label}: \"{key}\" holds a blank or non-string entry");
            list.Add(e.GetString()!.Trim());
        }
        if (list.Count == 0) throw new BoundaryRuleException(label, $"boundary rule {label}: \"{key}\" is empty");
        if (list.Distinct(StringComparer.OrdinalIgnoreCase).Count() != list.Count) throw new BoundaryRuleException(label, $"boundary rule {label}: \"{key}\" repeats an entry");
        return [.. list];
    }

    // ---- applying ----

    private sealed record WorkingRegion(RawRegion Raw, List<RawPolygon> Parts);

    private abstract class Step(BoundaryRule info)
    {
        public BoundaryRule Info { get; } = info;

        protected BoundaryRuleException Mismatch(string detail) => new(Info.Name, $"rule {Info.Name} no longer matches: {detail}");
    }

    private abstract class IndexStep(BoundaryRule info) : Step(info)
    {
        public abstract RegionIndex Apply(RegionIndex index);
    }

    /// <summary>
    /// Moves subdivisions from one region to another: the parts of <c>from</c> made only of the subdivisions' outer-ring
    /// vertices (with the same area) are removed, the subdivisions' polygons are added to <c>to</c>, and the boundary
    /// lines all of whose points are those vertices (the subdivisions' own boundary, now internal) are removed.
    /// </summary>
    private sealed class MoveAdmin1Step(BoundaryRule info, string[] codes, string from, string to, int? expectParts, int? expectLines) : Step(info)
    {
        public void Apply(List<WorkingRegion> regions, List<IReadOnlyList<AstroCore.Earth.GeoPosition>> lines, IReadOnlyList<RawSubdivision> subdivisions)
        {
            var source = Single(regions, from);
            var target = Single(regions, to);

            var moved = new List<RawPolygon>();
            foreach (var code in codes)
            {
                var found = subdivisions.Where(s => s.Code.Equals(code, StringComparison.OrdinalIgnoreCase)).ToList();
                if (found.Count != 1) throw Mismatch($"expected 1 subdivision {code}, found {found.Count}");
                if (!found[0].ParentId.Equals(from, StringComparison.OrdinalIgnoreCase)) throw Mismatch($"{code} is in {found[0].ParentId}, not {from}");
                if (found[0].Parts.Count == 0) throw Mismatch($"{code} has no polygon");
                moved.AddRange(found[0].Parts);
            }
            var vertices = new HashSet<(double Lon, double Lat)>();
            foreach (var p in moved)
                if (p.Rings.Count > 0) vertices.UnionWith(p.Rings[0]);

            var removed = source.Parts.Where(p => p.Rings.Count > 0 && p.Rings[0].Count > 0 && p.Rings[0].All(vertices.Contains)).ToList();
            if (removed.Count == 0) throw Mismatch($"no part of {from} is made of the subdivisions' vertices");
            if (expectParts is int ep && removed.Count != ep) throw Mismatch($"expected {ep} part(s) of {from} made of the subdivisions' vertices, found {removed.Count}");
            double removedArea = removed.Sum(Area), movedArea = moved.Sum(Area);
            if (Math.Abs(removedArea - movedArea) > AreaTolerance * movedArea)
                throw Mismatch($"the {from} part(s) cover {removedArea:0.#####} square degrees, the subdivisions {movedArea:0.#####}");

            int dropped = lines.Count(l => IsOwnLine(l, vertices));
            if (expectLines is int el && dropped != el) throw Mismatch($"expected {el} boundary line(s) on the subdivisions, found {dropped}");

            source.Parts.RemoveAll(removed.Contains);
            target.Parts.AddRange(moved);
            lines.RemoveAll(l => IsOwnLine(l, vertices));
        }

        private WorkingRegion Single(List<WorkingRegion> regions, string id)
        {
            var found = regions.Where(w => w.Raw.Id.Equals(id, StringComparison.OrdinalIgnoreCase)).ToList();
            return found.Count == 1 ? found[0] : throw Mismatch($"expected 1 region {id}, found {found.Count}");
        }

        private static bool IsOwnLine(IReadOnlyList<AstroCore.Earth.GeoPosition> line, HashSet<(double Lon, double Lat)> vertices) =>
            line.Count > 0 && line.All(p => vertices.Contains((p.LongitudeDeg, p.LatitudeDeg)));

        /// <summary>Planar area in square degrees: the outer ring less the holes.</summary>
        private static double Area(RawPolygon p)
        {
            double a = 0;
            for (int r = 0; r < p.Rings.Count; r++) a += (r == 0 ? 1 : -1) * Math.Abs(Shoelace(p.Rings[r]));
            return a;
        }

        private static double Shoelace(IReadOnlyList<(double Lon, double Lat)> ring)
        {
            double s = 0;
            for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++) s += (ring[j].Lon * ring[i].Lat) - (ring[i].Lon * ring[j].Lat);
            return s / 2;
        }
    }

    /// <summary>Merges units into another region (<see cref="RegionIndex.WithMerges"/>); every id must be in the set.</summary>
    private sealed class MergeStep(BoundaryRule info, string[] units, string into) : IndexStep(info)
    {
        public override RegionIndex Apply(RegionIndex index)
        {
            if (index.ById(into) is null) throw Mismatch($"no region {into}");
            foreach (var u in units)
                if (index.ById(u) is null) throw Mismatch($"no region {u}");
            return index.WithMerges(units.ToDictionary(u => u, _ => into, StringComparer.OrdinalIgnoreCase));
        }
    }

    /// <summary>Adds names for regions (<see cref="RegionIndex.WithAliases"/>); every target must be in the set.</summary>
    private sealed class AliasStep(BoundaryRule info, Dictionary<string, string> names) : IndexStep(info)
    {
        public override RegionIndex Apply(RegionIndex index)
        {
            foreach (var (name, id) in names)
                if (index.ById(id) is null) throw Mismatch($"alias \"{name}\" points at {id}, which the set does not have");
            return index.WithAliases(names);
        }
    }
}
