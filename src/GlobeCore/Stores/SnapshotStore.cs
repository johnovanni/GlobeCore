namespace GlobeCore.Stores;

/// <summary>
/// Holds one immutable snapshot that writers replace whole. Readers take <see cref="Snapshot"/> without locking;
/// writers are serialised, and <see cref="Changed"/> is raised on the writing thread after the replacement.
/// </summary>
public sealed class SnapshotStore<T>(T empty) where T : class
{
    private readonly object _gate = new();
    private volatile T _snapshot = empty;

    public T Snapshot => _snapshot;

    /// <summary>Raised on the writing thread after <see cref="Snapshot"/> has been replaced.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Applies <paramref name="change"/> to the current snapshot under the write lock. A <c>null</c> result, or the
    /// same reference, means no change: nothing is replaced and no event is raised. Returns whether it changed.
    /// </summary>
    public bool Write(Func<T, T?> change)
    {
        lock (_gate)
        {
            var before = _snapshot;
            var after = change(before);
            if (after is null || ReferenceEquals(after, before)) return false;
            _snapshot = after;
        }
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }
}
