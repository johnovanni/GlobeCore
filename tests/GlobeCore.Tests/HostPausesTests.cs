using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using GlobeCore.Feeds;
using GlobeCore.Logging;
using GlobeCore.Time;

namespace GlobeCore.Tests;

/// <summary>A firewall challenge (HDX's AWS WAF, 26 Nov 2026) is a block with a pause, never a "changed format".</summary>
public sealed class HostPausesTests
{
    private const string TestUa = "GlobeCoreTests/1.0";
    private static readonly DateTime T0 = new(2026, 11, 26, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Uri Package = new("https://data.humdata.org/api/3/action/package_show?id=global-hpc-hno");

    private sealed class FakeClock(DateTime t) : IClock { public DateTime UtcNow { get; set; } = t; }

    /// <summary>Answers like HDX's firewall did: 202 Accepted, <c>x-amzn-waf-action: challenge</c>, an empty HTML body.</summary>
    private sealed class ChallengeHandler : HttpMessageHandler
    {
        public int Requests;
        public bool Challenge = true;
        public HttpStatusCode Status = HttpStatusCode.Accepted;
        public bool WafHeader = true;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref Requests);
            if (!Challenge)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"result\":{\"resources\":[]}}") });
            var r = new HttpResponseMessage(Status) { Content = new StringContent("", Encoding.UTF8, "text/html") };
            if (WafHeader) r.Headers.TryAddWithoutValidation(FeedHttp.WafActionHeader, "challenge");
            return Task.FromResult(r);
        }
    }

    [Theory]
    [InlineData(HttpStatusCode.Accepted, true)]    // what HDX sent
    [InlineData(HttpStatusCode.Accepted, false)]   // 202 to a GET is never data
    [InlineData(HttpStatusCode.OK, true)]          // the WAF header on any status
    [InlineData(HttpStatusCode.MethodNotAllowed, true)]
    public async Task ChallengeThrowsBlockedNotAnEmptyBody(HttpStatusCode status, bool header)
    {
        using var http = new FeedHttp(TestUa, new ChallengeHandler { Status = status, WafHeader = header });
        var e = await Assert.ThrowsAsync<FeedBlockedException>(() => http.GetStringAsync(Package, CancellationToken.None));
        Assert.Equal("data.humdata.org", e.Host);
        Assert.Null(e.RetryAfterUtc);   // no pauses kept
        Assert.Equal("data.humdata.org is refusing requests for now (a firewall check)", e.Message);
    }

    [Fact]
    public async Task APausedHostIsNotAskedUntilThePauseEnds()
    {
        var clock = new FakeClock(T0);
        var h = new ChallengeHandler();
        using var http = new FeedHttp(TestUa, h, pauses: new HostPauses(clock));
        var first = await Assert.ThrowsAsync<FeedBlockedException>(() => http.GetStringAsync(Package, CancellationToken.None));
        Assert.Equal(T0.AddHours(1), first.RetryAfterUtc);
        Assert.Equal("data.humdata.org is refusing requests for now (a firewall check); trying again after 26 Nov 13:00 UTC", first.Message);
        Assert.Equal(1, h.Requests);

        // During the pause: refused here, without a request (the same host, any path, any case).
        clock.UtcNow = T0.AddMinutes(59);
        var during = await Assert.ThrowsAsync<FeedBlockedException>(() =>
            http.GetBytesAsync(new Uri("https://DATA.humdata.org/dataset/x/resource/y/download/z.csv"), CancellationToken.None));
        Assert.Equal(T0.AddHours(1), during.RetryAfterUtc);
        Assert.Equal(1, h.Requests);

        // Another host is not affected.
        h.Challenge = false;
        await http.GetStringAsync(new Uri("https://api.example.org/x"), CancellationToken.None);
        Assert.Equal(2, h.Requests);

        // After the pause: asked again, and a normal answer clears the count.
        clock.UtcNow = T0.AddHours(1);
        Assert.Contains("resources", await http.GetStringAsync(Package, CancellationToken.None));
        Assert.Equal(3, h.Requests);
    }

    [Fact]
    public void PausesGrowOneTwoFourSixHoursAndASuccessStartsAgain()
    {
        var clock = new FakeClock(T0);
        var p = new HostPauses(clock);
        var got = new List<double>();
        for (int i = 0; i < 5; i++)
        {
            var until = p.Block("data.humdata.org");
            got.Add((until - clock.UtcNow).TotalHours);
            clock.UtcNow = until;   // asked again when the pause ends, and challenged again
        }
        Assert.Equal([1, 2, 4, 6, 6], got);

        p.Cleared("data.humdata.org");
        Assert.Null(p.PausedUntil("data.humdata.org"));
        Assert.Equal(clock.UtcNow.AddHours(1), p.Block("data.humdata.org"));
    }

    [Fact]
    public void ChallengesDuringAPauseAreTheSameRefusal()
    {
        // Four feeds ask side by side; each is challenged before the first pause is recorded. One pause of 1 h, not 1-2-4-6.
        var clock = new FakeClock(T0);
        var p = new HostPauses(clock);
        Assert.All(Enumerable.Range(0, 4).Select(_ => p.Block("data.humdata.org")), u => Assert.Equal(T0.AddHours(1), u));
        clock.UtcNow = T0.AddHours(1);
        Assert.Equal(T0.AddHours(3), p.Block("data.humdata.org"));   // the next refusal after the pause is the second
    }

    [Fact]
    public void AChallengeADayAfterTheLastPauseStartsTheCountAgain()
    {
        var clock = new FakeClock(T0);
        var p = new HostPauses(clock);
        p.Block("h");                                   // until T0 + 1 h
        clock.UtcNow = T0.AddHours(1) + HostPauses.ForgetAfter;
        Assert.Equal(clock.UtcNow.AddHours(1), p.Block("h"));
    }

    [Fact]
    public void PausesOutlastARestartAndAFarFuturePauseIsIgnored()
    {
        var dir = Path.Combine(Path.GetTempPath(), "globecore-hostpauses-" + Guid.NewGuid().ToString("N"));
        var file = Path.Combine(dir, "host-pauses.json");
        try
        {
            var clock = new FakeClock(T0);
            new HostPauses(clock, file).Block("data.humdata.org");
            Assert.True(File.Exists(file));

            // The next session reads it: still paused until T0 + 1 h.
            clock.UtcNow = T0.AddMinutes(30);
            Assert.Equal(T0.AddHours(1), new HostPauses(clock, file).PausedUntil("data.humdata.org"));

            // A clock set back by days would turn the saved pause into a very long one: more than 6 h ahead is not trusted.
            clock.UtcNow = T0.AddDays(-3);
            Assert.Null(new HostPauses(clock, file).PausedUntil("data.humdata.org"));

            // A cleared host is gone from the file.
            clock.UtcNow = T0.AddMinutes(30);
            new HostPauses(clock, file).Cleared("data.humdata.org");
            Assert.Null(new HostPauses(clock, file).PausedUntil("data.humdata.org"));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ADamagedFileIsLoggedAndIgnored()
    {
        var dir = Path.Combine(Path.GetTempPath(), "globecore-hostpauses-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "host-pauses.json");
        try
        {
            File.WriteAllText(file, "{ not json");
            var log = new ListLog();
            var p = new HostPauses(new FakeClock(T0), file, log);
            Assert.Null(p.PausedUntil("data.humdata.org"));
            Assert.Contains(log.Lines, l => l.StartsWith("WARN", StringComparison.Ordinal) && l.Contains("could not be read", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private sealed class ListLog : IAppLog
    {
        public List<string> Lines { get; } = [];
        public void Info(string m) => Lines.Add("INFO " + m);
        public void Warn(string m, Exception? e = null) => Lines.Add("WARN " + m);
        public void Error(string m, Exception? e = null) => Lines.Add("ERROR " + m);
    }
}
