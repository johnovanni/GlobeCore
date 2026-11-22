namespace GlobeCore.Regions;

/// <summary>The set before and after a <see cref="CurrentBoundaries.Swap"/>.</summary>
public sealed class BoundariesChangedEventArgs(BoundarySet old, BoundarySet @new) : EventArgs
{
    public BoundarySet Old { get; } = old;
    public BoundarySet New { get; } = @new;
}

/// <summary>
/// The live boundary set of an app. Readers (composing, picking, resolving names) take <see cref="Set"/> once per
/// operation and use only that reference, so a swap never mixes two editions; a <see cref="BoundarySet"/> is immutable.
/// </summary>
public sealed class CurrentBoundaries
{
    private readonly object _swapLock = new();
    private BoundarySet _set;

    public CurrentBoundaries(BoundarySet initial) => _set = initial ?? throw new ArgumentNullException(nameof(initial));

    /// <summary>The set in use now.</summary>
    public BoundarySet Set => Volatile.Read(ref _set);

    /// <summary>
    /// Raised on the swapping thread (usually the feed's), after the swap, so <see cref="Set"/> is already the new set.
    /// Swaps are serialised, so handlers see them in order; a handler marshals to the UI itself and must not wait for
    /// another thread that swaps.
    /// </summary>
    public event EventHandler<BoundariesChangedEventArgs>? Changed;

    /// <summary>Makes <paramref name="next"/> the live set and raises <see cref="Changed"/>. Returns the old set.</summary>
    public BoundarySet Swap(BoundarySet next)
    {
        ArgumentNullException.ThrowIfNull(next);
        lock (_swapLock)
        {
            var old = Interlocked.Exchange(ref _set, next);
            Changed?.Invoke(this, new BoundariesChangedEventArgs(old, next));
            return old;
        }
    }
}
