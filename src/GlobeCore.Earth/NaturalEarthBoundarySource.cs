using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using GlobeCore.Feeds;
using GlobeCore.Regions;

namespace GlobeCore.Earth;

/// <summary>
/// Natural Earth's releases on GitHub (<c>nvkelso/natural-earth-vector</c>): the latest release comes from the GitHub API
/// (unauthenticated, ETag-aware), and a release's three files from <c>raw.githubusercontent.com</c> at that tag. Pass the
/// app's shared <see cref="FeedHttp"/>, so its User-Agent applies and its <see cref="FeedHttp.BytesReceived"/> (wired to
/// the app's <see cref="DownloadMeter"/>) counts the downloads.
/// </summary>
public sealed partial class NaturalEarthBoundarySource(FeedHttp http) : IBoundarySource
{
    public static readonly Uri LatestReleaseUrl = new("https://api.github.com/repos/nvkelso/natural-earth-vector/releases/latest");

    /// <summary>The raw URL of one of a release's <c>geojson/</c> files.</summary>
    public static Uri FileUrl(string tag, string file) =>
        new($"https://raw.githubusercontent.com/nvkelso/natural-earth-vector/{tag}/geojson/{file}");

    private readonly FeedHttp _http = http ?? throw new ArgumentNullException(nameof(http));

    public async Task<ReleaseInfo?> LatestAsync(string? etag, CancellationToken ct)
    {
        ConditionalResponse response;
        try
        {
            response = await _http.GetConditionalAsync(LatestReleaseUrl, etag, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
        {
            throw new HttpRequestException(
                $"GitHub is limiting requests (HTTP {(int)ex.StatusCode}); will try again later", ex, ex.StatusCode);
        }
        if (response.NotModified) return null;

        try
        {
            using var doc = JsonDocument.Parse(response.Body);
            var root = doc.RootElement;
            var tag = root.GetProperty("tag_name").GetString() ?? "";
            if (!ReleaseTag().IsMatch(tag)) throw new ReleaseNotUsableException(Shorten(tag), $"tag not understood: {Shorten(tag)}");
            var published = DateTimeOffset.Parse(root.GetProperty("published_at").GetString() ?? "", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
            return new ReleaseInfo(tag, DateOnly.FromDateTime(published.UtcDateTime), response.ETag);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new InvalidDataException("GitHub's answer about the latest release was not understood: " + ex.Message, ex);
        }
    }

    public async Task<RawBoundaryData> DownloadAsync(ReleaseInfo release, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(release);
        if (!ReleaseTag().IsMatch(release.Tag)) throw new InvalidDataException($"unexpected release tag \"{Shorten(release.Tag)}\"");
        var files = new Dictionary<string, byte[]>();
        foreach (var name in NaturalEarthBoundaries.ReleaseFiles)
        {
            try
            {
                files[name] = await _http.GetBytesAsync(FileUrl(release.Tag, name), ct).ConfigureAwait(false);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                // The release exists but lacks a file: it is not a usable set, which no retry will change.
                throw new InvalidDataException($"{name} is not in release {release.Tag} (HTTP 404)", ex);
            }
            catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            {
                throw new HttpRequestException($"GitHub is limiting requests (HTTP {(int)ex.StatusCode}); will try again later", ex, ex.StatusCode);
            }
        }
        return NaturalEarthBoundaries.Parse(new StoredBoundaryFiles(release.Tag, release.Date, files));
    }

    private static string Shorten(string s) => s.Length <= 40 ? s : s[..40] + "...";

    // A version tag such as v5.1.2: it goes into a URL and a folder name, so nothing else is accepted.
    [GeneratedRegex(@"^v?\d{1,3}\.\d{1,3}(\.\d{1,3})?\z")]
    private static partial Regex ReleaseTag();
}
