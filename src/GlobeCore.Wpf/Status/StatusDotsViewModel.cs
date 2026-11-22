using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Media;
using System.Windows.Threading;
using GlobeCore.Feeds;

namespace GlobeCore.Wpf.Status;

/// <summary>One status dot. <see cref="Brush"/> is frozen.</summary>
public sealed record StatusDot(string Name, FeedState State, string Tooltip, Brush Brush);

/// <summary>Ok green, Retrying and Attention amber, Failing red, Paused blue, NotSetUp / NotStarted / Idle grey.</summary>
public static class StatusDotsBrushes
{
    public static readonly SolidColorBrush Ok = Frozen(0x34, 0xC7, 0x59);
    public static readonly SolidColorBrush Retrying = Frozen(0xFF, 0xCC, 0x00);
    public static readonly SolidColorBrush Attention = Retrying;
    public static readonly SolidColorBrush Failing = Frozen(0xFF, 0x3B, 0x30);
    public static readonly SolidColorBrush Paused = Frozen(0x0A, 0x84, 0xFF);
    public static readonly SolidColorBrush Idle = Frozen(0x8E, 0x8E, 0x93);

    public static SolidColorBrush For(FeedState s) => s switch
    {
        FeedState.Ok => Ok,
        FeedState.Retrying => Retrying,
        FeedState.Failing => Failing,
        FeedState.Attention => Attention,
        FeedState.Paused => Paused,
        _ => Idle,   // NotStarted, NotSetUp and Idle (not polled right now) are grey
    };

    private static SolidColorBrush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}

/// <summary>
/// One status dot per data source, in the order given (sources that first report later are added at the end).
/// Three consecutive failures show red (the scheduler reports <see cref="FeedState.Failing"/> at three; a
/// <see cref="FeedState.Retrying"/> status that already counts three is shown the same way).
/// </summary>
/// <param name="sourceNames">The dots to show from the start, grey until the source reports.</param>
/// <param name="dispatcher">When set, <see cref="Update"/> from another thread is queued onto it (BeginInvoke, never Invoke).</param>
public sealed class StatusDotsViewModel(IEnumerable<string> sourceNames, Dispatcher? dispatcher = null)
{
    private const int FailuresForRed = 3;

    public ObservableCollection<StatusDot> Dots { get; } = Initial(sourceNames);

    private static ObservableCollection<StatusDot> Initial(IEnumerable<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        var dots = new ObservableCollection<StatusDot>();
        foreach (var n in names.Distinct())
            dots.Add(Make(new FeedStatus(n, FeedState.NotStarted, null, null, 0, default)));
        return dots;
    }

    /// <summary>Adds or updates the source's dot. Safe from any thread when a dispatcher was given.</summary>
    public void Update(FeedStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(() => UpdateHere(status));
            return;
        }
        UpdateHere(status);
    }

    private void UpdateHere(FeedStatus status)
    {
        var dot = Make(status);
        for (int i = 0; i < Dots.Count; i++)
        {
            if (Dots[i].Name != status.Name) continue;
            if (Dots[i] != dot) Dots[i] = dot;
            return;
        }
        Dots.Add(dot);
    }

    private static StatusDot Make(FeedStatus s)
    {
        var state = s.State == FeedState.Retrying && s.ConsecutiveFailures >= FailuresForRed ? FeedState.Failing : s.State;
        return new StatusDot(s.Name, state, TooltipFor(s with { State = state }), StatusDotsBrushes.For(state));
    }

    public static string TooltipFor(FeedStatus s)
    {
        ArgumentNullException.ThrowIfNull(s);
        string lastOk = s.LastSuccessUtc is { } ok ? Utc(ok) : "";
        string error = string.IsNullOrWhiteSpace(s.LastError) ? "" : " (" + Shorten(s.LastError) + ")";
        var text = s.State switch
        {
            FeedState.Ok => s.Name + ": updated" + (lastOk.Length > 0 ? " " + lastOk : ""),
            FeedState.Retrying or FeedState.Failing =>
                s.Name + (s.State == FeedState.Retrying ? ": retrying" : ": failing") + error +
                " – next try " + Utc(s.NextRunUtc) + (lastOk.Length > 0 ? "; last updated " + lastOk : ""),
            FeedState.Attention => s.Name + ": needs attention" + (lastOk.Length > 0 ? "; last updated " + lastOk : ""),
            FeedState.NotSetUp => s.Name + ": not set up",
            FeedState.Paused => s.Name + ": paused",
            FeedState.Idle => s.Name + ": idle" + (lastOk.Length > 0 ? "; last updated " + lastOk : ""),
            _ => s.Name + ": waiting for the first update",
        };
        return string.IsNullOrWhiteSpace(s.Note) ? text : text + Environment.NewLine + s.Note;
    }

    private static string Utc(DateTime t) => t.ToString("HH:mm", CultureInfo.InvariantCulture) + " UTC";

    // Exception messages can be long (a URL, a stack of causes); a tooltip line should not be.
    private static string Shorten(string text)
    {
        var oneLine = text.ReplaceLineEndings(" ").Trim();
        return oneLine.Length <= 120 ? oneLine : oneLine[..117] + "...";
    }
}
