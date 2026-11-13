namespace UltraLightWallpaper.Core;

[Flags]
public enum PauseReason { None = 0, Manual = 1, MonitorPaused = 2, Occluded = 4, GlobalFullscreen = 8, GlobalOcclusion = 16, ResumeDelay = 32, SessionInactive = 64 }
public sealed record PlaybackDecision(string MonitorId, bool Suspended, PauseReason Reason);
public sealed record SuspensionResult(IReadOnlyList<PlaybackDecision> Decisions, long? NextEvaluationMs);

/// <summary>Pure policy engine. Call with monotonic milliseconds. It never owns a player or a timer.</summary>
public sealed class SuspensionController
{
    private sealed class Gate
    {
        public bool Paused;
        public long? ClearSince;
        public bool Update(bool pause, long now, int delay)
        {
            if (pause) { Paused = true; ClearSince = null; }
            else if (Paused)
            {
                ClearSince ??= now;
                if (now - ClearSince.Value >= delay) { Paused = false; ClearSince = null; }
            }
            return Paused;
        }
    }
    private readonly Dictionary<string, bool> covered = new();
    private readonly Dictionary<string, Gate> independent = new();
    private Gate global = new();
    private SuspensionSettings? previous;
    private PauseReason lastGlobalReason;

    public SuspensionResult Evaluate(IReadOnlyList<string> monitorIds, DetectionSnapshot detection, SuspensionSettings preferences, long nowMs,
        bool manualPause = false, IReadOnlySet<string>? pausedMonitors = null, bool sessionInactive = false)
    {
        var settings = preferences.Normalize();
        // A setting change is authoritative immediately; discard old mode/delay state.
        if (previous != settings) { covered.Clear(); independent.Clear(); global = new(); lastGlobalReason = PauseReason.None; previous = settings; }
        var connected = monitorIds.ToHashSet();
        foreach (string old in covered.Keys.Where(k => !connected.Contains(k)).ToArray()) { covered.Remove(old); independent.Remove(old); }
        foreach (string id in monitorIds)
        {
            double fraction = detection.Coverage.GetValueOrDefault(id);
            if (!double.IsFinite(fraction)) fraction = 0;
            bool wasCovered = covered.GetValueOrDefault(id);
            covered[id] = fraction >= settings.OcclusionThreshold ||
                (wasCovered && fraction > Math.Max(0, settings.OcclusionThreshold - settings.Hysteresis) + 1e-9);
            if (!independent.ContainsKey(id)) independent[id] = new();
        }

        bool globalTarget = settings.Enabled && (settings.Mode switch
        {
            SuspensionMode.GlobalFullscreen => settings.FullscreenDetectionEnabled && detection.AnyFullscreen,
            SuspensionMode.GlobalOcclusion => covered.Values.Any(c => c),
            _ => false
        });
        if (globalTarget) lastGlobalReason = settings.Mode == SuspensionMode.GlobalFullscreen ? PauseReason.GlobalFullscreen : PauseReason.GlobalOcclusion;
        bool globalPause = global.Update(globalTarget, nowMs, settings.ResumeDelayMs);
        var decisions = new List<PlaybackDecision>(monitorIds.Count);
        long? next = global.ClearSince is long globalSince ? globalSince + settings.ResumeDelayMs : null;
        foreach (string id in monitorIds)
        {
            PauseReason reason = PauseReason.None;
            if (manualPause) reason |= PauseReason.Manual;
            if (pausedMonitors?.Contains(id) == true) reason |= PauseReason.MonitorPaused;
            if (sessionInactive) reason |= PauseReason.SessionInactive;
            // Fullscreen mode falls back to independent coverage after the last fullscreen clears.
            bool localTarget = settings.Enabled && settings.Mode != SuspensionMode.GlobalOcclusion && covered[id];
            bool localPause = independent[id].Update(localTarget, nowMs, settings.ResumeDelayMs);
            if (settings.Enabled)
            {
                if (globalPause) reason |= globalTarget ? lastGlobalReason : PauseReason.ResumeDelay;
                else if (localPause) reason |= localTarget ? PauseReason.Occluded : PauseReason.ResumeDelay;
            }
            if (independent[id].ClearSince is long since)
                next = next is long n ? Math.Min(n, since + settings.ResumeDelayMs) : since + settings.ResumeDelayMs;
            decisions.Add(new(id, reason != PauseReason.None, reason));
        }
        return new(decisions, next);
    }
}
