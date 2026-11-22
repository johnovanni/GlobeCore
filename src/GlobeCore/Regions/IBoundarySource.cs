namespace GlobeCore.Regions;

/// <summary>A release of a boundary source: the tag, its publication date, and the ETag of the answer that described it.</summary>
/// <param name="Tag">The release tag (e.g. v5.1.2); <see cref="BoundaryStore.CompareTags"/> orders tags.</param>
/// <param name="ETag">The ETag of the "latest release" answer (with its quotes), to send back as If-None-Match; null if the server gave none.</param>
public sealed record ReleaseInfo(string Tag, DateOnly Date, string? ETag = null);

/// <summary>
/// Where a body's boundary sets come from (for Earth, the GitHub releases of Natural Earth:
/// <c>GlobeCore.Earth.NaturalEarthBoundarySource</c>). A Moon app plugs in its own. Network failures are thrown (the feed
/// keeps the current set and retries); a download that arrives but is not a usable set throws
/// <see cref="InvalidDataException"/> (the feed rejects that release).
/// </summary>
public interface IBoundarySource
{
    /// <summary>
    /// The newest release. <paramref name="etag"/> is the ETag of the last answer, so the source can ask whether it has
    /// changed; null is returned when the server says it has not.
    /// </summary>
    Task<ReleaseInfo?> LatestAsync(string? etag, CancellationToken ct);

    /// <summary>Downloads and reads the release's files (<see cref="RawBoundaryData.Files"/> holds the bytes, for <see cref="BoundaryStore"/>).</summary>
    /// <exception cref="InvalidDataException">A file is missing from the release, or is not the format expected.</exception>
    Task<RawBoundaryData> DownloadAsync(ReleaseInfo release, CancellationToken ct);
}
