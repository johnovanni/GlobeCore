using System.Globalization;
using System.Text.Json;
using AstroCore.Earth;

namespace GlobeCore.Regions;

/// <summary>The outcome of <see cref="BoundaryValidator.Validate(BoundarySet)"/>: valid when there are no problems.</summary>
public sealed record ValidationResult(IReadOnlyList<string> Problems)
{
    public bool IsValid => Problems.Count == 0;

    /// <summary>The problems joined for a status note or the log; empty when valid.</summary>
    public string Reason => string.Join("; ", Problems);
}

/// <summary>
/// Checks a boundary set before it is used: the rules applied cleanly (<see cref="Validate(RawBoundaryData, BoundaryRules, out BoundarySet?)"/>),
/// the region count is within ±10% of the bundled set's, every probe point lands in its expected region, and every
/// name the app's data sources use still resolves.
/// </summary>
/// <param name="probes">Points and the region id each must land in (an app's <c>boundary-probes.json</c>, see <see cref="ReadProbes"/>).</param>
/// <param name="unresolvedNames">The app's hook: given the set's index, the source names that no longer resolve (empty when all do).</param>
/// <param name="bundledRegionCount">The region count of the bundled set with the same rules applied.</param>
public sealed class BoundaryValidator(
    IReadOnlyList<(GeoPosition Point, string ExpectedId)> probes,
    Func<RegionIndex, IReadOnlyList<string>> unresolvedNames,
    int bundledRegionCount)
{
    /// <summary>How far the region count may move from the bundled count, as a fraction (inclusive).</summary>
    public const double CountTolerance = 0.10;

    private readonly IReadOnlyList<(GeoPosition Point, string ExpectedId)> _probes = probes ?? throw new ArgumentNullException(nameof(probes));
    private readonly Func<RegionIndex, IReadOnlyList<string>> _unresolvedNames = unresolvedNames ?? throw new ArgumentNullException(nameof(unresolvedNames));

    public ValidationResult Validate(BoundarySet set)
    {
        ArgumentNullException.ThrowIfNull(set);
        var problems = new List<string>();
        int count = set.Regions.All.Count;
        if (!CountInRange(count, bundledRegionCount))
            problems.Add(string.Create(CultureInfo.InvariantCulture,
                $"{count} regions, but the bundled set has {bundledRegionCount} (allowed ±{CountTolerance * 100:0}%)"));
        foreach (var (point, expected) in _probes)
        {
            var found = set.Regions.Find(point)?.Id;
            if (!string.Equals(found, expected, StringComparison.OrdinalIgnoreCase))
                problems.Add(string.Create(CultureInfo.InvariantCulture,
                    $"probe ({point.LatitudeDeg:0.###}°, {point.LongitudeDeg:0.###}°) expected {expected}, found {found ?? "none"}"));
        }
        IReadOnlyList<string> lost;
        try
        {
            lost = _unresolvedNames(set.Regions) ?? [];
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            lost = [$"(the name check failed: {ex.Message})"];
        }
        if (lost.Count > 0) problems.Add("names no longer resolve: " + string.Join(", ", lost));
        return new ValidationResult(problems);
    }

    /// <summary>Applies the rules, then validates; a rule that does not match is a problem, not an exception.</summary>
    /// <param name="set">The set with the rules applied, or null when it is not valid.</param>
    public ValidationResult Validate(RawBoundaryData raw, BoundaryRules rules, out BoundarySet? set)
    {
        ArgumentNullException.ThrowIfNull(raw);
        ArgumentNullException.ThrowIfNull(rules);
        set = null;
        BoundarySet applied;
        try
        {
            applied = rules.Apply(raw);
        }
        catch (BoundaryRuleException ex)
        {
            return new ValidationResult([ex.Message]);
        }
        var result = Validate(applied);
        if (result.IsValid) set = applied;
        return result;
    }

    /// <summary>True when <paramref name="count"/> is within ±10% of <paramref name="bundled"/> (inclusive).</summary>
    public static bool CountInRange(int count, int bundled) => Math.Abs(count - bundled) <= CountTolerance * bundled + 1e-9;

    /// <summary>
    /// Reads a probes file: <c>{"probes": [{"name": "Khartoum", "lat": 15.5, "lon": 32.53, "expect": "SDN"}, ...]}</c>
    /// (<c>name</c> is optional, for the reader). Comments and trailing commas are allowed.
    /// </summary>
    /// <exception cref="InvalidDataException">The file is malformed, or a probe lacks a valid point or an expected id.</exception>
    public static IReadOnlyList<(GeoPosition Point, string ExpectedId)> ReadProbes(Stream json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (!doc.RootElement.TryGetProperty("probes", out var list) || list.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("boundary probes: expected {\"probes\": [...]}");
            var result = new List<(GeoPosition, string)>();
            int n = 0;
            foreach (var p in list.EnumerateArray())
            {
                n++;
                if (p.ValueKind != JsonValueKind.Object
                    || !p.TryGetProperty("lat", out var lat) || lat.ValueKind != JsonValueKind.Number
                    || !p.TryGetProperty("lon", out var lon) || lon.ValueKind != JsonValueKind.Number
                    || !p.TryGetProperty("expect", out var expect) || expect.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(expect.GetString()))
                    throw new InvalidDataException($"boundary probe #{n}: needs \"lat\", \"lon\" and \"expect\"");
                double la = lat.GetDouble(), lo = lon.GetDouble();
                if (Math.Abs(la) > 90 || Math.Abs(lo) > 180) throw new InvalidDataException($"boundary probe #{n}: ({la}, {lo}) is not a point");
                result.Add((new GeoPosition(la, lo), expect.GetString()!.Trim()));
            }
            return result;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"boundary probes: not valid JSON ({ex.Message})", ex);
        }
    }
}
