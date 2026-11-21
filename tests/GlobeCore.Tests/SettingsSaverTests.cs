using GlobeCore.Storage;

namespace GlobeCore.Tests;

public class SettingsSaverTests
{
    private static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(500);

    [Fact]
    public void SavesAreDebouncedAndEachSaveRestartsTheDelay()
    {
        var inner = new FakeSettingsStore();
        var time = new ManualTimeProvider();
        using var saver = new SettingsSaver<TestSettings>(inner, Delay, time: time);
        saver.Save(new TestSettings { NightStrength = 0.1 });
        saver.Save(new TestSettings { NightStrength = 0.2 });
        Assert.Equal(0, inner.SaveCount);               // nothing written synchronously
        Assert.Equal(0.2, saver.Load().NightStrength);  // but Load sees the pending value

        time.Advance(TimeSpan.FromMilliseconds(400));
        saver.Save(new TestSettings { NightStrength = 0.3 });
        time.Advance(TimeSpan.FromMilliseconds(400));   // 800 ms since the first save, 400 since the last
        Assert.Equal(0, inner.SaveCount);

        time.Advance(TimeSpan.FromMilliseconds(100));
        Assert.Equal(1, inner.SaveCount);
        Assert.Equal(0.3, inner.Saved[0].NightStrength);
        time.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(1, inner.SaveCount);
    }

    [Fact]
    public void TimerSaveRunsOffTheCallingThread()
    {
        var inner = new FakeSettingsStore();
        using var saver = new SettingsSaver<TestSettings>(inner, TimeSpan.FromMilliseconds(50));
        saver.Save(new TestSettings { NightStrength = 0.3 });
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (inner.SaveCount == 0 && DateTime.UtcNow < deadline) Thread.Sleep(10);
        Assert.Equal(1, inner.SaveCount);
        Assert.NotEqual(Environment.CurrentManagedThreadId, inner.SaveThreadIds[0]);
    }

    [Fact]
    public void FailedTimerSaveIsLoggedAndKeptForRetry()
    {
        var inner = new FakeSettingsStore { FailuresToThrow = 1 };
        var time = new ManualTimeProvider();
        var log = new RecordingLog();
        using var saver = new SettingsSaver<TestSettings>(inner, Delay, log, time);
        saver.Save(new TestSettings { MinMagnitude = 6 });
        time.Advance(Delay);   // the write throws inside the timer callback: logged, not thrown
        Assert.Equal(0, inner.SaveCount);
        Assert.Single(log.Warnings);

        saver.Flush();   // still pending, so this retries
        Assert.Equal(1, inner.SaveCount);
        Assert.Equal(6, inner.Saved[0].MinMagnitude);
    }

    [Fact]
    public void FlushWritesPendingOnceAndLoadComesFromInner()
    {
        var inner = new FakeSettingsStore(new TestSettings { MinMagnitude = 5 });
        using var saver = new SettingsSaver<TestSettings>(inner, TimeSpan.FromSeconds(30), time: new ManualTimeProvider());
        Assert.Equal(5, saver.Load().MinMagnitude);
        saver.Flush();
        Assert.Equal(0, inner.SaveCount);   // nothing pending
        saver.Save(new TestSettings { MinMagnitude = 6 });
        saver.Flush();
        Assert.Equal(1, inner.SaveCount);
        Assert.Equal(6, inner.Saved[0].MinMagnitude);
        saver.Flush();
        Assert.Equal(1, inner.SaveCount);
    }
}
