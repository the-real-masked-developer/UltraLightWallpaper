namespace UltraLightWallpaper.Core;

public sealed record MonitorSnapshot(string Id, PixelRect Bounds, PixelRect WorkArea, uint Dpi = 96, string Name = "Display", bool Primary = false);

public static class WindowStyles
{
    public const uint Child = 0x40000000, Caption = 0x00c00000, ThickFrame = 0x00040000;
    public const uint ToolWindow = 0x80, NoActivate = 0x08000000, Transparent = 0x20;
}

/// <summary>A single top-level window, in physical screen coordinates, sampled on the UI thread.</summary>
public sealed record WindowSnapshot
{
    public long Handle { get; init; }
    public uint ProcessId { get; init; }
    public string ClassName { get; init; } = "";
    public PixelRect Frame { get; init; }
    public PixelRect Client { get; init; }
    public uint Style { get; init; }
    public uint ExtendedStyle { get; init; }
    public bool Visible { get; init; } = true;
    public bool Minimized { get; init; }
    public bool Maximized { get; init; }
    public bool Cloaked { get; init; }
    public bool OnCurrentDesktop { get; init; } = true;
    public bool Foreground { get; init; }
    public bool Owned { get; init; }
    public bool Opaque { get; init; } = true;
    public bool SystemSurface { get; init; }
    public bool Wallpaper { get; init; }
    public bool Eligible => Visible && !Minimized && !Cloaked && OnCurrentDesktop && !SystemSurface && !Wallpaper && !Frame.IsEmpty && (Style & WindowStyles.Child) == 0;
    public bool Occludes => Eligible && Opaque;
}

public sealed record FullscreenMatch(long Handle, string MonitorId, string Evidence);
public sealed record DetectionSnapshot(IReadOnlyDictionary<string, double> Coverage, IReadOnlyList<FullscreenMatch> FullscreenWindows)
{
    public bool AnyFullscreen => FullscreenWindows.Count != 0;
}

public static class WindowAnalyzer
{
    /// <param name="windows">Visible desktop windows in top-to-bottom Z order.</param>
    /// <param name="exclusiveReported">Only QUNS_RUNNING_D3D_FULL_SCREEN, never BUSY or PRESENTATION_MODE.</param>
    public static DetectionSnapshot Analyze(IReadOnlyList<MonitorSnapshot> monitors, IReadOnlyList<WindowSnapshot> windows, int toleranceDip, bool exclusiveReported = false, bool detectFullscreen = true)
    {
        var opaque = windows.Where(w => w.Occludes).Select(w => w.Frame).ToArray();
        var fractions = monitors.ToDictionary(m => m.Id, m => Core.Coverage.Fraction(m.Bounds, opaque));
        var matches = new List<FullscreenMatch>();
        if (!detectFullscreen) return new(fractions, matches);
        var above = new List<PixelRect>();
        foreach (var w in windows)
        {
            // An owned popup can be the actual fullscreen video/game surface. Ownership
            // alone is not a reason to discard it; reject dialog and overlay traits instead.
            bool candidate = w.Eligible && w.Opaque && w.ClassName != "#32770" &&
                (w.ExtendedStyle & (WindowStyles.ToolWindow | WindowStyles.NoActivate | WindowStyles.Transparent | 0x1u)) == 0;
            if (candidate)
            {
                foreach (var m in monitors)
                {
                    int tolerance = (int)Math.Ceiling(Math.Clamp(toleranceDip, 0, 16) * m.Dpi / 96d);
                    var visiblePart = w.Frame.Intersect(m.Bounds);
                    // Alt+Tab: a fully obscured fullscreen window is not currently presented.
                    bool exposed = visiblePart.Area > 0 && Core.Coverage.UnionArea(visiblePart, above) < visiblePart.Area;
                    bool borderless = (w.Style & WindowStyles.Caption) == 0 &&
                        ((w.Style & WindowStyles.ThickFrame) == 0 || !w.Maximized) &&
                        w.Frame.Matches(m.Bounds, tolerance) && w.Client.Matches(m.Bounds, tolerance);
                    if (exposed && borderless) matches.Add(new(w.Handle, m.Id, w.Foreground ? "Foreground borderless fullscreen" : "Visible borderless fullscreen"));
                    else if (w.Foreground && exclusiveReported && visiblePart.Area > 0)
                        matches.Add(new(w.Handle, m.Id, "Shell reports exclusive Direct3D fullscreen"));
                }
            }
            if (w.Occludes) above.Add(w.Frame);
        }
        return new(fractions, matches);
    }
}
