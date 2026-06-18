using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Microsoft.Win32;
using GenSpeed.Core;

namespace GenSpeed.App;

/// <summary>Actions du HUB (assistant) branchées sur la fenêtre principale — on ORCHESTRE l'existant.
/// Toutes optionnelles : un bouton n'apparaît que si son action est fournie.</summary>
public sealed record WizardActions(
    // Actions « retour au menu » : possédées par l'assistant (owner) et awaitables → l'assistant reste ouvert.
    System.Func<Window, System.Threading.Tasks.Task>? OpenForkCatalog = null,   // catalogue de mods fork (ForksWindow)
    System.Func<Window, System.Threading.Tasks.Task>? OpenOptions = null,       // options de jeu (GameOptionsWindow)
    System.Func<Window, System.Threading.Tasks.Task>? OpenUninstall = null,     // désinstall — AFFINER (page à cocher)
    System.Func<Window, System.Threading.Tasks.Task>? OpenUninstallAll = null,  // désinstall — TOUT direct (sans page)
    System.Func<Window, System.Threading.Tasks.Task>? OpenDiagnostic = null,    // diagnostic mismatch / parité LAN
    System.Action? OpenTable = null,                       // « affiner » vitesse/caméra → fenêtre principale (tableau)
    // Vitesse/caméra : pousse les facteurs/caméra (valeurs BRUTES) du composant partagé de l'assistant vers le moteur
    // de patch ; applique aux lignes COCHÉES dans le tableau ci-dessous.
    System.Action<Window, System.Collections.Generic.IReadOnlyDictionary<string, double>, System.Collections.Generic.IReadOnlyDictionary<string, string?>>? ApplySpeedCamRawAll = null,
    // Construit le tableau des jeux/mods (cases à cocher) que l'assistant insère au-dessus des blocs vitesse/caméra.
    System.Func<FrameworkElement>? BuildModTable = null,
    // « Revenir à l'original » : restaure (déspeede) les lignes COCHÉES dans le tableau.
    System.Action<Window>? RestoreSelected = null,
    System.Func<Window, System.Threading.Tasks.Task>? LaunchGame = null,  // lancer un jeu/mod (sélecteur d'install)
    System.Action<Window, string>? LaunchInstall = null);  // lancer l'exe d'une install PRÉCISE (boutons par jeu)

/// <summary>Assistant d'installation propre (Phase B — UI). Guide l'utilisateur à travers 4 objectifs :
/// garder le jeu d'origine VIERGE (M0), créer une base saine (M1 = copie + GenPatcher), créer une install
/// jouable (M2 = base + mods/outils), ou préparer un fork (copie d'une base vierge où coller le mod autonome).
///
/// 100% local et non destructif pour la source : la copie est faite par robocopy via <see cref="InstallManager"/>.
/// Le cycle de vie Steam (installer le jeu absent) passe par le protocole steam:// — Steam valide, GenSpeed ne
/// télécharge rien lui-même. Voir [[install-assistant-design]]. L'orchestration GenPatcher (CLI) est Phase C :
/// ici on guide l'étape, on ne l'automatise pas encore.</summary>
public sealed class InstallWizardWindow : Window
{
    // Ordre OBJECTIF-D'ABORD : Goal (que veux-tu ?) → Source (garantir le jeu de base) → [Destination] →
    // Options → [Run] → Done. Termes utilisateur SANS « M » (M0/M1/Mx = interne seulement).
    private enum Step { Goal, Source, Waiting, Destination, Options, Run, Done, SpeedAll }
    // Modèle GenPatcher-free, M0 reste VIERGE et sert de source UNIQUE (le jeu Steam est re-téléchargeable,
    // donc pas de master de sauvegarde séparé). KeepVanilla = « juste jouer » (jeu de base seul, sans copie) ;
    // GenLauncher = M1 = COPIE de M0 + GenLauncher ; Fork = Mx = COPIE de M0 + fork autonome (Reborn Omega…).
    // All = Installation complète : M1 (GenLauncher) PUIS ouverture du catalogue pour un fork (M2).
    private enum Goal { KeepVanilla, GenLauncher, Fork, All }

    private static Brush B(string key) => (Brush)Application.Current.FindResource(key);
    private static Style? St(string key) => Application.Current.TryFindResource(key) as Style;

    private readonly GenConfig _config;
    private readonly System.Action<string> _log;
    private readonly System.Action<string> _register;   // install normale → tableau (EnsureInstallListed + LoadMods)
    private readonly WizardActions _act;                 // actions du HUB branchées sur la fenêtre principale
    private System.Func<Window, System.Threading.Tasks.Task>? _openForkCatalog => _act.OpenForkCatalog;

    private Step _step = Step.Goal;
    private Goal _goal = Goal.GenLauncher;
    private string? _sourceDir;
    private string? _destDir;
    private CopyResult? _copyResult;

    // Suivi auto de l'install Steam + initialisation (étape Waiting).
    private System.Windows.Threading.DispatcherTimer? _poll;
    private string? _watchAppId;     // appli Steam surveillée
    private bool _initPhase;         // false = install en cours ; true = init en cours
    private bool _gameSeen;          // process du jeu vu (pour détecter sa fermeture = init fini)
    private int _closeChecks;        // ticks écoulés depuis la fermeture du jeu (délai de grâce écriture Options.ini)
    private bool _polling;           // garde anti-ré-entrance (les Dialogs modaux laissent le timer ticker)
    private int _progress;           // % d'install Steam
    private TextBlock? _waitText;
    private ProgressBar? _waitBar;
    private bool _glTriggered;       // auto-install GenLauncher déclenchée une seule fois
    private bool _glReady;           // GenLauncher posé + calé (→ écran final affiche le bouton « Lancer »)
    private string? _glExePath;      // chemin de GenLauncher.exe posé
    private bool _prereqMode;        // écran d'attente = installation des prérequis VC++/DirectX
    private List<string> _installQueue = new();   // jeux Steam restant à installer (ex. ZH après Generals)

    private readonly StackPanel _body = new() { Margin = new Thickness(20, 16, 20, 16) };
    private readonly StackPanel _footer = new()
        { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(16, 8, 16, 12) };

    public static void Show(Window owner, GenConfig config, System.Action<string> log,
                            System.Action<string> register, WizardActions? actions = null)
        // NON-modal : au démarrage l'owner (fenêtre principale) n'est PAS affiché (l'assistant est la 1re fenêtre
        // visible → aucun clignotement du tableau). ShowDialog l'exigerait visible ; Show() non.
        => new InstallWizardWindow(owner, config, log, register, actions).Show();

    private InstallWizardWindow(Window owner, GenConfig config, System.Action<string> log,
                               System.Action<string> register, WizardActions? actions)
    {
        _config = config; _log = log; _register = register; _act = actions ?? new WizardActions();

        // M0 = source UNIQUE (install vierge, Steam de préférence) : auto-détectée → pas de question source.
        // Objectif-d'abord : on démarre TOUJOURS sur le choix d'objectif (le jeu de base est garanti ensuite).
        _sourceDir = AutoDetectM0();

        Title = Loc.T("wiz.app.title"); _ownerWin = owner;
        // L'owner (fenêtre principale) peut être DÉJÀ affiché (bascule depuis le mode avancé) ou PAS ENCORE
        // (démarrage : l'assistant est la 1re fenêtre). On n'attache l'Owner WPF et on ne centre/dimensionne sur lui
        // que s'il est visible (Owner exige une fenêtre déjà affichée). Sinon : taille par défaut, centré écran.
        bool ownerVisible = owner != null && owner.IsVisible;
        if (ownerVisible) Owner = owner;
        // Une fois l'assistant affiché, on MASQUE la fenêtre principale SI elle était visible (sinon en déplaçant
        // l'assistant on verrait le tableau derrière). Restaurée seulement en « Mode avancé » (voir OnClosed).
        Loaded += (_, _) => { if (_ownerWin is { IsVisible: true }) _ownerWin.Hide(); };
        // Revenir sur l'assistant (ex. après avoir installé un mod dans GenLauncher) → rafraîchir l'accueil SI le
        // paysage des installs/mods a changé (signature légère) — sinon pas de re-rendu (zéro clignotement).
        Activated += (_, _) =>
        {
            if (!IsLoaded || _step != Step.Goal) return;
            var sig = HubSignature();
            if (sig != _lastHubSig) { _lastHubSig = sig; Render(); }
        };
        Width = ownerVisible && owner!.ActualWidth > 200 ? owner.ActualWidth : 1100;
        Height = ownerVisible && owner!.ActualHeight > 200 ? owner.ActualHeight : 720;
        WindowStartupLocation = ownerVisible ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
        Background = B("bgRoot"); Foreground = B("fg");
        FontFamily = new FontFamily("Segoe UI"); FontSize = 13;

        var root = new DockPanel();
        Content = root;

        // En-tête : logo + « GenSpeed Assistant » à gauche ; thème + langue + « Mode avancé » à droite.
        var head = new DockPanel { Margin = new Thickness(20, 14, 20, 6), LastChildFill = false };
        var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var logo = LogoImage(34); if (logo != null) left.Children.Add(logo);
        left.Children.Add(new TextBlock { Text = "GenSpeed Assistant", Foreground = B("accent"),
            FontFamily = new FontFamily("Consolas"), FontWeight = FontWeights.Bold, FontSize = 20,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) });
        // Version + horodatage de l'EXE : vérifier d'un coup d'œil qu'on lance bien le dernier build.
        left.Children.Add(new TextBlock { Text = BuildInfo.Label(), Foreground = B("dim"), FontSize = 11,
            FontFamily = new FontFamily("Consolas"), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(10, 0, 0, 3) });
        DockPanel.SetDock(left, Dock.Left); head.Children.Add(left);

        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var themeCombo = new ComboBox { Width = 140, Margin = new Thickness(0, 0, 8, 0) };
        foreach (var (_, name) in ThemeManager.Themes) themeCombo.Items.Add(name);
        int ti = Array.FindIndex(ThemeManager.Themes, t => t.Key == _config.LastTheme); themeCombo.SelectedIndex = ti < 0 ? 0 : ti;
        themeCombo.SelectionChanged += (_, _) =>
        {
            if (themeCombo.SelectedIndex < 0) return;
            string k = ThemeManager.Themes[themeCombo.SelectedIndex].Key;
            ThemeManager.Apply(k); _config.LastTheme = k; ConfigStore.Save(_config);
            Background = B("bgRoot"); Foreground = B("fg"); Render();   // re-rend avec les nouvelles couleurs
        };
        right.Children.Add(themeCombo);
        var langBtn = new Button { Content = Loc.I.Lang == 0 ? "EN" : "FR", Width = 44, Margin = new Thickness(0, 0, 8, 0) };
        langBtn.Click += (_, _) =>
        {
            Loc.I.SetLanguage(1 - Loc.I.Lang); _config.LastLang = Loc.I.Lang; ConfigStore.Save(_config);
            Title = Loc.T("wiz.app.title"); Render();
        };
        right.Children.Add(langBtn);
        var adv = new Button { Content = Loc.T("wiz.advanced"), Padding = new Thickness(10, 4, 10, 4) };
        adv.Click += (_, _) => { _toAdvanced = true; Close(); }; // bascule vers le tableau (mode avancé), ne quitte PAS
        right.Children.Add(adv);
        DockPanel.SetDock(right, Dock.Right); head.Children.Add(right);

