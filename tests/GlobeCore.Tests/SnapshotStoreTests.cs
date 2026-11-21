using GlobeCore.Stores;

namespace GlobeCore.Tests;

public sealed class SnapshotStoreTests
{
    private sealed record Counter(int N);

    [Fact]
    public void WriteRaisesChangedOnce()
    {
        var store = new SnapshotStore<Counter>(new Counter(0));
        var raised = 0;
        object? sender = null;
        store.Changed += (s, _) => { raised++; sender = s; };

        Assert.True(store.Write(c => c with { N = c.N + 1 }));

        Assert.Equal(1, store.Snapshot.N);
        Assert.Equal(1, raised);
        Assert.Same(store, sender);
    }

    [Fact]
    public async Task ChangedIsRaisedOnTheWritingThreadAfterTheSnapshotIsReplaced()
    {
        var store = new SnapshotStore<Counter>(new Counter(0));
        int? thread = null, seen = null;
        store.Changed += (_, _) => { thread = Environment.CurrentManagedThreadId; seen = store.Snapshot.N; };
        var writer = 0;
        await Task.Run(() => { writer = Environment.CurrentManagedThreadId; store.Write(c => c with { N = 5 }); });
        Assert.Equal(writer, thread);
        Assert.Equal(5, seen);
    }

    [Fact]
    public void NullWriteRaisesNothing()
    {
        var empty = new Counter(0);
        var store = new SnapshotStore<Counter>(empty);
        var raised = 0;
        store.Changed += (_, _) => raised++;

        Assert.False(store.Write(_ => null));
        Assert.False(store.Write(c => c));   // same reference: no change either

        Assert.Equal(0, raised);
        Assert.Same(empty, store.Snapshot);
    }

    [Fact]
    public void ConcurrentWritesAllApply()
    {
        var store = new SnapshotStore<Counter>(new Counter(0));
        var raised = 0;
        store.Changed += (_, _) => Interlocked.Increment(ref raised);

        Parallel.For(0, 100, _ => store.Write(c => c with { N = c.N + 1 }));

        Assert.Equal(100, store.Snapshot.N);
        Assert.Equal(100, raised);
    }
}
