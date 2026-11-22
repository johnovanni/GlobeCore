using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using AstroCore.Earth;
using GlobeCore.Earth;
using GlobeCore.Feeds;
using GlobeCore.Regions;

namespace GlobeCore.Tests;

public sealed class BoundaryUpdateFeedTests : IDisposable
{
    private const string Latest = "https://api.github.com/repos/nvkelso/natural-earth-vector/releases/latest";
    private const string Raw = "https://raw.githubusercontent.com/nvkelso/natural-earth-vector/";

    // Small hand-made release descriptions (the real ones carry many more fields; the source reads only these two).
    private const string Release512 = """{"tag_name":"v5.1.2","name":"Version 5.1.2","published_at":"2022-05-13T17:55:14Z"}""";
    private const string Release520 = """{"tag_name":"v5.2.0","name":"Version 5.2.0 (fictional)","published_at":"2026-12-01T10:00:00Z"}""";

    private static readonly DateTime Noon = new(2026, 12, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "BoundaryUpdateFeedTests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    // ---- fictional v5.2.0, built once from the bundled 5.1.2 files: Luxembourg's outline is nudged, and the admin-1 file
    // ---- is the bundled Crimea/Sevastopol extract plus two made-up units (in place of the full admin-1 file).

    private static readonly Lazy<Dictionary<string, byte[]>> Files520 = new(BuildFiles520);

    private static byte[] Bundled(string name)
    {
        using var s = EarthAssets.Open(name);
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    private static Dictionary<string, byte[]> BuildFiles520()
    {
        var countries = JsonNode.Parse(Bundled(EarthAssets.CountriesFile))!;
        var lux = countries["features"]!.AsArray().Single(f => (string?)f!["properties"]!["ISO_A3_EH"] == "LUX")!;
        var geometry = lux["geometry"]!;
        var ring = geometry["type"]!.GetValue<string>() == "Polygon" ? geometry["coordinates"]![0]! : geometry["coordinates"]![0]![0]!;
        double moved = ring[0]![0]!.GetValue<double>() + 0.01;
        ring[0]![0] = moved;
        ring[ring.AsArray().Count - 1]![0] = moved;   // keep the ring closed

        var admin1 = JsonNode.Parse(Bundled(EarthAssets.CrimeaRegionsFile))!;
        var features = admin1["features"]!.AsArray();
        foreach (var (code, adm0, lon) in new[] { ("SS-EC", "SDS", 30.0), ("FR-75", "FRA", 2.0) })
            features.Add(JsonNode.Parse($$$"""
                {"type":"Feature","properties":{"iso_3166_2":"{{{code}}}","adm0_a3":"{{{adm0}}}","name":"Made-up unit {{{code}}}"},
                 "geometry":{"type":"Polygon","coordinates":[[[{{{lon}}},5.0],[{{{lon + 1}}},5.0],[{{{lon + 1}}},6.0],[{{{lon}}},6.0],[{{{lon}}},5.0]]]}}
                """));
        return new Dictionary<string, byte[]>
        {
            [NaturalEarthBoundaries.CountriesFile] = Encoding.UTF8.GetBytes(countries.ToJsonString()),
            [NaturalEarthBoundaries.LinesFile] = Bundled(EarthAssets.LandBoundariesFile),
            [NaturalEarthBoundaries.Admin1File] = Encoding.UTF8.GetBytes(admin1.ToJsonString()),
        };
    }

    // ---- a scripted web server

    private sealed class Web : HttpMessageHandler
    {
        public readonly List<HttpRequestMessage> Requests = [];
        public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            lock (Requests) Requests.Add(request);
            var response = Respond(request);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }

        public int Count { get { lock (Requests) return Requests.Count; } }
        public int RawCount { get { lock (Requests) return Requests.Count(r => r.RequestUri!.ToString().StartsWith(Raw, StringComparison.Ordinal)); } }
    }

    private static HttpResponseMessage Ok(byte[] body, string? etag = null)
    {
        var r = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
        if (etag is not null) r.Headers.TryAddWithoutValidation("ETag", etag);
        return r;
    }

    private static HttpResponseMessage Json(string json, string? etag = null) => Ok(Encoding.UTF8.GetBytes(json), etag);

    /// <summary>A server whose latest release is <paramref name="release"/>, with files from <paramref name="files"/> (null = 404).</summary>
    private static Func<HttpRequestMessage, HttpResponseMessage> Server(string release, Func<string, byte[]?> files, string? etag = "\"r1\"") => req =>
    {
        var url = req.RequestUri!.ToString();
        if (url == Latest)
            return req.Headers.IfNoneMatch.Any(t => t.Tag == etag) ? new HttpResponseMessage(HttpStatusCode.NotModified) : Json(release, etag);
        if (url.StartsWith(Raw, StringComparison.Ordinal))
        {
            var name = url[(url.LastIndexOf('/') + 1)..];
            var bytes = files(name);
            return bytes is null ? new HttpResponseMessage(HttpStatusCode.NotFound) : Ok(bytes);
        }
        return new HttpResponseMessage(HttpStatusCode.NotFound);
    };

    private sealed class Rig : IDisposable
    {
        public required Web Web { get; init; }
        public required FeedHttp Http { get; init; }
        public required BoundaryUpdateFeed Feed { get; init; }
        public required CurrentBoundaries Current { get; init; }
        public required BoundaryStore Store { get; init; }
        public required RecordingLog Log { get; init; }
        public required BoundarySet Initial { get; init; }
        public DownloadMeter? Meter { get; init; }
        public void Dispose() => Http.Dispose();
    }

    private Rig MakeRig(Func<HttpRequestMessage, HttpResponseMessage> respond, bool withMeter = false, long? limit = null)
    {
        var web = new Web { Respond = respond };
        var http = new FeedHttp("GlobeCoreTests/1.0", web);
        var rules = NaturalEarthBoundaries.TransitionalDefaultRules;
        var initial = NaturalEarthBoundaries.LoadBundled(rules);
        var validator = new BoundaryValidator(
            [(new GeoPosition(15.6, 32.5), "SDN"), (new GeoPosition(50.45, 30.5), "UKR"), (new GeoPosition(45.0, 34.0), "UKR")],
            _ => [], initial.Regions.All.Count);
        var log = new RecordingLog();
        var store = new BoundaryStore(Path.Combine(_dir, "boundaries"));
        var current = new CurrentBoundaries(initial);
        DownloadMeter? meter = null;
        if (withMeter)
        {
            meter = new DownloadMeter(new TestClock(Noon), TimeZoneInfo.Utc, null, log) { LimitBytes = limit };
            http.BytesReceived += meter.Add;
        }
        var feed = new BoundaryUpdateFeed(new NaturalEarthBoundarySource(http), rules, validator, store, current, log, meter);
        return new Rig { Web = web, Http = http, Feed = feed, Current = current, Store = store, Log = log, Initial = initial, Meter = meter };
    }

    [Fact]
    public async Task NoNewerReleaseDownloadsNothing()
    {
        using var rig = MakeRig(Server(Release512, _ => null, "\"r1\""));
        Assert.Equal("Borders", rig.Feed.Name);
        Assert.Equal(TimeSpan.FromHours(24), rig.Feed.Interval);

        Assert.Equal(FeedRunResult.Ok, await rig.Feed.RunOnceAsync(CancellationToken.None));
        Assert.Equal(1, rig.Web.Count);   // one request: the release description
        Assert.Equal(0, rig.Web.RawCount);
        Assert.Equal("Using Natural Earth 5.1.2 (2022-05-13); no newer release", rig.Feed.StatusNote);
        Assert.Same(rig.Initial, rig.Current.Set);

        // The next check sends the ETag and the server says nothing changed.
        Assert.Equal(FeedRunResult.Ok, await rig.Feed.RunOnceAsync(CancellationToken.None));
        Assert.Equal(2, rig.Web.Count);
        Assert.Equal("\"r1\"", rig.Web.Requests[1].Headers.IfNoneMatch.Single().Tag);
        Assert.Equal(0, rig.Web.RawCount);
        Assert.Equal("Using Natural Earth 5.1.2 (2022-05-13); no newer release", rig.Feed.StatusNote);
        Assert.Empty(rig.Store.StoredTags());
    }

    [Fact]
    public async Task NewerValidReleaseIsStoredAndSwapped()
    {
        using var rig = MakeRig(Server(Release520, n => Files520.Value.GetValueOrDefault(n)));
        var changes = new List<BoundariesChangedEventArgs>();
        rig.Current.Changed += (_, e) => changes.Add(e);

        Assert.Equal(FeedRunResult.Ok, await rig.Feed.RunOnceAsync(CancellationToken.None));

        var now = rig.Current.Set;
        Assert.Equal("v5.2.0", now.Tag);
        Assert.Equal("Natural Earth 5.2.0 (2026-12-01)", now.Edition);
        Assert.Contains("Crimea shown as part of Ukraine", now.Describe());   // the rules were applied to the new set
        Assert.Equal("UKR", now.Regions.Find(new GeoPosition(45.0, 34.0))?.Id);
        var change = Assert.Single(changes);
        Assert.Same(rig.Initial, change.Old);
        Assert.Same(now, change.New);

        Assert.Equal(["v5.2.0"], rig.Store.StoredTags());   // stored with its manifest
        Assert.True(File.Exists(Path.Combine(_dir, "boundaries", "v5.2.0", BoundaryStore.ManifestFile)));
        Assert.True(File.Exists(Path.Combine(_dir, "boundaries", "v5.2.0", NaturalEarthBoundaries.Admin1File)));

        // The log lists the regions whose shapes changed: Luxembourg only.
        var line = Assert.Single(rig.Log.Infos, m => m.Contains("changed", StringComparison.Ordinal));
        Assert.Contains("1 region changed", line);
        Assert.Contains("LUX", line);
        Assert.DoesNotContain("DEU", line);
        Assert.Contains("Using Natural Earth 5.2.0 (2026-12-01)", rig.Feed.StatusNote);

        // Next check: the same tag is not newer than the set in use, so nothing more is downloaded.
        int raws = rig.Web.RawCount;
        await rig.Feed.RunOnceAsync(CancellationToken.None);
        Assert.Equal(raws, rig.Web.RawCount);
        Assert.Equal("Using Natural Earth 5.2.0 (2026-12-01); no newer release", rig.Feed.StatusNote);
    }

    [Fact]
    public async Task TruncatedFileIsRejectedAndCurrentSetKept()
    {
        var truncated = Files520.Value[NaturalEarthBoundaries.LinesFile][..5000];
        using var rig = MakeRig(Server(Release520, n => n == NaturalEarthBoundaries.LinesFile ? truncated : Files520.Value.GetValueOrDefault(n)));

        var ex = await Assert.ThrowsAsync<BoundaryRejectedException>(() => rig.Feed.RunOnceAsync(CancellationToken.None));
        Assert.StartsWith("Rejected v5.2.0: ", ex.Message);
        Assert.Same(rig.Initial, rig.Current.Set);
        Assert.Empty(rig.Store.StoredTags());   // nothing half-stored
        Assert.False(Directory.Exists(Path.Combine(_dir, "boundaries", "v5.2.0")));
        Assert.Contains(rig.Log.Warnings, m => m.StartsWith("Rejected v5.2.0", StringComparison.Ordinal));
        Assert.Contains("Natural Earth 5.1.2", rig.Feed.StatusNote);

        // The release is not downloaded again on every retry, and stays rejected (amber) until a newer release appears.
        int raws = rig.Web.RawCount;
        var again = await Assert.ThrowsAsync<BoundaryRejectedException>(() => rig.Feed.RunOnceAsync(CancellationToken.None));
        Assert.Equal(ex.Message, again.Message);
        Assert.Equal(raws, rig.Web.RawCount);
    }

    [Fact]
    public async Task HtmlErrorPageInPlaceOfAFileIsRejected()
    {
        var html = Encoding.UTF8.GetBytes("<!DOCTYPE html><html><body>Service temporarily unavailable</body></html>");
        using var rig = MakeRig(Server(Release520, n => n == NaturalEarthBoundaries.CountriesFile ? html : Files520.Value.GetValueOrDefault(n)));
        var ex = await Assert.ThrowsAsync<BoundaryRejectedException>(() => rig.Feed.RunOnceAsync(CancellationToken.None));
        Assert.StartsWith("Rejected v5.2.0: ", ex.Message);
        Assert.Same(rig.Initial, rig.Current.Set);
        Assert.Empty(rig.Store.StoredTags());
    }

    [Fact]
    public async Task MissingFileRejectsTheWholeSet()
    {
        using var rig = MakeRig(Server(Release520, n => n == NaturalEarthBoundaries.Admin1File ? null : Files520.Value.GetValueOrDefault(n)));
        var ex = await Assert.ThrowsAsync<BoundaryRejectedException>(() => rig.Feed.RunOnceAsync(CancellationToken.None));
        Assert.StartsWith("Rejected v5.2.0: ", ex.Message);
        Assert.Contains(NaturalEarthBoundaries.Admin1File, ex.Message);
        Assert.Same(rig.Initial, rig.Current.Set);
        Assert.Empty(rig.Store.StoredTags());
    }

    [Fact]
    public async Task SetThatFailsValidationIsRejected()
    {
        // The release has lost Sudan: the Khartoum probe fails, so the set is not used.
        var countries = JsonNode.Parse(Files520.Value[NaturalEarthBoundaries.CountriesFile])!;
        var features = countries["features"]!.AsArray();
        features.Remove(features.Single(f => (string?)f!["properties"]!["ISO_A3_EH"] == "SDN"));
        var bad = Encoding.UTF8.GetBytes(countries.ToJsonString());
        using var rig = MakeRig(Server(Release520, n => n == NaturalEarthBoundaries.CountriesFile ? bad : Files520.Value.GetValueOrDefault(n)));
        var ex = await Assert.ThrowsAsync<BoundaryRejectedException>(() => rig.Feed.RunOnceAsync(CancellationToken.None));
        Assert.StartsWith("Rejected v5.2.0: ", ex.Message);
        Assert.Contains("SDN", ex.Message);
        Assert.Same(rig.Initial, rig.Current.Set);
        Assert.Empty(rig.Store.StoredTags());
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData((HttpStatusCode)429)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task RateLimitedKeepsSet(HttpStatusCode status)
    {
        bool down = true;
        var ok = Server(Release520, n => Files520.Value.GetValueOrDefault(n));
        using var rig = MakeRig(req => down ? new HttpResponseMessage(status) : ok(req));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => rig.Feed.RunOnceAsync(CancellationToken.None));
        Assert.IsNotType<BoundaryRejectedException>(ex);
        Assert.Same(rig.Initial, rig.Current.Set);
        Assert.Empty(rig.Store.StoredTags());

        // A later run (the scheduler's retry) picks the release up.
        down = false;
        Assert.Equal(FeedRunResult.Ok, await rig.Feed.RunOnceAsync(CancellationToken.None));
        Assert.Equal("v5.2.0", rig.Current.Set.Tag);
    }

    [Fact]
    public async Task NetworkErrorKeepsSet()
    {
        using var rig = MakeRig(_ => throw new HttpRequestException("no route to host"));
        await Assert.ThrowsAsync<HttpRequestException>(() => rig.Feed.RunOnceAsync(CancellationToken.None));
        Assert.Same(rig.Initial, rig.Current.Set);
    }

    [Fact]
    public async Task FailureWhileDownloadingIsRetriedNotRejected()
    {
        int calls = 0;
        using var rig = MakeRig(Server(Release520, n => ++calls == 2 ? throw new HttpRequestException("connection reset") : Files520.Value.GetValueOrDefault(n)));
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => rig.Feed.RunOnceAsync(CancellationToken.None));
        Assert.IsNotType<BoundaryRejectedException>(ex);
        Assert.Same(rig.Initial, rig.Current.Set);
        Assert.Equal(FeedRunResult.Ok, await rig.Feed.RunOnceAsync(CancellationToken.None));   // not remembered as rejected
        Assert.Equal("v5.2.0", rig.Current.Set.Tag);
    }

