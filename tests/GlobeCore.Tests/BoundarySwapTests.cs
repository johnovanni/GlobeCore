using AstroCore.Earth;
using GlobeCore.Geo;
using GlobeCore.Regions;
using static GlobeCore.Tests.RegionTestData;

namespace GlobeCore.Tests;

public class BoundarySwapTests
{
    /// <summary>A set whose one region and one line both carry the generation number, so a torn read would show.</summary>
    private static BoundarySet Generation(int n)
    {
        var region = new Region($"R{n}", $"Region {n}", [], new GeoPosition(0, 0),
            [new GeoPolygon([Ring(0, 0, 10).Select(p => new GeoPosition(p.Lat, p.Lon)).ToList()])]);
        var line = new GeoLine([new GeoPosition(n % 80, 0), new GeoPosition(n % 80, 1)]);
        return new BoundarySet($"Edition {n}", new DateOnly(2020, 1, 1).AddDays(n), new RegionIndex([region]), [line]) { Tag = $"v{n}" };
    }

    [Fact]
    // Review focus 5: the feed thread swaps while readers pick and compose; each reader sees one whole set.
    public void SwapFromFeedThreadIsAtomic()
    {
        var current = new CurrentBoundaries(Generation(0));
        var seenInHandler = new List<(BoundarySet Old, BoundarySet New, BoundarySet Live)>();
        current.Changed += (_, e) => { lock (seenInHandler) seenInHandler.Add((e.Old, e.New, current.Set)); };

        const int swaps = 2000, readerCount = 4;
        int torn = 0, reads = 0, started = 0, stop = 0;
        // Dedicated threads, so a busy thread pool (other test classes run in parallel) cannot delay the readers.
        var readers = Enumerable.Range(0, readerCount).Select(_ => new Thread(() =>
        {
            Interlocked.Increment(ref started);
            while (Volatile.Read(ref stop) == 0)
            {
                var set = current.Set;   // once per operation
                int n = int.Parse(set.Tag[1..]);
                var region = set.Regions.Find(new GeoPosition(5, 5));
                if (region?.Id != $"R{n}" || set.Edition != $"Edition {n}" || set.Lines[0].Points[0].LatitudeDeg != n % 80) Interlocked.Increment(ref torn);
                Interlocked.Increment(ref reads);
            }
        })).ToList();
        readers.ForEach(t => t.Start());
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref started) == readerCount, TimeSpan.FromSeconds(30)));

        var feed = new Thread(() => { for (int i = 1; i <= swaps; i++) current.Swap(Generation(i)); });
        feed.Start();
        feed.Join();
        Volatile.Write(ref stop, 1);
        readers.ForEach(t => t.Join());

        Assert.Equal(0, torn);
        Assert.True(reads > 0);
        Assert.Equal($"v{swaps}", current.Set.Tag);
        // Changed is raised after each swap, in order, with the new set already live.
        Assert.Equal(swaps, seenInHandler.Count);
        for (int i = 0; i < swaps; i++)
        {
            Assert.Equal($"v{i}", seenInHandler[i].Old.Tag);
            Assert.Equal($"v{i + 1}", seenInHandler[i].New.Tag);
            Assert.Same(seenInHandler[i].New, seenInHandler[i].Live);
        }
    }

    [Fact]
    public void SwapReturnsTheOldSetAndRefusesNull()
    {
        var first = Generation(1);
        var current = new CurrentBoundaries(first);
        Assert.Same(first, current.Swap(Generation(2)));
        Assert.Throws<ArgumentNullException>(() => current.Swap(null!));
        Assert.Throws<ArgumentNullException>(() => new CurrentBoundaries(null!));
    }

    [Fact]
    public void DescribeNamesEditionAndRules()
    {
        var set = Rules(MovePeninsula).Apply(Sample());
        Assert.Equal("Test Atlas 1.0.0 (2020-01-02); Peninsula shown as part of Beta", set.Describe());
        Assert.Equal("Test Atlas 1.0.0 (2020-01-02)", BoundaryRules.None.Apply(Sample()).Describe());
    }

    [Fact]
    // Review item (b): one handler failing neither stops the others nor escapes Swap.
    public void AThrowingChangedHandlerDoesNotStopTheOthersOrEscape()
    {
        var log = new RecordingLog();
        var current = new CurrentBoundaries(Generation(0), log);
        var called = new List<string>();
        current.Changed += (_, _) => called.Add("first");
        current.Changed += (_, _) => throw new InvalidOperationException("handler broke");
        current.Changed += (_, _) => called.Add("third");

        var next = Generation(1);
        var old = current.Swap(next);   // does not throw

        Assert.Equal(["first", "third"], called);
        Assert.Same(next, current.Set);
        Assert.Equal("v0", old.Tag);
        Assert.Contains(log.Warnings, m => m.Contains("Changed handler", StringComparison.Ordinal));
    }
}
