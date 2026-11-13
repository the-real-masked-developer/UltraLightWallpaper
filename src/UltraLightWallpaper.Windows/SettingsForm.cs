using UltraLightWallpaper.Core;

namespace UltraLightWallpaper.Windows;

public sealed class SettingsForm : Form
{
    private readonly Engine engine;
    private readonly Action<AppSettings> save;
    private readonly ComboBox displays = Choice("Écran sélectionné");
    private readonly MonitorMap map = new() { Height = 150, Dock = DockStyle.Fill };
    private readonly Label file = Copy("Choisissez une vidéo locale.");
    private readonly Label playback = Copy("");
    private readonly Label metadata = Copy("");
    private readonly Label banner = Copy("Les réglages sont enregistrés automatiquement.");
    private readonly CheckBox monitorEnabled = Toggle("Activer cet écran");
    private readonly CheckBox monitorPaused = Toggle("Mettre cet écran en pause");
    private readonly CheckBox loop = Toggle("Lire en boucle");
    private readonly ComboBox scale = Choice("Ajustement de l’image");
    private readonly TrackBar focalX = Slider("Position horizontale");
    private readonly TrackBar focalY = Slider("Position verticale");
    private readonly NumericUpDown speed = Number("Vitesse", .25m, 2, .05m, 2);
    private readonly NumericUpDown start = Number("Début en secondes", 0, 86400, .04m, 3);
    private readonly NumericUpDown end = Number("Fin en secondes", 0, 86400, .04m, 3);
    private readonly CheckBox fileEnd = Toggle("Jusqu’à la fin du fichier");
    private readonly RangeTimeline timeline = new() { Height = 68, Dock = DockStyle.Fill };
    private readonly Label rangeInfo = Copy("");
    private readonly Button applyRange;
    private readonly CheckBox automatic = Toggle("Activer la suspension automatique");
    private readonly ComboBox mode = Choice("Mode de suspension automatique");
    private readonly NumericUpDown threshold = Number("Seuil de couverture en pourcentage", 50, 100, 1, 0);
    private readonly NumericUpDown delay = Number("Délai de reprise en millisecondes", 0, 5000, 50, 0);
    private readonly NumericUpDown tolerance = Number("Tolérance plein écran en DIP", 0, 16, 1, 0);
    private readonly NumericUpDown interval = Number("Vérification de secours en millisecondes", 500, 10000, 250, 0);
    private readonly CheckBox fullscreen = Toggle("Détecter les applications en plein écran");
    private readonly CheckBox manual = Toggle("Pause manuelle sur tous les écrans");
    private readonly CheckBox startup = Toggle("Démarrer avec Windows");
    private readonly CheckBox dark = Toggle("Apparence sombre");
    private readonly Label hardware = Copy("");
    private readonly TextBox diagnostic = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill, Font = new("Consolas", 9), AccessibleName = "Rapport de diagnostic" };
    private readonly System.Windows.Forms.Timer statusTimer = new() { Interval = 500 };
    private readonly ToolTip tips = new();
    private readonly List<Control> pages = [];
    private readonly List<Button> navigation = [];
    private bool loading, allowClose, rangeDirty, syncingRange;
    private string? selectedId;
    private string monitorFingerprint = "";
    private double knownDuration;
    public event Action? ExitRequested;

    public SettingsForm(Engine engine, Action<AppSettings> save)
    {
        this.engine = engine; this.save = save;
        Text = "UltraLightWallpaper · Paramètres";
        string appHost = Path.ChangeExtension(typeof(SettingsForm).Assembly.Location, ".exe");
        Icon = File.Exists(appHost) ? Icon.ExtractAssociatedIcon(appHost) : SystemIcons.Application;
        AutoScaleMode = AutoScaleMode.Dpi; Font = new("Segoe UI", 10);
        MinimumSize = new(640, 560); Size = new(1020, 850); StartPosition = FormStartPosition.CenterScreen;
        var header = new Panel { Dock = DockStyle.Top, Height = 84, Padding = new(24, 12, 24, 8), Tag = "shell" };
        header.Controls.Add(new Label { Text = "UltraLightWallpaper", Font = new("Segoe UI Semibold", 21), AutoSize = true, Location = new(24, 9) });
        header.Controls.Add(new Label { Text = "Votre bureau, en mouvement.    /    1.1", AutoSize = true, Location = new(26, 53), Tag = "muted" });
        var nav = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new(20, 0, 20, 10), Tag = "shell" };
        var host = new Panel { Dock = DockStyle.Fill };
        var wallpapers = new ScrollPage(); var performance = new ScrollPage(); var general = new ScrollPage();
        var diagnostics = new Panel { Dock = DockStyle.Fill, Padding = new(22) };
        pages.AddRange([wallpapers, performance, general, diagnostics]);
        string[] titles = ["Fonds d’écran", "Performances", "Application", "Diagnostic"];
        for (int i = 0; i < pages.Count; i++)
        {
            int index = i; var button = ActionButton(titles[i], () => SelectPage(index));
            button.Tag = "nav"; navigation.Add(button); nav.Controls.Add(button); host.Controls.Add(pages[i]);
        }
        var diagnosticButtons = Actions(ActionButton("Actualiser", () => diagnostic.Text = engine.Diagnostics()), ActionButton("Enregistrer…", SaveDiagnostics), ActionButton("Relancer le moteur", engine.Restart));
        diagnosticButtons.Dock = DockStyle.Bottom; diagnostics.Controls.Add(diagnostic); diagnostics.Controls.Add(diagnosticButtons);
        banner.Dock = DockStyle.Bottom; banner.AutoSize = false; banner.Height = 48; banner.Padding = new(24, 8, 24, 6); banner.Tag = "muted";
        Controls.Add(host); Controls.Add(nav); Controls.Add(header); Controls.Add(banner);

        var displayCard = Card("01   Écrans", "Chaque écran garde sa vidéo, son cadrage et sa vitesse.");
        Add(displayCard, map, displays, file);
        Add(displayCard, Actions(ActionButton("Choisir une vidéo…", ChooseVideo, true), ActionButton("Retirer", () => { ChangeMonitor(m => m with { VideoPath = null }); LoadMonitor(); }), ActionButton("Actualiser les écrans", engine.RefreshMonitors)));
        Add(displayCard, Actions(monitorEnabled, monitorPaused), metadata, playback); wallpapers.AddCard(displayCard);

        var rangeCard = Card("02   Séquence vidéo", "Déplacez les deux poignées ou saisissez des secondes précises. La boucle répète uniquement cette séquence.");
        Add(rangeCard, timeline, Actions(Field("Début · secondes", start), Field("Fin · secondes", end)), fileEnd, loop);
        Add(rangeCard, Actions(ActionButton("Début = position actuelle", () => CapturePosition(true)), ActionButton("Fin = position actuelle", () => CapturePosition(false))));
        applyRange = ActionButton("Appliquer la séquence", ApplyRange, true); applyRange.Enabled = false;
        Add(rangeCard, rangeInfo, Actions(applyRange, ActionButton("Toute la vidéo", ResetRange))); wallpapers.AddCard(rangeCard);

        var framing = Card("03   Image et mouvement", "Le cadrage et la vitesse s’appliquent immédiatement.");
        scale.Items.AddRange(["Remplir l’écran · recadrer", "Tout afficher · conserver les proportions", "Étirer sur tout l’écran"]);
        Add(framing, Copy("Ajustement de l’image"), scale, Copy("Position horizontale"), focalX, Copy("Position verticale"), focalY);
        Add(framing, Actions(Field("Vitesse · ×", speed), ActionButton("Centrer l’image", () => { ChangeMonitor(m => m with { FocalX = .5, FocalY = .5 }); LoadFraming(); })));
        wallpapers.AddCard(framing);

        var suspension = Card("Suspension automatique", "Économisez des ressources lorsque vos fonds d’écran ne sont plus visibles.");
        mode.Items.AddRange(["Écrans indépendants", "Tout mettre en pause si une application est en plein écran", "Tout mettre en pause si un écran est suffisamment couvert"]);
        Add(suspension, automatic, Copy("Comportement"), mode);
        Add(suspension, Actions(Field("Couverture · %", threshold), Field("Délai de reprise · ms", delay)), fullscreen);
        Add(suspension, Actions(Field("Tolérance plein écran · DIP", tolerance), Field("Vérification de secours · ms", interval)));
        Add(suspension, Copy("Le délai de reprise et une marge de couverture de 3 points évitent les pauses et reprises répétées."), manual);
        performance.AddCard(suspension);
        var efficiency = Card("Lecture économe", "La suspension conserve le lecteur et sa dernière image.");
        Add(efficiency, hardware, ActionButton("Réglages basse consommation", () => { ChangeSuspension(s => s with { Enabled = true, VerificationIntervalMs = 2000, ResumeDelayMs = 1000 }); LoadPerformance(); })); performance.AddCard(efficiency);

        var preferences = Card("Vos préférences", "Les changements sont conservés au prochain lancement.");
        Add(preferences, startup, dark, Copy("Fermer cette fenêtre laisse les fonds d’écran actifs. Retrouvez les commandes dans la zone de notification."));
        Add(preferences, Actions(ActionButton("Tout mettre en pause", () => engine.SetManualPause(true)), ActionButton("Tout reprendre", () => engine.SetManualPause(false)))); general.AddCard(preferences);
        var about = Card("UltraLightWallpaper 1.1", "Vidéos locales · plusieurs écrans · décodage matériel");
        Add(about, Copy("Vos vidéos restent à leur emplacement d’origine. Les réglages et journaux sont enregistrés dans LocalAppData\\UltraLightWallpaper."), ActionButton("Quitter l’application", () => ExitRequested?.Invoke())); general.AddCard(about);

        displays.SelectedIndexChanged += (_, _) => { if (!loading && displays.SelectedIndex >= 0) { selectedId = engine.Monitors[displays.SelectedIndex].Id; LoadMonitor(); } };
        map.Selected += id => { selectedId = id; loading = true; displays.SelectedIndex = engine.Monitors.ToList().FindIndex(m => m.Id == id); loading = false; LoadMonitor(); };
        monitorEnabled.CheckedChanged += (_, _) => ChangeMonitor(m => m with { Enabled = monitorEnabled.Checked });
        monitorPaused.CheckedChanged += (_, _) => ChangeMonitor(m => m with { Paused = monitorPaused.Checked });
        loop.CheckedChanged += (_, _) => ChangeMonitor(m => m with { Loop = loop.Checked });
        scale.SelectedIndexChanged += (_, _) => { if (scale.SelectedIndex >= 0) ChangeMonitor(m => m with { Scaling = (ScalingMode)scale.SelectedIndex }); };
        focalX.ValueChanged += (_, _) => ChangeMonitor(m => m with { FocalX = focalX.Value / 100d });
        focalY.ValueChanged += (_, _) => ChangeMonitor(m => m with { FocalY = focalY.Value / 100d });
        speed.ValueChanged += (_, _) => ChangeMonitor(m => m with { Speed = (float)speed.Value });
        start.ValueChanged += (_, _) => DraftRangeChanged(); end.ValueChanged += (_, _) => DraftRangeChanged(); fileEnd.CheckedChanged += (_, _) => DraftRangeChanged();
        timeline.RangeChanged += (a, b) =>
        {
            syncingRange = true; start.Value = (decimal)a; end.Value = (decimal)b; fileEnd.Checked = false; syncingRange = false; DraftRangeChanged();
        };
        automatic.CheckedChanged += (_, _) => ChangeSuspension(s => s with { Enabled = automatic.Checked });
        mode.SelectedIndexChanged += (_, _) => { if (mode.SelectedIndex >= 0) ChangeSuspension(s => s with { Mode = (SuspensionMode)mode.SelectedIndex }); };
        threshold.ValueChanged += (_, _) => ChangeSuspension(s => s with { OcclusionThreshold = (double)threshold.Value / 100 });
        delay.ValueChanged += (_, _) => ChangeSuspension(s => s with { ResumeDelayMs = (int)delay.Value });
        tolerance.ValueChanged += (_, _) => ChangeSuspension(s => s with { FullscreenToleranceDip = (int)tolerance.Value });
        interval.ValueChanged += (_, _) => ChangeSuspension(s => s with { VerificationIntervalMs = (int)interval.Value });
        fullscreen.CheckedChanged += (_, _) => ChangeSuspension(s => s with { FullscreenDetectionEnabled = fullscreen.Checked });
        manual.CheckedChanged += (_, _) => { if (!loading) engine.SetManualPause(manual.Checked); };
        startup.CheckedChanged += (_, _) => { if (!loading) save(engine.Settings with { StartWithWindows = startup.Checked }); };
        dark.CheckedChanged += (_, _) => { if (!loading) { save(engine.Settings with { DarkMode = dark.Checked }); SetTheme(); } };
        engine.Changed += RefreshStatus;
        statusTimer.Tick += (_, _) => RefreshStatus();
        VisibleChanged += (_, _) => { statusTimer.Enabled = Visible; if (Visible) RefreshStatus(); };
        Shown += (_, _) => SetTheme();
        FormClosing += (_, e) => { if (!allowClose && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
        LoadControls(); SelectPage(0);
    }

    private void SelectPage(int index)
    {
        for (int i = 0; i < pages.Count; i++) { pages[i].Visible = i == index; navigation[i].Tag = i == index ? "selected" : "nav"; }
        pages[index].BringToFront(); if (index == 3) diagnostic.Text = engine.Diagnostics(); SetTheme();
    }
    private static Label Copy(string text) => new() { Text = text, AutoSize = true, Dock = DockStyle.Fill, Margin = new(0, 3, 0, 8), Tag = "muted" };
    private static CheckBox Toggle(string text) => new ThemeCheckBox { Text = text, AccessibleName = text, AutoSize = true, Margin = new(0, 8, 18, 8), FlatStyle = FlatStyle.Flat };
    private static ComboBox Choice(string name) => new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat, AccessibleName = name, Margin = new(0, 3, 0, 10) };
    private static TrackBar Slider(string name) => new() { Minimum = 0, Maximum = 100, TickStyle = TickStyle.None, AutoSize = false, Height = 34, Dock = DockStyle.Fill, AccessibleName = name, Margin = new(0, 0, 0, 10) };
    private static NumericUpDown Number(string name, decimal min, decimal max, decimal increment, int decimals) => new() { AccessibleName = name, Minimum = min, Maximum = max, Increment = increment, DecimalPlaces = decimals, Width = 180, BorderStyle = BorderStyle.FixedSingle, ThousandsSeparator = true };
    private static Button ActionButton(string text, Action action, bool primary = false)
    {
        var button = new Button { Text = text, AccessibleName = text, AutoSize = true, MinimumSize = new(100, 38), FlatStyle = FlatStyle.Flat, Padding = new(14, 5, 14, 5), Margin = new(0, 4, 10, 4), Cursor = Cursors.Hand, Tag = primary ? "primary" : "button" };
        button.FlatAppearance.BorderSize = 0; button.Click += (_, _) => action(); return button;
    }
    private static FlowLayoutPanel Actions(params Control[] controls)
    {
        var flow = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill, WrapContents = true, Margin = new(0, 2, 0, 8) };
        flow.Controls.AddRange(controls); return flow;
    }
    private static TableLayoutPanel Field(string title, Control input)
    {
        var field = Stack(); field.Width = 220; field.Dock = DockStyle.None; field.Margin = new(0, 4, 20, 6); field.AutoSizeMode = AutoSizeMode.GrowOnly;
        Add(field, Copy(title), input); return field;
    }
    private static TableLayoutPanel Stack()
    {
        var table = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill, Margin = Padding.Empty };
        table.ColumnStyles.Add(new(SizeType.Percent, 100)); return table;
    }
    private static TableLayoutPanel Card(string title, string description)
    {
        var card = Stack(); card.Padding = new(22, 18, 22, 14); card.Margin = new(0, 0, 0, 18); card.Tag = "card";
        Add(card, new Label { Text = title, AutoSize = true, Font = new("Segoe UI Semibold", 14), Margin = new(0, 0, 0, 8), Dock = DockStyle.Fill }, Copy(description)); return card;
    }
    private static void Add(TableLayoutPanel table, params Control[] controls)
    {
        foreach (var control in controls) { table.RowCount++; table.RowStyles.Add(new(SizeType.AutoSize)); table.Controls.Add(control, 0, table.RowCount - 1); }
    }
    private MonitorSettings SelectedSettings => selectedId is null ? new() : engine.Settings.Monitors.GetValueOrDefault(selectedId) ?? new();
    private PlaybackSession? SelectedSession => selectedId is not null ? engine.Sessions.GetValueOrDefault(selectedId) : null;
    private void ChangeMonitor(Func<MonitorSettings, MonitorSettings> change)
    {
        if (loading || selectedId is null) return;
        var monitors = new Dictionary<string, MonitorSettings>(engine.Settings.Monitors, StringComparer.OrdinalIgnoreCase) { [selectedId] = change(SelectedSettings) };
        save(engine.Settings with { Monitors = monitors });
    }
    private void ChangeSuspension(Func<SuspensionSettings, SuspensionSettings> change)
    { if (!loading) { save(engine.Settings with { Suspension = change(engine.Settings.Suspension) }); UpdatePerformanceAvailability(); } }
    private void ChooseVideo()
    {
        using var dialog = new OpenFileDialog { Title = "Choisir une vidéo", Filter = "Vidéos|*.mp4;*.mkv;*.webm;*.mov;*.avi;*.m4v;*.wmv|Tous les fichiers|*.*", CheckFileExists = true };
        if (dialog.ShowDialog(this) == DialogResult.OK) { ChangeMonitor(m => m with { VideoPath = dialog.FileName, Enabled = true, StartSeconds = 0, EndSeconds = null }); LoadMonitor(); }
    }
    private void LoadControls()
    {
        loading = true;
        monitorFingerprint = string.Join("|", engine.Monitors.Select(m => $"{m.Id}:{m.Bounds}:{m.Dpi}"));
        displays.Items.Clear();
        foreach (var m in engine.Monitors) displays.Items.Add($"Écran {displays.Items.Count + 1}  ·  {m.Bounds.Width} × {m.Bounds.Height}{(m.Primary ? "  ·  Principal" : "")}");
        if (selectedId is null || !engine.Monitors.Any(m => m.Id == selectedId)) selectedId = engine.Monitors.FirstOrDefault(m => m.Primary)?.Id ?? engine.Monitors.FirstOrDefault()?.Id;
        displays.SelectedIndex = engine.Monitors.ToList().FindIndex(m => m.Id == selectedId);
        manual.Checked = engine.ManualPause; startup.Checked = engine.Settings.StartWithWindows; dark.Checked = engine.Settings.DarkMode;
        loading = false; LoadPerformance(); LoadMonitor(); SetTheme();
    }
    private void LoadPerformance()
    {
        loading = true; var s = engine.Settings.Suspension;
        automatic.Checked = s.Enabled; mode.SelectedIndex = (int)s.Mode; threshold.Value = (decimal)(s.OcclusionThreshold * 100);
        delay.Value = s.ResumeDelayMs; tolerance.Value = s.FullscreenToleranceDip; interval.Value = s.VerificationIntervalMs; fullscreen.Checked = s.FullscreenDetectionEnabled;
        loading = false; UpdatePerformanceAvailability();
    }
    private void UpdatePerformanceAvailability()
    {
        mode.Enabled = threshold.Enabled = delay.Enabled = interval.Enabled = automatic.Checked;
        fullscreen.Enabled = automatic.Checked && mode.SelectedIndex == (int)SuspensionMode.GlobalFullscreen;
        tolerance.Enabled = fullscreen.Enabled && fullscreen.Checked;
    }
    private void LoadFraming()
    {
        loading = true; var m = SelectedSettings; scale.SelectedIndex = (int)m.Scaling;
        focalX.Value = (int)(m.FocalX * 100); focalY.Value = (int)(m.FocalY * 100); speed.Value = (decimal)m.Speed; loading = false;
    }
    private void LoadMonitor()
    {
        loading = true; var m = SelectedSettings;
        monitorEnabled.Checked = m.Enabled; monitorPaused.Checked = m.Paused; loop.Checked = m.Loop;
        knownDuration = (SelectedSession?.LengthMs ?? 0) / 1000d;
        start.Value = (decimal)m.StartSeconds; end.Value = (decimal)Math.Min(86400, m.EndSeconds ?? knownDuration); fileEnd.Checked = m.EndSeconds is null;
        file.Text = m.VideoPath is null ? "Choisissez une vidéo locale." : Path.GetFileName(m.VideoPath); tips.SetToolTip(file, m.VideoPath);
        map.SetMonitors(engine.Monitors, selectedId); rangeDirty = false; loading = false; LoadFraming(); UpdateRange(); RefreshStatus();
    }
    private void DraftRangeChanged()
    {
        if (loading || syncingRange) return; rangeDirty = true; UpdateRange();
    }
    private void UpdateRange()
    {
        end.Enabled = !fileEnd.Checked;
        var draft = SelectedSettings with { StartSeconds = (double)start.Value, EndSeconds = fileEnd.Checked ? null : (double)end.Value };
        bool valid = PlaybackRange.TryCreate(draft, (long)Math.Round(knownDuration * 1000), out _, out string error);
        applyRange.Enabled = rangeDirty && valid && knownDuration > 0;
        double stop = fileEnd.Checked ? knownDuration : (double)end.Value;
        timeline.SetRange(knownDuration, (double)start.Value, stop);
        rangeInfo.Text = !valid ? error : knownDuration <= 0 ? "Choisissez une vidéo pour régler la séquence."
            : $"Séquence : {Math.Max(0, stop - (double)start.Value):0.000} s  ·  Vidéo : {knownDuration:0.000} s" + (rangeDirty ? "  ·  Modifications à appliquer" : "");
        rangeInfo.ForeColor = valid ? Palette.Muted(engine.Settings.DarkMode) : Color.Coral;
    }
    private void ApplyRange()
    {
        var draft = SelectedSettings with { StartSeconds = (double)start.Value, EndSeconds = fileEnd.Checked ? null : (double)end.Value };
        if (!PlaybackRange.TryCreate(draft, (long)Math.Round(knownDuration * 1000), out _, out string error)) { ShowError(error); return; }
        ChangeMonitor(_ => draft); rangeDirty = false; UpdateRange(); banner.Text = "Séquence appliquée et enregistrée.";
    }
    private void CapturePosition(bool first)
    {
        if (SelectedSession is not { } session || knownDuration <= 0) return;
        decimal position = (decimal)Math.Min(86400, session.TimeMs / 1000d);
        if (first) start.Value = position; else { fileEnd.Checked = false; end.Value = position; }
    }
    private void ResetRange() { start.Value = 0; fileEnd.Checked = true; end.Value = (decimal)Math.Min(86400, knownDuration); rangeDirty = true; UpdateRange(); }
    private void RefreshStatus()
    {
        if (IsDisposed || loading) return;
        if (monitorFingerprint != string.Join("|", engine.Monitors.Select(m => $"{m.Id}:{m.Bounds}:{m.Dpi}"))) { LoadControls(); return; }
        playback.Text = selectedId is null ? "Aucun écran connecté" : engine.StatusFor(selectedId);
        var session = SelectedSession; metadata.Text = session?.Metadata ?? "";
        double duration = (session?.LengthMs ?? 0) / 1000d;
        if (duration > 0 && Math.Abs(knownDuration - duration) > .001)
        {
            knownDuration = duration;
            if (fileEnd.Checked) { loading = true; end.Value = (decimal)Math.Min(86400, duration); loading = false; }
            UpdateRange();
        }
        timeline.SetPosition((session?.TimeMs ?? 0) / 1000d);
        hardware.Text = engine.BackendInitializing ? "Le moteur vidéo se prépare en arrière-plan…" : engine.HardwareStatus;
        if (manual.Checked != engine.ManualPause) { loading = true; manual.Checked = engine.ManualPause; loading = false; }
    }
    private void SetTheme() { Palette.Apply(this, engine.Settings.DarkMode); UpdateRange(); }
    private void SaveDiagnostics()
    {
        using var dialog = new SaveFileDialog { Filter = "Rapport texte|*.txt", FileName = "UltraLightWallpaper-diagnostics.txt" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { File.WriteAllText(dialog.FileName, engine.Diagnostics()); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { ShowError("Enregistrement impossible : " + e.Message); }
    }
    public void ShowError(string message) { banner.Text = message; banner.ForeColor = Color.Coral; }
    public void CloseForExit() { allowClose = true; Close(); }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { engine.Changed -= RefreshStatus; statusTimer.Dispose(); tips.Dispose(); }
        base.Dispose(disposing);
    }
}

