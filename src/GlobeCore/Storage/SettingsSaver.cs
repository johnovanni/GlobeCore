using GlobeCore.Logging;

namespace GlobeCore.Storage;

/// <summary>
/// Debounced, off-thread front for a settings store. <see cref="Save"/> only records the value and restarts a
/// timer; the inner store is written on a thread-pool thread once no change has come for the delay (500 ms by
/// default). A failed write can sleep up to about 200 ms while it retries, so view models (on the UI thread) save
/// through this, never through the underlying store directly.
/// <see cref="Load"/> returns the latest value, saved or still pending, so read-modify-write callers stay consistent.
/// </summary>
public sealed class SettingsSaver<T> : ISettingsStore<T>, IDisposable where T : class
{
    public static readonly TimeSpan DefaultDelay = TimeSpan.FromMilliseconds(500);

    private readonly ISettingsStore<T> _inner;
    private readonly TimeSpan _delay;
    private readonly IAppLog _log;
    private readonly ITimer _timer;
    private readonly object _gate = new();
    private readonly object _writeGate = new();   // one inner write at a time
    private T _current;
    private T? _pending;
    private bool _disposed;

    /// <param name="time">Supplies the debounce timer; tests pass a manual one. Defaults to the system's.</param>
    public SettingsSaver(ISettingsStore<T> inner, TimeSpan? delay = null, IAppLog? log = null, TimeProvider? time = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _delay = delay ?? DefaultDelay;
        _log = log ?? new NullLog();
        _current = inner.Load();
        _timer = (time ?? TimeProvider.System).CreateTimer(_ => OnTimer(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public T Load()
    {
        lock (_gate) return _current;
    }

    public void Save(T s)
    {
        ArgumentNullException.ThrowIfNull(s);
        lock (_gate)
        {
            _current = s;
            _pending = s;
            if (!_disposed) _timer.Change(_delay, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>
    /// Writes the pending value now, on the calling thread (shutdown calls this). If the write throws, the value
    /// stays pending (unless a newer one arrived meanwhile), so the next save or flush tries again.
    /// </summary>
    public void Flush()
    {
        lock (_writeGate)
        {
            T? toWrite;
            lock (_gate) { toWrite = _pending; _pending = null; }
            if (toWrite is null) return;
            try { _inner.Save(toWrite); }
            catch
            {
                lock (_gate) _pending ??= toWrite;
                throw;
            }
        }
    }

    // A timer callback must not throw: that would end the process.
    private void OnTimer()
    {
        try { Flush(); }
        catch (Exception ex) { _log.Warn("Could not save settings", ex); }
    }

    /// <summary>Stops the timer. Call <see cref="Flush"/> first to keep a pending change.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        _timer.Dispose();
    }
}
