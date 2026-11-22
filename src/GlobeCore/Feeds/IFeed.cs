namespace GlobeCore.Feeds;

/// <summary>What one feed run did, so the scheduler can show it honestly.</summary>
public enum FeedRunResult
{
    /// <summary>Data was fetched and stored.</summary>
    Ok,
    /// <summary>Nothing to do this time (for example, the layer is off or its area is not in view); the status is kept.</summary>
    Skipped,
    /// <summary>The feed needs configuration (such as an API key) it does not have.</summary>
    NotSetUp,
    /// <summary>Paused because the daily download limit is reached.</summary>
    Paused,
    /// <summary>
    /// The run finished and kept the current data, but the owner should know why it did not update (for example a
    /// downloaded set was rejected). Not a failure: no backoff, the next run is at the feed's normal interval, and the
    /// reason is the feed's <see cref="IFeedStatusNote"/>.
    /// </summary>
    Attention,
}

public interface IFeed
{
    string Name { get; }
    TimeSpan Interval { get; }
    Task<FeedRunResult> RunOnceAsync(CancellationToken ct);
}

/// <summary>
/// A feed that has its own line for the status tooltip, such as "Using Natural Earth 5.1.2 (2022-05-13); no newer release".
/// <see cref="FeedScheduler"/> reads it after every run that ended OK or failed.
/// </summary>
public interface IFeedStatusNote
{
    string? StatusNote { get; }
}
