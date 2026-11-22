using System.Windows.Media;
using GlobeCore.Feeds;
using GlobeCore.Wpf.Status;

namespace GlobeCore.Tests;

public class StatusDotsViewModelTests
{
    private static readonly DateTime T0 = new(2026, 11, 22, 14, 3, 0, DateTimeKind.Utc);

    private static FeedStatus S(string name, FeedState state, int failures = 0, DateTime? ok = null, string? error = null, string? note = null) =>
        new(name, state, ok, error, failures, T0.AddMinutes(2), note);

    [Fact]
    public void DotsStartGreyInTheGivenOrder()
    {
        var vm = new StatusDotsViewModel(["UCDP", "IPC"]);
        Assert.Equal(["UCDP", "IPC"], vm.Dots.Select(d => d.Name));
        Assert.All(vm.Dots, d => Assert.Equal(FeedState.NotStarted, d.State));
        Assert.Equal("UCDP: waiting for the first update", vm.Dots[0].Tooltip);
    }

    [Fact]
    public void UpdateReplacesTheNamedDotAndAddsUnknownOnes()
    {
        var vm = new StatusDotsViewModel(["UCDP", "IPC"]);
        vm.Update(S("IPC", FeedState.Ok, ok: T0));
        Assert.Equal(2, vm.Dots.Count);
        Assert.Equal("IPC: updated 14:03 UTC", vm.Dots[1].Tooltip);
        Assert.Equal(StatusDotsBrushes.Ok.Color, ((SolidColorBrush)vm.Dots[1].Brush).Color);
        vm.Update(S("WHO", FeedState.Ok, ok: T0));
        Assert.Equal(["UCDP", "IPC", "WHO"], vm.Dots.Select(d => d.Name));
    }

    [Fact]
    public void StatusDotsTurnRedAfterThreeFailures()
    {
        var vm = new StatusDotsViewModel(["UCDP"]);
        vm.Update(S("UCDP", FeedState.Retrying, 1, error: "timeout"));
        Assert.Equal(Color.FromRgb(0xFF, 0xCC, 0x00), ((SolidColorBrush)vm.Dots[0].Brush).Color);
        vm.Update(S("UCDP", FeedState.Retrying, 2, error: "timeout"));
        Assert.Equal(Color.FromRgb(0xFF, 0xCC, 0x00), ((SolidColorBrush)vm.Dots[0].Brush).Color);
        vm.Update(S("UCDP", FeedState.Failing, 3, error: "timeout"));
        Assert.Equal(Color.FromRgb(0xFF, 0x3B, 0x30), ((SolidColorBrush)vm.Dots[0].Brush).Color);
        Assert.Equal(FeedState.Failing, vm.Dots[0].State);
        Assert.Equal("UCDP: failing (timeout) – next try 14:05 UTC", vm.Dots[0].Tooltip);
    }

    [Fact]
    public void ThreeConsecutiveFailuresAreRedEvenIfTheStateSaysRetrying()
    {
        var vm = new StatusDotsViewModel(["UCDP"]);
        vm.Update(S("UCDP", FeedState.Retrying, 3, error: "timeout"));
        Assert.Equal(FeedState.Failing, vm.Dots[0].State);
        Assert.Equal(StatusDotsBrushes.Failing.Color, ((SolidColorBrush)vm.Dots[0].Brush).Color);
    }

    [Fact]
    public void PausedIsBlueWithTheNoteOnItsOwnLine()
    {
        var vm = new StatusDotsViewModel(["IPC"]);
        vm.Update(S("IPC", FeedState.Paused, note: "Daily download limit reached"));
        Assert.Equal(Color.FromRgb(0x0A, 0x84, 0xFF), ((SolidColorBrush)vm.Dots[0].Brush).Color);
        Assert.Equal("IPC: paused" + Environment.NewLine + "Daily download limit reached", vm.Dots[0].Tooltip);
    }

    [Fact]
    public void TooltipTextForEachState()
    {
        Assert.Equal("A: retrying (timeout) – next try 14:05 UTC; last updated 14:03 UTC",
            StatusDotsViewModel.TooltipFor(S("A", FeedState.Retrying, 1, T0, "timeout")));
        Assert.Equal("A: not set up", StatusDotsViewModel.TooltipFor(S("A", FeedState.NotSetUp)));
        Assert.Equal("A: idle; last updated 14:03 UTC", StatusDotsViewModel.TooltipFor(S("A", FeedState.Idle, ok: T0)));
        Assert.EndsWith("...) – next try 14:05 UTC",
            StatusDotsViewModel.TooltipFor(S("A", FeedState.Failing, 3, error: new string('x', 300))));
    }
}