    [Fact]
    public async Task DownloadsAreCounted()
    {
        using var rig = MakeRig(Server(Release520, n => Files520.Value.GetValueOrDefault(n)), withMeter: true);
        await rig.Feed.RunOnceAsync(CancellationToken.None);
        long files = Files520.Value.Values.Sum(b => b.Length);
        Assert.True(rig.Meter!.TodayBytes >= files, $"counted {rig.Meter.TodayBytes}, files are {files}");
        Assert.True(rig.Meter.TodayBytes < files + 2000);
    }

    [Fact]
    public async Task PausesWhenTheDailyLimitIsReached()
    {
        using var rig = MakeRig(Server(Release520, n => Files520.Value.GetValueOrDefault(n)), withMeter: true, limit: 1000);
        rig.Meter!.Add(1000);
        Assert.Equal(FeedRunResult.Paused, await rig.Feed.RunOnceAsync(CancellationToken.None));
        Assert.Equal(0, rig.Web.Count);
    }

    [Fact]
    public async Task SchedulerShowsTheNotesAndAmberOnRejection()
    {
        var truncated = Files520.Value[NaturalEarthBoundaries.LinesFile][..5000];
        using var rig = MakeRig(Server(Release520, n => n == NaturalEarthBoundaries.LinesFile ? truncated : Files520.Value.GetValueOrDefault(n)));
        using var scheduler = new FeedScheduler([rig.Feed], new TestClock(Noon), rig.Log, "idle");
        await scheduler.TickAsync(CancellationToken.None);
        var status = Assert.Single(scheduler.Statuses);
        Assert.Equal(FeedState.Retrying, status.State);   // amber
        Assert.StartsWith("Rejected v5.2.0: ", status.LastError);
        Assert.Contains("Using Natural Earth 5.1.2 (2022-05-13)", status.Note);
        Assert.Same(rig.Initial, rig.Current.Set);
    }

