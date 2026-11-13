using UltraLightWallpaper.Core;
using Xunit;

namespace UltraLightWallpaper.Tests;

public sealed class SettingsTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "UltraLightWallpaper-tests-" + Guid.NewGuid());
    [Theory] [InlineData(SuspensionMode.IndependentMonitors)] [InlineData(SuspensionMode.GlobalFullscreen)] [InlineData(SuspensionMode.GlobalOcclusion)]
    public void ModeAndMonitorConfigurationPersistAcrossNewStore(SuspensionMode mode)
    {
        var path = Path.Combine(directory, "settings.json");
        var settings = new AppSettings { Suspension = new() { Mode = mode, ResumeDelayMs = 1250, FullscreenToleranceDip = 4 }, Monitors = new() { ["negative-monitor"] = new() { VideoPath = @"C:\Video\portrait.mp4", FocalX = .25, Scaling = ScalingMode.Cover, Paused = true } } };
        new SettingsStore(path).Save(settings);
        var loaded = new SettingsStore(path).Load();
        Assert.Equal(settings.Suspension, loaded.Suspension); Assert.Equal(settings.Monitors["negative-monitor"], loaded.Monitors["negative-monitor"]);
    }
    [Fact] public void CorruptionRecoversPreviousGoodBackup()
    {
        var path = Path.Combine(directory, "settings.json"); var store = new SettingsStore(path);
        store.Save(new() { Suspension = new() { Mode = SuspensionMode.GlobalFullscreen } }); store.Save(new());
        File.WriteAllText(path, "{ interrupted");
        Assert.Equal(SuspensionMode.GlobalFullscreen, store.Load().Suspension.Mode); Assert.Contains("Recovered", store.RecoveryMessage);
    }
    [Fact] public void InvalidConfigurationUsesDefaultsWithExplanation()
    {
        Directory.CreateDirectory(directory); var path = Path.Combine(directory, "settings.json"); File.WriteAllText(path, "null");
        var store = new SettingsStore(path); Assert.Equal(SuspensionMode.IndependentMonitors, store.Load().Suspension.Mode); Assert.NotNull(store.RecoveryMessage);
    }
    [Fact] public void SavingRecoveredSettingsPreservesValidBackupAndBadInput()
    {
        var path = Path.Combine(directory, "settings.json"); var store = new SettingsStore(path);
        store.Save(new() { Suspension = new() { Mode = SuspensionMode.GlobalFullscreen } }); store.Save(new());
        File.WriteAllText(path, "broken"); var recovered = store.Load(); store.Save(recovered);
        Assert.Equal("broken", File.ReadAllText(path + ".invalid"));
        Assert.Contains("GlobalFullscreen", File.ReadAllText(path + ".bak"));
        Assert.Equal(SuspensionMode.GlobalFullscreen, new SettingsStore(path).Load().Suspension.Mode);
    }
    [Fact] public void UnsupportedSchemaIsNotSilentlyRead()
    {
        Directory.CreateDirectory(directory); var path = Path.Combine(directory, "settings.json"); File.WriteAllText(path, "{\"SchemaVersion\":99}");
        var store = new SettingsStore(path); store.Load(); Assert.Contains("Unsupported schema", store.RecoveryMessage);
    }
    [Fact] public void ValuesAreNormalized()
    {
        var s = new SuspensionSettings { Mode = (SuspensionMode)100, OcclusionThreshold = double.NaN, ResumeDelayMs = -9, VerificationIntervalMs = 1, FullscreenToleranceDip = 100 }.Normalize();
        Assert.Equal(.95, s.OcclusionThreshold); Assert.Equal(0, s.ResumeDelayMs); Assert.Equal(500, s.VerificationIntervalMs); Assert.Equal(16, s.FullscreenToleranceDip); Assert.Equal(SuspensionMode.IndependentMonitors, s.Mode);
    }
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
