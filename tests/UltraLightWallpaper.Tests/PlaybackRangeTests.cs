using UltraLightWallpaper.Core;
using Xunit;

namespace UltraLightWallpaper.Tests;

public sealed class PlaybackRangeTests
{
    [Theory]
    [InlineData(2.125, 5.625, 10000, true, 2125, 5625)]
    [InlineData(0, null, 64640, true, 0, 64640)]
    [InlineData(2, null, 208880, true, 2000, 208880)]
    [InlineData(5, 4d, 10000, false, 0, 0)]
    [InlineData(1, 1.05, 10000, false, 0, 0)]
    [InlineData(10, null, 10000, false, 0, 0)]
    [InlineData(0, 11d, 10000, false, 0, 0)]
    [InlineData(2, 5d, 0, false, 0, 0)]
    public void ValidatesActualMediaBounds(double start, double? end, long duration, bool valid, long expectedStart, long expectedEnd)
    {
        bool result = PlaybackRange.TryCreate(new() { StartSeconds = start, EndSeconds = end }, duration, out var range, out var error);
        Assert.Equal(valid, result);
        if (valid) { Assert.Equal(expectedStart, range.StartMs); Assert.Equal(expectedEnd, range.EndMs); Assert.Empty(error); }
        else Assert.NotEmpty(error);
    }
    [Fact] public void UntrimmedUnknownDurationCanPlay()
    {
        Assert.True(PlaybackRange.TryCreate(new(), 0, out var range, out _)); Assert.Null(range.EndMs);
    }
    [Fact] public void RecoveryPositionStaysInsideSegment()
    {
        var range = new PlaybackRange(2125, 5625);
        Assert.Equal(2125, range.ClampPosition(0)); Assert.Equal(5525, range.ClampPosition(9000)); Assert.Equal(4000, range.ClampPosition(4000));
    }
    [Fact] public void MillisecondTrimPersistsAndOldSettingsStillLoad()
    {
        string dir = Path.Combine(Path.GetTempPath(), "ULW-range-" + Guid.NewGuid());
        try
        {
            var store = new SettingsStore(Path.Combine(dir, "settings.json"));
            store.Save(new() { Monitors = new() { ["screen"] = new() { StartSeconds = 2.125, EndSeconds = 5.625 } } });
            var actual = new SettingsStore(store.Path).Load().Monitors["screen"];
            Assert.Equal(2.125, actual.StartSeconds); Assert.Equal(5.625, actual.EndSeconds);
            File.WriteAllText(store.Path, "{\"SchemaVersion\":1,\"Monitors\":{\"screen\":{\"StartSeconds\":2}}}");
            Assert.Null(new SettingsStore(store.Path).Load().Monitors["screen"].EndSeconds);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