    [Fact]
    public async Task SchedulerShowsTheNoteWhenNothingIsNewer()
    {
        using var rig = MakeRig(Server(Release512, _ => null));
        using var scheduler = new FeedScheduler([rig.Feed], new TestClock(Noon), rig.Log, "idle");
        await scheduler.TickAsync(CancellationToken.None);
        var status = Assert.Single(scheduler.Statuses);
        Assert.Equal(FeedState.Ok, status.State);
        Assert.Equal("Using Natural Earth 5.1.2 (2022-05-13); no newer release", status.Note);
    }

    [Fact]
    public async Task ReleaseWithAnUnsafeTagIsNotDownloaded()
    {
        using var rig = MakeRig(Server("""{"tag_name":"../../evil","published_at":"2026-12-01T10:00:00Z"}""", _ => null));
        await Assert.ThrowsAsync<InvalidDataException>(() => rig.Feed.RunOnceAsync(CancellationToken.None));
        Assert.Equal(0, rig.Web.RawCount);
    }

    [Fact]
    public async Task FeedHttpHonoursETagAndStatuses()
    {
        var web = new Web
        {
            Respond = req => req.Headers.IfNoneMatch.Any(t => t.Tag == "\"x\"")
                ? new HttpResponseMessage(HttpStatusCode.NotModified) : Json("hello", "\"x\""),
        };
        using var http = new FeedHttp("GlobeCoreTests/1.0", web);
        var first = await http.GetConditionalAsync(new Uri("http://h/a"), null, CancellationToken.None);
        Assert.False(first.NotModified);
        Assert.Equal("hello", Encoding.UTF8.GetString(first.Body));
        Assert.Equal("\"x\"", first.ETag);
        var second = await http.GetConditionalAsync(new Uri("http://h/a"), first.ETag, CancellationToken.None);
        Assert.True(second.NotModified);
        Assert.Empty(second.Body);
        web.Respond = _ => new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => http.GetConditionalAsync(new Uri("http://h/a"), null, CancellationToken.None));
        Assert.Equal((HttpStatusCode)429, ex.StatusCode);
    }
}
