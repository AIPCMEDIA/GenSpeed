using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using GenSpeed.Core;

namespace GenSpeed.App;

/// <summary>Panneau « Mods autonomes (forks) » : catalogue ÉDITABLE (nom → dépôt GitHub) + installation automatique
/// d'un fork sur une COPIE de ZH vierge (M0 jamais touché). Voir <see cref="ForkInstaller"/> et
/// <see cref="ForkCatalog"/>. Reborn Omega est livré par défaut ; Generals X (ou un autre) s'ajoute ici sans
/// recompiler.</summary>
public sealed class ForksWindow : Window
{
    private static Brush B(string key) => (Brush)Application.Current.FindResource(key);
    private static Style? St(string key) => Application.Current.TryFindResource(key) as Style;

    private readonly GenConfig _config;
    private readonly Action<string> _log;
    private readonly Action<string> _onInstalled;
    private readonly List<string> _installs;
    private readonly List<ForkDef> _work;

    private ComboBox _srcCombo = null!;
    private TextBox _destBox = null!;
    private StackPanel _listPanel = null!;
    private Border _progPanel = null!;
    private ProgressBar _bar = null!;
    private TextBlock _progText = null!;
    private ScrollViewer _scroll = null!;
    private Panel _footer = null!;
    private bool _busy;

    // Carte d'édition d'un fork → ses champs (relus à l'install / à la sauvegarde).
    private readonly List<(ForkDef Def, TextBox Name, TextBox Repo, TextBox Regex)> _cards = new();

    public static void Show(Window owner, GenConfig config, List<string> installs, Action<string> log, Action<string> onInstalled)
        => new ForksWindow(owner, config, installs, log, onInstalled).ShowDialog();