internal sealed class ScrollPage : Panel
{
    private readonly TableLayoutPanel content = new() { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, RowCount = 0, Location = new(22, 14), Margin = Padding.Empty };
    public ScrollPage()
    {
        Dock = DockStyle.Fill; AutoScroll = true; Padding = new(22, 14, 22, 20);
        content.ColumnStyles.Add(new(SizeType.Percent, 100)); Controls.Add(content);
        SizeChanged += (_, _) => Fit(); Layout += (_, _) => Fit();
    }
    public void AddCard(Control card) { content.RowCount++; content.RowStyles.Add(new(SizeType.AutoSize)); content.Controls.Add(card, 0, content.RowCount - 1); Fit(); }
    private void Fit()
    {
        int width = Math.Max(100, ClientSize.Width - Padding.Horizontal - SystemInformation.VerticalScrollBarWidth);
        if (content.Width != width) { content.MinimumSize = new(width, 0); content.MaximumSize = new(width, 0); content.Width = width; }
    }
}

internal static class Palette
{
    public static readonly Color Accent = Color.FromArgb(114, 208, 190);
    public static Color Muted(bool dark) => dark ? Color.FromArgb(161, 174, 187) : Color.FromArgb(75, 92, 103);
    public static void Apply(Control c, bool dark, Color? inherited = null)
    {
        Color shell = dark ? Color.FromArgb(17, 24, 32) : Color.FromArgb(240, 244, 246);
        Color card = dark ? Color.FromArgb(27, 37, 47) : Color.White;
        Color input = dark ? Color.FromArgb(37, 49, 61) : Color.FromArgb(229, 236, 241);
        Color text = dark ? Color.FromArgb(231, 239, 243) : Color.FromArgb(23, 39, 49);
        c.BackColor = c.Tag as string == "card" ? card : c is TextBox or ComboBox or NumericUpDown or Button ? input : inherited ?? shell;
        c.ForeColor = c.Tag as string == "muted" ? Muted(dark) : text;
        if (c.Tag as string is "primary" or "selected") { c.BackColor = Accent; c.ForeColor = Color.FromArgb(16, 40, 37); }
        if (c.Tag as string == "nav") c.BackColor = shell;
        foreach (Control child in c.Controls) Apply(child, dark, c.BackColor);
        if (c is Form form && form.IsHandleCreated) { int value = dark ? 1 : 0; Native.DwmSetWindowAttribute(form.Handle, 20, ref value, sizeof(int)); }
    }
}

