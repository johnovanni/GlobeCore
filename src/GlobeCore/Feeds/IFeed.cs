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
