using System.Diagnostics;
using System.Text.Json;
using UltraLightWallpaper.Core;
using UltraLightWallpaper.Windows;

namespace UltraLightWallpaper.Integration;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        if (args.Contains("--native-tests") || args.Contains("--playback") || args.Contains("--loop-tests") || args.Contains("--ui-tests"))
        {
            using var harness = new Harness(args); Application.Run(harness); return harness.Failures == 0 ? 0 : 1;
        }
        Console.WriteLine(DesktopHost.Describe());
        foreach (var monitor in WindowInspector.GetMonitors()) Console.WriteLine(monitor);
        Console.WriteLine("Verified host: " + (DesktopHost.Discover(args.Contains("--attach"))?.ToString() ?? "none"));
        return 0;
    }
}

internal sealed class Harness : ApplicationContext
{
    private readonly Control dispatcher = new();
    private readonly System.Windows.Forms.Timer kick = new() { Interval = 50 };
    private readonly List<object> results = [];
    private readonly List<object> samples = [];
    private readonly string[] args;
    private Engine? engine;
    private readonly List<Form> windows = [];
    public int Failures { get; private set; }
    public Harness(string[] args)
    {
        this.args = args; dispatcher.CreateControl();
        kick.Tick += async (_, _) => { kick.Stop(); await Run(); }; kick.Start();
    }
    private string? Arg(string flag) { int index = Array.IndexOf(args, flag); return index >= 0 && index + 1 < args.Length ? args[index + 1] : null; }
    private void Check(string name, bool condition, string? detail = null)
    {
        results.Add(new { name, passed = condition, detail });
        Console.WriteLine($"{(condition ? "PASS" : "FAIL")} {name} {detail}");
        if (!condition) Failures++;
    }
    private Form Window(MonitorSnapshot monitor, bool framed = false)
    {
        var form = new Form
        {
            Text = framed ? "UltraLightWallpaper test · Ordinary application" : "UltraLightWallpaper test · Borderless fullscreen",
            FormBorderStyle = framed ? FormBorderStyle.Sizable : FormBorderStyle.None,
            StartPosition = FormStartPosition.Manual,
            Bounds = new(monitor.Bounds.Left, monitor.Bounds.Top, monitor.Bounds.Width, monitor.Bounds.Height),
            BackColor = Color.FromArgb(25, 38, 61), ShowInTaskbar = true
        };
        form.Controls.Add(new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.White,
            Font = new("Segoe UI", 24), Text = "UltraLightWallpaper\nAutomated fullscreen acceptance test\nThis temporary window will close automatically." });
        windows.Add(form); form.Show(); if (framed) form.WindowState = FormWindowState.Maximized; return form;
    }
    private async Task Run()
    {
        string report = Arg("--report") ?? Path.GetFullPath("integration-report.json");
        try
        {
            Console.WriteLine($"Integration PID={Environment.ProcessId}");
            if (args.Contains("--native-tests")) await NativeTests();
            if (args.Contains("--ui-tests")) await LayoutTests();
            if (Arg("--loop-tests") is string loopVideo) await LoopTests(Path.GetFullPath(loopVideo), Arg("--second-video"));
            if (Arg("--playback") is string video) await PlaybackTests(Path.GetFullPath(video), report);
        }
        catch (Exception e) { Check("Integration harness", false, e.ToString()); }
        finally
        {
            string? diagnostic = engine?.Diagnostics();
            foreach (var window in windows) window.Dispose(); windows.Clear();
            engine?.Dispose(); dispatcher.Dispose(); kick.Dispose();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(report))!);
            File.WriteAllText(report, JsonSerializer.Serialize(new { timestamp = DateTimeOffset.Now, os = Environment.OSVersion.ToString(), processors = Environment.ProcessorCount, failures = Failures, results, samples, diagnostics = diagnostic }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"Report: {report}; failures={Failures}"); ExitThread();
        }
    }
    private async Task NativeTests()
    {
        var monitors = WindowInspector.GetMonitors();
        Check("Native monitor discovery", monitors.Count > 0);
        Check("Negative-coordinate portrait monitor", monitors.Any(m => m.Bounds.Left < 0 && m.Bounds.Height > m.Bounds.Width));
        using var inspector = new WindowInspector();
        var topology = DesktopHost.Discover(true);
        Check("Verified WorkerW below icon view", topology?.IsValid == true, topology?.Strategy);
        foreach (var monitor in monitors)
        {
            using var full = Window(monitor); await Task.Delay(350);
            DetectionSnapshot Capture() => WindowAnalyzer.Analyze(monitors, inspector.Capture().Where(w => w.Handle == full.Handle.ToInt64()).ToArray(), 2);
            Check($"Native borderless fullscreen {monitor.Bounds}", Capture().FullscreenWindows.Any(f => f.MonitorId == monitor.Id));
            full.WindowState = FormWindowState.Minimized; await Task.Delay(200);
            Check("Minimized fullscreen excluded", !Capture().AnyFullscreen);
            full.WindowState = FormWindowState.Normal; await Task.Delay(200);
            full.Hide(); await Task.Delay(200);
            Check("Invisible fullscreen excluded", !Capture().AnyFullscreen);
        }
        using (var maximized = Window(monitors[0], true))
        {
            await Task.Delay(300);
            var capture = inspector.Capture().Where(w => w.Handle == maximized.Handle.ToInt64()).ToArray();
            var analysis = WindowAnalyzer.Analyze(monitors, capture, 2);
            Check("Native maximized ordinary window not fullscreen", !analysis.AnyFullscreen);
            Check("Native maximized coverage reaches 95%", analysis.Coverage[monitors[0].Id] >= .95, analysis.Coverage[monitors[0].Id].ToString("P2"));
            var ids = monitors.Select(m => m.Id).ToArray();
            var global = new SuspensionController().Evaluate(ids, analysis, new() { Mode = SuspensionMode.GlobalOcclusion }, 0);
            Check("Native coverage drives global suspension", global.Decisions.All(d => d.Suspended));
            var local = new SuspensionController().Evaluate(ids, analysis, new(), 0);
            Check("Independent mode leaves other monitor playing", local.Decisions.Count(d => d.Suspended) == 1);
        }
        if (monitors.Count > 1)
        {
            using var first = Window(monitors[0]); using var second = Window(monitors[1]); await Task.Delay(300);
            var handles = new HashSet<long> { first.Handle.ToInt64(), second.Handle.ToInt64() };
            DetectionSnapshot Capture() => WindowAnalyzer.Analyze(monitors, inspector.Capture().Where(w => handles.Contains(w.Handle)).ToArray(), 2);
            Check("Two native fullscreen windows retained", Capture().FullscreenWindows.Count == 2);
            first.Close(); await Task.Delay(200); Check("Closing one keeps second fullscreen", Capture().FullscreenWindows.Count == 1);
            second.Close(); await Task.Delay(200); Check("Final close clears fullscreen", !Capture().AnyFullscreen);
        }
        foreach (var window in windows) window.Dispose(); windows.Clear();
    }
    private async Task PlaybackTests(string video, string report)
    {
        var monitors = WindowInspector.GetMonitors();
        var settings = new AppSettings { Suspension = new() { Enabled = false }, Monitors = monitors.ToDictionary(m => m.Id, _ => new MonitorSettings { VideoPath = video }) };
        engine = new Engine(dispatcher, settings);
        var startupDeadline = Stopwatch.StartNew();
        while (startupDeadline.Elapsed < TimeSpan.FromSeconds(20) &&
            (engine.Sessions.Count != monitors.Count || engine.Sessions.Values.Any(s => s.DecodedFrames < 10))) await Task.Delay(100);
        Check("Real video player created on every monitor", engine.Sessions.Count == monitors.Count, engine.Diagnostics());
        Check("Players have decoded video frames", engine.Sessions.Count > 0 && engine.Sessions.Values.All(s => s.DecodedFrames > 0));
        Check("Hardware decoder evidence", engine.HardwareStatus.Contains("Using D3D11VA", StringComparison.OrdinalIgnoreCase), engine.HardwareStatus);
        if (engine.Sessions.Count == 0 || engine.Sessions.Values.Any(s => s.Failed || s.DecodedFrames == 0)) return;
        var handles = engine.Sessions.ToDictionary(p => p.Key, p => p.Value.SurfaceHandle);
        int seconds = int.TryParse(Arg("--phase-seconds"), out int value) ? Math.Clamp(value, 5, 600) : 15;
        await Measure("playing", seconds, report);
        engine.SetManualPause(true); await Task.Delay(1500);
        var stopped = engine.Sessions.ToDictionary(p => p.Key, p => (p.Value.DecodedFrames, p.Value.TimeMs));
        await Measure("suspended", seconds, report);
        Check("Suspension stops sustained video decoding", engine.Sessions.All(p => p.Value.DecodedFrames <= stopped[p.Key].DecodedFrames + 2));
        Check("Suspension preserves media position", engine.Sessions.All(p => Math.Abs(p.Value.TimeMs - stopped[p.Key].TimeMs) < 100));
        Check("Pause preserves native surfaces", engine.Sessions.All(p => p.Value.SurfaceHandle == handles[p.Key]));
        engine.SetManualPause(false); await Task.Delay(1000);
        await Measure("resumed", seconds, report);
        Check("Resume retains player surfaces", engine.Sessions.All(p => p.Value.SurfaceHandle == handles[p.Key]));
        Check("Resume decodes more frames", engine.Sessions.All(p => p.Value.DecodedFrames > stopped[p.Key].DecodedFrames + 30));
        engine.ApplySettings(settings with { Suspension = new() { Mode = SuspensionMode.GlobalFullscreen, ResumeDelayMs = 500 } });
        foreach (var monitor in monitors)
        {
            using var full = Window(monitor); long evaluations = engine.EvaluationCount;
            await Task.Delay(450);
            Check("WinEvent triggers prompt reevaluation", engine.EvaluationCount > evaluations);
            Check($"Fullscreen on {monitor.Bounds} suspends every real player", engine.Sessions.Values.All(s => s.Suspended && s.Reason.HasFlag(PauseReason.GlobalFullscreen)));
            full.Close(); await Task.Delay(1200);
            Check("Closing fullscreen restores normal policy", engine.Sessions.Values.All(s => !s.Reason.HasFlag(PauseReason.GlobalFullscreen) && !s.Reason.HasFlag(PauseReason.ResumeDelay)));
        }
        if (monitors.Count > 1)
        {
            using var first = Window(monitors[0]); using var second = Window(monitors[1]); await Task.Delay(400);
            Check("Multiple fullscreen windows suspend real players", engine.Sessions.Values.All(s => s.Reason.HasFlag(PauseReason.GlobalFullscreen)));
            first.Close(); await Task.Delay(700);
            Check("Second fullscreen keeps every real player paused", engine.Sessions.Values.All(s => s.Reason.HasFlag(PauseReason.GlobalFullscreen)));
            second.Close(); await Task.Delay(1300);
            Check("Final fullscreen clears global reason", engine.Sessions.Values.All(s => !s.Reason.HasFlag(PauseReason.GlobalFullscreen)));
        }
        Check("Fullscreen transitions never recreate surfaces", engine.Sessions.All(p => p.Value.SurfaceHandle == handles[p.Key]));
        engine.ApplySettings(settings); await Task.Delay(500);
        Check("Disabling automatic suspension immediately resumes", engine.Sessions.Values.All(s => !s.Suspended));
        File.WriteAllText(Path.ChangeExtension(report, ".diagnostics.txt"), engine.Diagnostics());
    }
    private async Task LayoutTests()
    {
        using var emptyEngine = new Engine(dispatcher, new());
        using var form = new SettingsForm(emptyEngine, emptyEngine.ApplySettings);
        windows.Add(form); form.Show();
        static IEnumerable<Control> Descendants(Control c) => c.Controls.Cast<Control>().SelectMany(x => new[] { x }.Concat(Descendants(x)));
        foreach (var size in new[] { new Size(640, 560), new Size(800, 650), new Size(1200, 900) })
        {
            form.Size = size; await Task.Delay(250);
            foreach (string title in new[] { "Fonds d’écran", "Performances", "Application", "Diagnostic" })
            {
                var nav = Descendants(form).OfType<Button>().Single(b => b.Text == title); nav.PerformClick(); await Task.Delay(100);
                var overflow = Descendants(form).Where(c => c.Visible && c.Parent is not null && c is Button or ComboBox or NumericUpDown)
                    .Where(c => c.Right > c.Parent!.ClientSize.Width + 2 || c.Left < 0).Select(c => c.Text + ":" + c.Bounds).ToArray();
                Check($"Responsive {size.Width}x{size.Height} · {title}", overflow.Length == 0, string.Join(", ", overflow));
                Check($"No horizontal scrolling {size.Width} · {title}", !Descendants(form).OfType<ScrollableControl>().Any(c => c.Visible && c.HorizontalScroll.Visible));
            }
        }
        form.Size = new(1020, 850); Descendants(form).OfType<Button>().Single(b => b.Text == "Fonds d’écran").PerformClick();
        if (int.TryParse(Arg("--preview-seconds"), out int seconds)) await Task.Delay(Math.Clamp(seconds, 1, 600) * 1000);
        form.CloseForExit(); windows.Remove(form);
    }
    private async Task WaitForPlayers(int count)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(30) && (engine!.Sessions.Count != count || engine.Sessions.Values.Any(s => s.DecodedFrames < 10))) await Task.Delay(100);
        Check("All monitors decode", engine!.Sessions.Count == count && engine.Sessions.Values.All(s => !s.Failed && s.DecodedFrames > 0), engine.Diagnostics());
    }
    private async Task ObserveWraps(string name, int minimumWraps, TimeSpan timeout)
    {
        var initial = engine!.Sessions.ToDictionary(p => p.Key, p => (Session: p.Value, p.Value.SurfaceHandle, p.Value.VideoOutputStarts, p.Value.EndRecoveries));
        var previous = engine.Sessions.ToDictionary(p => p.Key, p => p.Value.TimeMs);
        var wraps = previous.Keys.ToDictionary(k => k, _ => 0);
        var framesAtWrap = previous.Keys.ToDictionary(k => k, _ => 0);
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < timeout && (wraps.Values.Any(v => v < minimumWraps) || engine.Sessions.Any(p => p.Value.DisplayedFrames < framesAtWrap[p.Key] + 12)))
        {
            await Task.Delay(100);
            foreach (var (id, session) in engine.Sessions)
            {
                long current = session.TimeMs;
                if (previous[id] - current > 750 && current >= session.Range.StartMs - 150)
                {
                    wraps[id]++; framesAtWrap[id] = session.DisplayedFrames;
                    Console.WriteLine($"WRAP {name} {session.Monitor.Name} #{wraps[id]} {previous[id]} -> {current}; displayed={session.DisplayedFrames}");
                    samples.Add(new { phase = name, monitor = session.Monitor.Name, wrap = wraps[id], previous = previous[id], current, displayed = session.DisplayedFrames });
                }
                previous[id] = current;
            }
        }
        foreach (var (id, s) in engine.Sessions)
        {
            Check($"{name}: wraps on {s.Monitor.Name}", wraps[id] >= minimumWraps, $"Observed {wraps[id]}; time={s.TimeMs}; {s.PlayerState}");
            Check($"{name}: frames continue after final wrap {s.Monitor.Name}", s.DisplayedFrames >= framesAtWrap[id] + 12);
            Check($"{name}: retained player, decoder and surface {s.Monitor.Name}", ReferenceEquals(s, initial[id].Session) && s.SurfaceHandle == initial[id].SurfaceHandle && s.VideoOutputStarts == initial[id].VideoOutputStarts && s.EndRecoveries == initial[id].EndRecoveries);
        }
    }
    private async Task LoopTests(string firstVideo, string? secondVideo)
    {
        var monitors = WindowInspector.GetMonitors();
        var settings = new AppSettings { Suspension = new() { Enabled = false }, Monitors = monitors.Select((m, i) => (m, i)).ToDictionary(p => p.m.Id,
            p => new MonitorSettings { VideoPath = p.i == 0 && secondVideo is not null ? secondVideo : firstVideo, Loop = true, StartSeconds = 2.125, EndSeconds = 5.625, Speed = p.m.Primary ? 1.5f : 1.2f }) };
        engine = new Engine(dispatcher, settings); await WaitForPlayers(monitors.Count);
        if (engine.Sessions.Count != monitors.Count || engine.Sessions.Values.Any(s => s.Failed || s.DecodedFrames == 0)) return;
        await ObserveWraps("Trimmed segment", 4, TimeSpan.FromSeconds(24));
        engine.SetManualPause(true); await Task.Delay(1000);
        var paused = engine.Sessions.ToDictionary(p => p.Key, p => (p.Value.TimeMs, p.Value.DecodedFrames)); await Task.Delay(4000);
        Check("Segment stays suspended across several loop durations", engine.Sessions.All(p => Math.Abs(p.Value.TimeMs - paused[p.Key].TimeMs) < 100 && p.Value.DecodedFrames <= paused[p.Key].DecodedFrames + 2));
        engine.SetManualPause(false); await ObserveWraps("Resumed segment", 2, TimeSpan.FromSeconds(15));
        settings = settings with { Monitors = settings.Monitors.ToDictionary(p => p.Key, p => p.Value with { Loop = false }) };
        engine.ApplySettings(settings); await WaitForPlayers(monitors.Count); await Task.Delay(6000);
        Check("Loop disabled stops at segment end", engine.Sessions.Values.All(s => s.PlayerState == LibVLCSharp.Shared.VLCState.Ended && s.TimeMs < 6000 && s.EndRecoveries == 0));
        engine.Evaluate(); Check("Ended status remains accurate after policy evaluation", engine.Sessions.Values.All(s => s.Status.Contains("terminée")));
        // Exercise real EOF cheaply by retaining only the last seconds of each original file.
        settings = settings with { Monitors = settings.Monitors.ToDictionary(p => p.Key, p => p.Value with { Loop = true, StartSeconds = Math.Max(0, engine.Sessions[p.Key].LengthMs / 1000d - 3.5), EndSeconds = null }) };
        engine.ApplySettings(settings); await WaitForPlayers(monitors.Count); await ObserveWraps("Natural end of both original files", 3, TimeSpan.FromSeconds(24));
        if (args.Contains("--full-duration"))
        {
            settings = settings with { Monitors = settings.Monitors.ToDictionary(p => p.Key, p => p.Value with { StartSeconds = 0, EndSeconds = null }) };
            engine.ApplySettings(settings); await WaitForPlayers(monitors.Count);
            double longest = engine.Sessions.Values.Max(s => s.LengthMs / 1000d / s.Settings.Speed);
            await ObserveWraps("Full original files", 2, TimeSpan.FromSeconds(longest * 2 + 20));
        }
        engine.Dispose(); engine = null;
    }
    private async Task Measure(string phase, int seconds, string report)
    {
        File.WriteAllText(Path.ChangeExtension(report, ".phase.txt"), $"{Environment.ProcessId} {phase}");
        using var process = Process.GetCurrentProcess();
        var before = process.TotalProcessorTime; var watch = Stopwatch.StartNew();
        for (int i = 0; i < seconds; i++)
        {
            await Task.Delay(1000); process.Refresh();
            samples.Add(new { phase, elapsed = watch.Elapsed.TotalSeconds, privateMiB = process.PrivateMemorySize64 / 1048576d, workingMiB = process.WorkingSet64 / 1048576d, handles = process.HandleCount,
                decoded = engine!.Sessions.Values.Sum(s => s.DecodedFrames), displayed = engine.Sessions.Values.Sum(s => s.DisplayedFrames), lost = engine.Sessions.Values.Sum(s => s.LostFrames) });
        }
        double cpu = (process.TotalProcessorTime - before).TotalSeconds / watch.Elapsed.TotalSeconds / Environment.ProcessorCount * 100;
        Console.WriteLine($"MEASURE {phase}: {watch.Elapsed.TotalSeconds:F1}s CPU={cpu:F3}% total capacity; private={process.PrivateMemorySize64 / 1048576d:F1} MiB");
        results.Add(new { name = "Performance " + phase, seconds = watch.Elapsed.TotalSeconds, cpuPercentTotalCapacity = cpu, privateMiB = process.PrivateMemorySize64 / 1048576d });
    }
}
