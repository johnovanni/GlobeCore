using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using GlobeCore.Feeds;
using GlobeCore.Logging;
using GlobeCore.Time;

namespace GlobeCore.Regions;

/// <summary>
/// Checks once a day for a newer release of the body's boundary data and adopts it when it is whole and valid:
/// <list type="number">
/// <item>asks the source for the latest release (an unchanged ETag answers "nothing new" without a body);</item>
/// <item>only when its tag is newer than the tag of <see cref="CurrentBoundaries.Set"/>, downloads the files;</item>
/// <item>applies the rules and validates with <see cref="BoundaryValidator"/>; a failure is a rejection: the run returns
/// <see cref="FeedRunResult.Attention"/> (the dot goes amber with "Rejected v5.2.0: reason" as its note, on the normal
/// interval) and changes nothing. The rejection is remembered (in memory and in <c>rejected.json</c> beside the store),
/// so the release is not downloaded again until a newer one appears, or until the app's rules or probes or GlobeCore
/// itself change (the <see cref="Fingerprint"/> stored with it);</item>
/// <item>stores the set in the <see cref="BoundaryStore"/>, swaps it into <see cref="CurrentBoundaries"/> (raising its
/// Changed event) and logs the ids of the regions whose shapes changed.</item>
/// </list>
/// Network errors, rate limits (403, 429) and server errors (5xx) are thrown and keep the current set; the scheduler
/// backs off and retries. After a failed download of a release's files, the files are not downloaded again for
/// <see cref="DownloadBackoff"/> (6 hours); runs in that time still ask for the latest release, then report the same
/// failure with "next try after" a local time. The ETag and that back-off are kept in memory only.
/// </summary>
public sealed class BoundaryUpdateFeed : IFeed, IFeedStatusNote
{
    /// <summary>How long a failed download of a release's files (network error, timeout, 5xx) waits before the next one.</summary>
    public static readonly TimeSpan DownloadBackoff = TimeSpan.FromHours(6);

    private readonly IBoundarySource _source;
    private readonly BoundaryRules _rules;
    private readonly BoundaryValidator _validator;
    private readonly BoundaryStore _store;
    private readonly CurrentBoundaries _current;
    private readonly IAppLog _log;
    private readonly string _fingerprint;
    private readonly DownloadMeter? _meter;
    private readonly IClock _clock;

    private readonly object _gate = new();
    private string? _etag;
    private string? _rejectedTag;
    private string? _rejection;
    private string? _note;
    // The last failed download: the release (to stand in for it when the release check answers "not modified"), when the
    // next download may start, and the failure as shown.
    private ReleaseInfo? _failedRelease;
    private DateTime _nextDownloadUtc;
    private string? _downloadFailure;

