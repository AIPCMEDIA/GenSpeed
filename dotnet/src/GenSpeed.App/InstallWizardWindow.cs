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
    private enum Goal { KeepM0, BaseM1, PlayableM2, Fork }

    private static Brush B(string key) => (Brush)Application.Current.FindResource(key);
    private static Style? St(string key) => Application.Current.TryFindResource(key) as Style;

    private readonly GenConfig _config;
    private readonly System.Action<string> _log;
    private readonly System.Action<string> _register;   // EnsureInstallListed(dir) + LoadMods()

    private Step _step = Step.Source;
    private Goal _goal = Goal.BaseM1;
    private string? _sourceDir;
    private string? _destDir;
    private CopyResult? _copyResult;

    private readonly StackPanel _body = new() { Margin = new Thickness(20, 16, 20, 16) };
    private readonly StackPanel _footer = new()
        { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(16, 8, 16, 12) };
    private TextBlock _stepLabel = null!;

    public static void Show(Window owner, GenConfig config, System.Action<string> log, System.Action<string> register)
        => new InstallWizardWindow(owner, config, log, register).ShowDialog();

    private InstallWizardWindow(Window owner, GenConfig config, System.Action<string> log, System.Action<string> register)
    {
        _config = config; _log = log; _register = register;

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

    // ----- Étape 1 : Source -----
    private void RenderSource()
    {
        _body.Children.Add(Title2("wiz.s1.title"));
        _body.Children.Add(Para("wiz.s1.intro"));

        var installs = InstallDiscovery.DiscoverAll(_config.KnownInstalls);
        if (installs.Count > 0)
        {
            if (_sourceDir == null && installs.Count == 1) _sourceDir = installs[0];   // une seule : présélection
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

        _body.Children.Add(GoalRow(Goal.KeepM0, "wiz.goal.keep", "wiz.goal.keep.desc"));
        _body.Children.Add(GoalRow(Goal.BaseM1, "wiz.goal.base", "wiz.goal.base.desc"));
        _body.Children.Add(GoalRow(Goal.PlayableM2, "wiz.goal.play", "wiz.goal.play.desc"));
        _body.Children.Add(GoalRow(Goal.Fork, "wiz.goal.fork", "wiz.goal.fork.desc"));

        var back = NavButton("wiz.back"); back.Click += (_, _) => { _step = Step.Source; Render(); };
        var cancel = NavButton("wiz.cancel"); cancel.Click += (_, _) => Close();
        var next = NavButton("wiz.next", primary: true);
        next.Click += (_, _) =>
        {
            if (_goal == Goal.KeepM0) { _step = Step.Done; Render(); }   // pas de copie : on garde M0
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

        _body.Children.Add(MakeButton("wiz.s3.pick", () =>
        {
            var dlg = new OpenFolderDialog { Title = Loc.T("wiz.s3.pick") };
            if (dlg.ShowDialog() == true) { _destDir = dlg.FolderName; Render(); }
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

    /// <summary>Affiche les garde-fous (source vierge pour un fork, NTFS, espace, src≠dest) et renvoie
    /// vrai si la copie peut démarrer (aucun bloqueur).</summary>
    private bool RenderGuards()
    {
        if (_destDir == null || _sourceDir == null) return false;
        bool ok = true;

        void Line(bool good, string text)
        {
            _body.Children.Add(new TextBlock { Text = text, Foreground = good ? B("fg") : B("orange"),
                TextWrapping = TextWrapping.Wrap, LineHeight = 17, Margin = new Thickness(0, 2, 0, 2) });
        }

        // src == dest : bloquant.
        if (string.Equals(Path.GetFullPath(_sourceDir).TrimEnd('\\'), Path.GetFullPath(_destDir).TrimEnd('\\'), System.StringComparison.OrdinalIgnoreCase))
        { Line(false, Loc.T("wiz.guard.same")); return false; }

        // Source vierge — exigée pour un fork (consigne des mods), conseillée pour M1.
        var intrus = InstallManager.NonVanillaItems(_sourceDir);
        if (_goal == Goal.Fork || _goal == Goal.BaseM1)
        {
            if (intrus.Count == 0) Line(true, Loc.T("wiz.guard.vanilla.ok"));
            else
            {
                Line(false, string.Format(Loc.T("wiz.guard.vanilla.warn"), string.Join(", ", intrus.Take(5))));
                if (_goal == Goal.Fork) ok = false;   // fork : bloquant ; M1 : simple avertissement
            }
        }

        // NTFS (symlinks GenLauncher).
        bool ntfs = InstallManager.IsNtfs(_destDir);
        Line(ntfs, Loc.T(ntfs ? "wiz.guard.ntfs.ok" : "wiz.guard.ntfs.bad"));

        // Espace disque.
        long need = InstallManager.DirSizeBytes(_sourceDir);
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
        if (_sourceDir == null || _destDir == null) return;
        _step = Step.Run; Render();
        _log(string.Format(Loc.T("wiz.s4.copying"), _destDir));
        _copyResult = await System.Threading.Tasks.Task.Run(() => InstallManager.CopyInstall(_sourceDir!, _destDir!));
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
        if (_goal == Goal.KeepM0)
        {
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
            // Prochaine étape selon l'objectif (GenPatcher = guidé ; orchestration CLI = Phase C).
            string nextKey = _goal switch
            {
                Goal.BaseM1 => "wiz.done.next.base",
                Goal.PlayableM2 => "wiz.done.next.play",
                Goal.Fork => "wiz.done.next.fork",
                _ => "wiz.done.next.base",
            };
            _body.Children.Add(new TextBlock { Text = Loc.T(nextKey), Foreground = B("orange"),
                TextWrapping = TextWrapping.Wrap, LineHeight = 18, Margin = new Thickness(0, 0, 0, 8) });
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
            _register(finalDir);   // enregistre + rafraîchit le tableau principal
            _body.Children.Add(new TextBlock { Text = Loc.T("wiz.done.registered"), Foreground = B("dim"),
                FontSize = 12, Margin = new Thickness(0, 0, 0, 8) });

            _body.Children.Add(MakeButton("wiz.done.openfolder", () =>
            {
                try { Process.Start(new ProcessStartInfo { FileName = finalDir, UseShellExecute = true }); } catch { }
            }));
        }

        var finish = NavButton("wiz.finish", primary: true); finish.Click += (_, _) => Close();
        AddFooter(finish);
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