    private ForksWindow(Window owner, GenConfig config, List<string> installs, Action<string> log, Action<string> onInstalled)
    {
        _config = config; _log = log; _onInstalled = onInstalled; _installs = installs;
        _work = ForkCatalog.Effective(config.Forks).Select(f => f.Clone()).ToList();

        Title = Loc.T("fork.title"); Owner = owner; Width = 800; Height = 660;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = B("bgRoot"); Foreground = B("fg");
        FontFamily = new FontFamily("Segoe UI"); FontSize = 13;

        var root = new DockPanel();

        // ----- En-tête -----
        var head = new StackPanel { Margin = new Thickness(16, 14, 16, 6) };
        head.Children.Add(new TextBlock { Text = "🔧  " + Loc.T("fork.title"), Foreground = B("accent"),
            FontFamily = new FontFamily("Consolas"), FontWeight = FontWeights.Bold, FontSize = 18 });
        head.Children.Add(new TextBlock { Text = Loc.T("fork.intro"), Foreground = B("dim"), FontSize = 12,
            TextWrapping = TextWrapping.Wrap, LineHeight = 17, Margin = new Thickness(0, 3, 0, 0) });
        head.Children.Add(new Border { Height = 1, Background = B("bgFrame2"), Margin = new Thickness(0, 8, 0, 0) });
        DockPanel.SetDock(head, Dock.Top); root.Children.Add(head);

        // ----- Pied -----
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(16, 8, 16, 12) };
        var add = new Button { Content = Loc.T("fork.add"), MinWidth = 150, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(12, 6, 12, 6) };
        add.Click += (_, _) => { SyncCardsToWork(); _work.Add(new ForkDef { Id = "fork-" + Guid.NewGuid().ToString("N")[..6], Name = Loc.T("fork.new"), Repo = "", DataAssetRegex = @"\.zip$" }); RebuildList(); };
        var save = new Button { Content = Loc.T("fork.savecat"), MinWidth = 160, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(12, 6, 12, 6) };
        if (St("PrimaryButton") is { } s) save.Style = s;
        save.Click += (_, _) => SaveCatalog();
        var close = new Button { Content = Loc.T("links.close"), MinWidth = 100, Padding = new Thickness(12, 6, 12, 6) };
        close.Click += (_, _) => Close();
        footer.Children.Add(add); footer.Children.Add(save); footer.Children.Add(close);
        _footer = footer;
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);

        // ----- Zone progression (cachée au repos) -----
        _bar = new ProgressBar { Height = 16, Minimum = 0, Maximum = 100, Margin = new Thickness(0, 0, 0, 4) };
        _progText = new TextBlock { Foreground = B("dim"), FontSize = 12, TextWrapping = TextWrapping.Wrap };
        _progPanel = new Border
        {
            Margin = new Thickness(16, 0, 16, 8), Padding = new Thickness(10),
            Background = B("bgFrame2"), Visibility = Visibility.Collapsed,
            Child = new StackPanel { Children = { _bar, _progText } },
        };
        DockPanel.SetDock(_progPanel, Dock.Bottom); root.Children.Add(_progPanel);

        // ----- Corps : source/dest + liste des forks -----
        var body = new StackPanel { Margin = new Thickness(16, 8, 16, 8) };

        body.Children.Add(new TextBlock { Text = Loc.T("fork.src"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 2, 0, 2) });
        var srcRow = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 8) };
        var browseSrc = new Button { Content = Loc.T("fork.browse"), Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(6, 0, 0, 0) };
        browseSrc.Click += (_, _) => BrowseSource();
        _srcCombo = new ComboBox { Padding = new Thickness(6, 5, 6, 5), VerticalContentAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(browseSrc, Dock.Right); srcRow.Children.Add(browseSrc); srcRow.Children.Add(_srcCombo);
        body.Children.Add(srcRow);
        FillSources();

        body.Children.Add(new TextBlock { Text = Loc.T("fork.dest"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 2, 0, 2) });
        var destRow = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 4) };
        var browseDest = new Button { Content = Loc.T("fork.browse"), Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(6, 0, 0, 0) };
        browseDest.Click += (_, _) => { var d = new OpenFolderDialog { Title = Loc.T("fork.dest") }; if (d.ShowDialog() == true) _destBox.Text = d.FolderName; };
        _destBox = new TextBox { Text = _config.InstallParent ?? InstallManager.SuggestInstallParent(),
            FontFamily = new FontFamily("Consolas"), FontSize = 12, Padding = new Thickness(6, 5, 6, 5), VerticalContentAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(browseDest, Dock.Right); destRow.Children.Add(browseDest); destRow.Children.Add(_destBox);
        body.Children.Add(destRow);
        body.Children.Add(new TextBlock { Text = Loc.T("fork.dest.hint"), Foreground = B("dim"), FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });

        body.Children.Add(new Border { Height = 1, Background = B("bgFrame2"), Margin = new Thickness(0, 4, 0, 8) });
        body.Children.Add(new TextBlock { Text = Loc.T("fork.catalog"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });

        _listPanel = new StackPanel();
        body.Children.Add(_listPanel);
        RebuildList();

        _scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = body };
        root.Children.Add(_scroll);
        Content = root;
    }

    // ===== Sources M0 (copies vierges candidates) =====
    private void FillSources()
    {
        _srcCombo.Items.Clear();
        // Source = jeu d'origine VIERGE. On AUTO-DÉTECTE (Steam/EA/connus) — fiable même si la liste passée est
        // incomplète (cas du passage depuis l'assistant « Installation complète » → le combo était vide).
        var cands = InstallDiscovery.DiscoverAll(_config.KnownInstalls).Where(IsVanillaSource).ToList();
        foreach (var d in _installs)
            if (IsVanillaSource(d) && !cands.Any(c => string.Equals(c, d, StringComparison.OrdinalIgnoreCase)))
                cands.Add(d);
        foreach (var d in cands) _srcCombo.Items.Add(d);
        if (_srcCombo.Items.Count > 0) _srcCombo.SelectedIndex = 0;
    }

    private static bool IsVanillaSource(string d)
    {
        try { return InstallManager.IsVanilla(d) && !File.Exists(Path.Combine(d, "GenLauncher.exe")); }
        catch { return false; }
    }

    private void BrowseSource()
    {
        var dlg = new OpenFolderDialog { Title = Loc.T("fork.src") };
        if (dlg.ShowDialog() != true) return;
        if (!GameLocator.IsZhFolder(dlg.FolderName)) { Dialogs.Info(this, "GenSpeed", Loc.T("fork.src.notzh")); return; }
        if (!_srcCombo.Items.Contains(dlg.FolderName)) _srcCombo.Items.Add(dlg.FolderName);
        _srcCombo.SelectedItem = dlg.FolderName;
    }

    // ===== Liste éditable =====
    private void RebuildList()
    {
        _listPanel.Children.Clear();
        _cards.Clear();
        foreach (var f in _work)
        {
            var card = new Border { Background = B("bgFrame2"), Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(10), CornerRadius = new CornerRadius(4) };
            var sp = new StackPanel();

            // Toujours visible : le NOM.
            var nameBox = LabeledBox(sp, Loc.T("fork.f.name"), f.Name);

            if (!string.IsNullOrWhiteSpace(f.Notes))
                sp.Children.Add(new TextBlock { Text = "ℹ " + f.Notes, Foreground = B("dim"), FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 4) });

            // Champs TECHNIQUES (source GitHub + filtre) repliés sous « Avancé » — cachés au novice, déployés
            // automatiquement pour un fork NEUF (repo vide, à renseigner).
            var adv = new StackPanel { Visibility = string.IsNullOrWhiteSpace(f.Repo) ? Visibility.Visible : Visibility.Collapsed };
            var repoBox = LabeledBox(adv, Loc.T("fork.f.repo"), f.Repo);
            var regexBox = LabeledBox(adv, Loc.T("fork.f.regex"), f.DataAssetRegex);
            var advToggle = new Button { Content = Loc.T("fork.advanced"), HorizontalAlignment = HorizontalAlignment.Left,
                Background = System.Windows.Media.Brushes.Transparent, BorderThickness = new Thickness(0),
                Foreground = B("dim"), Padding = new Thickness(0, 2, 0, 2), Margin = new Thickness(0, 2, 0, 0), Cursor = System.Windows.Input.Cursors.Hand };
            advToggle.Click += (_, _) => adv.Visibility = adv.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
            sp.Children.Add(advToggle);
            sp.Children.Add(adv);

            var btns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            var install = new Button { Content = Loc.T("fork.install"), Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 0) };
            if (St("PrimaryButton") is { } ps) install.Style = ps;
            var captured = f;
            install.Click += (_, _) => InstallFork(captured);
            var local = new Button { Content = Loc.T("fork.local"), Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 0, 8, 0) };
            local.Click += (_, _) => InstallFromLocal(captured);
            var remove = new Button { Content = Loc.T("fork.remove"), Padding = new Thickness(10, 6, 10, 6) };
            remove.Click += (_, _) => { SyncCardsToWork(); _work.Remove(captured); RebuildList(); };
            btns.Children.Add(install); btns.Children.Add(local); btns.Children.Add(remove);
            sp.Children.Add(btns);

            card.Child = sp;
            _listPanel.Children.Add(card);
            _cards.Add((f, nameBox, repoBox, regexBox));
        }
    }

    private TextBox LabeledBox(Panel parent, string label, string value)
    {
        parent.Children.Add(new TextBlock { Text = label, Foreground = B("fg"), FontSize = 11, Margin = new Thickness(0, 4, 0, 1) });
        var tb = new TextBox { Text = value, FontFamily = new FontFamily("Consolas"), FontSize = 12, Padding = new Thickness(6, 4, 6, 4) };
        parent.Children.Add(tb);
        return tb;
    }

    /// <summary>Relit les champs des cartes dans les ForkDef (avant install / sauvegarde / rebuild).</summary>
    private void SyncCardsToWork()
    {
        foreach (var (def, name, repo, regex) in _cards)
        {
            def.Name = (name.Text ?? "").Trim();
            def.Repo = (repo.Text ?? "").Trim();
            def.DataAssetRegex = string.IsNullOrWhiteSpace(regex.Text) ? @"\.zip$" : regex.Text.Trim();
            if (string.IsNullOrWhiteSpace(def.Id)) def.Id = Slug(def.Name);
        }
    }

    private static string Slug(string s)
    {
        s = new string((s ?? "").ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
        while (s.Contains("--")) s = s.Replace("--", "-");
        s = s.Trim('-');
        return s.Length > 0 ? s : "fork-" + Guid.NewGuid().ToString("N")[..6];
    }

    private void SaveCatalog()
    {
        SyncCardsToWork();
        var clean = _work.Where(f => !string.IsNullOrWhiteSpace(f.Name) && !string.IsNullOrWhiteSpace(f.Repo)).ToList();
        // Si identique aux défauts → ne rien stocker (on suivra les futurs défauts codés).
        _config.Forks = SameAsDefaults(clean) ? new List<ForkDef>() : clean;
        ConfigStore.Save(_config);
        _log(Loc.T("fork.saved"));
        Dialogs.Info(this, "GenSpeed", Loc.T("fork.saved"));
    }

    private static bool SameAsDefaults(List<ForkDef> list)
    {
        var def = ForkCatalog.Defaults();
        if (list.Count != def.Count) return false;
        for (int i = 0; i < list.Count; i++)
            if (list[i].Id != def[i].Id || list[i].Repo != def[i].Repo || list[i].Name != def[i].Name) return false;
        return true;
    }

    // ===== Installation d'un fork (commun) =====

    /// <summary>Valide la base M0 + l'emplacement et calcule le dossier cible. null si invalide (message affiché).</summary>
    private (string Src, string Dest, string Parent)? PrepareInstall(ForkDef fork)
    {
        string? src = _srcCombo.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(src) || !Directory.Exists(src)) { Dialogs.Info(this, "GenSpeed", Loc.T("fork.nosrc")); return null; }
        if (!IsVanillaSource(src) && !Dialogs.Confirm(this, Loc.T("fork.title"), Loc.T("fork.notvanilla"))) return null;

        string parent = (_destBox.Text ?? "").Trim();
        if (string.IsNullOrWhiteSpace(parent)) { Dialogs.Info(this, "GenSpeed", Loc.T("fork.nodest")); return null; }
        string dest = Path.Combine(parent, SafeFolder(fork.Name));
        if (Directory.Exists(dest)) { Dialogs.Info(this, "GenSpeed", string.Format(Loc.T("fork.destexists"), dest)); return null; }
        return (src, dest, parent);
    }

    /// <summary>Enregistre une install de fork réussie (identité + lanceur + install connue) et notifie l'UI.</summary>
    private void RegisterAndFinish(ForkDef fork, ForkInstallResult res, string dest, string parent)
    {
        if (!_config.KnownInstalls.Any(p => string.Equals(p.TrimEnd('\\', '/'), dest.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)))
            _config.KnownInstalls.Add(dest);
        _config.InstallForks[dest] = fork.Id;
        if (res.PrimaryExe != null)
        {
            string exeName = Path.GetFileName(res.PrimaryExe);
            _config.LaunchExes[dest + "::"] = exeName;                       // clé « aucun mod coché »
            _config.LaunchExes[dest + "::VANILLA (Data/INI)"] = exeName;     // clé probable (cible du fork cochée)
        }
        _config.InstallParent = parent;
        // Installer un fork est une intention EXPLICITE de persister : réactive la sauvegarde si un « désinstaller
        // tout » l'avait coupée (Suppressed) dans la même session — sinon le fork ne serait pas écrit et
        // disparaîtrait au redémarrage (cause réelle du « fork absent après reboot »).
        ConfigStore.Suppressed = false;
        ConfigStore.Save(_config);

        _log(string.Format(Loc.T("fork.done"), fork.Name, dest));
        _onInstalled(dest);
        Dialogs.Info(this, "GenSpeed", string.Format(Loc.T("fork.done.msg"), fork.Name, dest));
        FillSources();
    }

    // ===== Install depuis GitHub (dernière release) =====
    private async void InstallFork(ForkDef fork)
    {
        if (_busy) return;
        SyncCardsToWork();
        if (string.IsNullOrWhiteSpace(fork.Repo)) { Dialogs.Info(this, "GenSpeed", Loc.T("fork.norepo")); return; }
        var prep = PrepareInstall(fork);
        if (prep == null) return;
        var (src, dest, parent) = prep.Value;

        SetBusy(true);
        try
        {
            Progress(Loc.T("fork.fetching"), 0);
            var rel = await ForkInstaller.FetchLatestReleaseAsync(fork.Repo);
            if (rel == null) { Dialogs.Info(this, "GenSpeed", string.Format(Loc.T("fork.norelease"), fork.Repo)); return; }

            var data = ForkInstaller.PickDataAsset(rel, fork.DataAssetRegex);
            var exes = rel.Exes.ToList();
            if (data == null)
            {
                // Pas d'archive de données sur GitHub (cas Reborn Omega v1.01 : données dans un .rar à part).
                // On guide vers l'install « depuis un fichier local ».
                string why = rel.Rars.Any() ? string.Format(Loc.T("fork.onlyrar"), rel.Tag) : string.Format(Loc.T("fork.nodata"), rel.Tag);
                Dialogs.Info(this, "GenSpeed", why);
                return;
            }

            // CONFIRMATION de téléchargement (nom, source, taille) — règle de sécurité.
            long totalMb = (data.Size + exes.Sum(e => e.Size)) >> 20;
            var names = new List<string> { $"{data.Name} ({data.Size >> 20} Mo)" };
            foreach (var e in exes) names.Add($"{e.Name} ({e.Size >> 20} Mo)");
            string confirmMsg = string.Format(Loc.T("fork.confirm"),
                fork.Name, rel.Tag, "github.com/" + fork.Repo, string.Join("\n  • ", names), totalMb, dest);
            if (!Dialogs.Confirm(this, Loc.T("fork.title"), confirmMsg)) return;

            var progress = new Progress<ForkProgress>(p => Progress($"{p.Phase}  {(string.IsNullOrEmpty(p.Detail) ? "" : "· " + p.Detail)}", p.Percent));
            string tmp = Path.Combine(Path.GetTempPath(), "genspeed-fork-" + fork.Id);
            _log(string.Format(Loc.T("fork.installing"), fork.Name, src, dest));
            var res = await ForkInstaller.InstallAsync(src, dest, fork, rel, tmp, progress);
            if (!res.Ok) { Dialogs.Info(this, "GenSpeed", string.Format(Loc.T("fork.failed"), res.Error)); _log("⚠ " + res.Error); return; }
            RegisterAndFinish(fork, res, dest, parent);
        }
        finally { SetBusy(false); }
    }

    // ===== Install depuis un fichier/dossier local (.zip / .rar / .7z / dossier déjà extrait) =====
    private async void InstallFromLocal(ForkDef fork)
    {
        if (_busy) return;
        SyncCardsToWork();
        var prep = PrepareInstall(fork);
        if (prep == null) return;
        var (src, dest, parent) = prep.Value;

        // Choisir : une archive, ou un dossier déjà extrait.
        string optArchive = Loc.T("fork.local.archive"), optFolder = Loc.T("fork.local.folder");
        string? choice = Dialogs.Choose(this, Loc.T("fork.local"), Loc.T("fork.local.msg"), new[] { optArchive, optFolder });
        if (choice == null) return;

        string? localSource = null;
        if (choice == optArchive)
        {
            var dlg = new OpenFileDialog { Title = Loc.T("fork.local.archive"),
                Filter = "Archives (*.zip;*.rar;*.7z)|*.zip;*.rar;*.7z|Tous les fichiers|*.*",
                InitialDirectory = InstallManager.DownloadsFolder() };
            if (dlg.ShowDialog() != true) return;
            localSource = dlg.FileName;
            // .rar/.7z sans 7-Zip → message clair avant de copier 3 Go pour rien.
            string ext = Path.GetExtension(localSource).ToLowerInvariant();
            if (ext is ".rar" or ".7z" && ForkInstaller.SevenZipPath() == null)
            { Dialogs.Info(this, "GenSpeed", Loc.T("fork.no7zip")); return; }
        }
        else
        {
            var dlg = new OpenFolderDialog { Title = Loc.T("fork.local.folder") };
            if (dlg.ShowDialog() != true) return;
            localSource = dlg.FolderName;
        }

        if (!Dialogs.Confirm(this, Loc.T("fork.title"),
            string.Format(Loc.T("fork.local.confirm"), fork.Name, localSource, dest))) return;

        SetBusy(true);
        try
        {
            var progress = new Progress<ForkProgress>(p => Progress($"{p.Phase}  {(string.IsNullOrEmpty(p.Detail) ? "" : "· " + p.Detail)}", p.Percent));
            string tmp = Path.Combine(Path.GetTempPath(), "genspeed-fork-" + fork.Id);
            _log(string.Format(Loc.T("fork.installing"), fork.Name, src, dest));
            var res = await ForkInstaller.InstallFromLocalAsync(src, dest, fork, localSource!, tmp, progress);
            if (!res.Ok) { Dialogs.Info(this, "GenSpeed", string.Format(Loc.T("fork.failed"), res.Error)); _log("⚠ " + res.Error); return; }
            RegisterAndFinish(fork, res, dest, parent);
        }
        finally { SetBusy(false); }
    }

    private static string SafeFolder(string name)
    {
        var bad = Path.GetInvalidFileNameChars();
        string s = new string((name ?? "fork").Select(c => bad.Contains(c) ? '_' : c).ToArray()).Trim();
        return s.Length > 0 ? s : "Fork";
    }

    // ===== Helpers UI =====
    private void SetBusy(bool busy)
    {
        _busy = busy;
        _progPanel.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        _scroll.IsEnabled = !busy;
        _footer.IsEnabled = !busy;
        if (!busy) { _bar.Value = 0; _progText.Text = ""; }
    }

    private void Progress(string text, int pct)
    {
        _progText.Text = text;
        _bar.Value = Math.Clamp(pct, 0, 100);
    }
}
