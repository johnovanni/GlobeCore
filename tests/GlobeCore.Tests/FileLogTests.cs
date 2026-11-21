using System.IO;
using GlobeCore.Logging;
using GlobeCore.Time;

namespace GlobeCore.Tests;

public sealed class FileLogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ew3d-log-" + Guid.NewGuid().ToString("N"));

    public FileLogTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private sealed class FixedClock(DateTime t) : IClock { public DateTime UtcNow => t; }

    [Fact]
    public void TwoPrefixesInOneFolderDoNotPruneEachOther()
    {
        var now = new DateTime(2026, 11, 12, 14, 3, 5, 123, DateTimeKind.Utc);
        var oldA = Path.Combine(_dir, "appa-20261101.log");
        var oldB = Path.Combine(_dir, "appb-20261101.log");
        File.WriteAllText(oldA, "x"); File.SetLastWriteTimeUtc(oldA, now.AddDays(-8));
        File.WriteAllText(oldB, "x"); File.SetLastWriteTimeUtc(oldB, now.AddDays(-8));

        using (var log = new FileLog(_dir, new FixedClock(now), "appa-")) log.Info("hello");

        Assert.False(File.Exists(oldA));   // its own old file is pruned
        Assert.True(File.Exists(oldB));    // the other app's is not
        var today = Directory.GetFiles(_dir, "appa-*.log").Single(f => f != oldA);
        Assert.Contains("hello", File.ReadAllText(today));
        Assert.Empty(Directory.GetFiles(_dir, "earthwatch3d-*.log"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void EmptyPrefixIsRejected(string? prefix)
        => Assert.ThrowsAny<ArgumentException>(() => new FileLog(_dir, new FixedClock(DateTime.UtcNow), prefix!));

    [Fact]
    public void FileLogWritesAndPrunesOldFiles()
    {
        var now = new DateTime(2026, 11, 12, 14, 3, 5, 123, DateTimeKind.Utc);
        var old = Path.Combine(_dir, "earthwatch3d-20261101.log");
        var recent = Path.Combine(_dir, "earthwatch3d-20261110.log");
        var other = Path.Combine(_dir, "other.log");
        File.WriteAllText(old, "x"); File.SetLastWriteTimeUtc(old, now.AddDays(-8));
        File.WriteAllText(recent, "x"); File.SetLastWriteTimeUtc(recent, now.AddDays(-2));
        File.WriteAllText(other, "x"); File.SetLastWriteTimeUtc(other, now.AddDays(-30));

        using (var log = new FileLog(_dir, new FixedClock(now), "earthwatch3d-"))
        {
            log.Info("hello");
            log.Warn("careful", new InvalidOperationException("boom"));
            log.Error("bad");
        }

        Assert.False(File.Exists(old));
        Assert.True(File.Exists(recent));
        Assert.True(File.Exists(other));

        var file = Directory.GetFiles(_dir, "earthwatch3d-*.log").Single(f => f != recent);
        Assert.Equal($"earthwatch3d-{now.ToLocalTime():yyyyMMdd}.log", Path.GetFileName(file));
        var lines = File.ReadAllLines(file);
        Assert.Equal(3, lines.Length);
        Assert.Equal("2026-11-12T14:03:05.123Z INFO hello", lines[0]);
        Assert.StartsWith("2026-11-12T14:03:05.123Z WARN careful | ", lines[1]);
        Assert.Contains("boom", lines[1]);
        Assert.StartsWith("2026-11-12T14:03:05.123Z ERROR bad", lines[2]);
    }

    [Fact]
    public async Task ConcurrentWritesDoNotInterleave()
    {
        var now = new DateTime(2026, 11, 12, 14, 0, 0, DateTimeKind.Utc);
        using (var log = new FileLog(_dir, new FixedClock(now), "earthwatch3d-"))
            await Task.WhenAll(Enumerable.Range(0, 8).Select(t => Task.Run(() =>
            {
                for (var i = 0; i < 50; i++) log.Info($"t{t} line {i}");
            })));
        var file = Directory.GetFiles(_dir, "earthwatch3d-*.log").Single();
        var lines = File.ReadAllLines(file);
        Assert.Equal(400, lines.Length);
        Assert.All(lines, l => Assert.Matches(@"^\S+Z INFO t\d line \d+$", l));
    }

    private sealed class MovingClock(DateTime t) : IClock { public DateTime UtcNow { get; set; } = t; }

    [Fact]
    public void DailyCapStopsWritingWithOneNote()
    {
        var clock = new MovingClock(new DateTime(2026, 11, 12, 12, 0, 0, DateTimeKind.Utc));
        using var log = new FileLog(_dir, clock, "earthwatch3d-", maxBytesPerDay: 2000);
        for (int i = 0; i < 100; i++) log.Info($"line {i} " + new string('x', 40));
        var file = Directory.GetFiles(_dir, "earthwatch3d-*.log").Single();
        var lines = File.ReadAllLines(file);
        Assert.True(new FileInfo(file).Length <= 2000 + 200, "the file stays near the cap");
        Assert.Single(lines, l => l.Contains("log cap reached"));
        Assert.EndsWith("log cap reached (2000 bytes); further lines today are dropped", lines[^1]);
        Assert.DoesNotContain(lines, l => l.Contains("line 99 "));

        clock.UtcNow = clock.UtcNow.AddDays(1);   // a new day, a new file and a new allowance
        log.Info("next day");
        Assert.Equal(2, Directory.GetFiles(_dir, "earthwatch3d-*.log").Length);
    }

    [Fact]
    public void CapCountsWhatIsAlreadyInTodaysFile()
    {
        var now = new DateTime(2026, 11, 12, 12, 0, 0, DateTimeKind.Utc);
        var file = Path.Combine(_dir, $"earthwatch3d-{now.ToLocalTime():yyyyMMdd}.log");
        File.WriteAllText(file, new string('y', 1990) + Environment.NewLine);
        using (var log = new FileLog(_dir, new FixedClock(now), "earthwatch3d-", maxBytesPerDay: 2000)) log.Info("does not fit");
        var lines = File.ReadAllLines(file);
        Assert.DoesNotContain(lines, l => l.Contains("does not fit"));
        Assert.Contains(lines, l => l.Contains("log cap reached"));
    }
}
