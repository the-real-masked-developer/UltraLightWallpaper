using UltraLightWallpaper.Core;
using Xunit;

namespace UltraLightWallpaper.Tests;

public sealed class SuspensionTests
{
    private static DetectionSnapshot Detection(double a = 0, double b = 0, params string[] fullscreen) =>
        new(new Dictionary<string, double> { ["A"] = a, ["B"] = b }, fullscreen.Select((id, i) => new FullscreenMatch(i + 1, id, "test")).ToArray());
    private static SuspensionSettings Settings(SuspensionMode mode) => new() { Mode = mode, ResumeDelayMs = 600 };
    private static bool[] Paused(SuspensionResult r) => r.Decisions.Select(d => d.Suspended).ToArray();
    [Fact] public void DefaultIsIndependent()
    {
        Assert.Equal(SuspensionMode.IndependentMonitors, new SuspensionSettings().Mode);
        var c = new SuspensionController(); Assert.Equal([true, false], Paused(c.Evaluate(["A", "B"], Detection(.95, .3), new(), 0)));
    }
    [Theory] [InlineData("A")] [InlineData("B")]
    public void FullscreenEitherMonitorPausesAll(string id)
    {
        var c = new SuspensionController(); var r = c.Evaluate(["A", "B"], Detection(0, 0, id), Settings(SuspensionMode.GlobalFullscreen), 0);
        Assert.Equal([true, true], Paused(r)); Assert.All(r.Decisions, d => Assert.Equal(PauseReason.GlobalFullscreen, d.Reason));
    }
    [Fact] public void GlobalOcclusionUsesAnyMonitorAndNotFullscreen()
    {
        var c = new SuspensionController(); var settings = Settings(SuspensionMode.GlobalOcclusion);
        Assert.Equal([true, true], Paused(c.Evaluate(["A", "B"], Detection(0, .95), settings, 0)));
        var clear = new SuspensionController(); Assert.Equal([false, false], Paused(clear.Evaluate(["A", "B"], Detection(0, 0, "A"), settings, 0)));
    }
    [Fact] public void MaximizedAppInGamingModeOnlyPausesItsMonitor()
    {
        var c = new SuspensionController(); Assert.Equal([true, false], Paused(c.Evaluate(["A", "B"], Detection(1, 0), Settings(SuspensionMode.GlobalFullscreen), 0)));
    }
    [Fact] public void OnlyFinalFullscreenExitStartsResumeDelay()
    {
        var c = new SuspensionController(); var s = Settings(SuspensionMode.GlobalFullscreen);
        c.Evaluate(["A", "B"], Detection(0, 0, "A", "B"), s, 0);
        Assert.Null(c.Evaluate(["A", "B"], Detection(0, 0, "B"), s, 1000).NextEvaluationMs);
        var releasing = c.Evaluate(["A", "B"], Detection(), s, 2000); Assert.Equal(2600, releasing.NextEvaluationMs);
        Assert.Equal([true, true], Paused(c.Evaluate(["A", "B"], Detection(), s, 2599)));
        Assert.Equal([false, false], Paused(c.Evaluate(["A", "B"], Detection(), s, 2600)));
    }
    [Fact] public void RenewedFullscreenCancelsPendingResume()
    {
        var c = new SuspensionController(); var s = Settings(SuspensionMode.GlobalFullscreen);
        c.Evaluate(["A", "B"], Detection(0, 0, "A"), s, 0); c.Evaluate(["A", "B"], Detection(), s, 100);
        Assert.Null(c.Evaluate(["A", "B"], Detection(0, 0, "B"), s, 650).NextEvaluationMs);
        Assert.Equal([true, true], Paused(c.Evaluate(["A", "B"], Detection(0, 0, "B"), s, 800)));
    }
    [Fact] public void CoverageHysteresisPreventsOscillation()
    {
        var c = new SuspensionController(); var s = Settings(SuspensionMode.IndependentMonitors);
        c.Evaluate(["A", "B"], Detection(.95), s, 0);
        foreach (double coverage in new[] { .949, .951, .94, .93 }) Assert.True(c.Evaluate(["A", "B"], Detection(coverage), s, 1000).Decisions[0].Suspended);
        c.Evaluate(["A", "B"], Detection(.90), s, 2000);
        Assert.False(c.Evaluate(["A", "B"], Detection(.90), s, 2600).Decisions[0].Suspended);
    }
    [Fact] public void MonitorDisconnectClearsItsGlobalLatch()
    {
        var c = new SuspensionController(); var s = Settings(SuspensionMode.GlobalOcclusion);
        c.Evaluate(["A", "B"], Detection(0, 1), s, 0);
        c.Evaluate(["A"], Detection(), s, 100);
        Assert.Equal([false], Paused(c.Evaluate(["A"], Detection(), s, 700)));
    }
    [Fact] public void Exactly92PercentStartsDefaultHysteresisRelease()
    {
        var controller = new SuspensionController(); var settings = new SuspensionSettings();
        controller.Evaluate(["A"], Detection(.95), settings, 0);
        var clearing = controller.Evaluate(["A"], Detection(.92), settings, 10);
        Assert.Equal(660, clearing.NextEvaluationMs);
        Assert.False(controller.Evaluate(["A"], Detection(.92), settings, 660).Decisions[0].Suspended);
    }
    [Fact] public void MonitorReconnectJoinsExistingGlobalPause()
    {
        var c = new SuspensionController(); var s = Settings(SuspensionMode.GlobalFullscreen);
        c.Evaluate(["A"], Detection(0, 0, "A"), s, 0);
        Assert.Equal([true, true], Paused(c.Evaluate(["A", "B"], Detection(0, 0, "A"), s, 100)));
    }
    [Fact] public void SettingsAndDisableApplyImmediately()
    {
        var c = new SuspensionController(); var s = Settings(SuspensionMode.GlobalOcclusion);
        c.Evaluate(["A", "B"], Detection(1), s, 0);
        Assert.Equal([true, false], Paused(c.Evaluate(["A", "B"], Detection(1), s with { Mode = SuspensionMode.IndependentMonitors }, 1)));
        Assert.Equal([false, false], Paused(c.Evaluate(["A", "B"], Detection(1), s with { Enabled = false }, 2)));
    }
    [Fact] public void ManualAndPerMonitorPauseSurviveAutomaticResume()
    {
        var c = new SuspensionController(); var s = Settings(SuspensionMode.GlobalFullscreen);
        c.Evaluate(["A", "B"], Detection(0, 0, "A"), s, 0, true);
        c.Evaluate(["A", "B"], Detection(), s, 100, true);
        Assert.Equal([true, true], Paused(c.Evaluate(["A", "B"], Detection(), s, 1000, true)));
        Assert.Equal([false, true], Paused(c.Evaluate(["A", "B"], Detection(), s, 1001, false, new HashSet<string> { "B" })));
    }
    [Fact] public void ResumeAllCannotOverrideActiveFullscreen()
    {
        var c = new SuspensionController(); var s = Settings(SuspensionMode.GlobalFullscreen);
        Assert.Equal([true, true], Paused(c.Evaluate(["A", "B"], Detection(0, 0, "A"), s, 0, false)));
    }
    [Fact] public void SessionPauseWorksWithAutomaticDisabled()
    {
        Assert.All(new SuspensionController().Evaluate(["A", "B"], Detection(), new() { Enabled = false }, 0, sessionInactive: true).Decisions,
            d => Assert.Equal(PauseReason.SessionInactive, d.Reason));
    }
    [Fact] public void DisabledFullscreenFallsBackToIndependentCoverage()
    {
        var s = Settings(SuspensionMode.GlobalFullscreen) with { FullscreenDetectionEnabled = false };
        Assert.Equal([true, false], Paused(new SuspensionController().Evaluate(["A", "B"], Detection(1, 0, "A"), s, 0)));
    }
    [Fact] public void FullscreenHasPriorityThenReturnsToIndependentState()
    {
        var c = new SuspensionController(); var s = Settings(SuspensionMode.GlobalFullscreen);
        Assert.All(c.Evaluate(["A", "B"], Detection(1, 0, "B"), s, 0).Decisions, d => Assert.Equal(PauseReason.GlobalFullscreen, d.Reason));
        c.Evaluate(["A", "B"], Detection(1), s, 1);
        var resumed = c.Evaluate(["A", "B"], Detection(1), s, 601);
        Assert.Equal([true, false], Paused(resumed)); Assert.Equal(PauseReason.Occluded, resumed.Decisions[0].Reason);
    }
}
