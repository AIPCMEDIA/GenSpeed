using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using GenSpeed.Core;

namespace GenSpeed.App;

public partial class MainWindow : Window
{
    private Brush Fg  => (Brush)FindResource("fg");
    private Brush Dim => (Brush)FindResource("dim");

    private GenConfig _config = null!;
    private List<SpeedPreset> Speeds => _config.SpeedPresets;
    private string? _gameDir;
    private List<Target> _targets = new();

    // (clé cat, symbole, couleur pastille)
    private static readonly (string Key, string Sym, string Dot)[] Cats =
    {
        ("deplacement","×","#2ECC71"), ("projectiles","×","#2ECC71"), ("visee","×","#2ECC71"),
        ("construction","÷","#2ECC71"), ("tir","÷","#2ECC71"), ("pouvoirs","÷","#2ECC71"),
        ("deploiement","÷","#2ECC71"), ("economie_collecte","÷","#2ECC71"), ("economie_gain","×","#E67E22"),
        ("detection","×","#E74C3C"), ("soin","×","#2ECC71"), ("merite","×","#E67E22"),
    };

    private static readonly (string Var, string HintKey)[] CamVars =
    {
        ("CameraPitch", "cam.hint.pitch"), ("CameraHeight", "cam.hint.h"),
        ("MaxCameraHeight", "cam.hint.max"), ("MinCameraHeight", "cam.hint.min"),
    };
    private static readonly string[] CamAllVars =
        { "CameraPitch", "CameraYaw", "CameraHeight", "MaxCameraHeight", "MinCameraHeight", "DrawEntireTerrain" };
    private static readonly string[] CamOrderDefault =
        { "Cam haute", "Cam max", "Cam eloignee", "Vue satellite" };

    private readonly List<(string Key, TextBlock Tb)> _catLabels = new();
    private readonly List<(string Key, TextBlock Tb)> _camHints = new();
    private readonly Dictionary<string, TextBox> _catBoxes = new();
    private readonly Dictionary<string, Control> _camControls = new();

    private List<string> _camNames = new();   // [0] = sentinelle "vue par défaut"
    private int _camIdx;
    private bool _suppressFactor;

    public MainWindow()
    {
        InitializeComponent();
        _config = ConfigStore.Load();
        Loc.I.SetLanguage(_config.LastLang);

        foreach (var (_, name) in ThemeManager.Themes) ThemeBox.Items.Add(name);
        int ti = Array.FindIndex(ThemeManager.Themes, t => t.Key == _config.LastTheme);
        ThemeBox.SelectedIndex = ti < 0 ? 0 : ti;

        BuildCategoryGrid();
        BuildCameraGrid();
        SetupSpeedSlider();
        SetupCamSlider();
        RefreshTexts();

        Loc.LanguageChanged += RefreshTexts;
        WireToolbar();
        // Un clic n'importe où sur une ligne coche/décoche le mod (UX simple).
        ModGrid.AddHandler(UIElement.MouseLeftButtonUpEvent, new MouseButtonEventHandler(ModGrid_RowClick), true);
        SetupModContextMenu();
        Log(Loc.T("log.start"));
        LoadMods();
    }

    private bool _logVisible = true;
    private void OnToggleLog(object sender, RoutedEventArgs e)
    {
        _logVisible = !_logVisible;
        JournalCol.Width = new GridLength(_logVisible ? 270 : 0);
        JournalSplitCol.Width = new GridLength(_logVisible ? 6 : 0);
        JournalPanel.Visibility = _logVisible ? Visibility.Visible : Visibility.Collapsed;
        JournalSplitter.Visibility = _logVisible ? Visibility.Visible : Visibility.Collapsed;
        LogBtn.Content = Loc.T(_logVisible ? "tb.log" : "tb.logshow");
    }

    private void ModGrid_RowClick(object sender, MouseButtonEventArgs e)
    {
        var dep = e.OriginalSource as DependencyObject;
        while (dep != null && dep is not DataGridRow) dep = VisualTreeHelper.GetParent(dep);
        if (dep is DataGridRow { Item: ModRow mr }) mr.Sel = !mr.Sel;
    }

    // ===== Menu contextuel (clic droit) sur un mod =====
    private readonly List<(MenuItem Item, string Key)> _ctxItems = new();

    private void SetupModContextMenu()
    {
        var cm = new ContextMenu();
        void Add(string key, Action<Target> act)
        {
            var mi = new MenuItem { Header = Loc.T(key) };
            mi.Click += (_, _) => { var t = (ModGrid.SelectedItem as ModRow)?.Target; if (t != null) act(t); };
            _ctxItems.Add((mi, key));
            cm.Items.Add(mi);
        }
        Add("preview.key", t => RunPreview("key", t));
        Add("preview.full", t => RunPreview("full", t));
        Add("preview.mod", t => RunPreview("mod", t));
        cm.Items.Add(new Separator());
        Add("ctx.open", OpenModFolder);
        cm.Opened += (_, _) => { foreach (var (mi, key) in _ctxItems) mi.Header = Loc.T(key); };
        ModGrid.ContextMenu = cm;

        // Le clic droit sélectionne la ligne sous le curseur (cible du menu).
        ModGrid.PreviewMouseRightButtonDown += (_, e) =>
        {
            var dep = e.OriginalSource as DependencyObject;
            while (dep != null && dep is not DataGridRow) dep = VisualTreeHelper.GetParent(dep);
            if (dep is DataGridRow row) row.IsSelected = true;
        };
    }

