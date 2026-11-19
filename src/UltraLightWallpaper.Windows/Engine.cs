using System.Diagnostics;
using System.Text;
using LibVLCSharp.Shared;
using UltraLightWallpaper.Core;

namespace UltraLightWallpaper.Windows;

public sealed class Engine : IDisposable
{
    private readonly Control dispatcher;
    private readonly WindowInspector inspector = new();
    private readonly WindowEvents events = new();
    private readonly SuspensionController policy = new();
    private readonly System.Windows.Forms.Timer evaluateTimer = new() { Interval = 100 };
    private readonly System.Windows.Forms.Timer verifyTimer = new();
    private readonly System.Windows.Forms.Timer healthTimer = new() { Interval = 5000 };
    private readonly System.Windows.Forms.Timer resumeTimer = new();
    private readonly System.Windows.Forms.Timer wakeRecoveryTimer = new() { Interval = 1200 };
    private readonly System.Windows.Forms.Timer deviceProbeTimer = new() { Interval = 1000 };
    private readonly Dictionary<string, PlaybackSession> sessions = new();
    private readonly Dictionary<string, string> failures = new();
    private readonly Dictionary<string, int> retries = new();
    private readonly Dictionary<string, long> recoverPositions = new();
    private VideoBackend? backend;
    private bool backendInitializing;
    private string? backendError;
    private DesktopTopology? topology;
    private bool disposed, sessionInactive, wakeRecoveryPending, displayRecoveryArmed;
    private int displayRecoveryRetries;
    public AppSettings Settings { get; private set; }
    public IReadOnlyList<MonitorSnapshot> Monitors { get; private set; } = [];
    public IReadOnlyDictionary<string, PlaybackSession> Sessions => sessions;
    public DetectionSnapshot Detection { get; private set; } = new(new Dictionary<string, double>(), []);
    public SuspensionResult Decisions { get; private set; } = new([], null);
    public bool ManualPause { get; private set; }
    public double LastScanMs { get; private set; }
    public long EvaluationCount { get; private set; }
    public string ShellStatus { get; private set; } = "No active wallpapers";
    public string HardwareStatus => backend?.HardwareStatus ?? "Hardware decoder initializes when a video is assigned";
    public bool BackendInitializing => backendInitializing;
    public event Action? Changed;

