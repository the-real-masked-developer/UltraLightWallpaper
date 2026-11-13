using LibVLCSharp.Shared;
using System.Diagnostics;
using System.Globalization;
using UltraLightWallpaper.Core;

namespace UltraLightWallpaper.Windows;

public sealed class VideoBackend : IDisposable
{
    internal LibVLC Instance { get; }
    public string HardwareStatus { get; private set; } = "D3D11VA requested; awaiting decoder evidence";
    private readonly object logSync = new();
    private long errorWindow;
    private int errorsInWindow;
    private bool disposed;
    private readonly bool verbose = Environment.GetEnvironmentVariable("ULW_VERBOSE_NATIVE") == "1";
    public VideoBackend()
    {
        var startup = Stopwatch.StartNew();
        string nativePath = Path.Combine(AppContext.BaseDirectory, "libvlc", "win-x64");
        if (!File.Exists(Path.Combine(nativePath, "libvlc.dll"))) throw new FileNotFoundException("Bundled libVLC runtime is missing. Extract the entire release package.");
        if (Directory.EnumerateFiles(Path.Combine(nativePath, "plugins"), "*.dll", SearchOption.AllDirectories).Any(p => p.Length >= 260))
            throw new InvalidOperationException("The installation path is too long for libVLC 3 plugins. Move the complete application folder to a shorter path and restart.");
        LibVLCSharp.Shared.Core.Initialize(nativePath);
        var options = new List<string> { "--no-audio", "--no-spu", "--no-osd", "--no-video-title-show", "--no-snapshot-preview",
            "--avcodec-hw=d3d11va", "--vout=direct3d11", "--no-input-fast-seek", "--file-caching=300", "--verbose=2" };
        // VLC otherwise scans hundreds of plug-ins at every cold launch. Build once on the background initializer.
        if (!File.Exists(Path.Combine(nativePath, "plugins", "plugins.dat"))) options.Add("--reset-plugins-cache");
        Instance = new LibVLC(options.ToArray());
        Instance.Log += OnLog;
        Log.Write($"libVLC {Instance.Version}; Direct3D11 rendering / D3D11VA preferred; initialized in {startup.Elapsed.TotalMilliseconds:F0}ms.");
    }
    private void OnLog(object? sender, LogEventArgs e)
    {
        if (verbose) Log.Write($"libVLC [{e.Level}] {e.Module}: {e.Message}");
        if (e.Message.Contains("Using D3D11VA", StringComparison.OrdinalIgnoreCase) || e.Message.Contains("Using DXVA2", StringComparison.OrdinalIgnoreCase))
        {
            HardwareStatus = e.Message;
            Log.Write("Hardware decoder: " + e.Message);
        }
        else if (e.Level == LogLevel.Error && !verbose)
        {
            lock (logSync)
            {
                if (Environment.TickCount64 - errorWindow >= 5000) { errorWindow = Environment.TickCount64; errorsInWindow = 0; }
                if (errorsInWindow++ < 16) Log.Write($"libVLC {e.Module}: {e.Message}");
            }
        }
    }
    public void Dispose() { if (disposed) return; disposed = true; Instance.Log -= OnLog; Instance.Dispose(); }
}

