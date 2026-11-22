#if PERF_LOG
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace GlobeCore.Wpf.Diagnostics;

/// <summary>
/// Performance log, compiled only when <c>PERF_LOG</c> is defined (build with
/// <c>"-p:DefineConstants=TRACE%3BRELEASE%3BPERF_LOG"</c>; the global property reaches GlobeCore's projects too, so
/// guard calls with <c>#if PERF_LOG</c>). Appends timestamped lines (UTC, invariant culture) to
/// %TEMP%\GlobeCore-perf.log, or to %TEMP%\&lt;appName&gt;-perf.log once the app calls <see cref="Configure"/>.
/// GlobeCore writes its own lines here (the layer host's compose timings); an app can write its lines through
/// <see cref="Write"/> too, so the whole run is in one file with one writer. Thread-safe.
/// </summary>
public static class PerfLog
{
    private static readonly object Gate = new();
    private static string _filePath = Path.Combine(Path.GetTempPath(), "GlobeCore-perf.log");

    /// <summary>The file lines are appended to.</summary>
    public static string FilePath { get { lock (Gate) return _filePath; } }

    /// <summary>
    /// Names the log after the app: %TEMP%\&lt;appName&gt;-perf.log. Call it at start-up, before anything is logged;
    /// lines written before it went to the previous file.
    /// </summary>
    public static void Configure(string appName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appName);
        lock (Gate) _filePath = Path.Combine(Path.GetTempPath(), appName + "-perf.log");
    }

    /// <summary>Appends "&lt;UTC time&gt; &lt;message&gt;" as one line.</summary>
    public static void Write(string message)
    {
        string line = string.Create(CultureInfo.InvariantCulture, $"{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss.fffZ} {message}{Environment.NewLine}");
        lock (Gate) File.AppendAllText(_filePath, line);
    }

    /// <summary>"ws=… MB private=… MB managed=… MB": this process's working set, private bytes and managed heap.</summary>
    public static string Memory()
    {
        using var p = Process.GetCurrentProcess();
        return string.Create(CultureInfo.InvariantCulture,
            $"ws={p.WorkingSet64 / 1048576.0:F0} MB private={p.PrivateMemorySize64 / 1048576.0:F0} MB managed={GC.GetTotalMemory(false) / 1048576.0:F0} MB");
    }
}
#endif
