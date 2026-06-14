using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using GenSpeed.Core;

namespace GenSpeed.App;

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
    private enum Step { Source, Goal, Destination, Run, Done }
    // Modèle GenPatcher-free, M0 reste VIERGE et sert de source UNIQUE (le jeu Steam est re-téléchargeable,
    // donc pas de master de sauvegarde séparé). KeepVanilla = garder M0 tel quel ;
    // GenLauncher = M1 = COPIE de M0 + GenLauncher ; Fork = Mx = COPIE de M0 + fork autonome (Reborn Omega…).
    private enum Goal { KeepVanilla, GenLauncher, Fork }

    private static Brush B(string key) => (Brush)Application.Current.FindResource(key);
    private static Style? St(string key) => Application.Current.TryFindResource(key) as Style;

    private readonly GenConfig _config;
    private readonly System.Action<string> _log;
    private readonly System.Action<string> _register;   // install normale → tableau (EnsureInstallListed + LoadMods)

    private Step _step = Step.Source;
    private Goal _goal = Goal.GenLauncher;
    private string? _sourceDir;
    private string? _destDir;
    private CopyResult? _copyResult;

    private readonly StackPanel _body = new() { Margin = new Thickness(20, 16, 20, 16) };
    private readonly StackPanel _footer = new()
        { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(16, 8, 16, 12) };
    private TextBlock _stepLabel = null!;

    public static void Show(Window owner, GenConfig config, System.Action<string> log,
                            System.Action<string> register)
        => new InstallWizardWindow(owner, config, log, register).ShowDialog();

    private InstallWizardWindow(Window owner, GenConfig config, System.Action<string> log,
                               System.Action<string> register)
    {
        _config = config; _log = log; _register = register;

        // M0 = source UNIQUE (install vierge, Steam de préférence) : auto-détectée → pas de question source.
        // Si M0 est prête (initialisée), on démarre direct sur le choix d'objectif (étape 2).
        _sourceDir = AutoDetectM0();
        if (_sourceDir != null && !InstallManager.NeedsInit(_sourceDir)) _step = Step.Goal;

        Title = Loc.T("wiz.title"); Owner = owner; Width = 720; Height = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = B("bgRoot"); Foreground = B("fg");
        FontFamily = new FontFamily("Segoe UI"); FontSize = 13;

        var root = new DockPanel();
        Content = root;

        // En-tête : titre + indicateur d'étape.
        var head = new StackPanel { Margin = new Thickness(20, 16, 20, 6) };
        head.Children.Add(new TextBlock
        {
            Text = "🧙  " + Loc.T("wiz.title"), Foreground = B("accent"),
            FontFamily = new FontFamily("Consolas"), FontWeight = FontWeights.Bold, FontSize = 19,
        });
        _stepLabel = new TextBlock { Foreground = B("dim"), FontSize = 12, Margin = new Thickness(0, 2, 0, 0) };
        head.Children.Add(_stepLabel);
        head.Children.Add(new Border { Height = 1, Background = B("bgFrame2"), Margin = new Thickness(0, 8, 0, 0) });
        DockPanel.SetDock(head, Dock.Top);
        root.Children.Add(head);

        DockPanel.SetDock(_footer, Dock.Bottom);
        root.Children.Add(_footer);

        root.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _body });

        Render();
    }

    // ===== Rendu par étape =====
    private void Render()
    {
        _body.Children.Clear();
        _footer.Children.Clear();
        int n = (int)_step + 1;
        _stepLabel.Text = string.Format(Loc.T("wiz.step"), System.Math.Min(n, 4), 4);

        switch (_step)
        {
            case Step.Source: RenderSource(); break;
            case Step.Goal: RenderGoal(); break;
            case Step.Destination: RenderDestination(); break;
            case Step.Run: RenderRun(); break;
            case Step.Done: RenderDone(); break;
        }
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

    /// <summary>M0 = source unique : la 1re install VIERGE découverte (Steam en priorité). null si aucune.</summary>
    private string? AutoDetectM0()
    {
        foreach (var d in InstallDiscovery.DiscoverAll(_config.KnownInstalls))
            if (InstallManager.IsVanilla(d)) return d;
        return null;
    }

    // ----- Étape 1 : M0 (source vierge, normalement auto-détectée) -----
    private void RenderSource()
    {
        _body.Children.Add(Title2("wiz.s1.title"));
        _body.Children.Add(Para("wiz.s1.intro"));

        var installs = InstallDiscovery.DiscoverAll(_config.KnownInstalls);
        if (installs.Count > 0)
        {
            // Présélection M0 : la 1re install vierge (sinon la 1re tout court).
            if (_sourceDir == null) _sourceDir = installs.FirstOrDefault(InstallManager.IsVanilla) ?? installs[0];
            _body.Children.Add(new TextBlock { Text = Loc.T("wiz.s1.found"), Foreground = B("dim"), Margin = new Thickness(0, 0, 0, 6) });
            foreach (var dir in installs) _body.Children.Add(SourceRow(dir));

            // Garde-fou d'initialisation : la source choisie n'a jamais été lancée (INIZH.big présent) →
            // l'avertir + proposer de la lancer une fois, AVANT de passer à la suite.
            if (_sourceDir != null && InstallManager.NeedsInit(_sourceDir))
            {
                _body.Children.Add(new Border { Height = 1, Background = B("bgFrame2"), Margin = new Thickness(0, 10, 0, 8) });
                _body.Children.Add(new TextBlock
                {
                    Text = Loc.T("wiz.s1.init.warn"), Foreground = B("orange"),
                    TextWrapping = TextWrapping.Wrap, LineHeight = 17, Margin = new Thickness(0, 0, 0, 6),
                });
                _body.Children.Add(MakeButton("wiz.s1.init.btn", () => InitInstall(_sourceDir!), primary: true));
                _body.Children.Add(MakeButton("wiz.s1.init.recheck", Render));
            }
        }
        else
        {
            _body.Children.Add(new TextBlock { Text = Loc.T("wiz.s1.none"), Foreground = B("orange"),
                FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10) });

            // Zero Hour = choix principal : il embarque les fichiers de Generals (cf. ZH_Generals), donc il suffit.
            _body.Children.Add(MakeButton("wiz.s1.install.zh", () => SteamInstall(InstallManager.AppIdZeroHour), primary: true));
            _body.Children.Add(new TextBlock
            {
                Text = Loc.T("wiz.s1.install.note"), Foreground = B("dim"), FontSize = 11,
                TextWrapping = TextWrapping.Wrap, LineHeight = 16, Margin = new Thickness(2, 2, 0, 8),
            });
            // Generals = facultatif (campagne d'origine uniquement).
            _body.Children.Add(MakeButton("wiz.s1.install.gen", () => SteamInstall(InstallManager.AppIdGenerals)));
            _body.Children.Add(new Border { Height = 1, Background = B("bgFrame2"), Margin = new Thickness(0, 8, 0, 8) });
            _body.Children.Add(MakeButton("wiz.s1.refresh", Render));
        }

        // Toujours offrir un dossier manuel (EA App, rétail, copie, fork extrait).
        _body.Children.Add(MakeButton("wiz.s1.other", PickSourceFolder));

        var cancel = NavButton("wiz.cancel"); cancel.Click += (_, _) => Close();
        var next = NavButton("wiz.next", primary: true);
        // Bloqué tant que la source n'est pas choisie OU pas initialisée (init obligatoire avant la suite).
        bool ready = _sourceDir != null && !InstallManager.NeedsInit(_sourceDir);
        next.IsEnabled = ready;
        if (_sourceDir != null && !ready) next.ToolTip = Loc.T("wiz.s1.init.blocked");
        next.Click += (_, _) => { _step = Step.Goal; Render(); };
        AddFooter(cancel, next);
    }

    private Border SourceRow(string dir)
    {
        bool selected = string.Equals(dir, _sourceDir, System.StringComparison.OrdinalIgnoreCase);
        var (badgeKey, badgeColor) = VanillaBadge(dir);

        var inner = new StackPanel { Orientation = Orientation.Horizontal };
        inner.Children.Add(new Border
        {
            Width = 10, Height = 10, CornerRadius = new CornerRadius(5),
            Background = badgeColor, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0),
        });
        var col = new StackPanel();
        col.Children.Add(new TextBlock { Text = Path.GetFileName(dir.TrimEnd('\\', '/')), Foreground = B("fg"), FontWeight = FontWeights.SemiBold });
        string sub = dir + "   ·   " + Loc.T(badgeKey);
        if (InstallManager.NeedsInit(dir)) sub += "   ·   ⚠ " + Loc.T("wiz.s1.needinit");
        col.Children.Add(new TextBlock { Text = sub, Foreground = B("dim"), FontSize = 11 });
        inner.Children.Add(col);

        var border = new Border
        {
            BorderBrush = selected ? B("accent") : B("border"),
            BorderThickness = new Thickness(selected ? 2 : 1),
            Background = selected ? B("bgFrame2") : B("bgFrame"),
            CornerRadius = new CornerRadius(4), Padding = new Thickness(10, 7, 10, 7),
            Margin = new Thickness(0, 2, 0, 2), Cursor = System.Windows.Input.Cursors.Hand,
            Child = inner,
        };
        border.MouseLeftButtonUp += (_, _) => { _sourceDir = dir; Render(); };
        return border;
    }

    private (string key, Brush color) VanillaBadge(string dir)
    {
        if (InstallManager.IsVanilla(dir)) return ("wiz.badge.vanilla", new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)));
        if (InstallManager.IsModded(dir)) return ("wiz.badge.modded", new SolidColorBrush(Color.FromRgb(0xFF, 0xB3, 0x00)));
        return ("wiz.badge.other", new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E)));
    }

    private void SteamInstall(string appId)
    {
        // Message AVANT : Steam ouvre l'install ; rappeler de LANCER le jeu une fois après (initialisation),
        // puis de revenir cliquer « Rechercher à nouveau ». Pas de bouton « lancer » dédié : on lance depuis Steam.
        if (!Dialogs.Confirm(this, Loc.T("wiz.title"), Loc.T("wiz.s1.steam.before"))) return;
        if (InstallManager.SteamLifecycle("install", appId))
            Dialogs.Info(this, Loc.T("wiz.title"), Loc.T("wiz.s1.steam.started"));
        else
            Dialogs.Info(this, Loc.T("wiz.title"), Loc.T("wiz.s1.steam.failed"));
    }

    /// <summary>Initialise une install non lancée : la démarre une fois via Steam (steam://run/&lt;appId&gt;).
    /// Hors Steam (copie/fork) : pas d'appId → conseiller un lancement manuel.</summary>
    private void InitInstall(string dir)
    {
        string? appId = InstallManager.SteamAppId(dir);
        if (appId != null)
            Dialogs.Info(this, Loc.T("wiz.title"),
                InstallManager.SteamLifecycle("run", appId) ? Loc.T("wiz.s1.init.started") : Loc.T("wiz.s1.steam.failed"));
        else
            Dialogs.Info(this, Loc.T("wiz.title"), Loc.T("wiz.s1.init.manual"));
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

    // ----- Étape 2 : Objectif -----
    private void RenderGoal()
    {
        _body.Children.Add(Title2("wiz.s2.title"));
        _body.Children.Add(Para("wiz.s2.intro"));

        _body.Children.Add(GoalRow(Goal.KeepVanilla, "wiz.goal.keep", "wiz.goal.keep.desc"));
        _body.Children.Add(GoalRow(Goal.GenLauncher, "wiz.goal.modded", "wiz.goal.modded.desc"));
        _body.Children.Add(GoalRow(Goal.Fork, "wiz.goal.fork", "wiz.goal.fork.desc"));

        var back = NavButton("wiz.back"); back.Click += (_, _) => { _step = Step.Source; Render(); };
        var cancel = NavButton("wiz.cancel"); cancel.Click += (_, _) => Close();
        var next = NavButton("wiz.next", primary: true);
        next.Click += (_, _) =>
        {
            // Seul « garder M0 » ne copie rien ; M1 (GenLauncher) et Mx (Fork) sont des copies de M0.
            if (_goal == Goal.KeepVanilla) { _step = Step.Done; Render(); }
            else { _step = Step.Destination; Render(); }
        };
        AddFooter(cancel, back, next);
    }

    private Border GoalRow(Goal g, string titleKey, string descKey)
    {
        bool selected = _goal == g;
        var col = new StackPanel();
        col.Children.Add(new TextBlock { Text = Loc.T(titleKey), Foreground = selected ? B("accent") : B("fg"), FontWeight = FontWeights.SemiBold });
        col.Children.Add(new TextBlock { Text = Loc.T(descKey), Foreground = B("dim"), FontSize = 11, TextWrapping = TextWrapping.Wrap, LineHeight = 16, Margin = new Thickness(0, 1, 0, 0) });
        var border = new Border
        {
            BorderBrush = selected ? B("accent") : B("border"),
            BorderThickness = new Thickness(selected ? 2 : 1),
            Background = selected ? B("bgFrame2") : B("bgFrame"),
            CornerRadius = new CornerRadius(4), Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 3, 0, 3), Cursor = System.Windows.Input.Cursors.Hand,
            Child = col,
        };
        border.MouseLeftButtonUp += (_, _) => { _goal = g; Render(); };
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

        _body.Children.Add(MakeButton("wiz.s3.pick", () =>
        {
            var dlg = new OpenFolderDialog { Title = Loc.T("wiz.s3.pick") };
            if (dlg.ShowDialog() != true) return;
            _destDir = dlg.FolderName;
            Render();
        }));

        _body.Children.Add(new TextBlock
        {
            Text = Loc.T("wiz.s3.dest") + " " + (_destDir ?? Loc.T("wiz.s3.nodest")),
            Foreground = _destDir != null ? B("fg") : B("dim"),
            FontFamily = new FontFamily("Consolas"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 10),
        });

        bool ok = RenderGuards();

        var back = NavButton("wiz.back"); back.Click += (_, _) => { _step = Step.Goal; Render(); };
        var cancel = NavButton("wiz.cancel"); cancel.Click += (_, _) => Close();
        var copy = NavButton("wiz.copy", primary: true);
        copy.IsEnabled = ok;
        copy.Click += async (_, _) => await StartCopyAsync();
        AddFooter(cancel, back, copy);
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

    // ----- Étape 5 : Terminé -----
    private void RenderDone()
    {
        _body.Children.Add(new TextBlock
        {
            Text = Loc.T("wiz.done.title"), Foreground = B("accent"),
            FontWeight = FontWeights.Bold, FontSize = 16, Margin = new Thickness(0, 0, 0, 8),
        });

        string? finalDir;
        if (_goal == Goal.KeepVanilla)
        {
            // Sans copie : on enregistre l'install Steam (M0) telle quelle.
            finalDir = _sourceDir;
            _body.Children.Add(new TextBlock { Text = Loc.T("wiz.done.keep"), Foreground = B("fg"),
                TextWrapping = TextWrapping.Wrap, LineHeight = 18, Margin = new Thickness(0, 0, 0, 8) });
        }
        else if (_copyResult is { Ok: true })
        {
            finalDir = _destDir;
            _body.Children.Add(new TextBlock
            {
                Text = string.Format(Loc.T("wiz.done.copyok"), _destDir, Mb(_copyResult.Bytes)),
                Foreground = B("fg"), TextWrapping = TextWrapping.Wrap, LineHeight = 18, Margin = new Thickness(0, 0, 0, 6),
            });
            // Prochaine étape selon l'objectif.
            string nextKey = _goal == Goal.Fork ? "wiz.done.next.fork" : "wiz.done.modded";
            _body.Children.Add(new TextBlock { Text = Loc.T(nextKey), Foreground = B("orange"),
                TextWrapping = TextWrapping.Wrap, LineHeight = 18, Margin = new Thickness(0, 0, 0, 8) });
            // M1 (GenLauncher) : statut des prérequis système + guidage GenLauncher (M0 reste vierge).
            if (_goal == Goal.GenLauncher) RenderModdedSteps(_destDir!);
        }
        else
        {
            finalDir = null;
            _body.Children.Add(new TextBlock
            {
                Text = string.Format(Loc.T("wiz.done.copyfail"), _copyResult?.Error ?? "?"),
                Foreground = B("orange"), TextWrapping = TextWrapping.Wrap, LineHeight = 18,
            });
        }

        if (finalDir != null)
        {
            // Toutes les installs (M0/M1/Mx) sont enregistrées dans le tableau.
            _register(finalDir);
            _body.Children.Add(new TextBlock { Text = Loc.T("wiz.done.registered"),
                Foreground = B("dim"), FontSize = 12, Margin = new Thickness(0, 0, 0, 8) });

            _body.Children.Add(MakeButton("wiz.done.openfolder", () =>
            {
                try { Process.Start(new ProcessStartInfo { FileName = finalDir, UseShellExecute = true }); } catch { }
            }));
        }

        var finish = NavButton("wiz.finish", primary: true); finish.Click += (_, _) => Close();
        AddFooter(finish);
    }

    /// <summary>Étapes post-copie d'une install moddée : prérequis système (redists/DirectX — guider si
    /// absents, ça ne touche pas M0) + GenLauncher (télécharger via navigateur, puis GenSpeed dézippe et
    /// pose l'exe dans <paramref name="destDir"/> — la copie).</summary>
    private void RenderModdedSteps(string destDir)
    {
        var pr = InstallManager.CheckPrereqs();
        if (pr.AllOk)
            _body.Children.Add(new TextBlock { Text = Loc.T("wiz.prereq.ok"), Foreground = B("fg"),
                TextWrapping = TextWrapping.Wrap, LineHeight = 17, Margin = new Thickness(0, 0, 0, 6) });
        else
        {
            var missing = new List<string>();
            if (!pr.VcRedist) missing.Add(Loc.T("wiz.prereq.vc"));
            if (!pr.DirectX9) missing.Add(Loc.T("wiz.prereq.dx"));
            _body.Children.Add(new TextBlock { Text = string.Format(Loc.T("wiz.prereq.missing"), string.Join(", ", missing)),
                Foreground = B("orange"), TextWrapping = TextWrapping.Wrap, LineHeight = 17, Margin = new Thickness(0, 0, 0, 4) });
            _body.Children.Add(MakeButton("wiz.btn.directx", () => OpenUrl("https://www.microsoft.com/en-us/download/details.aspx?id=35")));
        }
        // Voie AUTO (recommandée) : GenSpeed télécharge le zip DIRECT (lien manifeste gen.insave.ovh),
        // dézippe, pose l'exe dans la copie et propose de lancer. Zéro navigateur, zéro manip.
        _body.Children.Add(MakeButton("wiz.btn.gl.auto", () => AutoInstallGenLauncher(destDir), primary: true));
        // Repli manuel : télécharger via le navigateur (manifeste→config→ModDB), puis installer le zip téléchargé.
        _body.Children.Add(MakeButton("wiz.btn.genlauncher", OpenGenLauncherDownload));
        _body.Children.Add(MakeButton("wiz.btn.gl.install", () => InstallGenLauncher(destDir)));
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
        string? url = await InstallManager.FetchGenLauncherDownloadLinkAsync();
        if (string.IsNullOrWhiteSpace(url)) url = _config.GenLauncherUrl;
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
        // Pré-créer l'Options.ini baseline (anti-mismatch + perf) AVANT le 1er lancement du jeu → calé dès la
        // 1re partie (le jeu lit l'existant et complète le reste). Ensuite AutoTune le maintient à chaque run.
        var optSeed = MultiplayerTuning.ApplyOptions(MultiplayerTuning.DefaultOptionsIniPath(), ScreenInfo.NativeResolution());
        if (optSeed.Ok && optSeed.Applied != 0) _log(string.Format(Loc.T("tune.opt.ok"), optSeed.Applied));
        // Raccourci Bureau (« GenLauncher » ; suffixe du dossier seulement si collision avec une autre install).
        CreateDesktopShortcut(res.ExePath!, destDir);
        if (Dialogs.Confirm(this, Loc.T("wiz.title"), string.Format(Loc.T("gl.launch.ask"), destDir)))
            try
            {
                Process.Start(new ProcessStartInfo { FileName = res.ExePath!, WorkingDirectory = destDir, UseShellExecute = true, Verb = "runas" });
                _log(string.Format(Loc.T("launch.started"), "GenLauncher.exe"));
            }
            catch (System.ComponentModel.Win32Exception) { _log(Loc.T("genl.cancel")); }
    }

    /// <summary>Ouvre le téléchargement GenLauncher dans le navigateur : lien à jour lu dans le manifeste
    /// p0ls3r ; à défaut le lien éditable de la config ; à défaut la page ModDB.</summary>
    private async void OpenGenLauncherDownload()
    {
        _log(Loc.T("gl.link.resolving"));
        string? url = await InstallManager.FetchGenLauncherDownloadLinkAsync();
        if (string.IsNullOrWhiteSpace(url)) url = _config.GenLauncherUrl;
        if (string.IsNullOrWhiteSpace(url)) url = "https://www.moddb.com/mods/genlauncher/downloads";
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