/// <summary>One retained decoder/player per monitor, with nested clipped native windows.</summary>
public sealed class PlaybackSession : IDisposable
{
    private readonly Control dispatcher;
    private readonly WallpaperWindow surface;
    private readonly WallpaperWindow videoWindow;
    private readonly Media media;
    private readonly MediaPlayer player;
    private readonly Task<MediaParsedStatus> parseTask;
    private bool disposed;
    private bool desiredPause;
    private bool ready;
    private bool layoutReady;
    private int videoWidth, videoHeight;
    private readonly long restoreTime;
    private bool restored;
    private bool ended;
    private long durationMs;
    public PlaybackRange Range { get; private set; }
    public int EndRecoveries { get; private set; }
    public int VideoOutputStarts { get; private set; }
    public VLCState PlayerState => disposed ? VLCState.Stopped : player.State;
    public MonitorSnapshot Monitor { get; private set; }
    public MonitorSettings Settings { get; private set; }
    public string Status { get; private set; } = "Ouverture de la vidéo…";
    public string Metadata { get; private set; } = "Lecture des informations vidéo…";
    public bool Failed { get; private set; }
    public bool RetryableFailure { get; private set; } = true;
    public bool Suspended => desiredPause;
    public PauseReason Reason { get; private set; }
    public long TimeMs => disposed ? 0 : Math.Max(0, player.Time);
    public long LengthMs => disposed ? 0 : Math.Max(durationMs, Math.Max(0, player.Length));
    public int DecodedFrames => disposed ? 0 : media.Statistics.DecodedVideo;
    public int DisplayedFrames => disposed ? 0 : media.Statistics.DisplayedPictures;
    public int LostFrames => disposed ? 0 : media.Statistics.LostPictures;
    public long SurfaceHandle => surface.Handle.ToInt64();
    public bool NativeWindowsValid => Native.IsWindow(surface.Handle) && Native.IsWindow(videoWindow.Handle);
    public event Action? Changed;
    public PlaybackSession(VideoBackend backend, Control dispatcher, DesktopTopology topology, MonitorSnapshot monitor, MonitorSettings settings, bool paused, long restoreTimeMs = 0)
    {
        if (settings.VideoPath is null || !File.Exists(settings.VideoPath)) throw new FileNotFoundException("The assigned video is missing.", settings.VideoPath);
        Monitor = monitor; Settings = settings; this.dispatcher = dispatcher; desiredPause = paused; restoreTime = restoreTimeMs;
        if (!Native.GetClientRect((nint)topology.Parent, out var hostRect)) throw new InvalidOperationException("Desktop host no longer exists.");
        Native.MapWindowPoints((nint)topology.Parent, 0, ref hostRect, 2);
        if (hostRect.Pixels.Intersect(monitor.Bounds).Area != monitor.Bounds.Area)
            throw new InvalidOperationException("The verified desktop host does not cover this monitor. Refresh displays or restart Explorer.");
        var rect = new Native.Rect { Left = monitor.Bounds.Left, Top = monitor.Bounds.Top, Right = monitor.Bounds.Right, Bottom = monitor.Bounds.Bottom };
        Native.MapWindowPoints(0, (nint)topology.Parent, ref rect, 2);
        surface = new((nint)topology.Parent, rect.Pixels, false);
        try
        {
            videoWindow = new(surface.Handle, new(0, 0, monitor.Bounds.Width, monitor.Bounds.Height), true);
            media = new Media(backend.Instance, settings.VideoPath, FromType.FromPath);
            media.AddOption(":no-audio"); media.AddOption(":no-spu");
            player = new MediaPlayer(backend.Instance) { Hwnd = videoWindow.Handle, EnableMouseInput = false, EnableKeyInput = false, Mute = true };
            player.Playing += OnPlaying; player.Vout += OnVout; player.EncounteredError += OnError; player.EndReached += OnEnd;
            parseTask = media.Parse(MediaParseOptions.ParseLocal, 5000);
            _ = OpenAsync();
        }
        catch
        {
            disposed = true;
            player?.Dispose(); media?.Dispose(); videoWindow?.Dispose(); surface.Dispose(); throw;
        }
    }
    private async Task OpenAsync()
    {
        try
        {
            await parseTask.ConfigureAwait(false);
            Post(() =>
            {
                durationMs = Math.Max(0, media.Duration);
                if (!PlaybackRange.TryCreate(Settings, durationMs, out var range, out string error))
                { Fail(error, false); return; }
                Range = range;
                // libVLC 3's input-repeat is an unsigned count, range 0..65535. -1 means no repeat.
                // The input seeks internally to start-time, retaining the decoder and video output.
                media.AddOption(":input-repeat=" + (Settings.Loop ? "65535" : "0"));
                if (Range.StartMs > 0) media.AddOption(":start-time=" + (Range.StartMs / 1000d).ToString("0.###", CultureInfo.InvariantCulture));
                if (Settings.EndSeconds is not null && Range.EndMs is long end)
                    media.AddOption(":stop-time=" + (end / 1000d).ToString("0.###", CultureInfo.InvariantCulture));
                if (!player.Play(media)) Fail("Impossible d’ouvrir cette vidéo. Vérifiez le fichier et son codec.");
                Changed?.Invoke();
            });
        }
        catch (Exception e) { Post(() => Fail("Lecture des informations vidéo impossible : " + e.Message)); }
    }
    private void Fail(string message, bool retryable = true) { Failed = true; RetryableFailure = retryable; Status = message; Log.Write(message + " " + Settings.VideoPath); Changed?.Invoke(); }
    private void Post(Action action)
    {
        if (disposed || dispatcher.IsDisposed || !dispatcher.IsHandleCreated) return;
        try { dispatcher.BeginInvoke(() => { if (!disposed) action(); }); }
        catch (InvalidOperationException) when (dispatcher.IsDisposed || !dispatcher.IsHandleCreated) { /* Dispatcher shutdown; no native state is accessed. */ }
    }
    private void OnPlaying(object? sender, EventArgs e) => Post(() =>
    {
        ready = true;
        UpdateVideoGeometry();
        RestoreInitialPosition();
        if (Math.Abs(player.Rate - Settings.Speed) > .001f) player.SetRate(Settings.Speed);
        if (desiredPause) player.SetPause(true);
        UpdateStatus();
    });
    private void OnVout(object? sender, MediaPlayerVoutEventArgs e) => Post(() => { if (e.Count > 0) VideoOutputStarts++; UpdateVideoGeometry(); });
    private void OnError(object? sender, EventArgs e) => Post(() => { Failed = true; Status = "Playback failed. Check the file, codec or graphics driver."; Log.Write(Status + " " + Settings.VideoPath); Changed?.Invoke(); });
    private void OnEnd(object? sender, EventArgs e) => Post(() =>
    {
        if (player.State != VLCState.Ended || ended) return;
        ended = true;
        if (Settings.Loop && !desiredPause) RestartEnded();
        UpdateStatus();
    });
    private void RestartEnded()
    {
        if (!ended || desiredPause || !Settings.Loop || Failed) return;
        // Rare EOF recovery (including exhaustion of the native repeat count), always off the VLC callback thread.
        // A paused EOF remains pending until policy explicitly permits playback.
        player.Stop(); ready = false; ended = false; restored = true; EndRecoveries++;
        Log.Write($"Rearming ended video on {Monitor.Name}; recovery={EndRecoveries}.");
        if (!player.Play(media)) Fail("Impossible de relancer la vidéo.");
    }
    private void UpdateVideoGeometry()
    {
        uint width = 0, height = 0;
        if (!player.Size(0, ref width, ref height) || width == 0 || height == 0) return;
        videoWidth = (int)width; videoHeight = (int)height;
        var track = media.Tracks.FirstOrDefault(t => t.TrackType == TrackType.Video);
        if (track.TrackType == TrackType.Video && track.Data.Video.Width > 0)
        {
            var v = track.Data.Video;
            double sar = v.SarDen == 0 || v.SarNum == 0 ? 1 : (double)v.SarNum / v.SarDen;
            videoWidth = Math.Max(1, (int)Math.Round(v.Width * sar)); videoHeight = (int)v.Height;
            if (v.Orientation is VideoOrientation.LeftTop or VideoOrientation.LeftBottom or VideoOrientation.RightTop or VideoOrientation.RightBottom)
                (videoWidth, videoHeight) = (videoHeight, videoWidth);
            Metadata = $"{v.Width} × {v.Height}  ·  {(v.FrameRateDen == 0 ? 0 : (double)v.FrameRateNum / v.FrameRateDen):0.##} fps  ·  {TimeSpan.FromMilliseconds(LengthMs):hh\\:mm\\:ss}";
        }
        layoutReady = true; ApplyLayout();
        RestoreInitialPosition();
        Native.ShowWindow(surface.Handle, 4); // SW_SHOWNOACTIVATE, once geometry is known.
        Changed?.Invoke();
    }
    private void RestoreInitialPosition()
    {
        if (restored) return;
        if (restoreTime <= 0) { restored = true; return; }
        if (player.Length <= 0) return;
        player.Time = Range.ClampPosition(restoreTime);
        restored = true;
    }
    private void ApplyLayout()
    {
        if (!layoutReady) return;
        var placement = VideoGeometry.Place(videoWidth, videoHeight, Monitor.Bounds.Width, Monitor.Bounds.Height, Settings.Scaling, Settings.FocalX, Settings.FocalY);
        player.AspectRatio = Settings.Scaling == ScalingMode.Stretch ? $"{Monitor.Bounds.Width}:{Monitor.Bounds.Height}" : null;
        videoWindow.SetBounds(placement.X, placement.Y, placement.Width, placement.Height);
    }
    public void UpdateLayout(MonitorSnapshot monitor, MonitorSettings settings, DesktopTopology topology)
    {
        bool layoutChanged = Monitor.Bounds != monitor.Bounds || Settings.Scaling != settings.Scaling || Settings.FocalX != settings.FocalX || Settings.FocalY != settings.FocalY;
        bool rateChanged = Settings.Speed != settings.Speed;
        Monitor = monitor; Settings = settings;
        if (layoutChanged)
        {
            var rect = new Native.Rect { Left = monitor.Bounds.Left, Top = monitor.Bounds.Top, Right = monitor.Bounds.Right, Bottom = monitor.Bounds.Bottom };
            Native.MapWindowPoints(0, (nint)topology.Parent, ref rect, 2);
            surface.SetBounds(rect.Left, rect.Top, rect.Pixels.Width, rect.Pixels.Height);
            ApplyLayout();
        }
        if (rateChanged && ready) player.SetRate(settings.Speed);
    }
    public void Suspend(bool pause, PauseReason reason)
    {
        bool reasonChanged = Reason != reason;
        Reason = reason;
        if (desiredPause == pause) { if (reasonChanged) UpdateStatus(); return; }
        desiredPause = pause;
        if (!pause && ended) RestartEnded();
        else if (ready && !Failed && !ended) player.SetPause(pause); // Explicit state; retains decoder, HWND and last frame.
        UpdateStatus();
    }
    private void UpdateStatus()
    {
        if (!Failed) Status = ended ? (Settings.Loop ? "En pause · reprise de la boucle en attente" : "Vidéo terminée · boucle désactivée")
            : !ready ? "Ouverture de la vidéo…" : desiredPause ? $"En pause · {PauseDescription()}" : "En lecture";
        Changed?.Invoke();
    }
    private string PauseDescription()
    {
        if (Reason.HasFlag(PauseReason.SessionInactive)) return "session Windows inactive";
        if (Reason.HasFlag(PauseReason.Manual)) return "pause manuelle";
        if (Reason.HasFlag(PauseReason.MonitorPaused)) return "pause de cet écran";
        if (Reason.HasFlag(PauseReason.GlobalFullscreen)) return "application en plein écran";
        if (Reason.HasFlag(PauseReason.GlobalOcclusion)) return "un écran est couvert";
        if (Reason.HasFlag(PauseReason.ResumeDelay)) return "reprise dans un instant";
        return Reason.HasFlag(PauseReason.Occluded) ? "écran couvert" : "en attente";
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        player.Playing -= OnPlaying; player.Vout -= OnVout; player.EncounteredError -= OnError; player.EndReached -= OnEnd;
        player.Stop(); player.Hwnd = 0; player.Dispose(); videoWindow.Dispose(); surface.Dispose();
        if (parseTask.IsCompleted) media.Dispose();
        else { media.ParseStop(); _ = DisposeMediaAfterParseAsync(); }
    }
    private async Task DisposeMediaAfterParseAsync()
    {
        try { await parseTask.ConfigureAwait(false); }
        catch (Exception e) { Log.Write("Metadata parsing stopped during disposal.", e); }
        finally { media.Dispose(); }
    }
}