internal sealed class ThemeCheckBox : CheckBox
{
    public ThemeCheckBox() { DoubleBuffered = true; }
    protected override void OnPaint(PaintEventArgs e)
    {
        if (SystemInformation.HighContrast) { base.OnPaint(e); return; }
        e.Graphics.Clear(BackColor);
        float scale = DeviceDpi / 96f; int size = (int)Math.Round(13 * scale), gap = (int)Math.Round(8 * scale);
        var box = new Rectangle(1, Math.Max(1, (Height - size) / 2), size, size);
        Color ink = Enabled ? ForeColor : BackColor.GetBrightness() < .5 ? Color.FromArgb(129, 145, 158) : Color.FromArgb(109, 122, 130);
        using var fill = new SolidBrush(Checked && Enabled ? Palette.Accent : BackColor);
        using var edge = new Pen(Checked && Enabled ? Palette.Accent : ink);
        e.Graphics.FillRectangle(fill, box); e.Graphics.DrawRectangle(edge, box);
        if (Checked)
        {
            using var check = new Pen(Enabled ? Color.FromArgb(16, 40, 37) : ink, Math.Max(1.5f, scale * 1.6f));
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.DrawLines(check, [new PointF(box.Left + size * .2f, box.Top + size * .5f), new PointF(box.Left + size * .43f, box.Top + size * .74f), new PointF(box.Left + size * .83f, box.Top + size * .25f)]);
        }
        TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(size + gap, 0, Math.Max(0, Width - size - gap), Height), ink, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, new Rectangle(size + gap, 0, Math.Max(0, Width - size - gap), Height));
    }
}

