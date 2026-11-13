namespace UltraLightWallpaper.Core;

/// <summary>A validated media-time range. An absent end means the actual end of the file.</summary>
public readonly record struct PlaybackRange(long StartMs, long? EndMs)
{
    public static bool TryCreate(MonitorSettings settings, long durationMs, out PlaybackRange range, out string error)
    {
        range = default; error = "";
        var normalized = settings.Normalize();
        long start = (long)Math.Round(normalized.StartSeconds * 1000);
        long? end = normalized.EndSeconds is double seconds ? (long)Math.Round(seconds * 1000) : durationMs > 0 ? durationMs : null;
        if (durationMs <= 0 && (start > 0 || normalized.EndSeconds is not null))
            error = "La durée de cette vidéo est inconnue. Impossible de vérifier les points de coupe.";
        else if (durationMs > 0 && (start >= durationMs || end > durationMs))
            error = "Le début et la fin doivent se trouver dans la vidéo.";
        else if (end is long stop && stop - start < Math.Min(100, durationMs > 0 ? durationMs : 100))
            error = "La fin doit suivre le début d’au moins 0,1 seconde.";
        if (error.Length > 0) return false;
        range = new(start, end); return true;
    }

    public long ClampPosition(long position) => Math.Clamp(position, StartMs, EndMs is long end ? Math.Max(StartMs, end - 100) : long.MaxValue);
}
