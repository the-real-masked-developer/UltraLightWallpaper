using System.Security.Cryptography;
using System.Text;
using System.Diagnostics;
using Microsoft.Win32;
using UltraLightWallpaper.Core;

namespace UltraLightWallpaper.Windows;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var launch = Stopwatch.StartNew();
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        string? Arg(string flag) { int i = Array.IndexOf(args, flag); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        if (args.Contains("--prepare-native-cache")) { using var native = new VideoBackend(); return 0; }
        string settingsPath = Arg("--settings") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UltraLightWallpaper", "settings.json");
        if (Arg("--diagnostics") is string report)
        {
            using var control = new Control(); control.CreateControl();
            using var diagnosticEngine = new Engine(control, new());
            File.WriteAllText(report, diagnosticEngine.Diagnostics()); return 0;
        }
        string identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(settingsPath).ToUpperInvariant())))[..16];
        using var mutex = new Mutex(true, @"Local\UltraLightWallpaper-" + identity, out bool first);
        if (!first) { MessageBox.Show("UltraLightWallpaper is already running. Open Settings from its notification-area icon.", "UltraLightWallpaper"); return 0; }
        try
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, e) => { Log.Write("Unexpected UI error.", e.Exception); MessageBox.Show(e.Exception.Message, "UltraLightWallpaper error", MessageBoxButtons.OK, MessageBoxIcon.Error); };
            using var context = new WallpaperContext(new SettingsStore(settingsPath), args.Contains("--hidden"), int.TryParse(Arg("--exit-after"), out int seconds) ? seconds : 0);
            Log.Write($"Application UI ready in {launch.Elapsed.TotalMilliseconds:F0}ms; video initialization runs in background.");
            Application.Run(context); return 0;
        }
        catch (Exception e) { Log.Write("Application could not start.", e); MessageBox.Show(e.Message, "UltraLightWallpaper startup error", MessageBoxButtons.OK, MessageBoxIcon.Error); return 1; }
    }
}

internal sealed class WallpaperContext : ApplicationContext
{
    private readonly Control dispatcher = new();
    private readonly SettingsStore store;
    private readonly Engine engine;
    private readonly SettingsForm form;
    private readonly NotifyIcon tray;
    private readonly System.Windows.Forms.Timer saveTimer = new() { Interval = 400 };
    private readonly System.Windows.Forms.Timer? exitTimer;
    private bool pendingSave, exiting;
    public WallpaperContext(SettingsStore store, bool hidden, int exitAfter)
    {
        this.store = store;
        dispatcher.CreateControl();
        var settings = store.Load();
        engine = new(dispatcher, settings);
        form = new(engine, ApplySettings);
        form.ExitRequested += ExitThread;
        if (store.RecoveryMessage is string warning) { Log.Write(warning); form.ShowError(warning); }
        var menu = new ContextMenuStrip();
        menu.Items.Add("Ouvrir les paramètres", null, (_, _) => ShowSettings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Tout mettre en pause", null, (_, _) => engine.SetManualPause(true));
        menu.Items.Add("Tout reprendre", null, (_, _) => engine.SetManualPause(false));
        menu.Items.Add("Relancer le moteur", null, (_, _) => engine.Restart());
        menu.Items.Add(new ToolStripSeparator()); menu.Items.Add("Quitter l’application", null, (_, _) => ExitThread());
        tray = new NotifyIcon { Text = "UltraLightWallpaper", Icon = form.Icon ?? SystemIcons.Application, ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => ShowSettings();
        saveTimer.Tick += (_, _) => FlushSettings();
        if (exitAfter > 0) { exitTimer = new() { Interval = Math.Clamp(exitAfter, 1, 86400) * 1000 }; exitTimer.Tick += (_, _) => ExitThread(); exitTimer.Start(); }
        if (!hidden) form.Show();
    }
    private void ShowSettings() { form.Show(); form.WindowState = FormWindowState.Normal; form.Activate(); }
    private void ApplySettings(AppSettings settings)
    {
        if (settings.StartWithWindows != engine.Settings.StartWithWindows)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
                if (settings.StartWithWindows)
                {
                    string command = string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase)
                        ? $"\"{Environment.ProcessPath}\" \"{typeof(Program).Assembly.Location}\" --hidden"
                        : $"\"{Environment.ProcessPath}\" --hidden";
                    key.SetValue("UltraLightWallpaper", command);
                }
                else key.DeleteValue("UltraLightWallpaper", false);
            }
            catch (Exception e) when (e is UnauthorizedAccessException or IOException or System.Security.SecurityException)
            { form.ShowError("Could not update startup: " + e.Message); Log.Write("Startup setting failed.", e); settings = settings with { StartWithWindows = engine.Settings.StartWithWindows }; }
        }
        engine.ApplySettings(settings); pendingSave = true; saveTimer.Stop(); saveTimer.Start();
    }
    private void FlushSettings()
    {
        saveTimer.Stop(); if (!pendingSave) return;
        try { store.Save(engine.Settings); pendingSave = false; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Log.Write("Settings could not be saved.", e); form.ShowError("Settings are active but could not be saved: " + e.Message); }
    }
    protected override void ExitThreadCore()
    {
        if (exiting) return; exiting = true;
        FlushSettings(); tray.Visible = false; form.CloseForExit(); engine.Dispose(); base.ExitThreadCore();
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (!exiting) { FlushSettings(); engine.Dispose(); }
            saveTimer.Dispose(); exitTimer?.Dispose(); tray.ContextMenuStrip?.Dispose(); tray.Dispose(); form.Dispose(); dispatcher.Dispose();
        }
        base.Dispose(disposing);
    }
}
