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
    private List<string> _installs = new();   // TOUTES les installs découvertes (plus d'« install active »)
    private List<Target> _targets = new();
    private string _lastModSig = "";          // signature disque (installs+mods GLM) au dernier LoadMods
    private bool _loadingMods;                 // garde-fou ré-entrance LoadMods

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

        // Rafraîchissement auto : quand on revient sur GenSpeed (ex. après avoir installé un mod dans
        // GenLauncher), re-scanner le tableau SI le disque a changé — sans devoir redémarrer GenSpeed.
        Activated += OnActivatedRefresh;
    }

    /// <summary>À l'activation de la fenêtre : si la signature disque (installs + mods GLM) a changé depuis le
    /// dernier chargement, recharge le tableau. Le calcul de signature est léger (énumération de dossiers, AUCUN
    /// hachage) → pas de rescan lourd tant que rien n'a bougé. Couvre « j'installe un mod dans GenLauncher puis
    /// je reviens sur GenSpeed ».</summary>
    private void OnActivatedRefresh(object? sender, EventArgs e)
    {
        if (_loadingMods || ConfigStore.Suppressed) return;
        try { if (LiveModSignature() != _lastModSig) { Log(Loc.T("log.refresh.auto")); LoadMods(); } }
        catch { /* best-effort */ }
    }

    /// <summary>Signature LÉGÈRE du paysage de mods sur le disque : chemins d'install + noms des mods GLM avec leur
    /// nombre de .gib + nb de .gib actifs à la racine (activation/désactivation). Aucune lecture d'archive ni hachage.</summary>
    private string LiveModSignature()
    {
        var sb = new System.Text.StringBuilder();
        var installs = InstallDiscovery.DiscoverAll(_config.KnownInstalls);
        foreach (var dir in installs.OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            sb.Append(dir.ToLowerInvariant()).Append('|');
            string glm = Path.Combine(dir, "GLM");
            if (Directory.Exists(glm))
                foreach (var m in Directory.EnumerateDirectories(glm).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                {
                    int gibs = 0; try { gibs = Directory.EnumerateFiles(m, "*.gib", SearchOption.AllDirectories).Count(); } catch { }
                    sb.Append(Path.GetFileName(m)).Append('=').Append(gibs).Append(';');
                }
            try { sb.Append("root:").Append(Directory.EnumerateFiles(dir, "*.gib").Count()); } catch { }
            sb.Append('\n');
        }
        return sb.ToString();
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
        cm.Items.Add(new Separator());
        Add("ctx.rename", OnRenameMod);
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
        if (string.IsNullOrEmpty(t.InstallDir)) return;
        string folder = t.Type switch
        {
            TargetType.Gib => Path.Combine(t.InstallDir, "GLM", t.Label),
            TargetType.Ini => Path.Combine(t.InstallDir, "Data", "INI"),
            _ => t.InstallDir,
        };
        if (!Directory.Exists(folder)) folder = t.InstallDir;
        try { Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = $"\"{folder}\"", UseShellExecute = true }); }
        catch { }
    }

    /// <summary>Nom affiché d'une cible : alias personnalisé si défini, sinon libellé convivial.</summary>
    private string DisplayName(string installDir, string label)
    {
        string key = installDir + "::" + label;
        return _config.ModAliases.TryGetValue(key, out var a) && !string.IsNullOrWhiteSpace(a) ? a : FriendlyLabel(label);
    }

    /// <summary>Renommer l'AFFICHAGE d'un mod dans le tableau (non destructif — n'affecte pas le patch ni le code LAN).</summary>
    private void OnRenameMod(Target t)
    {
        string? name = Dialogs.Prompt(this, Loc.T("ctx.rename"), Loc.T("rename.mod.msg"), DisplayName(t.InstallDir, t.Label));
        if (string.IsNullOrWhiteSpace(name)) return;
        string key = t.InstallDir + "::" + t.Label;
        if (name == FriendlyLabel(t.Label)) _config.ModAliases.Remove(key);   // remis au nom par défaut
        else _config.ModAliases[key] = name;
        ConfigStore.Save(_config);
        foreach (var r in _rows)
            if (r.Mod == t.Label && r.InstallDir == t.InstallDir)
                r.Display = DisplayName(t.InstallDir, t.Label);
    }

    private void WireToolbar()
    {
        Dropdown(DiagBtn, ("diag.export", OnDiagExport), ("diag.compare", OnDiagCompare), ("diag.verify", OnDiagVerify));
        Dropdown(ConfigBtn, ("wiz.cfg", OnInstallWizard), ("cfg.installs", OnCfgInstalls),
                            ("cfg.tune", OnCfgTuneMultiplayer), ("cfg.gllink", OnCfgGenLauncherUrl),
                            ("cfg.addinstall", OnCfgAddInstall), ("cfg.modsdir", OnCfgModsDir),
                            ("cfg.launcher", OnCfgLauncher),
                            ("cfg.uninstall", OnCfgUninstall),
                            ("cfg.reset", OnCfgReset), ("cfg.export", OnCfgExport), ("cfg.import", OnCfgImport));
        Dropdown(PreviewBtn, ("preview.key", () => RunPreview("key")),
                             ("preview.full", () => RunPreview("full")),
                             ("preview.mod", () => RunPreview("mod")));
        Dropdown(MpBtn, ("mp.replay", OnReplay));
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

    /// <summary>Case d'en-tête : coche / décoche TOUS les mods du tableau.</summary>
    private void OnToggleAll(object sender, RoutedEventArgs e)
    {
        bool check = (sender as CheckBox)?.IsChecked == true;
        foreach (var r in _rows) r.Sel = check;
    }

    /// <summary>Libellé d'affichage convivial (Vanilla → « Jeu de base »), localisé.</summary>
    private static string FriendlyLabel(string label) => label switch
    {
        "🎮 Vanilla" => Loc.T("vanilla.name"),
        "VANILLA (Data/INI)" => Loc.T("vanilla.ini"),
        _ => label,
    };

    private List<Target> CheckedTargets() =>
        _rows.Where(r => r.Sel && r.Target != null).Select(r => r.Target!).ToList();

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
        foreach (var (key, tb) in _catLabels) { tb.Text = Loc.T("cat." + key); tb.ToolTip = Loc.T("tip.cat." + key); }
        foreach (var (key, box) in _catBoxes) box.ToolTip = Loc.T("tip.cat." + key);
        foreach (var (key, tb) in _camHints) tb.Text = Loc.T(key);
        foreach (var kv in _camControls) kv.Value.ToolTip = Loc.T("tip.cam." + kv.Key);
        LangBtn.Content = Loc.I.Lang == 0 ? "EN" : "FR";
        if (LogBtn != null) LogBtn.Content = Loc.T(_logVisible ? "tb.log" : "tb.logshow");
        foreach (var r in _rows) r.Display = DisplayName(r.InstallDir, r.Mod);
        UpdateSpeedLabel();
        if (CamLabel != null) CamLabel.Text = _camIdx == 0 ? Loc.T("cam.default") : _camNames[_camIdx];
    }

    // ===== Liste des mods (détection réelle, TOUTES les installs) =====
    private readonly ObservableCollection<ModRow> _rows = new();

    private async void LoadMods()
    {
        if (_loadingMods) return;   // anti-ré-entrance (l'activation peut déclencher pendant un chargement)
        _loadingMods = true;
        try
        {
        Title = "GenSpeed";
        _config.KnownInstalls.RemoveAll(p => !Directory.Exists(p));
        SeedKnownFromShortcuts();   // « toujours savoir où est M2 » : réenregistre l'install GenLauncher via son raccourci Bureau
        _installs = await Task.Run(() => InstallDiscovery.DiscoverAll(_config.KnownInstalls));
        if (_installs.Count == 0)
        {
            // Rien trouvé : proposer 2 choix clairs (Installer via Steam → assistant / Indiquer un dossier),
            // plutôt qu'un sélecteur de dossier Windows brut.
            // Plus aucune install (ex. après un wipe) : VIDER le tableau pour refléter la réalité,
            // que l'utilisateur valide le dialogue ou l'annule (sinon il garderait les lignes supprimées).
            _rows.Clear(); _targets = new();
            if (!PromptNoInstall()) { Log(Loc.T("log.nogame")); return; }
            _installs = await Task.Run(() => InstallDiscovery.DiscoverAll(_config.KnownInstalls));
            if (_installs.Count == 0) { Log(Loc.T("log.nogame")); return; }   // toujours rien après le dialogue
        }
        // JSON = SOURCE DE VÉRITÉ : persister TOUTES les installs découvertes (M0 Steam inclus) dans la config,
        // pour qu'elles y figurent toujours et soient éditables. L'auto-découverte ne fait que COMPLÉTER (jamais
        // écraser les éditions de l'utilisateur) ; les chemins morts ont déjà été purgés plus haut.
        bool addedKnown = false;
        foreach (var d in _installs)
            if (!_config.KnownInstalls.Any(p => string.Equals(p.TrimEnd('\\', '/'), d.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)))
            { _config.KnownInstalls.Add(d); addedKnown = true; }
        if (addedKnown) ConfigStore.Save(_config);

        Log(string.Format(Loc.T("log.installs.found"), _installs.Count));
        AutoTune();   // calage auto (Options.ini + YAML GenLauncher), silencieux et idempotent

        var targets = new List<Target>();
        foreach (var dir in _installs)
        {
            try { targets.AddRange(await Task.Run(() => ModDetection.DetectTargets(dir))); }
            catch (Exception ex) { Log("⚠ " + InstallLabel(dir) + " : " + ex.Message); }
        }

        // Mods GenLauncher installés ailleurs (dossier GLM personnalisé, optionnel).
        if (!string.IsNullOrEmpty(_config.ModsDir) && Directory.Exists(_config.ModsDir)
            && !_installs.Any(d => string.Equals(_config.ModsDir, Path.Combine(d, "GLM"), StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var extra = await Task.Run(() => ModDetection.DetectGlmMods(_config.ModsDir!));
                if (extra.Count > 0) { targets.AddRange(extra); Log(string.Format(Loc.T("log.modsextra"), extra.Count, _config.ModsDir)); }
            }
            catch (Exception ex) { Log("⚠ " + ex.Message); }
        }

        _targets = targets;

        // Noms de groupe par install (calculés une fois — affichés en en-tête de groupe du tableau).
        var headers = _installs.ToDictionary(d => d, d => $"{InstallLabel(d)}   ·   {InstallType(d)}", StringComparer.OrdinalIgnoreCase);
        string HeaderFor(string dir) => headers.TryGetValue(dir, out var h) ? h : InstallLabel(dir);

        _rows.Clear();
        foreach (var t in targets)
            _rows.Add(new ModRow
            {
                Target = t, Mod = t.Label, Display = DisplayName(t.InstallDir, t.Label),
                InstallDir = t.InstallDir, InstallName = HeaderFor(t.InstallDir),
                Vitesse = Loc.T("orig"), Camera = Loc.T("orig"),
                Archives = t.ArchiveCount.ToString(), Ini = t.IniCount().ToString(),
                Patched = "—", Code = "…",
            });
        if (ModGrid.ItemsSource == null)
        {
            var view = new System.Windows.Data.ListCollectionView(_rows);
            view.GroupDescriptions.Add(new System.Windows.Data.PropertyGroupDescription(nameof(ModRow.InstallName)));
            ModGrid.ItemsSource = view;
        }
        Log(string.Format(Loc.T("log.detected"), targets.Count));

        int alreadyPatched = 0;
        foreach (var row in _rows.ToList())
        {
            var t = row.Target!;
            // État persistant : un mod est « patché » s'il a des sauvegardes .speedbak (survit au redémarrage).
            int patched = t.Files.Count(fp => File.Exists(fp + ".speedbak"));
            if (patched > 0)
            {
                row.Patched = $"{patched}/{t.ArchiveCount}";
                if (_config.PatchedState.TryGetValue(row.StateKey, out var ps) && !string.IsNullOrWhiteSpace(ps.Speed))
                {
                    row.Vitesse = ps.Speed;
                    row.Camera = string.IsNullOrWhiteSpace(ps.Camera) ? Loc.T("orig") : ps.Camera;
                    // Restaure les SHA du dernier patch -> protège contre la restauration d'un backup périmé.
                    if (ps.Files.Count > 0) row.PatchedFiles = new Dictionary<string, string>(ps.Files);
                }
                else
                {
                    row.Vitesse = Loc.T("patched.flag");   // patché mais réglage exact inconnu (patché hors mémoire)
                }
                alreadyPatched++;
            }
            row.Code = await CachedLanCode(t);
        }
        if (_hashCacheDirty) { ConfigStore.Save(_config); _hashCacheDirty = false; }
        if (alreadyPatched > 0) Log(string.Format(Loc.T("log.alreadypatched"), alreadyPatched));
        }
        finally
        {
            _loadingMods = false;
            try { _lastModSig = LiveModSignature(); } catch { }   // mémorise l'état disque → base de comparaison
        }
    }

    private bool _hashCacheDirty;

    /// <summary>Code LAN avec cache : ne re-hache (lourd) que si la signature mtime/taille a changé.</summary>
    private async Task<string> CachedLanCode(Target t)
    {
        string key = t.InstallDir + "::" + t.Label;
        var sig = BuildSig(t.Files);
        if (_config.HashCache.TryGetValue(key, out var ce) && SigEqual(ce.Sig, sig))
            return ce.Hash;                                   // cache valide -> instantané
        string h = await Task.Run(() => Hashing.InstallHash(t.InstallDir, t.Files).Hash);
        _config.HashCache[key] = new HashCacheEntry { Hash = h, Sig = sig };
        _hashCacheDirty = true;
        return h;
    }

    private static Dictionary<string, long[]> BuildSig(IEnumerable<string> files)
    {
        var sig = new Dictionary<string, long[]>(StringComparer.Ordinal);
        foreach (var f in files)
            try { var fi = new FileInfo(f); if (fi.Exists) sig[f] = new[] { fi.LastWriteTimeUtc.Ticks, fi.Length }; }
            catch { }
        return sig;
    }

    private static bool SigEqual(Dictionary<string, long[]> a, Dictionary<string, long[]> b)
    {
        if (a.Count != b.Count) return false;
        foreach (var kv in b)
            if (!a.TryGetValue(kv.Key, out var av) || av.Length != 2 || av[0] != kv.Value[0] || av[1] != kv.Value[1])
                return false;
        return true;
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
        public string InstallDir { get; set; } = "";    // install propriétaire (multi-installs)
        public string InstallName { get; set; } = "";   // libellé de groupe (nom · type)
        public string StateKey => InstallDir + "::" + Mod;   // clé PatchedState / résultats de patch
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

    private void Log(string msg)
    {
        LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\n");
        LogBox.ScrollToEnd();
    }
}
