using GlobeCore.Feeds;
using GlobeCore.Logging;

namespace GlobeCore.Regions;

/// <summary>
/// A release was downloaded but is not usable (a file is missing, truncated or not the expected format, a rule no longer
/// matches, or validation failed). The message reads "Rejected v5.2.0: &lt;reason&gt;". The feed throws it so that the
/// scheduler shows the "Borders" dot amber with that reason; the current set stays in use.
/// </summary>
public sealed class BoundaryRejectedException(string message) : Exception(message);

/// <summary>
/// Checks once a day for a newer release of the body's boundary data and adopts it when it is whole and valid:
/// <list type="number">
/// <item>asks the source for the latest release (an unchanged ETag answers "nothing new" without a body);</item>
/// <item>only when its tag is newer than the tag of <see cref="CurrentBoundaries.Set"/>, downloads the files;</item>
/// <item>applies the rules and validates with <see cref="BoundaryValidator"/>; a failure throws
/// <see cref="BoundaryRejectedException"/> and changes nothing (the release is then not downloaded again until a newer one appears);</item>
/// <item>stores the set in the <see cref="BoundaryStore"/>, swaps it into <see cref="CurrentBoundaries"/> (raising its
/// Changed event) and logs the ids of the regions whose shapes changed.</item>
/// </list>
/// Network errors, rate limits (403, 429) and server errors (5xx) are thrown too and keep the current set; the scheduler
/// backs off and retries. The ETag and the rejected tag are kept in memory only.
/// </summary>
public sealed class BoundaryUpdateFeed : IFeed, IFeedStatusNote
{
    private readonly IBoundarySource _source;
    private readonly BoundaryRules _rules;
    private readonly BoundaryValidator _validator;
    private readonly BoundaryStore _store;
    private readonly CurrentBoundaries _current;
    private readonly IAppLog _log;
    private readonly DownloadMeter? _meter;

    private readonly object _gate = new();
    private string? _etag;
    private string? _rejectedTag;
    private string? _rejection;
    private string? _note;

    /// <param name="meter">When given, the feed pauses (<see cref="FeedRunResult.Paused"/>) while its daily limit is reached.
    /// The bytes themselves are counted by the app wiring <see cref="FeedHttp.BytesReceived"/> to the meter.</param>
    public BoundaryUpdateFeed(IBoundarySource source, BoundaryRules rules, BoundaryValidator validator, BoundaryStore store,
        CurrentBoundaries current, IAppLog log, DownloadMeter? meter = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _current = current ?? throw new ArgumentNullException(nameof(current));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _meter = meter;
        _note = UsingNote(current.Set, "");
    }

    public string Name => "Borders";

    public TimeSpan Interval { get; } = TimeSpan.FromHours(24);

    /// <summary>For the status tooltip: "Using Natural Earth 5.1.2 (2022-05-13); no newer release" and the like.</summary>
    public string? StatusNote { get { lock (_gate) return _note; } }

    public async Task<FeedRunResult> RunOnceAsync(CancellationToken ct)
    {
        if (_meter is { LimitReached: true }) return FeedRunResult.Paused;

        string? etag;
        lock (_gate) etag = _etag;
        var latest = await _source.LatestAsync(etag, ct).ConfigureAwait(false);
        var inUse = _current.Set;

        if (latest is null)
        {
            // Nothing changed since the last answer, so whatever was decided about it still stands.
            ThrowIfRejected(inUse, null);
            SetNote(UsingNote(inUse, "; no newer release"));
            return FeedRunResult.Ok;
        }

        if (BoundaryStore.CompareTags(latest.Tag, inUse.Tag) <= 0)
        {
            lock (_gate) { _etag = latest.ETag; _rejectedTag = null; _rejection = null; }
            SetNote(UsingNote(inUse, "; no newer release"));
            return FeedRunResult.Ok;
        }

        // The same release that was rejected before: do not download it again.
        lock (_gate)
        {
            if (_rejectedTag == latest.Tag) _etag = latest.ETag;
        }
        ThrowIfRejected(inUse, latest.Tag);

        RawBoundaryData raw;
        try
        {
            raw = await _source.DownloadAsync(latest, ct).ConfigureAwait(false);
        }
        catch (InvalidDataException ex)
        {
            throw Reject(latest, inUse, ex.Message);
        }

        var result = _validator.Validate(raw, _rules, out var set);
        if (set is null) throw Reject(latest, inUse, result.Reason);

        if (raw.Files.Count > 0 && !_store.Save(latest.Tag, latest.Date, raw.Files, out var error))
            _log.Warn($"Boundaries: could not store {set.Edition}; it is in use for this session only", error);

        var old = _current.Swap(set);
        var changed = ChangedRegionIds(old, set);
        _log.Info($"Boundaries: now {set.Edition} (was {old.Edition}); " +
                  $"{changed.Count} {(changed.Count == 1 ? "region" : "regions")} changed" +
                  (changed.Count > 0 ? ": " + string.Join(", ", changed) : ""));

        lock (_gate) { _etag = latest.ETag; _rejectedTag = null; _rejection = null; }
        SetNote(UsingNote(set, $" (updated from {old.Edition})"));
        return FeedRunResult.Ok;
    }

    // Throws the remembered rejection, for any release (onlyForTag null) or for the one named.
    private void ThrowIfRejected(BoundarySet inUse, string? onlyForTag)
    {
        string? message, tag;
        lock (_gate)
        {
            if (_rejection is null || (onlyForTag is not null && _rejectedTag != onlyForTag)) return;
            message = _rejection;
            tag = _rejectedTag;
        }
        SetNote(UsingNote(inUse, $"; kept after rejecting {tag}"));
        throw new BoundaryRejectedException(message);
    }

    private BoundaryRejectedException Reject(ReleaseInfo release, BoundarySet inUse, string reason)
    {
        var message = $"Rejected {release.Tag}: {reason}";
        lock (_gate) { _rejectedTag = release.Tag; _rejection = message; _etag = release.ETag; }
        SetNote(UsingNote(inUse, $"; kept after rejecting {release.Tag}"));
        _log.Warn(message);
        return new BoundaryRejectedException(message);
    }

    private void SetNote(string note) { lock (_gate) _note = note; }

    private static string UsingNote(BoundarySet set, string suffix) => $"Using {set.Edition}{suffix}";

    /// <summary>The ids of regions that are new, gone, or whose polygons differ between two sets, in id order.</summary>
    public static IReadOnlyList<string> ChangedRegionIds(BoundarySet before, BoundarySet after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        static Dictionary<string, List<Region>> ById(BoundarySet s) =>
            s.Regions.All.GroupBy(r => r.Id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        var a = ById(before);
        var b = ById(after);
        var changed = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in a.Keys.Union(b.Keys, StringComparer.OrdinalIgnoreCase))
        {
            if (!a.TryGetValue(id, out var x) || !b.TryGetValue(id, out var y) || x.Count != y.Count
                || x.Where((r, i) => !SameShape(r, y[i])).Any())
                changed.Add(id);
        }
        return [.. changed];
    }

    private static bool SameShape(Region x, Region y)
    {
        if (x.Polygons.Count != y.Polygons.Count) return false;
        for (int i = 0; i < x.Polygons.Count; i++)
        {
            var rx = x.Polygons[i].Rings;
            var ry = y.Polygons[i].Rings;
            if (rx.Count != ry.Count) return false;
            for (int r = 0; r < rx.Count; r++)
                if (!rx[r].SequenceEqual(ry[r])) return false;
        }
        return true;
    }
}