internal sealed class MonitorMap : Control
{
    private IReadOnlyList<MonitorSnapshot> monitors = [];
    private string? selected;
    private readonly Dictionary<string, RectangleF> targets = [];
    public event Action<string>? Selected;
    public MonitorMap() { DoubleBuffered = true; Cursor = Cursors.Hand; AccessibleName = "Disposition des écrans"; ResizeRedraw = true; }
    public void SetMonitors(IReadOnlyList<MonitorSnapshot> value, string? id) { monitors = value; selected = id; Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); targets.Clear(); if (monitors.Count == 0) return;
        int left = monitors.Min(m => m.Bounds.Left), top = monitors.Min(m => m.Bounds.Top), right = monitors.Max(m => m.Bounds.Right), bottom = monitors.Max(m => m.Bounds.Bottom);
        float ratio = Math.Min((Width - 30f) / (right - left), (Height - 28f) / (bottom - top));
        float offset = (Width - (right - left) * ratio) / 2;
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        for (int i = 0; i < monitors.Count; i++)
        {
            var m = monitors[i]; var box = new RectangleF(offset + (m.Bounds.Left - left) * ratio + 3, 10 + (m.Bounds.Top - top) * ratio + 3, m.Bounds.Width * ratio - 6, m.Bounds.Height * ratio - 6);
            using var brush = new SolidBrush(m.Id == selected ? Color.FromArgb(43, 85, 82) : Color.FromArgb(45, 58, 70));
            using var pen = new Pen(m.Id == selected ? Palette.Accent : Color.FromArgb(92, 111, 127), 2);
            e.Graphics.FillRectangle(brush, box); e.Graphics.DrawRectangle(pen, box.X, box.Y, box.Width, box.Height);
            using var font = new Font("Segoe UI Semibold", 17); using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            e.Graphics.DrawString((i + 1).ToString(), font, Brushes.White, box, format); targets[m.Id] = box;
        }
    }
    protected override void OnMouseClick(MouseEventArgs e) { foreach (var p in targets) if (p.Value.Contains(e.Location)) Selected?.Invoke(p.Key); base.OnMouseClick(e); }
}

