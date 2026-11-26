using System.Globalization;
using System.Text.Json;
using GlobeCore.Logging;
using GlobeCore.Storage;
using GlobeCore.Time;

namespace GlobeCore.Feeds;

/// <summary>
/// A server's firewall answered with a challenge (AWS WAF's <c>x-amzn-waf-action</c> header, or <c>202 Accepted</c> to a GET),
/// or the host is paused after one. Not a format or network fault: the feed keeps its data and tries again at
/// <see cref="RetryAfterUtc"/>.
/// </summary>
/// <param name="RetryAfterUtc">When the host may be asked again; null when no <see cref="HostPauses"/> is kept.</param>
public sealed class FeedBlockedException(string host, DateTime? retryAfterUtc)
    : HttpRequestException(MessageFor(host, retryAfterUtc))
{
    public string Host { get; } = host;
    public DateTime? RetryAfterUtc { get; } = retryAfterUtc;

    public static string MessageFor(string host, DateTime? retryAfterUtc) =>
        $"{host} is refusing requests for now (a firewall check)"
        + (retryAfterUtc is { } t ? "; trying again after " + t.ToString("d MMM HH:mm", CultureInfo.InvariantCulture) + " UTC" : "");
}

/// <summary>
/// Hosts that answered with a firewall challenge, and how long each is left alone: 1 h, then 2 h, 4 h and 6 h for
/// challenges that follow one another; a success clears the count. With a file the pauses outlast a restart, so
/// reopening the app does not ask a host that is refusing again at once. Thread-safe.
/// </summary>
public sealed class HostPauses
{
    /// <summary>The pause after the first, second, third and later challenges in a row.</summary>
    public static readonly IReadOnlyList<TimeSpan> Steps =
        [TimeSpan.FromHours(1), TimeSpan.FromHours(2), TimeSpan.FromHours(4), TimeSpan.FromHours(6)];

    /// <summary>A challenge this long after the last pause ended starts the count again.</summary>
    public static readonly TimeSpan ForgetAfter = TimeSpan.FromHours(24);

    private sealed record Entry(int Strikes, DateTime UntilUtc);

    private readonly IClock _clock;
    private readonly string? _file;
    private readonly IAppLog? _log;
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _hosts;

    /// <param name="file">Where the pauses are kept between runs (JSON); null keeps them in memory only.</param>
    public HostPauses(IClock clock, string? file = null, IAppLog? log = null)
    {
        _clock = clock;
        _file = file;
        _log = log;
        _hosts = Read();
    }

    /// <summary>When <paramref name="host"/> may be asked again, if it is paused now; otherwise null.</summary>
    public DateTime? PausedUntil(string host)
    {
        var now = _clock.UtcNow;
        lock (_gate)
        {
            if (!_hosts.TryGetValue(Key(host), out var e)) return null;
            // A pause longer than the longest step was not written by this clock (a changed system time): it is ignored.
            return e.UntilUtc > now && e.UntilUtc - now <= Steps[^1] ? e.UntilUtc : null;
        }
    }

    /// <summary>Records a challenge from <paramref name="host"/> and returns the end of its pause.</summary>
    public DateTime Block(string host)
    {
        var now = _clock.UtcNow;
        DateTime until;
        lock (_gate)
        {
            var known = _hosts.TryGetValue(Key(host), out var e);
            // Requests already in flight when the first challenge came (feeds run side by side) are the same refusal: the
            // pause stands, and it is not counted again.
            if (known && e!.UntilUtc > now && e.UntilUtc - now <= Steps[^1]) return e.UntilUtc;
            var strikes = known && now - e!.UntilUtc < ForgetAfter ? e.Strikes + 1 : 1;
            until = now + Steps[Math.Min(strikes, Steps.Count) - 1];
            _hosts[Key(host)] = new Entry(strikes, until);
            Write();
        }
        return until;
    }

    /// <summary>A normal answer from <paramref name="host"/>: its count of challenges is cleared.</summary>
    public void Cleared(string host)
    {
        lock (_gate)
        {
            if (_hosts.Remove(Key(host))) Write();
        }
    }

    private static string Key(string host) => host.ToLowerInvariant();

    private Dictionary<string, Entry> Read()
    {
        var empty = new Dictionary<string, Entry>(StringComparer.Ordinal);
        if (_file is null || !File.Exists(_file)) return empty;
        try
        {
            var read = JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(_file));
            return read is null ? empty : new Dictionary<string, Entry>(read.Where(kv => kv.Value is not null), StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            _log?.Warn($"The host pauses in {_file} could not be read; none are kept", ex);
            return empty;
        }
    }

    // Under the lock.
    private void Write()
    {
        if (_file is null) return;
        try
        {
            if (Path.GetDirectoryName(_file) is { Length: > 0 } dir) Directory.CreateDirectory(dir);
            if (!AtomicFile.TryWriteAllText(_file, JsonSerializer.Serialize(_hosts), out var error))
                _log?.Warn($"The host pauses could not be saved to {_file}", error);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log?.Warn($"The host pauses could not be saved to {_file}", ex);
        }
    }
}
