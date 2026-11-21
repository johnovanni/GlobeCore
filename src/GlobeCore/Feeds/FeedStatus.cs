namespace GlobeCore.Feeds;

/// <summary>
/// A feed's state for its status dot. <see cref="Idle"/>: its last run was skipped (for example the layer is off, or its
/// area is not in view), so nothing is being fetched right now.
/// </summary>
public enum FeedState { NotStarted, Ok, Retrying, Failing, NotSetUp, Paused, Idle }

/// <param name="Note">Optional extra line for the tooltip, such as "Daily download limit reached".</param>
public sealed record FeedStatus(
    string Name, FeedState State, DateTime? LastSuccessUtc, string? LastError,
    int ConsecutiveFailures, DateTime NextRunUtc, string? Note = null);
