#if PERF_LOG
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace GlobeCore.Wpf.Diagnostics;

/// <summary>PERF_LOG builds only: appends timestamped lines to %TEMP%\GlobeCore-perf.log (or the name set by <see cref="Configure"/>).</summary>
internal static class PerfLog
{
    public static string FilePath { get; private set; } = Path.Combine(Path.GetTempPath(), "GlobeCore-perf.log");

    /// <summary>Names the log after the app: %TEMP%\&lt;appName&gt;-perf.log.</summary>
    public static void Configure(string appName) => FilePath = Path.Combine(Path.GetTempPath(), appName + "-perf.log");
    private static readonly object Gate = new();

    public static void Write(string message)
    {
        string line = string.Create(CultureInfo.InvariantCulture, $"{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss.fffZ} {message}{Environment.NewLine}");
        lock (Gate) File.AppendAllText(FilePath, line);
    }

    public static string Memory()
    {
        using var p = Process.GetCurrentProcess();
        return string.Create(CultureInfo.InvariantCulture,
            $"ws={p.WorkingSet64 / 1048576.0:F0} MB private={p.PrivateMemorySize64 / 1048576.0:F0} MB managed={GC.GetTotalMemory(false) / 1048576.0:F0} MB");
    }
}
#endif