    /// <param name="fingerprint">The app's <see cref="Fingerprint"/> of its rules and probes files. A remembered rejection
    /// made with another fingerprint is forgotten, so an app or GlobeCore update can recover a release.</param>
    /// <param name="meter">When given, the feed pauses (<see cref="FeedRunResult.Paused"/>) while its daily limit is reached.
    /// The bytes themselves are counted by the app wiring <see cref="FeedHttp.BytesReceived"/> to the meter.</param>
    /// <param name="clock">For the download back-off; the system clock when null.</param>
    public BoundaryUpdateFeed(IBoundarySource source, BoundaryRules rules, BoundaryValidator validator, BoundaryStore store,
        CurrentBoundaries current, IAppLog log, string fingerprint, DownloadMeter? meter = null, IClock? clock = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _current = current ?? throw new ArgumentNullException(nameof(current));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);
        _fingerprint = fingerprint;
        _meter = meter;
        _clock = clock ?? new SystemClock();
        _note = UsingNote(current.Set, "");
        LoadPersistedRejection(current.Set);
    }

    public string Name => "Borders";

    public TimeSpan Interval { get; } = TimeSpan.FromHours(24);

    /// <summary>For the status tooltip: "Using Natural Earth 5.1.2 (2022-05-13); no newer release" and the like.</summary>
    public string? StatusNote { get { lock (_gate) return _note; } }

    /// <summary>
    /// The fingerprint a remembered rejection is kept with: the SHA-256 (lower-case hex) of the app's rules file bytes,
    /// then its probes file bytes, then GlobeCore's informational version (UTF-8). Pass the files' bytes as read.
    /// </summary>
    public static string Fingerprint(ReadOnlySpan<byte> rulesFile, ReadOnlySpan<byte> probesFile)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        sha.AppendData(rulesFile);
        sha.AppendData(probesFile);
        sha.AppendData(System.Text.Encoding.UTF8.GetBytes(GlobeCoreVersion));
        return Convert.ToHexStringLower(sha.GetHashAndReset());
    }

    private static string GlobeCoreVersion { get; } =
        typeof(BoundaryUpdateFeed).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(BoundaryUpdateFeed).Assembly.GetName().Version?.ToString() ?? "";

    public async Task<FeedRunResult> RunOnceAsync(CancellationToken ct)
    {
        if (_meter is { LimitReached: true }) return FeedRunResult.Paused;

        string? etag;
        lock (_gate) etag = _etag;
        ReleaseInfo? latest;
        try
        {
            latest = await _source.LatestAsync(etag, ct).ConfigureAwait(false);
        }
        catch (ReleaseNotUsableException ex)
        {
            return Reject(new ReleaseInfo(ex.Tag, default), _current.Set, ex.Message);
        }
        var inUse = _current.Set;

        // "Not modified" after a failed download: the latest release is still the one that failed.
        if (latest is null) lock (_gate) latest = _failedRelease;

        if (latest is null)
        {
            // Nothing changed since the last answer, so whatever was decided about it still stands.
            if (AlreadyRejected(inUse, null)) return FeedRunResult.Attention;
            SetNote(UsingNote(inUse, "; no newer release"));
            return FeedRunResult.Ok;
        }

        if (BoundaryStore.CompareTags(latest.Tag, inUse.Tag) <= 0)
        {
            lock (_gate) { _etag = latest.ETag; _rejectedTag = null; _rejection = null; _failedRelease = null; }
            ClearPersistedRejection();
            SetNote(UsingNote(inUse, "; no newer release"));
            return FeedRunResult.Ok;
        }

        // The same release that was rejected before (maybe in an earlier session): do not download it again.
        if (AlreadyRejected(inUse, latest.Tag))
        {
            lock (_gate) _etag = latest.ETag;
            return FeedRunResult.Attention;
        }

        // Ruling R5: within 6 hours of a failed download, report that failure again instead of downloading.
        string? waiting = null;
        lock (_gate)
        {
            if (_failedRelease is not null && _clock.UtcNow < _nextDownloadUtc)
            {
                _etag = latest.ETag ?? _etag;
                waiting = _downloadFailure;
            }
        }
        if (waiting is not null)
        {
            SetNote($"{waiting}. Using {inUse.Edition}");
            throw new HttpRequestException(waiting);
        }

        RawBoundaryData raw;
        try
        {
            raw = await _source.DownloadAsync(latest, ct).ConfigureAwait(false);
        }
        catch (InvalidDataException ex)
        {
            return Reject(latest, inUse, ex.Message);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException or IOException
                                       || (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            // The first failure is the normal retrying failure; the next download waits DownloadBackoff.
            var next = _clock.UtcNow + DownloadBackoff;
            var failure = string.Create(CultureInfo.InvariantCulture,
                $"Could not download {latest.Tag} ({ex.Message}); next try after {next.ToLocalTime():HH:mm}");
            lock (_gate) { _failedRelease = latest; _nextDownloadUtc = next; _downloadFailure = failure; _etag = latest.ETag ?? _etag; }
            SetNote($"{failure}. Using {inUse.Edition}");
            throw;
        }
        lock (_gate) _failedRelease = null;

        var result = _validator.Validate(raw, _rules, out var set);
        if (set is null) return Reject(latest, inUse, result.Reason);

        if (raw.Files.Count > 0 && !_store.Save(latest.Tag, latest.Date, raw.Files, out var error))
            _log.Warn($"Boundaries: could not store {set.Edition}; it is in use for this session only", error);

        var old = _current.Swap(set);
        var changed = ChangedRegionIds(old, set);
        _log.Info($"Boundaries: now {set.Edition} (was {old.Edition}); " +
                  $"{changed.Count} {(changed.Count == 1 ? "region" : "regions")} changed" +
                  (changed.Count > 0 ? ": " + string.Join(", ", changed) : ""));

        lock (_gate) { _etag = latest.ETag; _rejectedTag = null; _rejection = null; }
        ClearPersistedRejection();
        SetNote(UsingNote(set, $" (updated from {old.Edition})"));
        return FeedRunResult.Ok;
    }

    // True (and the note says so) when a rejection is remembered, for any release (onlyForTag null) or for the one named.
    private bool AlreadyRejected(BoundarySet inUse, string? onlyForTag)
    {
        string? message, tag;
        lock (_gate)
        {
            if (_rejection is null || (onlyForTag is not null && _rejectedTag != onlyForTag)) return false;
            message = _rejection;
            tag = _rejectedTag;
        }
        SetNote(RejectedNote(inUse, tag, message));
        return true;
    }

    // Remembers the rejection (in memory and next to the store, so a restart does not download the release again) and
    // asks for attention: the dot goes amber on the feed's normal interval, which is not a failure.
    private FeedRunResult Reject(ReleaseInfo release, BoundarySet inUse, string reason)
    {
        var message = $"Rejected {release.Tag}: {reason}";
        bool isNew;
        lock (_gate)
        {
            isNew = _rejectedTag != release.Tag || _rejection != message;
            _rejectedTag = release.Tag; _rejection = message; _etag = release.ETag; _failedRelease = null;
        }
        SetNote(RejectedNote(inUse, release.Tag, message));
        if (isNew)
        {
            _log.Warn(message);
            PersistRejection(release.Tag, message);
        }
        return FeedRunResult.Attention;
    }

    private static string RejectedNote(BoundarySet inUse, string? tag, string? message) =>
        $"{message}. Using {inUse.Edition}; kept ({tag} not adopted)";

    private string RejectionFile => Path.Combine(_store.CacheDir, "rejected.json");

    private void LoadPersistedRejection(BoundarySet inUse)
    {
        try
        {
            if (!File.Exists(RejectionFile)) return;
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(RejectionFile));
            var tag = doc.RootElement.GetProperty("tag").GetString();
            var reason = doc.RootElement.GetProperty("reason").GetString();
            // Ruling R7: made with other rules or probes, or by another GlobeCore (or before fingerprints): forgotten.
            if (!doc.RootElement.TryGetProperty("fingerprint", out var fp) || fp.ValueKind != System.Text.Json.JsonValueKind.String
                || !string.Equals(fp.GetString(), _fingerprint, StringComparison.OrdinalIgnoreCase))
            {
                _log.Info($"Boundaries: forgot the remembered rejection of {tag} (the rules, the probes or GlobeCore have changed since)");
                ClearPersistedRejection();
                return;
            }
            // Only a release still newer than the set in use matters; anything older is history.
            if (string.IsNullOrEmpty(tag) || string.IsNullOrEmpty(reason) || BoundaryStore.CompareTags(tag, inUse.Tag) <= 0) return;
            _rejectedTag = tag;
            _rejection = reason;
            _note = RejectedNote(inUse, tag, reason);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or IOException or UnauthorizedAccessException
                                       or KeyNotFoundException or InvalidOperationException)
        {
            _log.Warn("Boundaries: the remembered rejection is unreadable and was ignored", ex);
        }
    }

    private void PersistRejection(string tag, string reason)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new { tag, reason, fingerprint = _fingerprint });
        if (!Storage.AtomicFile.TryWriteAllText(RejectionFile, json, out var error))
            _log.Warn("Boundaries: could not remember the rejection", error);
    }

    // Quiet when there is nothing to delete, the store folder included (it is created only when a set is stored).
    private void ClearPersistedRejection()
    {
        try
        {
            if (File.Exists(RejectionFile)) File.Delete(RejectionFile);
        }
        catch (DirectoryNotFoundException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warn("Boundaries: could not clear the remembered rejection", ex);
        }
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