    private void OpenModFolder(Target t)
    {
        if (_gameDir == null) return;
        string folder = t.Type == TargetType.Gib ? Path.Combine(_gameDir, "GLM", t.Label) : _gameDir;
        if (!Directory.Exists(folder)) folder = _gameDir;
        try { Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = $"\"{folder}\"", UseShellExecute = true }); }
        catch { }
    }

    private void WireToolbar()
    {
        Dropdown(DiagBtn, ("diag.export", OnDiagExport), ("diag.compare", OnDiagCompare));
        Dropdown(ConfigBtn, ("cfg.reset", OnCfgReset), ("cfg.export", OnCfgExport), ("cfg.import", OnCfgImport));
        Dropdown(PreviewBtn, ("preview.key", () => RunPreview("key")),
                             ("preview.full", () => RunPreview("full")),
                             ("preview.mod", () => RunPreview("mod")));
        Dropdown(MpBtn, ("mp.replay", OnReplay));
    }

    // ===== Aperçu =====
    private async void RunPreview(string mode, Target? target = null)
    {
        if (_gameDir == null) { Log(Loc.T("log.nogame")); return; }
        var t = target ?? CheckedTargets().FirstOrDefault();
        if (t == null) { Dialogs.Info(this, "GenSpeed", Loc.T("preview.nosel")); return; }
        bool onlyChanged = mode == "mod";
        if (onlyChanged && !t.Files.Any(fp => File.Exists(fp + ".speedbak")))
        { Dialogs.Info(this, "GenSpeed", Loc.T("preview.notpatched")); return; }

        ISet<string>? wanted = mode == "key" ? Preview.KeyVars : null;
        var (rows, patched, changed) = await Task.Run(() => Preview.Gather(_gameDir!, t, wanted, onlyChanged));
        if (rows.Count == 0) { Dialogs.Info(this, "GenSpeed", Loc.T("preview.none")); return; }

        string changedStr = patched ? string.Format(Loc.T("preview.changed"), changed) : "";
        string header = $"{t.Label}\n" + string.Format(Loc.T("preview.summary"), rows.Count, changedStr);
        PreviewWindow.Show(this, string.Format(Loc.T("preview.title"), t.Label), header, rows);
    }

    // ===== Dernier replay =====
    private void OnReplay()
    {
        var path = Replay.FindLatest();
        var fp = path != null ? Replay.Read(path) : null;
        if (fp == null) { Dialogs.Info(this, "GenSpeed", Loc.T("replay.none")); return; }
        string players = fp.Players.Count > 0 ? string.Join(", ", fp.Players) : "—";
        Dialogs.Info(this, Loc.T("replay.title"), string.Format(Loc.T("replay.body"),
            string.IsNullOrEmpty(fp.Version) ? "—" : fp.Version,
            string.IsNullOrEmpty(fp.Map) ? "—" : fp.Map,
            string.IsNullOrEmpty(fp.MapCrc) ? "—" : fp.MapCrc, players));
    }

    /// <summary>Attache un menu déroulant thémé (Popup de boutons) à un bouton de la barre d'outils.</summary>
    private void Dropdown(Button btn, params (string Key, Action Act)[] items)
    {
        var popup = new Popup { PlacementTarget = btn, Placement = PlacementMode.Bottom, StaysOpen = false };
        btn.Click += (_, _) =>
        {
            if (popup.IsOpen) { popup.IsOpen = false; return; }
            var panel = new StackPanel();
            foreach (var (key, act) in items)
            {
                var a = act;
                var b = new Button
                {
                    Content = Loc.T(key), HorizontalContentAlignment = HorizontalAlignment.Left,
                    Padding = new Thickness(12, 6, 12, 6), MinWidth = Math.Max(btn.ActualWidth, 200),
                };
                b.Click += (_, _) => { popup.IsOpen = false; a(); };
                panel.Children.Add(b);
            }
            popup.Child = new Border
            {
                BorderBrush = (Brush)FindResource("border"), BorderThickness = new Thickness(1),
                Background = (Brush)FindResource("bgFrame2"), Child = panel,
            };
            popup.IsOpen = true;
        };
    }

    // ===== Lancer GenLauncher =====
    private void OnLaunchGenLauncher(object sender, RoutedEventArgs e) => LaunchGenLauncher();

    private void LaunchGenLauncher()
    {
        if (_gameDir == null) { Log(Loc.T("log.nogame")); return; }
        string exe = Path.Combine(_gameDir, "GenLauncher.exe");
        if (!File.Exists(exe)) { Dialogs.Info(this, "GenSpeed", Loc.T("genl.notfound")); return; }
        try
        {
            Process.Start(new ProcessStartInfo { FileName = exe, WorkingDirectory = _gameDir, UseShellExecute = true, Verb = "runas" });
            Log(Loc.T("genl.launched"));
        }
        catch (System.ComponentModel.Win32Exception) { Log(Loc.T("genl.cancel")); }
    }

    /// <summary>Libellé d'affichage convivial (Vanilla → « Jeu de base »), localisé.</summary>
    private static string FriendlyLabel(string label) => label switch
    {
        "🎮 Vanilla" => Loc.T("vanilla.name"),
        "VANILLA (Data/INI)" => Loc.T("vanilla.ini"),
        _ => label,
    };

    private List<Target> CheckedTargets() =>
        (ModGrid.ItemsSource as IEnumerable<ModRow>)?.Where(r => r.Sel && r.Target != null)
            .Select(r => r.Target!).ToList() ?? new List<Target>();

    // ===== Code LAN =====
    private async void OnComputeLanCode(object sender, RoutedEventArgs e)
    {
        if (_gameDir == null) { Log(Loc.T("log.nogame")); return; }
        Log(Loc.T("lan.computing"));
        var targets = CheckedTargets();
        var r = await Task.Run(() =>
        {
            var files = ModDetection.BaseInstallFiles(_gameDir!).ToList();
            foreach (var t in targets) files.AddRange(t.Files);
            return Hashing.InstallHash(_gameDir!, files);
        });
        LanCodeLabel.Text = r.Hash;
        Log(string.Format(Loc.T("lan.done"), r.Hash, r.FileCount, r.TotalBytes / 1048576));
    }

    // ===== Diagnostic mismatch =====
    private async void OnDiagExport()
    {
        if (_gameDir == null) { Log(Loc.T("log.nogame")); return; }
        var modTargets = _targets.Where(t => t.Type == TargetType.Gib).ToList();
        var fp = await Task.Run(() => Diagnostics.Build(_gameDir!, modTargets));
        var dlg = new SaveFileDialog { Filter = "JSON|*.json", FileName = "GenSpeed-diagnostic.json" };
        if (dlg.ShowDialog() == true)
        {
            File.WriteAllText(dlg.FileName, Diagnostics.ExportJson(fp));
            Log(string.Format(Loc.T("diag.exported"), dlg.FileName));
        }
    }

    private async void OnDiagCompare()
    {
        if (_gameDir == null) { Log(Loc.T("log.nogame")); return; }
        var dlg = new OpenFileDialog { Filter = "JSON|*.json" };
        if (dlg.ShowDialog() != true) return;
        string json = File.ReadAllText(dlg.FileName);
        if (!Diagnostics.IsSyncFingerprint(json)) { Dialogs.Info(this, "GenSpeed", Loc.T("diag.badfile")); return; }
        var other = Diagnostics.Parse(json);
        var modTargets = _targets.Where(t => t.Type == TargetType.Gib).ToList();
        var mine = await Task.Run(() => Diagnostics.Build(_gameDir!, modTargets));
        DiagnosticWindow.Show(this, Diagnostics.Diff(mine, other));
    }

    // ===== Menu Config =====
    private void OnCfgReset()
    {
        SpeedSlider.Value = Math.Min(2, Speeds.Count - 1);
        ApplySpeedPreset(SpeedIdx);
        CamSlider.Value = 0;
        ApplyCamPreset(0);
        Log(Loc.T("cfg.reset.done"));
    }

    private void OnCfgExport()
    {
        var dlg = new SaveFileDialog { Filter = "JSON|*.json", FileName = "genspeed-config.json" };
        if (dlg.ShowDialog() == true)
        {
            ConfigStore.ExportTo(dlg.FileName, _config);
            Log(string.Format(Loc.T("cfg.exported"), dlg.FileName));
        }
    }

    private void OnCfgImport()
    {
        var dlg = new OpenFileDialog { Filter = "JSON|*.json" };
        if (dlg.ShowDialog() != true) return;
        var c = ConfigStore.ImportFrom(dlg.FileName);
        if (c == null) return;
        _config.SpeedPresets = c.SpeedPresets;
        _config.CameraPresets = c.CameraPresets;
        ConfigStore.Save(_config);
        SetupSpeedSlider();
        SetupCamSlider();
        Log(string.Format(Loc.T("cfg.imported"), dlg.FileName));
    }

    // ===== Textes dépendant de la langue =====
    private void RefreshTexts()
    {
        ColMod.Header = Loc.T("col.mod");
        ColSpeed.Header = Loc.T("col.speed");
        ColCamera.Header = Loc.T("col.camera");
        ColArchives.Header = Loc.T("col.archives");
        ColIni.Header = Loc.T("col.ini");
        ColPatched.Header = Loc.T("col.patched");
        ColCode.Header = Loc.T("col.code");
        foreach (var (key, tb) in _catLabels) tb.Text = Loc.T("cat." + key);
        foreach (var (key, tb) in _camHints) tb.Text = Loc.T(key);
        LangBtn.Content = Loc.I.Lang == 0 ? "EN" : "FR";
        if (LogBtn != null) LogBtn.Content = Loc.T(_logVisible ? "tb.log" : "tb.logshow");
        if (ModGrid.ItemsSource is IEnumerable<ModRow> mrows)
            foreach (var r in mrows) r.Display = FriendlyLabel(r.Mod);
        UpdateSpeedLabel();
        if (CamLabel != null) CamLabel.Text = _camIdx == 0 ? Loc.T("cam.default") : _camNames[_camIdx];
    }

    // ===== Liste des mods (détection réelle) =====
    private async void LoadMods()
    {
        _gameDir = (_config.GameDir != null && Directory.Exists(_config.GameDir))
            ? _config.GameDir : GameLocator.Detect();
        if (_gameDir == null) _gameDir = AskGameDir();   // 1er lancement : auto-détection échouée → sélection manuelle
        if (_gameDir == null) { Log(Loc.T("log.nogame")); return; }
        if (_config.GameDir != _gameDir) { _config.GameDir = _gameDir; ConfigStore.Save(_config); }
        Log(string.Format(Loc.T("log.gamedir"), _gameDir));

        List<Target> targets;
        try { targets = await Task.Run(() => ModDetection.DetectTargets(_gameDir)); }
        catch (Exception ex) { Log("⚠ " + ex.Message); return; }
        _targets = targets;

        var rows = new ObservableCollection<ModRow>();
        foreach (var t in targets)
            rows.Add(new ModRow
            {
                Target = t, Mod = t.Label, Display = FriendlyLabel(t.Label),
                Vitesse = Loc.T("orig"), Camera = Loc.T("orig"),
                Archives = t.ArchiveCount.ToString(), Ini = t.IniCount().ToString(),
                Patched = "—", Code = "…",
            });
        ModGrid.ItemsSource = rows;
        Log(string.Format(Loc.T("log.detected"), targets.Count));

        foreach (var row in rows)
        {
            var t = row.Target!;
            row.Code = await Task.Run(() => Hashing.InstallHash(_gameDir!, t.Files).Hash);
        }
    }

    /// <summary>Sélection manuelle du dossier de jeu (fallback si auto-détection échoue).</summary>
    private string? AskGameDir()
    {
        while (true)
        {
            var dlg = new OpenFolderDialog { Title = Loc.T("pick.title") };
            if (dlg.ShowDialog() != true) return null;
            if (GameLocator.IsZhFolder(dlg.FolderName)) return dlg.FolderName;
            Dialogs.Info(this, "GenSpeed", Loc.T("pick.invalid"));
        }
    }

    private sealed class ModRow : INotifyPropertyChanged
    {
        public Target? Target { get; init; }
        public Dictionary<string, string> PatchedFiles { get; set; } = new();
        private bool _sel;
        public bool Sel { get => _sel; set { if (_sel != value) { _sel = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Sel))); } } }
        public string Mod { get; set; } = "";   // label interne (clé de patch) — NE PAS afficher pour Vanilla
        private string _display = "";
        public string Display { get => _display; set { if (_display != value) { _display = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Display))); } } }
        public string Archives { get; set; } = "";
        public string Ini { get; set; } = "";

        private string _vitesse = "", _camera = "", _patched = "", _code = "";
        public string Vitesse { get => _vitesse; set => Set(ref _vitesse, value); }
        public string Camera  { get => _camera;  set => Set(ref _camera, value); }
        public string Patched { get => _patched; set => Set(ref _patched, value); }
        public string Code    { get => _code;    set => Set(ref _code, value); }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Set(ref string field, string value, [CallerMemberName] string? name = null)
        {
            if (field == value) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    // ===== Grilles =====
    private void BuildCategoryGrid()
    {
        foreach (var c in Cats)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 14, 3) };
            sp.Children.Add(new TextBlock
            {
                Text = "●", Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(c.Dot))
            });
            var lbl = new TextBlock
            {
                Width = 100, VerticalAlignment = VerticalAlignment.Center,
                Foreground = Fg, TextTrimming = TextTrimming.CharacterEllipsis
            };
            _catLabels.Add((c.Key, lbl));
            sp.Children.Add(lbl);
            sp.Children.Add(new TextBlock
            {
                Text = c.Sym, Margin = new Thickness(0, 0, 3, 0), VerticalAlignment = VerticalAlignment.Center,
                Foreground = Dim
            });
            var box = new TextBox { Text = c.Key == "detection" ? "1" : "2.0", Width = 46,
                                    VerticalContentAlignment = VerticalAlignment.Center };
            _catBoxes[c.Key] = box;
            sp.Children.Add(box);
            CatGrid.Children.Add(sp);
        }
    }

    private void BuildCameraGrid()
    {
        CamGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        CamGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        CamGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        int r = 0;
        foreach (var (var, hintKey) in CamVars)
        {
            CamGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var lbl = new TextBlock { Text = var, Foreground = Dim, FontFamily = new FontFamily("Consolas"),
                Margin = new Thickness(0, 3, 6, 3), HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(lbl, r); Grid.SetColumn(lbl, 0); CamGrid.Children.Add(lbl);

            var tb = new TextBox { Width = 70, Margin = new Thickness(0, 3, 0, 3) };
            _camControls[var] = tb;
            Grid.SetRow(tb, r); Grid.SetColumn(tb, 1); CamGrid.Children.Add(tb);

            var hint = new TextBlock { Foreground = Dim, FontSize = 11, Margin = new Thickness(6, 3, 0, 3),
                VerticalAlignment = VerticalAlignment.Center };
            _camHints.Add((hintKey, hint));
            Grid.SetRow(hint, r); Grid.SetColumn(hint, 2); CamGrid.Children.Add(hint);
            r++;
        }

        CamGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var dl = new TextBlock { Text = "DrawEntireTerrain", Foreground = Dim, FontFamily = new FontFamily("Consolas"),
            Margin = new Thickness(0, 3, 6, 3), HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center };
        Grid.SetRow(dl, r); Grid.SetColumn(dl, 0); CamGrid.Children.Add(dl);
        var cb = new ComboBox { Width = 70, Margin = new Thickness(0, 3, 0, 3) };
        cb.Items.Add(""); cb.Items.Add("Yes"); cb.Items.Add("No"); cb.SelectedIndex = 0;
        _camControls["DrawEntireTerrain"] = cb;
        Grid.SetRow(cb, r); Grid.SetColumn(cb, 1); CamGrid.Children.Add(cb);
        var dh = new TextBlock { Foreground = Dim, FontSize = 11, Margin = new Thickness(6, 3, 0, 3),
            VerticalAlignment = VerticalAlignment.Center };
        _camHints.Add(("cam.hint.terrain", dh));
        Grid.SetRow(dh, r); Grid.SetColumn(dh, 2); CamGrid.Children.Add(dh);
    }

    // ===== Slider vitesse =====
    private static string Fmt(double v) => Math.Round(v, 2).ToString(CultureInfo.InvariantCulture);

    private void SetupSpeedSlider()
    {
        SpeedSlider.Maximum = Math.Max(1, Speeds.Count - 1);
        int init = Math.Min(2, Speeds.Count - 1);
        if ((int)Math.Round(SpeedSlider.Value) == init) { UpdateSpeedLabel(); ApplySpeedPreset(init); }
        else SpeedSlider.Value = init;
    }

    private void SpeedSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_config == null) return;
        int i = Math.Clamp((int)Math.Round(SpeedSlider.Value), 0, Speeds.Count - 1);
        UpdateSpeedLabel();
        ApplySpeedPreset(i);
    }

    private void UpdateSpeedLabel()
    {
        if (SpeedLabel == null || _config == null) return;
        int i = Math.Clamp((int)Math.Round(SpeedSlider.Value), 0, Speeds.Count - 1);
        var p = Speeds[i];
        SpeedLabel.Text = $"{p.Name}  (×{p.Factor:g})";
    }

    private void ApplySpeedPreset(int i)
    {
        if (_catBoxes.Count == 0) return;
        var p = Speeds[i];
        _suppressFactor = true;
        FactorBox.Text = Fmt(p.Factor);
        _suppressFactor = false;
        foreach (var (key, _, _) in Cats)
        {
            double v = p.Cats != null && p.Cats.TryGetValue(key, out var cv) ? cv
                     : key == "detection" ? 1.0 : p.Factor;
            _catBoxes[key].Text = Fmt(v);
        }
    }

    private void FactorBox_Changed(object sender, TextChangedEventArgs e)
    {
        if (_suppressFactor || _catBoxes.Count == 0) return;
        if (!double.TryParse(FactorBox.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var f))
            return;
        foreach (var (key, box) in _catBoxes)
            if (key != "detection") box.Text = Fmt(f);
    }

    private int SpeedIdx => Math.Clamp((int)Math.Round(SpeedSlider.Value), 0, Speeds.Count - 1);

    private void OnSpeedSave(object sender, RoutedEventArgs e)
    {
        var p = Speeds[SpeedIdx];
        if (p.Locked) { Dialogs.Info(this, "GenSpeed", string.Format(Loc.T("msg.locked"), p.Name)); return; }
        if (!double.TryParse(FactorBox.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var f)) return;
        var cats = new Dictionary<string, double>();
        foreach (var (key, _, _) in Cats)
        {
            if (key == "detection") continue;
            if (double.TryParse(_catBoxes[key].Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
                && Math.Abs(v - f) > 0.001)
                cats[key] = Math.Round(v, 2);
        }
        p.Factor = Math.Round(f, 2);
        p.Cats = cats.Count > 0 ? cats : null;
        ConfigStore.Save(_config);
        UpdateSpeedLabel();
        Log(string.Format(Loc.T("log.psaved"), p.Name));
    }

    private void OnSpeedNew(object sender, RoutedEventArgs e)
    {
        string? name = Dialogs.Prompt(this, Loc.T("dlg.newspeed"), Loc.T("dlg.name"));
        if (string.IsNullOrWhiteSpace(name)) return;
        if (Speeds.Any(s => s.Name == name)) { Dialogs.Info(this, "GenSpeed", string.Format(Loc.T("msg.exists"), name)); return; }
        double.TryParse(FactorBox.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var f);
        var cats = new Dictionary<string, double>();
        foreach (var (key, _, _) in Cats)
        {
            if (key == "detection") continue;
            if (double.TryParse(_catBoxes[key].Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
                && Math.Abs(v - f) > 0.001) cats[key] = Math.Round(v, 2);
        }
        Speeds.Add(new SpeedPreset { Name = name, Locked = false, Factor = Math.Round(f, 2), Cats = cats.Count > 0 ? cats : null });
        ConfigStore.Save(_config);
        SpeedSlider.Maximum = Speeds.Count - 1;
        SpeedSlider.Value = Speeds.Count - 1;
        Log(string.Format(Loc.T("log.pnew"), name));
    }

    private void OnSpeedRename(object sender, RoutedEventArgs e)
    {
        var p = Speeds[SpeedIdx];
        if (p.Locked) { Dialogs.Info(this, "GenSpeed", string.Format(Loc.T("msg.locked"), p.Name)); return; }
        string? name = Dialogs.Prompt(this, Loc.T("dlg.rename"), Loc.T("dlg.newname"), p.Name);
        if (string.IsNullOrWhiteSpace(name)) return;
        if (Speeds.Any(s => s.Name == name && s != p)) { Dialogs.Info(this, "GenSpeed", string.Format(Loc.T("msg.exists"), name)); return; }
        string old = p.Name; p.Name = name;
        ConfigStore.Save(_config);
        UpdateSpeedLabel();
        Log(string.Format(Loc.T("log.prenamed"), old, name));
    }

    private void OnSpeedDelete(object sender, RoutedEventArgs e)
    {
        int i = SpeedIdx; var p = Speeds[i];
        if (p.Locked) { Dialogs.Info(this, "GenSpeed", string.Format(Loc.T("msg.locked"), p.Name)); return; }
        if (Speeds.Count <= 1) { Dialogs.Info(this, "GenSpeed", Loc.T("msg.minone")); return; }
        if (!Dialogs.Confirm(this, Loc.T("crud.delete"), string.Format(Loc.T("msg.delconfirm"), p.Name))) return;
        string name = p.Name;
        Speeds.RemoveAt(i);
        ConfigStore.Save(_config);
        SpeedSlider.Maximum = Speeds.Count - 1;
        int ni = Math.Min(i, Speeds.Count - 1);
        if ((int)Math.Round(SpeedSlider.Value) == ni) { UpdateSpeedLabel(); ApplySpeedPreset(ni); }
        else SpeedSlider.Value = ni;
        Log(string.Format(Loc.T("log.pdeleted"), name));
    }

    // ===== Slider caméra =====
    private List<string> CamOrder()
    {
        var order = CamOrderDefault.Where(n => _config.CameraPresets.ContainsKey(n)).ToList();
        var extras = _config.CameraPresets.Keys.Where(n => !order.Contains(n) && n != "Reset camera");
        return new List<string> { "__default__" }.Concat(order).Concat(extras).ToList();
    }

    private void SetupCamSlider()
    {
        _camNames = CamOrder();
        CamSlider.Maximum = Math.Max(1, _camNames.Count - 1);
        _camIdx = 0;
        if ((int)Math.Round(CamSlider.Value) == 0) { if (CamLabel != null) CamLabel.Text = Loc.T("cam.default"); ApplyCamPreset(0); }
        else CamSlider.Value = 0;
    }

    private void CamSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_config == null || _camControls.Count == 0 || CamLabel == null) return;
        _camIdx = Math.Clamp((int)Math.Round(CamSlider.Value), 0, _camNames.Count - 1);
        CamLabel.Text = _camIdx == 0 ? Loc.T("cam.default") : _camNames[_camIdx];
        ApplyCamPreset(_camIdx);
    }

    private void ApplyCamPreset(int i)
    {
        if (_camControls.Count == 0) return;
        Dictionary<string, string>? vals = (i > 0 && _config.CameraPresets.TryGetValue(_camNames[i], out var v)) ? v : null;
        foreach (var (var, _) in CamVars)
            if (_camControls[var] is TextBox tb)
                tb.Text = vals != null && vals.TryGetValue(var, out var vv) ? vv : "";
        if (_camControls["DrawEntireTerrain"] is ComboBox cb)
            cb.SelectedItem = vals != null && vals.TryGetValue("DrawEntireTerrain", out var dv) ? dv : "";
    }

    private Dictionary<string, string> ReadCamRaw()
    {
        var d = new Dictionary<string, string>();
        foreach (var var in CamAllVars)
        {
            if (var == "CameraYaw") { d[var] = ""; continue; }
            if (_camControls.TryGetValue(var, out var c))
                d[var] = c is ComboBox cb ? cb.SelectedItem as string ?? "" : ((TextBox)c).Text.Trim();
        }
        return d;
    }

    private void OnCamSave(object sender, RoutedEventArgs e)
    {
        if (_camIdx == 0) { Dialogs.Info(this, "GenSpeed", Loc.T("msg.camlocked")); return; }
        string name = _camNames[_camIdx];
        _config.CameraPresets[name] = ReadCamRaw();
        ConfigStore.Save(_config);
        Log(string.Format(Loc.T("log.psaved"), name));
    }

    private void OnCamNew(object sender, RoutedEventArgs e)
    {
        string? name = Dialogs.Prompt(this, Loc.T("dlg.newcam"), Loc.T("dlg.name"));
        if (string.IsNullOrWhiteSpace(name)) return;
        if (_config.CameraPresets.ContainsKey(name)) { Dialogs.Info(this, "GenSpeed", string.Format(Loc.T("msg.exists"), name)); return; }
        _config.CameraPresets[name] = ReadCamRaw();
        ConfigStore.Save(_config);
        SetupCamSlider();
        int ni = _camNames.IndexOf(name);
        if (ni > 0) CamSlider.Value = ni;
        Log(string.Format(Loc.T("log.pnew"), name));
    }

    private void OnCamRename(object sender, RoutedEventArgs e)
    {
        if (_camIdx == 0) { Dialogs.Info(this, "GenSpeed", Loc.T("msg.camlocked")); return; }
        string old = _camNames[_camIdx];
        string? name = Dialogs.Prompt(this, Loc.T("dlg.rename"), Loc.T("dlg.newname"), old);
        if (string.IsNullOrWhiteSpace(name) || name == old) return;
        if (_config.CameraPresets.ContainsKey(name)) { Dialogs.Info(this, "GenSpeed", string.Format(Loc.T("msg.exists"), name)); return; }
        _config.CameraPresets[name] = _config.CameraPresets[old];
        _config.CameraPresets.Remove(old);
        ConfigStore.Save(_config);
        SetupCamSlider();
        int ni = _camNames.IndexOf(name);
        if (ni > 0) CamSlider.Value = ni;
        Log(string.Format(Loc.T("log.prenamed"), old, name));
    }

    private void OnCamDelete(object sender, RoutedEventArgs e)
    {
        if (_camIdx == 0) { Dialogs.Info(this, "GenSpeed", Loc.T("msg.camlocked")); return; }
        string name = _camNames[_camIdx];
        if (!Dialogs.Confirm(this, Loc.T("crud.delete"), string.Format(Loc.T("msg.delconfirm"), name))) return;
        _config.CameraPresets.Remove(name);
        ConfigStore.Save(_config);
        SetupCamSlider();
        CamSlider.Value = 0;
        Log(string.Format(Loc.T("log.pdeleted"), name));
    }

    // ===== Thème / langue (persistés) =====
    private void ThemeBox_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeBox.SelectedIndex < 0) return;
        string key = ThemeManager.Themes[ThemeBox.SelectedIndex].Key;
        ThemeManager.Apply(key);
        if (_config != null) { _config.LastTheme = key; ConfigStore.Save(_config); }
    }

    private void LangBtn_Click(object sender, RoutedEventArgs e)
    {
        Loc.I.SetLanguage(1 - Loc.I.Lang);
        if (_config != null) { _config.LastLang = Loc.I.Lang; ConfigStore.Save(_config); }
    }

    // ===== Appliquer / Annuler (élévation UAC) =====
    private async void OnApply(object sender, RoutedEventArgs e) => await RunPatch("apply");
    private async void OnRestore(object sender, RoutedEventArgs e) => await RunPatch("restore");

    private async Task RunPatch(string mode)
    {
        if (_gameDir == null) { Log(Loc.T("log.nogame")); return; }
        var rows = (ModGrid.ItemsSource as IEnumerable<ModRow>)?
                   .Where(r => r.Sel && r.Target != null).ToList() ?? new List<ModRow>();
        if (rows.Count == 0) { Log(Loc.T("log.nosel")); return; }

        if (mode == "apply" &&
            !Dialogs.ConfirmApply(this, rows.Select(r => FriendlyLabel(r.Mod)), BuildChangeSummary(), _gameDir))
            return;

        var job = new PatchJob
        {
            Mode = mode, GameDir = _gameDir, Factors = ReadFactors(), Cam = ReadCam(),
            Labels = rows.Select(r => r.Mod).ToList(),
            PrevHashes = rows.ToDictionary(r => r.Mod, r => r.PatchedFiles),
            ResultPath = Path.Combine(Path.GetTempPath(), $"genspeed_result_{Guid.NewGuid():N}.json"),
        };
        string jobPath = Path.Combine(Path.GetTempPath(), $"genspeed_job_{Guid.NewGuid():N}.json");
        File.WriteAllText(jobPath, JsonSerializer.Serialize(job));

        ApplyBtn.IsEnabled = RestoreBtn.IsEnabled = false;
        Log(Loc.T(mode == "apply" ? "log.applying" : "log.restoring"));
        if (mode == "apply")
            Log($"   ⚡ {SpeedLabel.Text}  ·  📷 {CamLabel.Text}  →  {string.Join(", ", rows.Select(r => FriendlyLabel(r.Mod)))}");
        try
        {
            int code = await RunElevated(mode == "apply" ? "--apply" : "--restore", jobPath);
            if (code < 0) { Log(Loc.T("log.uaccancel")); return; }
            PatchResult? res = File.Exists(job.ResultPath)
                ? JsonSerializer.Deserialize<PatchResult>(File.ReadAllText(job.ResultPath)) : null;
            if (res == null) { Log("⚠ " + Loc.T("log.noresult")); return; }
            foreach (var err in res.Errors) Log("⚠ " + err);
            bool camApplied = mode == "apply" &&
                ReadCam().Any(kv => kv.Key != "CameraYaw" && !string.IsNullOrEmpty(kv.Value));
            foreach (var r in rows)
            {
                if (!res.Patched.TryGetValue(r.Mod, out var pf)) continue;
                r.PatchedFiles = pf;
                if (mode == "apply")
                {
                    r.Patched = $"{pf.Count}/{r.Target!.ArchiveCount}";
                    r.Vitesse = SpeedLabel.Text;
                    r.Camera = camApplied ? (_camIdx > 0 ? CamLabel.Text : Loc.T("cam.custom")) : Loc.T("orig");
                    Log($"   • {FriendlyLabel(r.Mod)} : {pf.Count}/{r.Target!.ArchiveCount} " + Loc.T("log.filespatched"));
                }
                else
                {
                    r.Patched = "—"; r.Vitesse = Loc.T("orig"); r.Camera = Loc.T("orig");
                    Log($"   • {FriendlyLabel(r.Mod)} : " + Loc.T("log.restoredmod"));
                }
            }
            Log(Loc.T(mode == "apply" ? "log.applied" : "log.restored"));
            foreach (var r in rows)
            {
                var t = r.Target!;
                r.Code = await Task.Run(() => Hashing.InstallHash(_gameDir!, t.Files).Hash);
            }

            if (mode == "apply")
            {
                var lan = await Task.Run(() =>
                {
                    var files = ModDetection.BaseInstallFiles(_gameDir!).ToList();
                    foreach (var r in rows) files.AddRange(r.Target!.Files);
                    return Hashing.InstallHash(_gameDir!, files);
                });
                LanCodeLabel.Text = lan.Hash;
                Dialogs.ApplyResult(this, Loc.T("result.body"), lan.Hash, LaunchGenLauncher);
            }
        }
        finally
        {
            ApplyBtn.IsEnabled = RestoreBtn.IsEnabled = true;
            try { File.Delete(jobPath); File.Delete(job.ResultPath); } catch { }
        }
    }

    private static Task<int> RunElevated(string verbArg, string jobPath) => Task.Run(() =>
    {
        try
        {
            var psi = new ProcessStartInfo { FileName = Environment.ProcessPath!, UseShellExecute = true, Verb = "runas" };
            psi.ArgumentList.Add(verbArg);
            psi.ArgumentList.Add(jobPath);
            var p = Process.Start(psi);
            if (p == null) return -1;
            p.WaitForExit();
            return p.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception) { return -1; }
    });

    /// <summary>Résumé « joueur » de ce que le patch va changer (par catégorie + caméra).</summary>
    private List<string> BuildChangeSummary()
    {
        var lines = new List<string>();
        foreach (var (key, _, _) in Cats)
        {
            if (_catBoxes.TryGetValue(key, out var box) &&
                double.TryParse(box.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var f) &&
                Math.Abs(f - 1) > 0.001)
                lines.Add(string.Format(Loc.T("fx." + key), Fmt(f)));
        }
        var cam = ReadCam();
        if (cam.Any(kv => kv.Key != "CameraYaw" && !string.IsNullOrEmpty(kv.Value)))
            lines.Add(string.Format(Loc.T("fx.camera"), _camIdx > 0 ? CamLabel.Text : Loc.T("cam.custom")));
        if (lines.Count == 0) lines.Add(Loc.T("fx.none"));
        return lines;
    }

    private Dictionary<string, double> ReadFactors()
    {
        var d = new Dictionary<string, double>();
        foreach (var (key, _, _) in Cats)
            if (_catBoxes.TryGetValue(key, out var box) &&
                double.TryParse(box.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                d[key] = v;
        return d;
    }

    private Dictionary<string, string?> ReadCam()
    {
        var d = new Dictionary<string, string?> { ["CameraYaw"] = "" };
        foreach (var (var, _) in CamVars)
            if (_camControls.TryGetValue(var, out var c) && c is TextBox tb) d[var] = tb.Text.Trim();
        if (_camControls.TryGetValue("DrawEntireTerrain", out var cc) && cc is ComboBox cb)
            d["DrawEntireTerrain"] = cb.SelectedItem as string ?? "";
        return d;
    }

    private void Log(string msg)
    {
        LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\n");
        LogBox.ScrollToEnd();
    }
}
