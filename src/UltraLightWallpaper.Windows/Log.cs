namespace UltraLightWallpaper.Windows;

public static class Log
{
    private static readonly object Sync = new();
    public static string DirectoryPath { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UltraLightWallpaper", "logs");
    public static event Action<string>? Error;
    public static void Write(string message, Exception? exception = null)
    {
        string line = $"{DateTimeOffset.Now:O} {message}" + (exception is null ? "" : " " + exception);
        System.Diagnostics.Trace.WriteLine(line);
        lock (Sync)
        {
            try
            {
                Directory.CreateDirectory(DirectoryPath);
                string file = Path.Combine(DirectoryPath, "engine.log");
                if (File.Exists(file) && new FileInfo(file).Length > 2 * 1024 * 1024)
                {
                    string backup = file + ".1";
                    File.Move(file, backup, true);
                }
                File.AppendAllText(file, line + Environment.NewLine);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { System.Diagnostics.Trace.WriteLine($"Log unavailable: {e.Message}"); }
        }
        if (exception is not null) Error?.Invoke(message + " " + exception.Message);
    }
}
