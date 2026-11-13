using System.Text.Json;
using System.Text.Json.Serialization;

namespace UltraLightWallpaper.Core;

public enum SuspensionMode { IndependentMonitors, GlobalFullscreen, GlobalOcclusion }

public sealed record SuspensionSettings
{
    public bool Enabled { get; init; } = true;
    public SuspensionMode Mode { get; init; } = SuspensionMode.IndependentMonitors;
    public double OcclusionThreshold { get; init; } = .95;
    public double Hysteresis { get; init; } = .03;
    public bool FullscreenDetectionEnabled { get; init; } = true;
    public int FullscreenToleranceDip { get; init; } = 2;
    public int ResumeDelayMs { get; init; } = 650;
    public int VerificationIntervalMs { get; init; } = 1000;
    public SuspensionSettings Normalize() => this with
    {
        Mode = Enum.IsDefined(Mode) ? Mode : SuspensionMode.IndependentMonitors,
        OcclusionThreshold = Math.Clamp(double.IsFinite(OcclusionThreshold) ? OcclusionThreshold : .95, .5, 1),
        Hysteresis = Math.Clamp(double.IsFinite(Hysteresis) ? Hysteresis : .03, 0, .1),
        FullscreenToleranceDip = Math.Clamp(FullscreenToleranceDip, 0, 16),
        ResumeDelayMs = Math.Clamp(ResumeDelayMs, 0, 5000),
        VerificationIntervalMs = Math.Clamp(VerificationIntervalMs, 500, 10000)
    };
}

public sealed record MonitorSettings
{
    public string? VideoPath { get; init; }
    public bool Enabled { get; init; } = true;
    public bool Paused { get; init; }
    public ScalingMode Scaling { get; init; } = ScalingMode.Cover;
    public double FocalX { get; init; } = .5;
    public double FocalY { get; init; } = .5;
    public bool Loop { get; init; } = true;
    public double StartSeconds { get; init; }
    public double? EndSeconds { get; init; }
    public float Speed { get; init; } = 1;
    public MonitorSettings Normalize() => this with
    {
        Scaling = Enum.IsDefined(Scaling) ? Scaling : ScalingMode.Cover,
        FocalX = Math.Clamp(double.IsFinite(FocalX) ? FocalX : .5, 0, 1),
        FocalY = Math.Clamp(double.IsFinite(FocalY) ? FocalY : .5, 0, 1),
        StartSeconds = Math.Clamp(double.IsFinite(StartSeconds) ? StartSeconds : 0, 0, 86400),
        EndSeconds = EndSeconds is double end && double.IsFinite(end) ? Math.Clamp(end, 0, 86400) : null,
        Speed = Math.Clamp(float.IsFinite(Speed) ? Speed : 1, .25f, 2f)
    };
}

public sealed record AppSettings
{
    public int SchemaVersion { get; init; } = 1;
    public SuspensionSettings Suspension { get; init; } = new();
    public Dictionary<string, MonitorSettings> Monitors { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public bool StartWithWindows { get; init; }
    public bool DarkMode { get; init; } = true;
    public AppSettings Normalize() => this with
    {
        Suspension = (Suspension ?? new()).Normalize(),
        Monitors = (Monitors ?? []).Where(p => !string.IsNullOrWhiteSpace(p.Key) && p.Value is not null)
            .ToDictionary(p => p.Key, p => p.Value.Normalize(), StringComparer.OrdinalIgnoreCase)
    };
}

/// <summary>Same-directory atomic replacement with previous-good backup and explicit recovery.</summary>
public sealed class SettingsStore(string path)
{
    public string Path { get; } = System.IO.Path.GetFullPath(path);
    public string? RecoveryMessage { get; private set; }
    private bool currentFileInvalid;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    public AppSettings Load()
    {
        RecoveryMessage = null;
        currentFileInvalid = false;
        if (!File.Exists(Path)) return new();
        try { return Read(Path); }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            currentFileInvalid = true;
            RecoveryMessage = $"Settings could not be loaded: {e.Message}";
            if (File.Exists(Path + ".bak"))
            {
                try { var result = Read(Path + ".bak"); RecoveryMessage += " Recovered previous backup."; return result; }
                catch (Exception backup) when (backup is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
                { RecoveryMessage += $" Backup unavailable: {backup.Message}"; }
            }
            return new();
        }
    }
    private static AppSettings Read(string file)
    {
        var value = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(file), JsonOptions) ?? throw new JsonException("Empty configuration.");
        if (value.SchemaVersion != 1) throw new NotSupportedException($"Unsupported schema {value.SchemaVersion}.");
        return value.Normalize();
    }
    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        string temp = Path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, settings.Normalize(), JsonOptions);
                stream.Flush(true);
            }
            if (File.Exists(Path) && currentFileInvalid)
            {
                File.Copy(Path, Path + ".invalid", true);
                File.Move(temp, Path, true); // Keep the last known-good backup when recovering.
                currentFileInvalid = false;
            }
            else if (File.Exists(Path)) File.Replace(temp, Path, Path + ".bak", true);
            else File.Move(temp, Path);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