        var headWrap = new StackPanel();
        headWrap.Children.Add(head);
        headWrap.Children.Add(new Border { Height = 1, Background = B("bgFrame2"), Margin = new Thickness(0, 8, 0, 0) });
        DockPanel.SetDock(headWrap, Dock.Top);
        root.Children.Add(headWrap);

        DockPanel.SetDock(_footer, Dock.Bottom);
        root.Children.Add(_footer);

        root.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _body });

        Render();
    }

    /// <summary>Logo de l'app (Assets/logo.png) en code-behind, ou null si introuvable.</summary>
    private static Image? LogoImage(double size)
    {
        try
        {
            return new Image
            {
                Source = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/logo.png")),
                Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center,
            };
        }
        catch { return null; }
    }

    // ===== Rendu par étape =====
    private void Render()
    {
        _body.Children.Clear();
        _footer.Children.Clear();
        // (Plus de numéros d'étape : l'assistant est un hub, pas un tunnel.)
        switch (_step)
        {
            case Step.Goal: RenderGoal(); break;
            case Step.Source: RenderSource(); break;
            case Step.Waiting: RenderWaiting(); break;
            case Step.Destination: RenderDestination(); break;
            case Step.Options: RenderOptions(); break;
            case Step.Run: RenderRun(); break;
            case Step.Done: RenderDone(); break;
            case Step.SpeedAll: RenderSpeedAll(); break;
        }
    }

    // L'assistant EST la porte d'entrée : le fermer (croix) quitte GenSpeed. Seule la bascule « Mode avancé »
    // (_toAdvanced) le ferme sans quitter — elle ré-affiche alors la fenêtre principale (tableau).
    private bool _toAdvanced;
    private readonly Window? _ownerWin;   // fenêtre principale, masquée pendant que l'assistant est ouvert
    protected override void OnClosed(EventArgs e)
    {
        StopPoll(); base.OnClosed(e);
        if (_toAdvanced) { _ownerWin?.Show(); _ownerWin?.Activate(); }   // bascule → on révèle le mode avancé
        else System.Windows.Application.Current?.Shutdown();             // croix → on quitte GenSpeed
    }

    private TextBlock Title2(string key) => new()
        { Text = Loc.T(key), Foreground = B("fg"), FontWeight = FontWeights.Bold, FontSize = 15, Margin = new Thickness(0, 0, 0, 4) };

    private TextBlock Para(string key) => new()
        { Text = Loc.T(key), Foreground = B("dim"), TextWrapping = TextWrapping.Wrap, LineHeight = 18, Margin = new Thickness(0, 0, 0, 10) };

    private Button NavButton(string key, bool primary = false)
    {
        var b = new Button { Content = Loc.T(key), MinWidth = 110, Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(10, 5, 10, 5) };
        if (primary && St("PrimaryButton") is { } s) b.Style = s;
        return b;
    }

    private void AddFooter(params Button[] buttons) { foreach (var b in buttons) _footer.Children.Add(b); }

    /// <summary>M0 = source unique : la 1re install VIERGE découverte, en PRÉFÉRANT Zero Hour (son dossier
    /// contient « Zero Hour ») — pour ne pas confondre avec un Generals installé à côté. null si aucune.</summary>
    private string? AutoDetectM0()
    {
        var vanilla = InstallDiscovery.DiscoverAll(_config.KnownInstalls).Where(InstallManager.IsVanilla).ToList();
        return vanilla.FirstOrDefault(d => d.Contains("Zero Hour", StringComparison.OrdinalIgnoreCase)) ?? vanilla.FirstOrDefault();
    }

    // ----- Étape 1 : M0 (base vierge, source UNIQUE — on VÉRIFIE, on ne fait pas choisir) -----
    private void RenderSource()
    {
        _body.Children.Add(Title2("wiz.s1.title"));
        _body.Children.Add(Para("wiz.s1.intro"));

        // M0 = base vierge auto-détectée. On garde un dossier indiqué à la main (PickSourceFolder) s'il est valide.
        if (_sourceDir == null || !Directory.Exists(_sourceDir) || !InstallManager.IsVanilla(_sourceDir))
            _sourceDir = AutoDetectM0() ?? _sourceDir;
        bool haveBase = _sourceDir != null && Directory.Exists(_sourceDir) && InstallManager.IsVanilla(_sourceDir);

        // Pas d'« Annuler » : « Précédent » suffit (retour au hub). Idem aux autres étapes.
        var back = NavButton("wiz.back"); back.Click += (_, _) => { _step = Step.Goal; Render(); };

        if (haveBase)
        {
            // Base trouvée → simple confirmation (pas de liste à choisir).
            bool init = InstallManager.IsInitialized(_sourceDir!);
            _body.Children.Add(new TextBlock
            {
                Text = string.Format(Loc.T("wiz.s1.base.ok"), _sourceDir),
                Foreground = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)),
                TextWrapping = TextWrapping.Wrap, LineHeight = 18, Margin = new Thickness(0, 2, 0, 6),
            });
            if (!init)
            {
                // Pas VRAIMENT initialisée (Options.ini pas encore écrit) → la lancer une fois avant de continuer.
                _body.Children.Add(new Border { Height = 1, Background = B("bgFrame2"), Margin = new Thickness(0, 6, 0, 8) });
                _body.Children.Add(new TextBlock { Text = Loc.T("wiz.s1.init.warn"), Foreground = B("orange"),
                    TextWrapping = TextWrapping.Wrap, LineHeight = 17, Margin = new Thickness(0, 0, 0, 6) });
                _body.Children.Add(MakeButton("wiz.s1.init.btn", () => InitInstall(_sourceDir!), primary: true));
                _body.Children.Add(MakeButton("wiz.s1.init.recheck", Render));
            }
            // (Pas de secours « indiquer un dossier » ici : la base EST détectée. Le secours n'apparaît que
            //  dans le cas « non détecté » plus bas.)

            // Base prête (initialisée) → carte « Continuer ». Sinon il faut d'abord initialiser (boutons ci-dessus).
            if (init)
                _body.Children.Add(ChoiceCard(Loc.T("wiz.continue"), "", () => ProceedAfterSource()));
            AddFooter(back);
            return;
        }

        // Aucune base vierge détectée → INSTALLER (Steam) OU INDIQUER où elle est (si la détection a foiré).
        _body.Children.Add(new TextBlock { Text = Loc.T("wiz.s1.none"), Foreground = B("dim"),
            TextWrapping = TextWrapping.Wrap, LineHeight = 16, Margin = new Thickness(0, 0, 0, 8) });
        // Cartes à action DIRECTE (plus de sélection + bouton « Installer ») : un clic lance l'install Steam.
        _body.Children.Add(ChoiceCard(Loc.T("wiz.s1.install.zh"), Loc.T("wiz.s1.install.note"),
            () => SteamInstall(InstallManager.AppIdZeroHour)));
        _body.Children.Add(ChoiceCard(Loc.T("wiz.s1.install.both"), Loc.T("wiz.s1.install.both.note"),
            () => { _installQueue = new List<string> { InstallManager.AppIdZeroHour }; SteamInstall(InstallManager.AppIdGenerals); }));
        _body.Children.Add(ChoiceCard(Loc.T("wiz.s1.other"), "", PickSourceFolder));   // « j'ai déjà le jeu, voici où »
        AddFooter(back);
    }

    /// <summary>Après avoir GARANTI le jeu de base (étape Source) : « juste jouer » → options ; un FORK → on délègue
    /// à l'auto-install catalogue (ForksWindow : téléchargement + overlay correct + identité + patch .pak), qui
    /// remplace l'ancien « copie + collage manuel » ; GenLauncher → choix de la destination puis copie.</summary>
    private void ProceedAfterSource()
    {
        if (_goal == Goal.Fork)
        {
            RunHubAction(_openForkCatalog);   // catalogue possédé par l'assistant → reste ouvert, revient au menu
            return;
        }
        _step = _goal == Goal.KeepVanilla ? Step.Options : Step.Destination;
        Render();
    }

    /// <summary>Lance l'install Steam d'un jeu PUIS surveille tout seul : sondage `StateFlags=4` → init →
    /// étape Objectif. L'utilisateur n'a plus à revenir cliquer « rechercher ».</summary>
    private void SteamInstall(string appId)
    {
        // Pas de pop-up de confirmation : on lance l'install directement (Steam s'ouvre, c'est explicite).
        if (!InstallManager.SteamLifecycle("install", appId)) { Dialogs.Info(this, Loc.T("wiz.title"), Loc.T("wiz.s1.steam.failed")); return; }
        _watchAppId = appId; _initPhase = false; _gameSeen = false;
        _progress = InstallManager.SteamAppProgressPercent(appId);
        _step = Step.Waiting; StartPoll(); Render();
    }

    /// <summary>Surveille l'INITIALISATION d'une install déjà présente mais jamais lancée (lance via Steam,
    /// détecte process lancé→fermé). Hors Steam : message de lancement manuel, on surveille quand même le process.</summary>
    private void InitInstall(string dir)
    {
        _watchAppId = InstallManager.SteamAppId(dir);
        _gameSeen = false; _step = Step.Waiting;
        StartPoll(); StartInitPhase();
    }

    private void StartPoll()
    {
        StopPoll();
        _poll = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _poll.Tick += (_, _) => PollTick();
        _poll.Start();
    }

    private void StopPoll() { _poll?.Stop(); _poll = null; }

    private void PollTick()
    {
        if (_polling) return;               // un Dialog modal peut laisser le timer ticker → anti-ré-entrance
        _polling = true;
        try
        {
            if (!_initPhase)
            {
                if (_watchAppId == null) { StopPoll(); return; }
                // Pas de % affiché (l'.acf Steam est peu fiable) : barre animée seule. On sonde juste la fin.
                if (InstallManager.SteamAppFullyInstalled(_watchAppId)) StartInitPhase();
            }
            else
            {
                // Signal d'init = INIZH.big consommé (IsInitialized). Sur une install neuve, _sourceDir n'est pas
                // encore connu (AutoDetectM0 ne tourne qu'à FinishInit) → on le résout ici.
                bool running = InstallManager.GameProcessRunning();
                string? dir = _sourceDir ?? AutoDetectM0();
                bool done = dir != null && InstallManager.IsInitialized(dir);
                if (running)
                {
                    _gameSeen = true; _closeChecks = 0;
                    // Init AUTOMATIQUE : dès qu'INIZH est consommé, on FERME le jeu nous-mêmes (pas besoin que
                    // l'utilisateur aille au menu + quitte → plus d'interaction avec le plein écran du 1er run).
                    if (done) { InstallManager.KillGame(); _sourceDir = dir; FinishInit(); }
                }
                else if (_gameSeen)
                {
                    // Le jeu a été fermé (par nous ou l'utilisateur) → conclure.
                    if (done) { _sourceDir = dir; FinishInit(); }
                    else if (++_closeChecks >= 3) InitIncomplete();   // petit délai de grâce (~6 s)
                }
            }
        }
        finally { _polling = false; }
    }

    /// <summary>Passe en phase init : explique, lance le jeu une fois (Steam), puis on attend qu'il soit
    /// lancé puis FERMÉ. _initPhase mis AVANT le dialog (évite une re-détection « install finie » ré-entrante).</summary>
    private void StartInitPhase()
    {
        _initPhase = true; _gameSeen = false; _closeChecks = 0;
        Render();
        // Sur une install NEUVE (aucun Options.ini), le jeu démarre en PLEIN ÉCRAN et IGNORE -win (1er lancement =
        // détection matériel). On pré-écrit donc un Options.ini avec une résolution FENÊTRÉE avant le lancement →
        // le jeu le respecte et démarre en fenêtre (testé). GenSpeed remettra la résolution native après l'init.
        if (MultiplayerTuning.FindOptionsIni() == null)
        {
            var seed = GameOptions.EffectiveIni(_config)
                .Select(kv => kv.Key.Equals("Resolution", StringComparison.OrdinalIgnoreCase) ? ("Resolution", "1024 768") : kv)
                .ToList();
            MultiplayerTuning.ApplyOptionsValues(MultiplayerTuning.DefaultOptionsIniPath(), seed);
        }
        // Plus de pop-up « installé, ne touche à rien » : l'info est affichée DANS l'écran d'attente (RenderWaiting,
        // phase init) → pas besoin d'un OK, on enchaîne tout seul.
        // Lancer en FENÊTRÉ (-win) : pour une install Steam → steam.exe -applaunch <appId> -win (Steam lance =
        // DRM OK + fenêtré) ; hors Steam → exe direct -win. Repli ultime : steam://run (plein écran).
        bool launched = _sourceDir != null && InstallManager.LaunchGameWindowed(_sourceDir, _watchAppId);
        if (!launched)
        {
            if (_watchAppId != null) InstallManager.SteamLifecycle("run", _watchAppId);
            else Dialogs.Info(this, Loc.T("wiz.title"), Loc.T("wiz.s1.init.manual"));
        }
    }

    /// <summary>Init terminée (jeu lancé puis fermé, ou bouton « C'est fait ») → M0 prêt → vérif prérequis
    /// système (en amont, une fois) → étape Objectif.</summary>
    private async void FinishInit()
    {
        StopPoll();
        _watchAppId = null; _initPhase = false;
        // File d'install (ex. « ZH + Generals » : Generals d'abord, puis ZH) → enchaîner le suivant.
        if (_installQueue.Count > 0) { var next = _installQueue[0]; _installQueue.RemoveAt(0); SteamInstall(next); return; }
        _sourceDir = AutoDetectM0() ?? _sourceDir;
        await EnsurePrereqsAsync();
        // Jeu de base prêt → on saute DIRECTEMENT à la suite (ne pas re-afficher l'étape 2/4, redondant) :
        // options (« juste jouer »), catalogue (Fork), ou destination (GenLauncher).
        ProceedAfterSource();
    }

    /// <summary>Le jeu s'est fermé SANS finir l'init (Options.ini absent — crash en plein chargement, clic ailleurs
    /// en plein écran…). On NE valide PAS : retour à l'étape jeu de base (qui re-affiche l'avertissement + le
    /// bouton « lancer une fois »), avec un message clair.</summary>
    private void InitIncomplete()
    {
        StopPoll();
        _initPhase = false; _gameSeen = false; _watchAppId = null;
        _step = Step.Source; Render();
        Dialogs.Info(this, Loc.T("wiz.title"), Loc.T("wiz.init.incomplete"));
    }

    /// <summary>Vérifie VC++/DirectX (système). Présents → ne fait rien (autonome). Manquants → propose
    /// l'auto-install (installeurs officiels Microsoft, silencieux/élevés, depuis le registre de liens). Repli :
    /// ouvre la page MS + on continue. JAMAIS de cul-de-sac jusqu'à l'objectif.</summary>
    private async System.Threading.Tasks.Task EnsurePrereqsAsync()
    {
        var pr = InstallManager.CheckPrereqs();
        if (pr.AllOk) return;

        var missing = new List<string>();
        if (!pr.VcRedist) missing.Add(Loc.T("wiz.prereq.vc"));
        if (!pr.DirectX9) missing.Add(Loc.T("wiz.prereq.dx"));
        if (!Dialogs.Confirm(this, Loc.T("wiz.title"), string.Format(Loc.T("wiz.prereq.ask"), string.Join(", ", missing))))
        { OpenUrl(_config.Link("directx_page")); return; }   // refus → page MS, on continue

        _prereqMode = true; _step = Step.Waiting; Render();
        var jobs = new List<(string Key, string Args)>();
        if (!pr.VcRedist) { jobs.Add(("vcredist_2005_x86", "/q")); jobs.Add(("vcredist_2008_x86", "/qb")); jobs.Add(("vcredist_2010_x86", "/passive /norestart")); }
        if (!pr.DirectX9) jobs.Add(("directx_web", "/Q"));
        foreach (var j in jobs)
        {
            string label = DownloadLinks.All.FirstOrDefault(e => e.Key == j.Key)?.Label ?? j.Key;
            if (_waitText != null) _waitText.Text = string.Format(Loc.T("wiz.prereq.installing"), label);
            await InstallManager.DownloadAndRunInstallerAsync(_config.Link(j.Key), j.Args);
        }
        _prereqMode = false;

        pr = InstallManager.CheckPrereqs();
        if (!pr.AllOk && Dialogs.Confirm(this, Loc.T("wiz.title"), Loc.T("wiz.prereq.stillmissing")))
            OpenUrl(_config.Link("directx_page"));
    }

    // ----- Étape : attente (install / init / prérequis en cours) -----
    private void RenderWaiting()
    {
        if (_prereqMode)
        {
            _body.Children.Add(Title2("wiz.prereq.title"));
            _body.Children.Add(Para("wiz.prereq.body"));
            _waitText = new TextBlock { Foreground = B("accent"), FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 6) };
            _waitText.Text = Loc.T("wiz.prereq.body");
            _body.Children.Add(_waitText);
            _body.Children.Add(new ProgressBar { IsIndeterminate = true, Height = 14, Margin = new Thickness(0, 8, 0, 0) });
            return;   // pas de bouton : on enchaîne tout seul
        }
        _body.Children.Add(Title2(_initPhase ? "wiz.wait.init.title" : "wiz.wait.install.title"));
        _body.Children.Add(Para(_initPhase ? "wiz.wait.init.body" : "wiz.wait.install.body"));
        // Texte d'état seulement en phase init (hint « ne touche à rien ») ; en install, pas de % (barre animée seule).
        _waitText = new TextBlock { Foreground = B("accent"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 6) };
        _waitText.Text = _initPhase ? Loc.T("wiz.wait.init.hint") : Loc.T("wiz.wait.install.wait");
        _body.Children.Add(_waitText);
        // Barre TOUJOURS animée (indéterminée) : le % de l'.acf Steam est peu fiable (flush par paliers → semble
        // figé). Le % reste affiché en texte à titre indicatif ; la fin réelle = StateFlags==4.
        _waitBar = new ProgressBar { IsIndeterminate = true, Height = 14, Margin = new Thickness(0, 8, 0, 0) };
        _body.Children.Add(_waitBar);

        var cancel = NavButton("wiz.cancel");
        cancel.Click += (_, _) => { StopPoll(); _watchAppId = null; _initPhase = false; _step = Step.Source; Render(); };
        if (_initPhase)
        {
            var done = NavButton("wiz.wait.init.done", primary: true);
            done.Click += (_, _) =>
            {
                if (_sourceDir != null && InstallManager.IsInitialized(_sourceDir)) FinishInit();
                else Dialogs.Info(this, Loc.T("wiz.title"), Loc.T("wiz.init.incomplete"));
            };
            AddFooter(cancel, done);
        }
        else AddFooter(cancel);
    }

    private void PickSourceFolder()
    {
        var dlg = new OpenFolderDialog { Title = Loc.T("wiz.s1.other") };
        if (dlg.ShowDialog() != true) return;
        if (!GameLocator.IsZhFolder(dlg.FolderName)) { Dialogs.Info(this, Loc.T("wiz.title"), Loc.T("wiz.s1.invalid")); return; }
        _sourceDir = dlg.FolderName;
        _register(_sourceDir);   // rendre visible dans l'app + la découverte
        Render();
    }

    // ----- Étape 2 : ACCUEIL-HUB (conscient de l'état) -----
    private void RenderGoal()
    {
        _body.Children.Add(Title2("wiz.s2.title"));
        _body.Children.Add(Para("wiz.s2.intro"));

        // Source de vérité = JSON : on affiche les installs enregistrées TELLES QUELLES (pas de re-filtre disque qui
        // ferait disparaître un fork sur disque endormi). Le label vient de install_forks (sans accès disque).
        var installs = InstallDiscovery.DiscoverForDisplay(_config.KnownInstalls);
        bool HasGl(string d) { try { return File.Exists(Path.Combine(d, "GenLauncher.exe")); } catch { return false; } }
        bool hasM1 = installs.Any(HasGl);
        bool hasM0 = installs.Any(InstallManager.IsVanilla);     // un jeu de base vierge existe
        int forkCount = installs.Count(d => !InstallManager.IsVanilla(d) && !HasGl(d));
        int nextFork = 2 + forkCount;
        // Objectifs sans objet → bascule le défaut : hub déjà là, ou base déjà là.
        if (hasM1 && (_goal == Goal.GenLauncher || _goal == Goal.All)) _goal = Goal.Fork;
        if (hasM0 && _goal == Goal.KeepVanilla) _goal = Goal.Fork;

        // ---- « Ce que tu as » ----
        RenderStateSummary(installs);

        // ---- Objectifs d'INSTALLATION, filtrés par l'état ----
        if (!hasM0)   // « installer le jeu de base » seulement s'il n'est PAS déjà là
            _body.Children.Add(GoalRow(Goal.KeepVanilla, Loc.T("wiz.goal.play"), Loc.T("wiz.goal.play.desc")));
        if (!hasM1)
            _body.Children.Add(GoalRow(Goal.GenLauncher, Loc.T("wiz.goal.modded"), Loc.T("wiz.goal.modded.desc")));
        _body.Children.Add(GoalRow(Goal.Fork, string.Format(Loc.T("wiz.goal.fork"), nextFork), Loc.T("wiz.goal.fork.desc")));
        if (!hasM1)
            _body.Children.Add(GoalRow(Goal.All, Loc.T("wiz.goal.all"), Loc.T("wiz.goal.all.desc")));

        // ---- Actions de GESTION (sur ce qui est déjà installé) ----
        if (installs.Count > 0) RenderManageActions();

        // Accueil (hub) : chaque carte d'objectif lance directement → plus de « Suivant ». Seul « Quitter » reste.
        var quit = NavButton("wiz.quit"); quit.Click += (_, _) => Close();   // sans _toAdvanced → OnClosed quitte l'app
        AddFooter(quit);
        _lastHubSig = HubSignature();   // base de comparaison pour le rafraîchissement au retour de focus
    }

    /// <summary>« Ce que tu as » : chaque install (type + nom) et, sous chacune, ses mods PATCHÉS par GenSpeed
    /// (vitesse/caméra mémorisées). Rend l'assistant conscient de l'état.</summary>
    private void RenderStateSummary(List<string> installs)
    {
        _body.Children.Add(new TextBlock { Text = Loc.T("wiz.hub.state"), FontWeight = FontWeights.SemiBold,
            Foreground = B("fg"), Margin = new Thickness(0, 2, 0, 4) });
        if (installs.Count == 0)
            _body.Children.Add(new TextBlock { Text = Loc.T("wiz.hub.nothing"), Foreground = B("dim"),
                FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 2) });
        foreach (var d in installs)
        {
            // Ligne d'install : libellé (type + nom) à gauche, bouton « ▶ Lancer » à droite.
            var row = new DockPanel { Margin = new Thickness(0, 3, 0, 0) };
            if (_act.LaunchInstall != null)
            {
                string dir = d;   // capture pour le clic
                var play = new Button { Content = Loc.T("wiz.hub.launch"), Padding = new Thickness(8, 3, 8, 3),
                    Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                play.Click += (_, _) => _act.LaunchInstall!(this, dir);
                DockPanel.SetDock(play, Dock.Right);
                row.Children.Add(play);
            }
            row.Children.Add(new TextBlock
            {
                Text = HubTypeLabel(d) + "  —  " + Path.GetFileName(d.TrimEnd('\\', '/')),
                Foreground = B("fg"), FontSize = 12, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center,
            });
            _body.Children.Add(row);

            // Mods SOUS cette install : dossiers de GLM (mods GenLauncher), chacun annoté ⚡/📷 s'il est patché.
            var shown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var mod in InstalledModNames(d))
            {
                string key = d + "::" + mod; shown.Add(key);
                _body.Children.Add(ModLine(mod, PatchedAnnotation(key)));
            }
            // Cibles patchées HORS GLM (ex. données du fork .pak, base vanilla) non déjà listées.
            foreach (var kv in _config.PatchedState
                         .Where(k => k.Key.StartsWith(d + "::", StringComparison.OrdinalIgnoreCase) && !shown.Contains(k.Key)
                                  && (!string.IsNullOrWhiteSpace(k.Value.Speed) || !string.IsNullOrWhiteSpace(k.Value.Camera))))
                _body.Children.Add(ModLine(kv.Key.Substring(d.Length + 2), PatchedAnnotation(kv.Key)));
        }
        _body.Children.Add(new Border { Height = 1, Background = B("bgFrame2"), Margin = new Thickness(0, 8, 0, 8) });
    }

    /// <summary>Noms des mods installés sous une install (sous-dossiers de GLM = mods GenLauncher). Léger (noms de
    /// dossiers seulement) ; alias appliqué si défini. Vide pour un jeu de base / fork (pas de GLM).</summary>
    private List<string> InstalledModNames(string installDir)
    {
        var outp = new List<string>();
        try
        {
            string glm = Path.Combine(installDir, "GLM");
            if (Directory.Exists(glm))
                foreach (var m in Directory.GetDirectories(glm).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                {
                    // Un mod n'est « installé » que s'il contient des .gib. GenLauncher, en désinstallant un mod,
                    // VIDE ses .gib mais LAISSE le dossier → un dossier vide ne doit PAS compter comme un mod.
                    bool hasContent = false;
                    try { hasContent = Directory.EnumerateFiles(m, "*.gib", SearchOption.AllDirectories).Any(); } catch { }
                    if (!hasContent) continue;
                    string name = Path.GetFileName(m);
                    if (_config.ModAliases.TryGetValue(name, out var alias) && !string.IsNullOrWhiteSpace(alias)) name = alias;
                    outp.Add(name);
                }
        }
        catch { }
        return outp;
    }

    private string? _lastHubSig;   // dernière signature du paysage installs/mods affichée (anti re-rendu inutile)

    /// <summary>Signature LÉGÈRE de « ce que tu as » : installs (JSON + Steam/EA) + leurs mods GLM. Sert à ne
    /// rafraîchir l'accueil (sur retour de focus) QUE si quelque chose a changé.</summary>
    private string HubSignature()
    {
        var sb = new System.Text.StringBuilder();
        try
        {
            foreach (var d in InstallDiscovery.DiscoverForDisplay(_config.KnownInstalls).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                sb.Append(d).Append('|');
                foreach (var mod in InstalledModNames(d)) sb.Append(mod).Append(';');
                sb.Append('\n');
            }
        }
        catch { }
        return sb.ToString();
    }

    /// <summary>Suffixe « ⚡ vitesse, 📷 caméra » si le mod (clé &lt;install&gt;::&lt;mod&gt;) est patché ; sinon vide.</summary>
    private string PatchedAnnotation(string key)
    {
        if (_config.PatchedState.TryGetValue(key, out var ps))
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(ps.Speed)) parts.Add("⚡ " + ps.Speed);
            if (!string.IsNullOrWhiteSpace(ps.Camera)) parts.Add("📷 " + ps.Camera);
            if (parts.Count > 0) return "   " + string.Join(",  ", parts);
        }
        return "";
    }

    /// <summary>Ligne « mod » indentée sous une install (puce + nom + annotation patché éventuelle).</summary>
    private TextBlock ModLine(string mod, string annotation) => new()
    {
        Text = "• " + mod + annotation, Foreground = B("dim"), FontSize = 11,
        TextWrapping = TextWrapping.Wrap, Margin = new Thickness(22, 1, 0, 0),
    };

    /// <summary>Type d'install pour l'accueil : 🔧 nom du fork / 🧩 Hub GenLauncher / 🎮 Jeu de base / 📦 autre.</summary>
    private string HubTypeLabel(string d)
    {
        if (_config.InstallForks.TryGetValue(d, out var id) && !string.IsNullOrWhiteSpace(id))
            return "🔧 " + (ForkCatalog.Find(_config.Forks, id)?.Name ?? id);
        try { if (File.Exists(Path.Combine(d, "GenLauncher.exe"))) return "🧩 " + Loc.T("wiz.hub.t.gl"); } catch { }
        if (InstallManager.IsVanilla(d)) return "🎮 " + Loc.T("wiz.hub.t.base");
        return "📦 " + Loc.T("wiz.hub.t.other");
    }

    /// <summary>Actions sur l'existant. TOUTES gardent l'assistant ouvert (pop-up possédé par lui) et reviennent
    /// au menu (GoHome) : vitesse/caméra, options, catalogue, diagnostic, désinstall (tout / affiner).</summary>
    private void RenderManageActions()
    {
        _body.Children.Add(new TextBlock { Text = Loc.T("wiz.hub.manage"), FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 6, 0, 4) });
        var grid = new UniformGrid { Columns = 2 };
        if (_act.ApplySpeedCamRawAll != null)
            grid.Children.Add(HubCard("wiz.hub.speed", () => { _step = Step.SpeedAll; Render(); }));
        if (_act.OpenOptions != null) grid.Children.Add(HubCard("wiz.hub.options", () => RunHubAction(_act.OpenOptions)));
        if (_act.OpenDiagnostic != null) grid.Children.Add(HubCard("wiz.hub.diag", () => RunHubAction(_act.OpenDiagnostic)));
        if (_act.OpenUninstall != null || _act.OpenUninstallAll != null)
            grid.Children.Add(HubCard("wiz.hub.uninstall", ChooseUninstall));
        _body.Children.Add(grid);
    }

    /// <summary>Carte cliquable (action de gestion) : encadré + libellé centré, surbrillance accent au survol.</summary>
    private Border HubCard(string key, System.Action onClick)
    {
        var txt = new TextBlock
        {
            Text = Loc.T(key), Foreground = B("fg"), FontWeight = FontWeights.SemiBold, FontSize = 12,
            TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center,
        };
        var card = new Border
        {
            Background = B("bgFrame2"), BorderBrush = B("border"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 14, 10, 14), Margin = new Thickness(0, 0, 6, 6),
            Cursor = System.Windows.Input.Cursors.Hand, Child = txt,
        };
        card.MouseLeftButtonUp += (_, _) => onClick();
        card.MouseEnter += (_, _) => card.BorderBrush = B("accent");
        card.MouseLeave += (_, _) => card.BorderBrush = B("border");
        return card;
    }

    /// <summary>Désinstaller : « Tout » (suppression directe, sans la page à cocher) ou « Affiner » (page à cocher).
    /// Les deux gardent l'assistant ouvert et reviennent au menu.</summary>
    private void ChooseUninstall()
    {
        var opts = new List<string>();
        if (_act.OpenUninstallAll != null) opts.Add(Loc.T("wiz.hub.uninstall.all"));
        if (_act.OpenUninstall != null) opts.Add(Loc.T("wiz.hub.uninstall.fine"));
        if (opts.Count == 0) return;
        string? pick = Dialogs.Choose(this, Loc.T("wiz.hub.uninstall"), Loc.T("wiz.hub.uninstall.choose"), opts);
        if (pick == null) return;
        if (pick == Loc.T("wiz.hub.uninstall.all")) RunHubAction(_act.OpenUninstallAll);
        else RunHubAction(_act.OpenUninstall);
    }

    /// <summary>Retourne à l'accueil du hub (re-rend l'état → reflète ce qui vient de changer).</summary>
    private void GoHome() { _step = Step.Goal; Render(); }

    /// <summary>Lance une action POSSÉDÉE par l'assistant (pop-up sur l'assistant qui reste ouvert), puis revient
    /// au menu. owner = l'assistant → les dialogues s'affichent au bon endroit.</summary>
    private async void RunHubAction(System.Func<Window, System.Threading.Tasks.Task>? action)
    {
        if (action != null) { try { await action(this); } catch { } }
        GoHome();
    }

    /// <summary>Page vitesse/caméra : TABLEAU des jeux/mods (cases à cocher) + bloc VITESSE + bloc CAMÉRA (mêmes blocs
    /// que la fenêtre principale, via le composant partagé), puis Appliquer (aux cochés) + Lancer un jeu/mod ou Retour.</summary>
    private void RenderSpeedAll()
    {
        _body.Children.Add(Title2("wiz.hub.scall.title"));
        _body.Children.Add(Para("wiz.speedall.intro"));

        // Disposition : TABLEAU à gauche (cases à cocher — l'utilisateur choisit les cibles), blocs vitesse/caméra
        // EMPILÉS à droite (mêmes que la fenêtre principale). Cohérent avec le mode avancé.
        var split = new Grid { Margin = new Thickness(0, 2, 0, 8) };
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 280 });
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        if (_act.BuildModTable != null)
        {
            var table = _act.BuildModTable();
            table.VerticalAlignment = VerticalAlignment.Stretch;   // remplit la hauteur de la colonne (espace en bas)
            Grid.SetColumn(table, 0);
            split.Children.Add(table);
        }

        // Composant PARTAGÉ : exactement les mêmes blocs (curseur + facteur + affinage + CRUD) que la fenêtre
        // principale. On en crée une instance propre à l'assistant, on lit ses valeurs au moment d'« Appliquer ».
        var panel = new SpeedCameraPanel { Width = 450, VerticalAlignment = VerticalAlignment.Top };
        panel.Init(_config, this);
        Grid.SetColumn(panel, 2);
        split.Children.Add(panel);
        _body.Children.Add(split);

        // Footer : Retour menu | Revenir à l'original | Lancer un jeu/mod | Appliquer (aux lignes cochées).
        var buttons = new List<Button>();
        var back = NavButton("wiz.back"); back.Click += (_, _) => GoHome();
        buttons.Add(back);
        if (_act.RestoreSelected != null)
        {
            var restore = NavButton("wiz.hub.scall.restore");
            restore.Click += (_, _) => _act.RestoreSelected(this);   // déspeede les lignes cochées ; on reste sur la page
            buttons.Add(restore);
        }
        if (_act.LaunchGame != null)
        {
            var launch = NavButton("wiz.speedall.launch");
            launch.Click += (_, _) => RunHubAction(_act.LaunchGame);
            buttons.Add(launch);
        }
        var apply = NavButton("wiz.hub.scall.apply", primary: true);
        apply.Click += (_, _) =>
        {
            _act.ApplySpeedCamRawAll?.Invoke(this, panel.ReadFactors(), panel.ReadCam());
            // On RESTE sur la page : l'utilisateur peut ensuite lancer un jeu.
        };
        buttons.Add(apply);
        AddFooter(buttons.ToArray());
    }


    /// <summary>Résout M0 (auto-détection) et dit s'il est PRÊT : présent, vierge ET initialisé. Si prêt, l'étape
    /// « jeu de base » n'a rien à demander → on la saute.</summary>
    private bool BaseReady()
    {
        if (_sourceDir == null || !Directory.Exists(_sourceDir) || !InstallManager.IsVanilla(_sourceDir))
            _sourceDir = AutoDetectM0() ?? _sourceDir;
        return _sourceDir != null && Directory.Exists(_sourceDir)
            && InstallManager.IsVanilla(_sourceDir) && InstallManager.IsInitialized(_sourceDir);
    }

    /// <summary>Objectif d'install (accueil) : carte à action directe — le clic LANCE l'objectif (plus de « Suivant »).</summary>
    private Border GoalRow(Goal g, string title, string desc) => ChoiceCard(title, desc, () =>
    {
        _goal = g; _destDir = null;
        if (BaseReady()) ProceedAfterSource();
        else { _step = Step.Source; Render(); }
    });

    /// <summary>Carte cliquable « titre + description » (survol accent) : le clic déclenche directement l'action.
    /// Sert aux objectifs (accueil) et aux choix d'installation (ZH / ZH+Generals / j'ai déjà le jeu).</summary>
    private Border ChoiceCard(string title, string desc, System.Action onClick)
    {
        var col = new StackPanel();
        col.Children.Add(new TextBlock { Text = title, Foreground = B("fg"), FontWeight = FontWeights.SemiBold });
        if (!string.IsNullOrEmpty(desc))
            col.Children.Add(new TextBlock { Text = desc, Foreground = B("dim"), FontSize = 11, TextWrapping = TextWrapping.Wrap,
                LineHeight = 16, Margin = new Thickness(0, 1, 0, 0) });
        var border = new Border
        {
            BorderBrush = B("border"), BorderThickness = new Thickness(1), Background = B("bgFrame"),
            CornerRadius = new CornerRadius(4), Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 3, 0, 3), Cursor = System.Windows.Input.Cursors.Hand, Child = col,
        };
        border.MouseEnter += (_, _) => border.BorderBrush = B("accent");
        border.MouseLeave += (_, _) => border.BorderBrush = B("border");
        border.MouseLeftButtonUp += (_, _) => onClick();
        return border;
    }

    // ----- Étape 3 : Destination + garde-fous -----
    private void RenderDestination()
    {
        _body.Children.Add(Title2("wiz.s3.title"));
        _body.Children.Add(Para("wiz.s3.intro"));

        // Rappel : la base copiée est TOUJOURS M0 (la source vierge auto-détectée).
        _body.Children.Add(new TextBlock { Text = string.Format(Loc.T("wiz.s3.forksrc"), _sourceDir),
            Foreground = B("dim"), FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });

        if (_goal == Goal.GenLauncher || _goal == Goal.All)
        {
            // M1 : le dossier s'appelle TOUJOURS « GenLauncher » ; on ne choisit que l'EMPLACEMENT (défaut proposé,
            // mémorisé). _destDir = <emplacement>\GenLauncher. (En mode « Installation complète », le fork viendra
            // ensuite via le catalogue.)
            if (_destDir == null)
            {
                string parent = _config.InstallParent ?? InstallManager.SuggestInstallParent();
                _destDir = Path.Combine(parent, InstallManager.GenLauncherFolderName);
            }
            _body.Children.Add(new TextBlock { Text = Loc.T("wiz.s3.m1note"), Foreground = B("dim"),
                FontSize = 11, TextWrapping = TextWrapping.Wrap, LineHeight = 16, Margin = new Thickness(0, 0, 0, 6) });
            _body.Children.Add(ChoiceCard(Loc.T("wiz.s3.pick.loc"), "", () =>
            {
                var dlg = new OpenFolderDialog { Title = Loc.T("wiz.s3.pick.loc") };
                try { dlg.InitialDirectory = Path.GetDirectoryName(_destDir); } catch { }
                if (dlg.ShowDialog() != true) return;
                _destDir = Path.Combine(dlg.FolderName, InstallManager.GenLauncherFolderName);
                _config.InstallParent = dlg.FolderName; ConfigStore.Save(_config);   // emplacement mémorisé
                Render();
            }));
        }
        else
        {
            _body.Children.Add(ChoiceCard(Loc.T("wiz.s3.pick"), "", () =>
            {
                var dlg = new OpenFolderDialog { Title = Loc.T("wiz.s3.pick") };
                if (dlg.ShowDialog() != true) return;
                _destDir = dlg.FolderName;
                Render();
            }));
        }

        _body.Children.Add(new TextBlock
        {
            Text = Loc.T("wiz.s3.dest") + " " + (_destDir ?? Loc.T("wiz.s3.nodest")),
            Foreground = _destDir != null ? B("fg") : B("dim"),
            FontFamily = new FontFamily("Consolas"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 10),
        });

        bool ok = RenderGuards();

        // Carte « Installer ici » UNIQUEMENT si les garde-fous passent (sinon l'utilisateur corrige d'abord).
        if (ok && _destDir != null)
            _body.Children.Add(ChoiceCard(string.Format(Loc.T("wiz.s3.installhere"), _destDir), "",
                () => { _step = Step.Options; Render(); }));
        // Retour → l'objectif (l'étape « jeu de base » est sautée quand M0 est déjà prêt).
        var back = NavButton("wiz.back"); back.Click += (_, _) => { _step = Step.Goal; Render(); };
        AddFooter(back);
    }

    /// <summary>Source réelle de la copie : TOUJOURS M0 (la source vierge auto-détectée à l'étape 1).</summary>
    private string? EffectiveSource() => _sourceDir;

    /// <summary>Affiche les garde-fous (source vierge pour un fork, NTFS, espace, src≠dest) et renvoie
    /// vrai si la copie peut démarrer (aucun bloqueur).</summary>
    private bool RenderGuards()
    {
        string? src = EffectiveSource();
        if (_destDir == null || src == null) return false;
        bool ok = true;

        void Line(bool good, string text)
        {
            _body.Children.Add(new TextBlock { Text = text, Foreground = good ? B("fg") : B("orange"),
                TextWrapping = TextWrapping.Wrap, LineHeight = 17, Margin = new Thickness(0, 2, 0, 2) });
        }

        // src == dest : bloquant.
        if (string.Equals(Path.GetFullPath(src).TrimEnd('\\'), Path.GetFullPath(_destDir).TrimEnd('\\'), System.StringComparison.OrdinalIgnoreCase))
        { Line(false, Loc.T("wiz.guard.same")); return false; }

        // Source vierge — M0 doit l'être : bloquant pour un fork (consigne des mods), simple avertissement
        // pour GenLauncher (on copie vierge puis on pose GenLauncher).
        var intrus = InstallManager.NonVanillaItems(src);
        if (intrus.Count == 0) Line(true, Loc.T("wiz.guard.vanilla.ok"));
        else
        {
            Line(false, string.Format(Loc.T("wiz.guard.vanilla.warn"), string.Join(", ", intrus.Take(5))));
            if (_goal == Goal.Fork) ok = false;   // fork : bloquant ; GenLauncher : simple avertissement
        }

        // NTFS (symlinks GenLauncher).
        bool ntfs = InstallManager.IsNtfs(_destDir);
        Line(ntfs, Loc.T(ntfs ? "wiz.guard.ntfs.ok" : "wiz.guard.ntfs.bad"));

        // Espace disque.
        long need = InstallManager.DirSizeBytes(src);
        long free = InstallManager.FreeSpaceBytes(_destDir);
        bool space = free < 0 || free >= need + (200L << 20);
        Line(space, string.Format(Loc.T(space ? "wiz.guard.space.ok" : "wiz.guard.space.bad"), Mb(free), Mb(need)));
        if (!space) ok = false;

        return ok;
    }

    private static string Mb(long bytes) => bytes < 0 ? "?" : $"{bytes >> 20} {Loc.T("unit.mb")}";

    // ----- Étape Options : toujours affichée, PRÉ-RÉGLÉE selon le PC ; l'utilisateur peut juste continuer,
    //        ou ouvrir le sélecteur détaillé. Globale (un seul Options.ini) → vaut pour le jeu de base ET les
    //        copies GenLauncher. Pour une copie, le détail (Vulkan + EffectiveIni) est ré-appliqué dans Placed(). -----
    private void RenderOptions()
    {
        _body.Children.Add(Title2("wiz.opt.title"));
        _body.Children.Add(Para("wiz.opt.intro"));
        _body.Children.Add(new TextBlock
        {
            Text = "🖥  " + string.Format(Loc.T("go.pc.detected"), PcInfo.Summary(), Loc.T("go.lvl." + PcInfo.RecommendedGraphics())),
            Foreground = B("accent"), FontSize = 12, TextWrapping = TextWrapping.Wrap, LineHeight = 16,
            Margin = new Thickness(0, 2, 0, 8), ToolTip = Loc.T("go.pc.tip"),
        });
        _body.Children.Add(ChoiceCard(Loc.T("wiz.opt.adjust"), "", () => GameOptionsWindow.Show(this, _config, () => { })));
        _body.Children.Add(new TextBlock { Text = Loc.T("go.scope"), Foreground = B("dim"), FontSize = 11,
            FontStyle = FontStyles.Italic, TextWrapping = TextWrapping.Wrap, LineHeight = 15, Margin = new Thickness(2, 8, 0, 0) });

        // Infos (sous le bouton) : IP LAN utilisée + emplacement GenLauncher pour une copie.
        string? ip = MultiplayerTuning.ReadOptionValue(MultiplayerTuning.DefaultOptionsIniPath(), "IPAddress");
        if (string.IsNullOrWhiteSpace(ip) || ip == NetInfo.Auto) ip = NetInfo.LanIp() ?? NetInfo.Auto;
        _body.Children.Add(new TextBlock { Text = string.Format(Loc.T("wiz.opt.ipinfo"), ip), Foreground = B("dim"),
            FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 6, 0, 0) });
        if (_goal != Goal.KeepVanilla && _destDir != null)
            _body.Children.Add(new TextBlock { Text = string.Format(Loc.T("wiz.opt.destinfo"), _destDir), Foreground = B("dim"),
                FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 2, 0, 0) });
        // (Le réseau — IP LAN/en ligne, SendDelay, pare-feu — est dans le sélecteur d'options, étape 3 : on ne le
        // duplique plus ici. Le bouton ci-dessus y donne accès.)

        // Action principale en CARTE (applique les options) : « Terminer » (juste jouer) ou « Copier » (créer la copie).
        _body.Children.Add(ChoiceCard(Loc.T(_goal == Goal.KeepVanilla ? "wiz.finish" : "wiz.copy"), "", async () =>
        {
            GameOptions.ApplyIni(_config);   // écrit l'Options.ini global (vaut pour toutes les installs)
            if (_goal == Goal.KeepVanilla) { _step = Step.Done; Render(); }
            else await StartCopyAsync();
        }));
        var back = NavButton("wiz.back");
        back.Click += (_, _) => { _step = _goal == Goal.KeepVanilla ? Step.Goal : Step.Destination; Render(); };
        AddFooter(back);
    }

    // ----- Étape 4 : Copie -----
    private void RenderRun()
    {
        _body.Children.Add(Title2("wiz.title"));
        _body.Children.Add(new TextBlock { Text = Loc.T("wiz.s4.copying"), Foreground = B("accent"),
            FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 6) });
        _body.Children.Add(new TextBlock { Text = Loc.T("wiz.s4.copyhint"), Foreground = B("dim"),
            TextWrapping = TextWrapping.Wrap, LineHeight = 17 });
        _body.Children.Add(new ProgressBar { IsIndeterminate = true, Height = 14, Margin = new Thickness(0, 14, 0, 0) });
        // Aucun bouton pendant la copie (robocopy ne supporte pas une annulation propre ici).
    }

    private async System.Threading.Tasks.Task StartCopyAsync()
    {
        string? src = EffectiveSource();
        if (src == null || _destDir == null) return;
        _step = Step.Run; Render();
        _log(string.Format(Loc.T("wiz.s4.copying"), _destDir));
        _copyResult = await System.Threading.Tasks.Task.Run(() => InstallManager.CopyInstall(src!, _destDir!));
        _step = Step.Done; Render();
    }

    // ----- Étape finale : CONCISE — « tout est prêt » + quoi faire ensuite (GenLauncher pour les mods, GenSpeed
    //        pour la vitesse/caméra). Plus de dialogues : GenLauncher posé + son emplacement = en INFO ici. -----
    private void RenderDone()
    {
        _body.Children.Add(new TextBlock { Text = Loc.T("wiz.done.title"), Foreground = B("accent"),
            FontWeight = FontWeights.Bold, FontSize = 17, Margin = new Thickness(0, 0, 0, 10) });

        // Installation complète (All) = on crée M1 comme GenLauncher, puis on enchaînera sur le catalogue fork.
        bool genLauncher = (_goal == Goal.GenLauncher || _goal == Goal.All) && _copyResult is { Ok: true };
        bool fork = _goal == Goal.Fork && _copyResult is { Ok: true };
        string? finalDir = _goal == Goal.KeepVanilla ? _sourceDir
                         : _copyResult is { Ok: true } ? _destDir : null;

        void Info(string text, Brush fg, double top = 4) => _body.Children.Add(new TextBlock
        { Text = text, Foreground = fg, TextWrapping = TextWrapping.Wrap, LineHeight = 18, Margin = new Thickness(0, top, 0, 0) });

        if (_copyResult is { Ok: false })
            Info(string.Format(Loc.T("wiz.done.copyfail"), _copyResult.Error ?? "?"), B("orange"));

        // Prérequis système (seulement si manquants — n'altère pas M0).
        if (finalDir != null && _goal != Goal.KeepVanilla)
        {
            var pr = InstallManager.CheckPrereqs();
            if (!pr.AllOk)
            {
                var miss = new List<string>();
                if (!pr.VcRedist) miss.Add(Loc.T("wiz.prereq.vc"));
                if (!pr.DirectX9) miss.Add(Loc.T("wiz.prereq.dx"));
                Info(string.Format(Loc.T("wiz.prereq.missing"), string.Join(", ", miss)), B("orange"));
                _body.Children.Add(MakeButton("wiz.btn.directx", () => OpenUrl(_config.Link("directx_page"))));
            }
        }

        if (genLauncher)
        {
            // Lance l'auto-install GenLauncher une seule fois ; le statut (en cours / prêt) s'affiche ici.
            if (!_glTriggered) { _glTriggered = true; Dispatcher.BeginInvoke(new Action(() => AutoInstallGenLauncher(_destDir!))); }
            Info(_glReady ? string.Format(Loc.T("wiz.done.gl.ready"), _destDir) : Loc.T("wiz.done.gl.installing"),
                 _glReady ? new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)) : B("accent"), 6);
            Info(Loc.T("wiz.done.mods"), B("fg"), 8);
            // Installation complète : une fois GenLauncher prêt, on enchaîne sur le fork (catalogue).
            if (_goal == Goal.All && _glReady) Info(Loc.T("wiz.done.all.next"), B("accent"), 8);
        }
        else if (fork)
            Info(string.Format(Loc.T("wiz.done.fork.note"), _destDir), B("fg"), 6);
        else if (_goal == Goal.KeepVanilla)
            Info(Loc.T("wiz.done.play"), B("fg"), 6);

        // À quoi sert GenSpeed (config dans l'interface principale) + LE point : même réglages des 2 PC en LAN.
        if (finalDir != null)
        {
            Info(Loc.T("wiz.done.speed"), B("fg"), 10);
            Info(Loc.T("wiz.done.lan"), new SolidColorBrush(Color.FromRgb(0xFF, 0xB3, 0x00)), 4);
            _register(finalDir);   // M0/M1/Mx enregistré dans le tableau
            Info(Loc.T("wiz.done.registered"), B("dim"), 10);
        }

        // Footer. Cas GenLauncher prêt → « Lancer GenLauncher » REMPLACE « Terminer » (le lancement clôt l'assistant).
        var btns = new List<Button>();
        if (genLauncher && _glReady && _glExePath != null)
        {
            // Ouvrir le dossier à GAUCHE, Lancer GenLauncher (action principale) à DROITE.
            if (finalDir != null)
            {
                var open = NavButton("wiz.done.openfolder");
                open.Click += (_, _) => { try { Process.Start(new ProcessStartInfo { FileName = finalDir, UseShellExecute = true }); } catch { } };
                btns.Add(open);
            }
            if (_goal == Goal.All)
            {
                // Installation complète : M1 prêt → dernière étape, on ouvre le catalogue pour le fork.
                var toFork = NavButton("wiz.done.tofork", primary: true);
                toFork.Click += (_, _) => RunHubAction(_openForkCatalog);   // catalogue possédé → assistant reste ouvert
                btns.Add(toFork);
            }
            else
            {
                var launch = NavButton("wiz.done.gl.launch", primary: true);
                launch.Click += (_, _) =>
                {
                    try { Process.Start(new ProcessStartInfo { FileName = _glExePath, WorkingDirectory = Path.GetDirectoryName(_glExePath), UseShellExecute = true, Verb = "runas" }); }
                    catch (System.ComponentModel.Win32Exception) { }
                    _toAdvanced = true; Close();
                };
                btns.Add(launch);
            }
        }
        else
        {
            // Pas (encore) de GenLauncher prêt → Ouvrir le dossier + Terminer (pour ne pas rester bloqué).
            if (finalDir != null)
            {
                var open = NavButton("wiz.done.openfolder");
                open.Click += (_, _) => { try { Process.Start(new ProcessStartInfo { FileName = finalDir, UseShellExecute = true }); } catch { } };
                btns.Add(open);
            }
            var finish = NavButton("wiz.finish", primary: true);
            finish.Click += (_, _) => { _toAdvanced = true; Close(); };
            btns.Add(finish);
        }
        AddFooter(btns.ToArray());
    }

    /// <summary>Pose GenLauncher.exe dans la copie depuis le zip téléchargé : auto-détecté dans Téléchargements
    /// (sinon sélection manuelle), dézippé localement. Aucun téléchargement web par GenSpeed.</summary>
    private void InstallGenLauncher(string destDir)
    {
        string? zip = InstallManager.FindDownloadedGenLauncherZip();
        // Trouvé dans Téléchargements : confirmer ; sinon (ou si refus) sélection manuelle.
        if (zip != null && !Dialogs.Confirm(this, Loc.T("wiz.title"), string.Format(Loc.T("gl.confirm"), zip)))
            zip = null;
        if (zip == null)
        {
            // Repli universel : peu importe où le navigateur a enregistré le zip. On ouvre dans le vrai
            // dossier Téléchargements (le plus probable), l'utilisateur navigue ailleurs si besoin.
            var dlg = new OpenFileDialog
            {
                Title = Loc.T("gl.pickzip"), Filter = "GenLauncher (*.zip)|*.zip",
                InitialDirectory = InstallManager.DownloadsFolder(),
            };
            if (dlg.ShowDialog() != true) return;
            zip = dlg.FileName;
        }
        Placed(destDir, InstallManager.InstallGenLauncherFromZip(zip, destDir));
    }

    /// <summary>Voie automatique : télécharge le zip GenLauncher (lien direct du manifeste), le dézippe et pose
    /// l'exe dans la copie, puis propose de lancer. Repli sur le navigateur si pas de lien direct exploitable.</summary>
    private async void AutoInstallGenLauncher(string destDir)
    {
        _log(Loc.T("gl.link.resolving"));
        string? url = await InstallManager.FetchGenLauncherDownloadLinkAsync(_config.Link("genlauncher_manifest"));
        if (string.IsNullOrWhiteSpace(url)) url = _config.Link("genlauncher_zip");
        if (string.IsNullOrWhiteSpace(url) || !url!.StartsWith("http", System.StringComparison.OrdinalIgnoreCase))
        { Dialogs.Info(this, Loc.T("wiz.title"), Loc.T("gl.auto.nourl")); return; }
        // ModDB = Cloudflare → pas téléchargeable en direct : on bascule sur le navigateur.
        if (url.Contains("moddb.com", System.StringComparison.OrdinalIgnoreCase))
        { Dialogs.Info(this, Loc.T("wiz.title"), Loc.T("gl.auto.moddb")); OpenGenLauncherDownload(); return; }

        _log(Loc.T("gl.auto.downloading"));
        string tmp = Path.Combine(Path.GetTempPath(), "GenLauncher_dl.zip");
        var dl = await InstallManager.DownloadToFileAsync(url, tmp);
        if (!dl.Ok) { Dialogs.Info(this, Loc.T("wiz.title"), string.Format(Loc.T("gl.auto.fail"), dl.Error)); return; }
        var res = InstallManager.InstallGenLauncherFromZip(tmp, destDir);
        try { File.Delete(tmp); } catch { }
        Placed(destDir, res);
    }

    /// <summary>GenLauncher posé (ou échec) → message + raccourci Bureau, puis proposition de le lancer.</summary>
    private void Placed(string destDir, GenLauncherResult res)
    {
        if (!res.Ok) { Dialogs.Info(this, Loc.T("wiz.title"), string.Format(Loc.T("gl.fail"), res.Error)); return; }
        _log(string.Format(Loc.T("gl.done"), res.ExePath));
        // Pré-configurer GenLauncher AVANT son 1er lancement : GenTool OFF + FirstStart false → il ne s'auto-
        // installe pas GenTool et ne propose pas son setup. (Crée le YAML baseline puisqu'il n'existe pas encore.)
        var seed = MultiplayerTuning.SeedOrTuneYaml(destDir);
        if (seed.Ok) _log(seed.Applied < 0 ? string.Format(Loc.T("gl.seeded"), seed.Path)
                                           : string.Format(Loc.T("tune.yaml.ok"), Path.GetFileName(destDir.TrimEnd('\\', '/')), seed.Applied));
        // Pousser le CHOIX Vulkan de l'utilisateur dans le YAML neuf (la baseline l'a mis à false) → dès le 1er
        // lancement GenLauncher est dans le bon mode, sans attendre le prochain AutoTune.
        var yp = MultiplayerTuning.FindGenLauncherYaml(destDir);
        if (yp != null) MultiplayerTuning.SetYamlKey(yp, "UseVulkan", GameOptions.Vulkan(_config) ? "true" : "false");
        // Pré-créer l'Options.ini AVANT le 1er lancement du jeu, avec les CHOIX de l'utilisateur (EffectiveIni,
        // PC-aware) — pas l'ancienne baseline figée. Ensuite AutoTune le maintient à chaque run.
        var optSeed = MultiplayerTuning.ApplyOptionsValues(MultiplayerTuning.DefaultOptionsIniPath(), GameOptions.EffectiveIni(_config));
        if (optSeed.Ok && optSeed.Applied != 0) _log(string.Format(Loc.T("tune.opt.ok"), optSeed.Applied));
        // Raccourci Bureau (« GenLauncher » ; suffixe du dossier seulement si collision avec une autre install).
        CreateDesktopShortcut(res.ExePath!, destDir);
        // Plus de pop-up « lancer ? » : GenLauncher est prêt → on l'indique en INFO sur l'écran final + bouton « Lancer ».
        _glExePath = res.ExePath; _glReady = true;
        if (_step == Step.Done) Render();
    }

    /// <summary>Ouvre le téléchargement GenLauncher dans le navigateur : lien à jour lu dans le manifeste
    /// p0ls3r ; à défaut le lien éditable de la config ; à défaut la page ModDB.</summary>
    private async void OpenGenLauncherDownload()
    {
        _log(Loc.T("gl.link.resolving"));
        string? url = await InstallManager.FetchGenLauncherDownloadLinkAsync(_config.Link("genlauncher_manifest"));
        if (string.IsNullOrWhiteSpace(url)) url = _config.Link("genlauncher_zip");
        if (string.IsNullOrWhiteSpace(url)) url = _config.Link("genlauncher_moddb");
        _log(string.Format(Loc.T("gl.link.using"), url));
        OpenUrl(url!);
    }

    /// <summary>Crée un raccourci « GenLauncher » sur le Bureau vers <paramref name="exePath"/>, marqué
    /// « Exécuter en tant qu'administrateur » (symlinks). Si un « GenLauncher.lnk » existe déjà et vise une
    /// AUTRE install, désambiguïse avec le nom du dossier (cas rare). Best-effort (try/catch).</summary>
    private void CreateDesktopShortcut(string exePath, string workingDir)
    {
        try
        {
            string desktop = System.Environment.GetFolderPath(System.Environment.SpecialFolder.DesktopDirectory);
            System.Type? t = System.Type.GetTypeFromProgID("WScript.Shell");
            if (t == null) return;
            dynamic shell = System.Activator.CreateInstance(t)!;

            string lnkPath = Path.Combine(desktop, "GenLauncher.lnk");
            if (File.Exists(lnkPath))
                try
                {
                    dynamic existing = shell.CreateShortcut(lnkPath);
                    string existingTarget = (string)existing.TargetPath;
                    if (!string.Equals(existingTarget, exePath, System.StringComparison.OrdinalIgnoreCase))
                        lnkPath = Path.Combine(desktop, "GenLauncher - " + Path.GetFileName(workingDir.TrimEnd('\\', '/')) + ".lnk");
                }
                catch { }

            var lnk = shell.CreateShortcut(lnkPath);
            lnk.TargetPath = exePath;
            lnk.WorkingDirectory = workingDir;
            lnk.IconLocation = exePath + ",0";
            lnk.Description = "GenLauncher";
            lnk.Save();
            // Marquer « Exécuter en tant qu'administrateur » : bit 0x20 de l'octet 0x15 du .lnk.
            try { var b = File.ReadAllBytes(lnkPath); if (b.Length > 0x15) { b[0x15] = (byte)(b[0x15] | 0x20); File.WriteAllBytes(lnkPath, b); } } catch { }
            _log(string.Format(Loc.T("gl.shortcut"), lnkPath));
        }
        catch { }
    }

    private static void OpenUrl(string url)
    {
        url = Loc.MsUrl(url);
        try { Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true }); } catch { }
    }

    // ----- Bouton « action » pleine largeur, aligné à gauche -----
    private Button MakeButton(string key, System.Action act, bool primary = false)
    {
        var b = new Button
        {
            Content = Loc.T(key), HorizontalContentAlignment = HorizontalAlignment.Left,
            HorizontalAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(12, 7, 12, 7),
            Margin = new Thickness(0, 3, 0, 3),
        };
        if (primary && St("PrimaryButton") is { } s) b.Style = s;
        b.Click += (_, _) => act();
        return b;
    }
}
