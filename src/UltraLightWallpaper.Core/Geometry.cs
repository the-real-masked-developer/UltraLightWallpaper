namespace UltraLightWallpaper.Core;

/// <summary>Physical screen pixels. Left and top may be negative.</summary>
public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Math.Max(0, Right - Left);
    public int Height => Math.Max(0, Bottom - Top);
    public long Area => (long)Width * Height;
    public bool IsEmpty => Width == 0 || Height == 0;
    public PixelRect Intersect(PixelRect other) => new(Math.Max(Left, other.Left), Math.Max(Top, other.Top), Math.Min(Right, other.Right), Math.Min(Bottom, other.Bottom));
    public bool Matches(PixelRect other, int tolerance) =>
        Math.Abs((long)Left - other.Left) <= tolerance && Math.Abs((long)Top - other.Top) <= tolerance &&
        Math.Abs((long)Right - other.Right) <= tolerance && Math.Abs((long)Bottom - other.Bottom) <= tolerance;
}

public static class Coverage
{
    /// <summary>Exact clipped rectangle union; overlapping windows are counted once.</summary>
    public static long UnionArea(PixelRect surface, IEnumerable<PixelRect> rectangles)
    {
        if (surface.IsEmpty) return 0;
        var clipped = rectangles.Select(r => r.Intersect(surface)).Where(r => !r.IsEmpty).ToArray();
        if (clipped.Length == 0) return 0;
        var xs = clipped.SelectMany(r => new[] { r.Left, r.Right }).Distinct().Order().ToArray();
        long total = 0;
        for (int i = 1; i < xs.Length; i++)
        {
            var spans = clipped.Where(r => r.Left < xs[i] && r.Right > xs[i - 1]).OrderBy(r => r.Top);
            int start = 0, end = 0;
            long length = 0;
            bool first = true;
            foreach (var r in spans)
            {
                if (first) { start = r.Top; end = r.Bottom; first = false; }
                else if (r.Top > end) { length += (long)end - start; start = r.Top; end = r.Bottom; }
                else end = Math.Max(end, r.Bottom);
            }
            if (!first) length += (long)end - start;
            total += length * ((long)xs[i] - xs[i - 1]);
        }
        return total;
    }

    public static double Fraction(PixelRect surface, IEnumerable<PixelRect> rectangles) => surface.Area == 0 ? 0 : (double)UnionArea(surface, rectangles) / surface.Area;
}

public enum ScalingMode { Cover, Contain, Stretch }
public readonly record struct VideoPlacement(int X, int Y, int Width, int Height);

public static class VideoGeometry
{
    /// <summary>Positions a GPU-rendered child surface inside a clipping monitor window.</summary>
    public static VideoPlacement Place(int videoWidth, int videoHeight, int targetWidth, int targetHeight, ScalingMode mode, double focalX = .5, double focalY = .5)
    {
        if (videoWidth <= 0 || videoHeight <= 0 || targetWidth <= 0 || targetHeight <= 0) throw new ArgumentOutOfRangeException(nameof(videoWidth));
        if (mode == ScalingMode.Stretch) return new(0, 0, targetWidth, targetHeight);
        double ratio = mode == ScalingMode.Cover
            ? Math.Max((double)targetWidth / videoWidth, (double)targetHeight / videoHeight)
            : Math.Min((double)targetWidth / videoWidth, (double)targetHeight / videoHeight);
        int width = Math.Max(1, (int)(mode == ScalingMode.Cover ? Math.Ceiling(videoWidth * ratio) : Math.Floor(videoWidth * ratio)));
        int height = Math.Max(1, (int)(mode == ScalingMode.Cover ? Math.Ceiling(videoHeight * ratio) : Math.Floor(videoHeight * ratio)));
        double x = mode == ScalingMode.Contain ? .5 : Math.Clamp(double.IsFinite(focalX) ? focalX : .5, 0, 1);
        double y = mode == ScalingMode.Contain ? .5 : Math.Clamp(double.IsFinite(focalY) ? focalY : .5, 0, 1);
        return new((int)Math.Round((targetWidth - width) * x), (int)Math.Round((targetHeight - height) * y), width, height);
    }
}