    public Engine(Control dispatcher, AppSettings settings)
    {
        this.dispatcher = dispatcher; Settings = settings.Normalize();
        events.Changed += RequestEvaluation;
        // Do not rebuild during the transient display layout reported while hot-plug is settling.
        events.DisplaysChanged += ScheduleWakeRecovery;
        events.DevicesChanged += () =>
        {
            if (!deviceProbeTimer.Enabled && !sessionInactive) deviceProbeTimer.Start();
        };
        events.ShellRestarted += Restart;
        events.SessionChanged += inactive =>
        {
            bool wasInactive = sessionInactive;
            sessionInactive = inactive;
            if (inactive) { wakeRecoveryTimer.Stop(); wakeRecoveryPending = false; }
            else if (wasInactive) ScheduleWakeRecovery();
            Evaluate();
        };
        events.RecoveryRequested += ScheduleWakeRecovery;
        wakeRecoveryTimer.Tick += (_, _) =>
        {
            wakeRecoveryTimer.Stop();
            if (disposed || sessionInactive) return;
            try
            {
                var current = WindowInspector.GetMonitors();
                if (current.Count == 0)
                {
                    // Windows is still enumerating outputs. Avoid clearing the last known topology.
                    Log.Write("Display wake deferred: no active monitors detected yet.");
                    wakeRecoveryTimer.Interval = 2000;
                    wakeRecoveryTimer.Start();
                    return;
                }
                Monitors = current;
                Log.Write($"Rebuilding wallpaper output after session/display topology change ({current.Count} active monitor(s)).");
                displayRecoveryRetries = 0;
                displayRecoveryArmed = true;
                Restart(); // Recreate native D3D11 outputs and restore playback positions.
            }
            catch (Exception error)
            {
                Log.Write("Display wake recovery failed; will retry.", error);
                wakeRecoveryTimer.Interval = 3000;
                wakeRecoveryTimer.Start();
                return;
            }
            finally
            {
                // While a retry is waiting, leave all existing video sessions suspended.
                if (!wakeRecoveryTimer.Enabled) wakeRecoveryPending = false;
                Evaluate();
            }
        };
        deviceProbeTimer.Tick += (_, _) =>
        {
            deviceProbeTimer.Stop();
            if (disposed || sessionInactive) return;
            var current = WindowInspector.GetMonitors();
            if (!Monitors.SequenceEqual(current))
            {
                Log.Write("Monitor hot-plug detected via WM_DEVICECHANGE.");
                ScheduleWakeRecovery();
            }
        };
        evaluateTimer.Tick += (_, _) => { evaluateTimer.Stop(); Evaluate(); };
        resumeTimer.Tick += (_, _) => { resumeTimer.Stop(); Evaluate(); };
        verifyTimer.Tick += (_, _) => Evaluate();
        healthTimer.Tick += (_, _) => CheckHealth();
        RefreshMonitors();
    }
    public void RefreshMonitors()
    {
        if (disposed) return;
        var current = WindowInspector.GetMonitors();
        if (!Monitors.SequenceEqual(current) && sessions.Count > 0)
        {
            Monitors = current;
            ScheduleWakeRecovery();
            return;
        }
        Monitors = current;
        Reconcile(); Evaluate(); Changed?.Invoke();
    }
    public void ApplySettings(AppSettings settings)
    {
        Settings = settings.Normalize();
        failures.Clear(); retries.Clear();
        Reconcile(); Evaluate(); Changed?.Invoke();
    }
    public void SetManualPause(bool pause) { ManualPause = pause; Evaluate(); Changed?.Invoke(); }
    public string StatusFor(string id) => sessions.TryGetValue(id, out var session) ? session.Status
        : failures.GetValueOrDefault(id, backendInitializing ? "Préparation du moteur vidéo…" : "Aucun fond d’écran actif");
    private bool WantsVideo(MonitorSnapshot monitor) => Settings.Monitors.TryGetValue(monitor.Id, out var setting) && setting.Enabled && !string.IsNullOrEmpty(setting.VideoPath);
    private void Reconcile()
    {
        var wanted = Monitors.Where(WantsVideo).ToDictionary(m => m.Id);
        foreach (string id in sessions.Keys.ToArray())
        {
            if (!wanted.TryGetValue(id, out var monitor))
            {
                // Preserve the playhead while Windows temporarily removes a physical monitor.
                recoverPositions[id] = sessions[id].TimeMs;
                sessions[id].Dispose(); sessions.Remove(id); retries.Remove(id);
                continue;
            }
            var cfg = Settings.Monitors[id]; var old = sessions[id].Settings;
            if (cfg.VideoPath != old.VideoPath || cfg.Loop != old.Loop || cfg.StartSeconds != old.StartSeconds || cfg.EndSeconds != old.EndSeconds || !sessions[id].NativeWindowsValid)
            { sessions[id].Dispose(); sessions.Remove(id); }
            else if (topology is not null) sessions[id].UpdateLayout(monitor, cfg, topology);
        }
        if (wanted.Count > 0)
        {
            if (topology is null || !topology.IsValid) topology = DesktopHost.Discover(true);
            ShellStatus = topology is null ? "No verified background WorkerW. Wallpaper stays detached. See diagnostics." : topology.Strategy;
            if (backend is null && !backendInitializing && backendError is null) _ = InitializeBackendAsync();
            if (topology is not null && backend is not null)
            {
                foreach (var monitor in wanted.Values.Where(m => !sessions.ContainsKey(m.Id) && !failures.ContainsKey(m.Id)))
                {
                    try
                    {
                        // Open paused, then apply a complete monitor snapshot before allowing playback.
                        var session = new PlaybackSession(backend, dispatcher, topology, monitor, Settings.Monitors[monitor.Id], true, recoverPositions.GetValueOrDefault(monitor.Id));
                        session.Changed += OnSessionChanged;
                        sessions.Add(monitor.Id, session); recoverPositions.Remove(monitor.Id);
                    }
                    catch (Exception e) when (e is IOException or InvalidOperationException or System.ComponentModel.Win32Exception or VLCException or DllNotFoundException)
                    { failures[monitor.Id] = e.Message; Log.Write("Wallpaper initialization failed.", e); }
                }
            }
        }
        else { ShellStatus = "No active wallpapers"; topology = null; }
        verifyTimer.Interval = Settings.Suspension.VerificationIntervalMs;
        verifyTimer.Enabled = wanted.Count > 0 && Settings.Suspension.Enabled;
        // Still check for reconnects if every configured monitor is temporarily disconnected.
        healthTimer.Enabled = Settings.Monitors.Values.Any(s => s.Enabled && !string.IsNullOrWhiteSpace(s.VideoPath));
        if (wanted.Count == 0) { evaluateTimer.Stop(); resumeTimer.Stop(); }
    }
    private async Task InitializeBackendAsync()
    {
        backendInitializing = true;
        VideoBackend? initialized = null;
        string? error = null;
        try { initialized = await Task.Run(() => new VideoBackend()).ConfigureAwait(false); }
        catch (Exception e) { error = e.Message; Log.Write("Video engine initialization failed.", e); }
        if (disposed || dispatcher.IsDisposed || !dispatcher.IsHandleCreated) { initialized?.Dispose(); return; }
        try
        {
            dispatcher.BeginInvoke(() =>
            {
                if (disposed) { initialized?.Dispose(); return; }
                backendInitializing = false; backend = initialized; backendError = error;
                if (error is not null) foreach (var m in Monitors.Where(WantsVideo)) failures[m.Id] = error;
                Reconcile(); Evaluate(); Changed?.Invoke();
            });
        }
        catch (InvalidOperationException) when (disposed || dispatcher.IsDisposed || !dispatcher.IsHandleCreated) { initialized?.Dispose(); }
    }
    private void OnSessionChanged() => Changed?.Invoke();
    private void ScheduleWakeRecovery()
    {
        if (disposed || sessionInactive) return;
        // Coalesce unlock, WM_DISPLAYCHANGE and hot-plug events into one delayed rebuild.
        wakeRecoveryPending = true;
        if (!wakeRecoveryTimer.Enabled)
        {
            wakeRecoveryTimer.Interval = 1200;
            wakeRecoveryTimer.Start();
        }
        Evaluate(); // Keep stale D3D output paused while Windows configures the screens.
    }
    private void RequestEvaluation()
    {
        if (disposed || sessions.Count == 0 || !Settings.Suspension.Enabled || evaluateTimer.Enabled) return;
        evaluateTimer.Start(); // Leading coalescing: event storms cannot postpone evaluation indefinitely.
    }
    public void Evaluate()
    {
        if (disposed) return;
        var watch = Stopwatch.StartNew();
        var active = Monitors.Where(WantsVideo).Select(m => m.Id).ToArray();
        Detection = Settings.Suspension.Enabled && active.Length > 0
            ? inspector.Analyze(Monitors, Settings.Suspension, sessions.Values.Select(s => (nint)s.SurfaceHandle).ToHashSet())
            : new(new Dictionary<string, double>(), []);
        Decisions = policy.Evaluate(Monitors.Select(m => m.Id).ToArray(), Detection, Settings.Suspension, Environment.TickCount64, ManualPause,
            Settings.Monitors.Where(p => p.Value.Paused).Select(p => p.Key).ToHashSet(), sessionInactive || wakeRecoveryPending);
        foreach (var decision in Decisions.Decisions)
            if (sessions.TryGetValue(decision.MonitorId, out var session)) session.Suspend(decision.Suspended, decision.Reason);
        resumeTimer.Stop();
        if (Decisions.NextEvaluationMs is long due && active.Length > 0)
        { resumeTimer.Interval = (int)Math.Clamp(due - Environment.TickCount64, 1, 5000); resumeTimer.Start(); }
        LastScanMs = watch.Elapsed.TotalMilliseconds; EvaluationCount++;
    }
    private void CheckHealth()
    {
        // No decoding/health churn while the screen or session is unavailable.
        if (disposed || sessionInactive || wakeRecoveryPending) return;
        var current = WindowInspector.GetMonitors();
        if (!Monitors.SequenceEqual(current))
        {
            Log.Write("Monitor topology changed without a corresponding display notification.");
            ScheduleWakeRecovery();
            return;
        }
        // A disconnected assigned monitor has no player to check until its original ID returns.
        if (!Monitors.Any(WantsVideo)) return;
        if (topology is null || !topology.IsValid || sessions.Values.Any(s => !s.NativeWindowsValid))
        {
            Restart();
            return;
        }
        if (displayRecoveryArmed)
        {
            if (failures.Count == 0) displayRecoveryArmed = false;
            else if (displayRecoveryRetries++ < 3)
            {
                Log.Write($"Retrying wallpaper creation after monitor hot-plug ({displayRecoveryRetries}/3).");
                Restart();
                return;
            }
            else displayRecoveryArmed = false;
        }
        foreach (var item in sessions.Where(p => p.Value.Failed && p.Value.RetryableFailure).ToArray())
        {
            int count = retries.GetValueOrDefault(item.Key);
            if (count >= 3) continue;
            recoverPositions[item.Key] = item.Value.TimeMs;
            item.Value.Dispose(); sessions.Remove(item.Key); failures.Remove(item.Key); retries[item.Key] = count + 1;
            Log.Write($"Recovering playback after failure, attempt {count + 1}/3.");
        }
        Reconcile(); Evaluate();
    }
    public void Restart()
    {
        if (disposed) return;
        foreach (var item in sessions) { recoverPositions[item.Key] = item.Value.TimeMs; item.Value.Dispose(); }
        sessions.Clear(); failures.Clear(); retries.Clear(); topology = null; backendError = null;
        Reconcile(); Evaluate(); Changed?.Invoke();
    }
    public string Diagnostics()
    {
        using var process = Process.GetCurrentProcess();
        var b = new StringBuilder();
        b.AppendLine($"UltraLightWallpaper 1.1.0 | {DateTimeOffset.Now:O} | {Environment.OSVersion} | x64={Environment.Is64BitProcess}");
        b.AppendLine($"Shell: {ShellStatus}\nDecoder: {HardwareStatus}");
        b.AppendLine($"Mode={Settings.Suspension.Mode}; Auto={Settings.Suspension.Enabled}; Fullscreen={Settings.Suspension.FullscreenDetectionEnabled}; Manual={ManualPause}; SessionInactive={sessionInactive}; WakeRecoveryPending={wakeRecoveryPending}; DisplayRecoveryRetries={displayRecoveryRetries}");
        b.AppendLine($"Threshold={Settings.Suspension.OcclusionThreshold:P0}; Hysteresis={Settings.Suspension.Hysteresis:P0}; Resume={Settings.Suspension.ResumeDelayMs}ms; Verify={Settings.Suspension.VerificationIntervalMs}ms");
        b.AppendLine($"Private={process.PrivateMemorySize64 / 1048576d:F1} MiB; WorkingSet={process.WorkingSet64 / 1048576d:F1} MiB; Managed={GC.GetTotalMemory(false) / 1048576d:F1} MiB; Handles={process.HandleCount}; GDI={Native.GetGuiResources(process.Handle, 0)}; USER={Native.GetGuiResources(process.Handle, 1)}");
        b.AppendLine($"Evaluations={EvaluationCount}; last scan={LastScanMs:F3}ms; Players={sessions.Count}");
        foreach (var m in Monitors)
        {
            b.AppendLine($"\n{m.Name} {m.Bounds} dpi={m.Dpi} primary={m.Primary}\n  Id={m.Id}\n  Covered={Detection.Coverage.GetValueOrDefault(m.Id):P2}; {StatusFor(m.Id)}");
            if (sessions.TryGetValue(m.Id, out var s)) b.AppendLine($"  HWND=0x{s.SurfaceHandle:X}; {s.Metadata}; time={s.TimeMs}; range={s.Range}; state={s.PlayerState}; decoded={s.DecodedFrames}; displayed={s.DisplayedFrames}; lost={s.LostFrames}; reason={s.Reason}; voutStarts={s.VideoOutputStarts}; eofRecoveries={s.EndRecoveries}");
        }
        foreach (var f in Detection.FullscreenWindows) b.AppendLine($"Fullscreen HWND=0x{f.Handle:X} monitor={f.MonitorId}: {f.Evidence}");
        b.AppendLine("\nDesktop topology:\n" + DesktopHost.Describe());
        return b.ToString();
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        events.Dispose(); evaluateTimer.Dispose(); verifyTimer.Dispose(); resumeTimer.Dispose(); wakeRecoveryTimer.Dispose(); deviceProbeTimer.Dispose(); healthTimer.Dispose();
        foreach (var session in sessions.Values) session.Dispose(); sessions.Clear(); backend?.Dispose(); inspector.Dispose();
    }
}
