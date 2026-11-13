using UltraLightWallpaper.Core;
using Xunit;

namespace UltraLightWallpaper.Tests;

public sealed class DetectionTests
{
    private static readonly MonitorSnapshot A = new("A", new(200, 0, 2760, 1440), new(200, 0, 2760, 1392));
    private static readonly MonitorSnapshot B = new("B", new(-880, 139, 200, 2059), new(-880, 139, 200, 2011));
    private static WindowSnapshot Full(MonitorSnapshot monitor, long handle = 1) => new() { Handle = handle, Frame = monitor.Bounds, Client = monitor.Bounds, Foreground = true };
    [Theory] [InlineData(false)] [InlineData(true)]
    public void DetectsBorderlessOnEitherMonitor(bool second)
    {
        var result = WindowAnalyzer.Analyze([A, B], [Full(second ? B : A)], 2);
        Assert.Single(result.FullscreenWindows); Assert.Equal(second ? "B" : "A", result.FullscreenWindows[0].MonitorId);
    }
    [Fact] public void MultipleFullscreenAppsIncludingBackgroundMonitorAreRetained()
    {
        var result = WindowAnalyzer.Analyze([A, B], [Full(A), Full(B, 2) with { Foreground = false }], 2);
        Assert.Equal(2, result.FullscreenWindows.Count);
    }
    [Fact] public void MaximizedFramedBrowserIsCoverageButNotFullscreen()
    {
        var browser = Full(A) with { Maximized = true, Style = WindowStyles.Caption | WindowStyles.ThickFrame, Client = A.WorkArea };
        var result = WindowAnalyzer.Analyze([A, B], [browser], 2);
        Assert.Empty(result.FullscreenWindows); Assert.Equal(1, result.Coverage["A"]);
    }
    [Fact] public void BorderlessMaximizedWorkAreaDoesNotCount()
    {
        Assert.False(WindowAnalyzer.Analyze([A], [Full(A) with { Maximized = true, Frame = A.WorkArea, Client = A.WorkArea }], 2).AnyFullscreen);
    }
    [Fact] public void LargeWindowAndSpanningWindowAreNotFullscreen()
    {
        Assert.False(WindowAnalyzer.Analyze([A], [Full(A) with { Client = new(210, 10, 2750, 1430), Frame = new(210, 10, 2750, 1430) }], 2).AnyFullscreen);
        Assert.False(WindowAnalyzer.Analyze([A, B], [Full(A) with { Client = new(-880, 0, 2760, 2059), Frame = new(-880, 0, 2760, 2059) }], 2).AnyFullscreen);
    }
    [Theory] [InlineData("hidden")] [InlineData("minimized")] [InlineData("cloaked")] [InlineData("desktop")] [InlineData("transparent")] [InlineData("tool")] [InlineData("noactivate")] [InlineData("system")] [InlineData("wallpaper")] [InlineData("owner")] [InlineData("child")]
    public void NonApplicationsNeverTriggerFullscreen(string kind)
    {
        var window = kind switch
        {
            "hidden" => Full(A) with { Visible = false }, "minimized" => Full(A) with { Minimized = true },
            "cloaked" => Full(A) with { Cloaked = true }, "desktop" => Full(A) with { OnCurrentDesktop = false },
            "transparent" => Full(A) with { Opaque = false }, "tool" => Full(A) with { ExtendedStyle = WindowStyles.ToolWindow },
            "noactivate" => Full(A) with { ExtendedStyle = WindowStyles.NoActivate }, "system" => Full(A) with { SystemSurface = true },
            "wallpaper" => Full(A) with { Wallpaper = true }, "owner" => Full(A) with { Owned = true, ClassName = "#32770" },
            _ => Full(A) with { Style = WindowStyles.Child }
        };
        Assert.False(WindowAnalyzer.Analyze([A], [window], 2, true).AnyFullscreen);
    }
    [Fact] public void ClientGeometryPreventsSizeOnlyFalsePositive()
    {
        Assert.False(WindowAnalyzer.Analyze([A], [Full(A) with { Client = new(210, 35, 2750, 1430) }], 2).AnyFullscreen);
    }
    [Fact] public void OwnedFullscreenPlayerIsNotMistakenForTransientDialog()
    {
        var owned = Full(A) with { Owned = true, Foreground = false };
        Assert.True(WindowAnalyzer.Analyze([A], [owned], 2).AnyFullscreen);
        Assert.False(WindowAnalyzer.Analyze([A], [owned with { ExtendedStyle = 1 }], 2).AnyFullscreen);
    }
    [Fact] public void DpiToleranceIsAppliedInPhysicalPixels()
    {
        var highDpi = A with { Dpi = 192 }; var offset = new PixelRect(204, 4, 2756, 1436);
        Assert.True(WindowAnalyzer.Analyze([highDpi], [Full(A) with { Frame = offset, Client = offset }], 2).AnyFullscreen);
        Assert.False(WindowAnalyzer.Analyze([A], [Full(A) with { Frame = offset, Client = offset }], 2).AnyFullscreen);
    }
    [Fact] public void AltTabFullyObscuredGameDoesNotCountButOtherMonitorDoes()
    {
        var browser = Full(A, 3) with { Style = WindowStyles.Caption | WindowStyles.ThickFrame };
        var result = WindowAnalyzer.Analyze([A, B], [browser, Full(A) with { Foreground = false }, Full(B, 2) with { Foreground = false }], 2);
        Assert.Single(result.FullscreenWindows); Assert.Equal("B", result.FullscreenWindows[0].MonitorId);
    }
    [Fact] public void SmallTransientOverlayDoesNotCancelGame()
    {
        var overlay = new WindowSnapshot { Handle = 2, Frame = new(300, 100, 600, 300), ExtendedStyle = WindowStyles.ToolWindow };
        Assert.True(WindowAnalyzer.Analyze([A], [overlay, Full(A) with { Foreground = false }], 2).AnyFullscreen);
    }
    [Fact] public void ShellExclusiveEvidenceDoesNotRequireBorderlessStyle()
    {
        var game = Full(A) with { Style = WindowStyles.Caption, Client = A.WorkArea };
        Assert.True(WindowAnalyzer.Analyze([A], [game], 2, true).AnyFullscreen);
        Assert.False(WindowAnalyzer.Analyze([A], [game], 2, false).AnyFullscreen);
        Assert.False(WindowAnalyzer.Analyze([A], [game with { Foreground = false }], 2, true).AnyFullscreen);
    }
    [Fact] public void DisabledFullscreenStillCalculatesOcclusion()
    {
        var r = WindowAnalyzer.Analyze([A], [Full(A)], 2, true, false);
        Assert.False(r.AnyFullscreen); Assert.Equal(1, r.Coverage["A"]);
    }
}