internal sealed class RangeTimeline : Control
{
    private double duration, start, end, position;
    private bool dragging, endHandle;
    public event Action<double, double>? RangeChanged;
    public RangeTimeline() { DoubleBuffered = true; ResizeRedraw = true; TabStop = true; Cursor = Cursors.SizeWE; AccessibleName = "Séquence vidéo, poignées début et fin"; AccessibleDescription = "Flèches pour ajuster, touche E pour la fin, D pour le début. Les champs numériques offrent le même réglage."; }
    public void SetRange(double length, double first, double last) { duration = length; start = first; end = last; Enabled = length > 0; Invalidate(); }
    public void SetPosition(double value) { if (Math.Abs(position - value) < .05) return; position = value; Invalidate(); }
    private float X(double value) => 12 + (float)(duration <= 0 ? 0 : Math.Clamp(value / duration, 0, 1)) * Math.Max(1, Width - 24);
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); var g = e.Graphics; g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var rail = new SolidBrush(Color.FromArgb(65, 82, 98)); using var selection = new SolidBrush(Palette.Accent);
        g.FillRectangle(rail, 12, 21, Math.Max(1, Width - 24), 10);
        g.FillRectangle(selection, X(start), 21, Math.Max(0, X(end) - X(start)), 10);
        g.FillEllipse(selection, X(start) - 6, 15, 12, 22); g.FillEllipse(selection, X(end) - 6, 15, 12, 22);
        using var pen = new Pen(ForeColor, 2); g.DrawLine(pen, X(position), 11, X(position), 40);
        TextRenderer.DrawText(g, $"{start:0.000} s", Font, new Point(8, 45), ForeColor);
        TextRenderer.DrawText(g, $"{end:0.000} s", Font, new Rectangle(8, 45, Math.Max(1, Width - 16), 22), ForeColor, TextFormatFlags.Right);
        if (Focused) ControlPaint.DrawFocusRectangle(g, new Rectangle((int)X(endHandle ? end : start) - 10, 9, 20, 34));
    }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (duration <= 0 || e.Button != MouseButtons.Left) return; Focus(); endHandle = Math.Abs(e.X - X(end)) < Math.Abs(e.X - X(start)); dragging = Capture = true; MoveHandle(e.X); }
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (dragging) MoveHandle(e.X); }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); dragging = Capture = false; }
    protected override void OnMouseCaptureChanged(EventArgs e) { base.OnMouseCaptureChanged(e); if (!Capture) dragging = false; }
    private void MoveHandle(int x) => SetHandle(duration * Math.Clamp((x - 12d) / Math.Max(1, Width - 24), 0, 1));
    private void SetHandle(double value)
    {
        double limit = Math.Min(86400, duration), gap = Math.Min(.1, limit);
        if (endHandle) end = Math.Round(Math.Clamp(value, Math.Min(limit, start + gap), limit), 3);
        else start = Math.Round(Math.Clamp(value, 0, Math.Min(limit, Math.Max(0, end - gap))), 3);
        RangeChanged?.Invoke(start, end); Invalidate();
    }
    protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) is Keys.Left or Keys.Right || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.E) endHandle = true; else if (e.KeyCode == Keys.D) endHandle = false;
        else if (e.KeyCode is Keys.Left or Keys.Right) { SetHandle((endHandle ? end : start) + (e.KeyCode == Keys.Left ? -1 : 1) * (e.Control ? .001 : .04)); e.Handled = true; }
        Invalidate();
    }
}
