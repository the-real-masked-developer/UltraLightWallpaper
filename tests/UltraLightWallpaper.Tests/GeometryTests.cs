using UltraLightWallpaper.Core;
using Xunit;

namespace UltraLightWallpaper.Tests;

public sealed class GeometryTests
{
    [Fact] public void PortraitCoverPreservesAspectAndFillsEveryPixel()
    {
        var p = VideoGeometry.Place(2560, 1440, 1080, 1920, ScalingMode.Cover);
        Assert.Equal(1920, p.Height); Assert.Equal(3414, p.Width); Assert.Equal(-1167, p.X); Assert.Equal(0, p.Y);
        Assert.InRange((double)p.Width / p.Height, 2560d / 1440, 2560d / 1440 + .001);
    }
    [Theory]
    [InlineData(0, 0)] [InlineData(.5, -1167)] [InlineData(1, -2334)]
    public void FocalPositionMovesCrop(double focus, int expected) => Assert.Equal(expected, VideoGeometry.Place(2560, 1440, 1080, 1920, ScalingMode.Cover, focus).X);
    [Fact] public void ContainCentersAndStretchFills()
    {
        Assert.Equal(new VideoPlacement(0, 656, 1080, 607), VideoGeometry.Place(2560, 1440, 1080, 1920, ScalingMode.Contain));
        Assert.Equal(new VideoPlacement(0, 0, 1080, 1920), VideoGeometry.Place(2560, 1440, 1080, 1920, ScalingMode.Stretch));
    }
    [Fact] public void ArbitraryAspectRatiosNeverUnderfillCover()
    {
        foreach (var (w, h) in new[] { (1, 1000), (1920, 1080), (3840, 2160), (1440, 2560), (123, 879), (2048, 858) })
        foreach (var (tw, th) in new[] { (1080, 1920), (2560, 1440), (1440, 2560) })
        {
            var p = VideoGeometry.Place(w, h, tw, th, ScalingMode.Cover);
            Assert.True(p.Width >= tw && p.Height >= th); Assert.True(p.X <= 0 && p.Y <= 0);
            Assert.True(p.X + p.Width >= tw && p.Y + p.Height >= th);
        }
    }
    [Fact] public void UnionClipsAndDoesNotDoubleCount()
    {
        var surface = new PixelRect(-100, 0, 0, 100);
        Assert.Equal(10000, Coverage.UnionArea(surface, [new(-120, -10, -40, 100), new(-60, 0, 20, 120)]));
        Assert.Equal(6000, Coverage.UnionArea(surface, [new(-100, 0, -40, 100), new(-100, 0, -40, 100)]));
        Assert.Equal(0, Coverage.UnionArea(surface, [new(0, 0, 100, 100)]));
    }
    [Fact] public void LargeAreasUse64Bits() => Assert.Equal(10_000_000_000, Coverage.UnionArea(new(-50000, -50000, 50000, 50000), [new(-50000, -50000, 50000, 50000)]));
    [Fact] public void SweepMatchesIndependentPixelOracle()
    {
        var random = new Random(917);
        for (int iteration = 0; iteration < 200; iteration++)
        {
            var rectangles = Enumerable.Range(0, 12).Select(_ => { int x = random.Next(-15, 15), y = random.Next(-15, 15); return new PixelRect(x, y, x + random.Next(1, 15), y + random.Next(1, 15)); }).ToArray();
            int expected = 0;
            for (int y = -10; y < 10; y++) for (int x = -10; x < 10; x++)
                if (rectangles.Any(r => r.Left <= x && r.Right > x && r.Top <= y && r.Bottom > y)) expected++;
            Assert.Equal(expected, Coverage.UnionArea(new(-10, -10, 10, 10), rectangles));
        }
    }
    [Fact] public void EmptySurfaceAndInvalidVideoAreHandled()
    {
        Assert.Equal(0, Coverage.Fraction(default, [new(0, 0, 100, 100)]));
        Assert.Throws<ArgumentOutOfRangeException>(() => VideoGeometry.Place(0, 100, 100, 100, ScalingMode.Cover));
    }
}
