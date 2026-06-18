using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Win32;
using GenSpeed.Core;

namespace GenSpeed.App;

public partial class MainWindow
{
    // ===== Assistant d'installation propre (wizard) =====
    /// <summary>Ouvre le wizard d'installation. M0 (source vierge) est auto-détecté ; toute install créée
    /// (M0 gardé, M1 GenLauncher, Mx fork) est enregistrée dans le tableau.</summary>
    private void OnInstallWizard()
    {
        ConfigStore.Suppressed = false;   // (ré)installer = on veut de nouveau persister (annule un wipe précédent même session)
        InstallWizardWindow.Show(this, _config, Log,
            dir => { EnsureInstallListed(dir); LoadMods(); },   // install → tableau
            new WizardActions(                                  // actions du HUB (on orchestre l'existant)
                OpenForkCatalog: owner => { OnCfgForksFrom(owner); return System.Threading.Tasks.Task.CompletedTask; },
                OpenOptions: owner => { GameOptionsWindow.Show(owner, _config, LoadMods); return System.Threading.Tasks.Task.CompletedTask; },
                OpenUninstall: owner => UninstallFrom(owner, wipeAll: false),
                OpenUninstallAll: owner => UninstallFrom(owner, wipeAll: true),
                OpenDiagnostic: DiagExportFrom,
                OpenTable: () => Activate(),                    // « affiner » → ramène la fenêtre principale (tableau)
                ApplySpeedCamRawAll: ApplySpeedCamRawAll,   // valeurs brutes du composant partagé de l'assistant
                BuildModTable: BuildAssistantModTable,       // tableau (cases à cocher) inséré dans la page vitesse/caméra
                RestoreSelected: owner => { _ = RunPatch("restore", owner); },   // « revenir à l'original » (lignes cochées)
                LaunchGame: owner => { LaunchPickFrom(owner); return System.Threading.Tasks.Task.CompletedTask; },
                LaunchInstall: (owner, dir) => LaunchInstallDir(owner, dir)));
    }

    /// <summary>Mode avancé → rebascule vers l'assistant (le hub), qui recouvre de nouveau le tableau.</summary>
    private void OnSwitchToAssistant(object sender, System.Windows.RoutedEventArgs e) => OnInstallWizard();

    /// <summary>Au démarrage sans aucune install détectée : proposer 2 choix clairs (Installer le jeu
    /// via Steam → ouvre l'assistant ; ou indiquer un dossier existant) au lieu d'un sélecteur Windows brut.</summary>
    /// <returns>Vrai si une action a été menée (l'appelant doit re-scanner les installs).</returns>
    private bool PromptNoInstall()
    {
        string steam = Loc.T("noinst.steam");
        string folder = Loc.T("noinst.folder");
        string? pick = Dialogs.Choose(this, Loc.T("wiz.title"), Loc.T("noinst.msg"), new[] { steam, folder });
        if (pick == steam) { OnInstallWizard(); return true; }   // l'assistant gère Steam + la suite
        if (pick == folder)
        {
            var dir = AskGameDir();
            if (dir == null) return false;
            EnsureInstallListed(dir);
            return true;
        }
        return false;   // annulé
    }


    /// <summary>« GenSpeed sait TOUJOURS où est M2 » : résout les raccourcis Bureau « GenLauncher » (fil d'Ariane
    /// sur le disque, créé par le wizard) → ajoute le dossier de l'install aux installs connues (persisté).
    /// Survit à un reset de la config (le raccourci reste), marche même pour une install faite à la main, et
    /// s'auto-répare à chaque démarrage. Les chemins morts/non-ZH sont ignorés (DiscoverAll re-filtre de toute façon).</summary>
    private void SeedKnownFromShortcuts()
    {
        bool added = false;
        foreach (var folder in DesktopGenLauncherTargets())
        {
            if (!GameLocator.IsZhFolder(folder)) continue;
            if (_config.KnownInstalls.Any(p => string.Equals(p.TrimEnd('\\', '/'), folder.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))) continue;
            _config.KnownInstalls.Add(folder);
            added = true;
            Log(string.Format(Loc.T("log.gl.found"), folder));
        }
        if (added) ConfigStore.Save(_config);
    }

    /// <summary>Dossiers d'install pointés par les raccourcis « GenLauncher*.lnk » du Bureau (utilisateur + public).
    /// Cible = ...\GenLauncher.exe → on retient son dossier. Best-effort via WScript.Shell (COM).</summary>
    private IEnumerable<string> DesktopGenLauncherTargets()
    {
        var outp = new List<string>();
        try
        {
            var t = Type.GetTypeFromProgID("WScript.Shell");
            if (t == null) return outp;
            dynamic shell = Activator.CreateInstance(t)!;
            foreach (var desk in new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
            })
            {
                if (string.IsNullOrEmpty(desk) || !Directory.Exists(desk)) continue;
                foreach (var lnk in Directory.EnumerateFiles(desk, "*.lnk"))
                {
                    if (!Path.GetFileName(lnk).Contains("GenLauncher", StringComparison.OrdinalIgnoreCase)) continue;
                    try
                    {
                        dynamic sc = shell.CreateShortcut(lnk);
                        string target = (sc.TargetPath as string) ?? "";
                        if (target.EndsWith("GenLauncher.exe", StringComparison.OrdinalIgnoreCase))
                        {
                            string? dir = Path.GetDirectoryName(target);
                            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) outp.Add(dir!);
                        }
                    }
                    catch { }
                }
            }
        }
        catch { }
        return outp;
    }

    /// <summary>CALAGE AUTOMATIQUE (sans clic) : appelé à chaque chargement du tableau. Cale silencieusement
    /// l'Options.ini existant (anti-mismatch + perf) et le GenLauncherCfg.yaml de chaque install GenLauncher
    /// (ou le crée si GenLauncher.exe est là mais pas encore lancé). Idempotent : ne réécrit que si une valeur
    /// change → sûr à relancer en boucle, auto-réparateur. Sauté pendant un wipe ou si GenLauncher est ouvert
    /// (il réécrirait son YAML). Le pré-seed à l'install couvre la 1re session ; ceci garde tout calé ensuite.</summary>
    private void AutoTune()
    {
        if (ConfigStore.Suppressed || _installs.Count == 0) return;

        // 1) Options.ini (unique, Documents) — applique les CHOIX de l'utilisateur (sinon défauts sûrs/PC-aware).
        //    Uniquement s'il existe déjà (créé par le jeu/wizard/picker ; on n'invente pas un fichier ici).
        string? opt = MultiplayerTuning.FindOptionsIni();
        if (opt != null)
        {
            var r = MultiplayerTuning.ApplyOptionsValues(opt, GameOptions.EffectiveIni(_config));
            if (r.Ok && r.Applied != 0) Log(string.Format(Loc.T("tune.auto.opt"), r.Applied));
            else if (!r.Ok) Log("⚠ " + r.Error);
        }

        // 2) GenLauncherCfg.yaml par install — pas touché si GenLauncher est ouvert (il l'écraserait).
        if (RunningGameProcs().Contains("GenLauncher")) return;
        string vulkan = GameOptions.Vulkan(_config) ? "true" : "false";   // choix utilisateur (défaut false)
        foreach (var dir in _installs)
        {
            bool hasYaml = MultiplayerTuning.FindGenLauncherYaml(dir) != null;
            bool hasExe = File.Exists(Path.Combine(dir, "GenLauncher.exe"));
            if (!hasYaml && !hasExe) continue;
            var yr = MultiplayerTuning.SeedOrTuneYaml(dir);
            var yp = MultiplayerTuning.FindGenLauncherYaml(dir);
            if (yp != null) MultiplayerTuning.SetYamlKey(yp, "UseVulkan", vulkan);
            if (yr.Ok && yr.Applied != 0) Log(string.Format(Loc.T("tune.auto.yaml"), InstallLabel(dir)));
        }
    }

    /// <summary>⚙ Config → panneau « 🔗 Liens de téléchargement » : édite toutes les URL dont GenSpeed a besoin
    /// (GenLauncher, VC++, DirectX) sans recompiler. Un lien cassé se corrige ici.</summary>
    private void OnCfgLinks() => LinksWindow.Show(this, _config);

    /// <summary>⚙ Config → sélecteur « 🎛 Options de jeu » : résolution, souris, qualité graphique, anti-mismatch,
    /// Vulkan — pré-cochés (analyse PC), tous modifiables, avec explication par option. Écrit Options.ini + YAML.</summary>
    private void OnCfgGameOptions() => GameOptionsWindow.Show(this, _config, LoadMods);

    /// <summary>⚙ Config → « 🔧 Mods autonomes (forks) » : catalogue éditable (nom → dépôt GitHub) + install auto
    /// d'un fork (Reborn Omega…) sur une COPIE de ZH vierge (M0 intact). L'install créée est ajoutée au tableau.</summary>
    private void OnCfgForks() => OnCfgForksFrom(this);

    /// <summary>Variante avec fenêtre propriétaire (l'assistant peut posséder la fenêtre du catalogue → reste ouvert).</summary>
    private void OnCfgForksFrom(System.Windows.Window owner) => ForksWindow.Show(owner, _config, _installs, Log,
        dir => { EnsureInstallListed(dir); LoadMods(); });

    /// <summary>🌐 Multijoueur → « IP réseau du jeu » : ouvre les options de jeu directement sur la section Réseau
    /// (IP LAN / en ligne + SendDelay + pare-feu).</summary>
    private void OnNetFix() => GameOptionsWindow.Show(this, _config, LoadMods, startOnNetwork: true);

    /// <summary>Déplace PHYSIQUEMENT une install (depuis le panneau « Mes installs ») vers un autre emplacement,
    /// et « tout suit » : dossier déplacé (même volume = instantané), installs connues mises à jour, raccourci
    /// Bureau GenLauncher mis à jour (si M1), emplacement mémorisé. M1 garde le nom « GenLauncher » ; un fork garde
    /// son nom. BLOQUÉ pour une install Steam (M0 → se déplace via Steam) ou si GenLauncher/jeu tourne.</summary>
    internal async Task MoveInstallInteractive(System.Windows.Window owner, string dir)
    {
        if (GenSpeed.Core.InstallManager.SteamAppId(dir) != null)
        { Dialogs.Info(owner, "GenSpeed", Loc.T("m1move.steam")); return; }
        if (RunningGameProcs().Any(p => p is "GenLauncher" or "modded" or "generals" or "GeneralsZH"))
        { Dialogs.Info(owner, "GenSpeed", Loc.T("m1move.running")); return; }

        bool isGl = File.Exists(Path.Combine(dir, "GenLauncher.exe"));
        string folderName = isGl ? GenSpeed.Core.InstallManager.GenLauncherFolderName : Path.GetFileName(dir.TrimEnd('\\', '/'));

        var dlg = new OpenFolderDialog { Title = string.Format(Loc.T("m1move.pick"), folderName) };
        try { dlg.InitialDirectory = Path.GetDirectoryName(dir); } catch { }
        if (dlg.ShowDialog() != true) return;
        string dest = Path.Combine(dlg.FolderName, folderName);
        if (string.Equals(Path.GetFullPath(dest).TrimEnd('\\', '/'), Path.GetFullPath(dir).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)) return;
        if (Directory.Exists(dest)) { Dialogs.Info(owner, "GenSpeed", string.Format(Loc.T("m1move.exists"), dest)); return; }

        Log(string.Format(Loc.T("m1move.moving"), dir, dest));
        var res = await Task.Run(() => GenSpeed.Core.InstallManager.MoveInstall(dir, dest));
        if (!res.Ok) { Dialogs.Info(owner, "GenSpeed", string.Format(Loc.T("m1move.fail"), res.Error)); Log("⚠ " + res.Error); return; }

        _config.KnownInstalls.RemoveAll(p => string.Equals(p.TrimEnd('\\', '/'), dir.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase));
        if (!_config.KnownInstalls.Any(p => string.Equals(p.TrimEnd('\\', '/'), dest.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)))
            _config.KnownInstalls.Add(dest);
        _config.InstallParent = dlg.FolderName;
        ConfigStore.Save(_config);
        if (isGl) UpdateGenLauncherShortcut(Path.Combine(dest, "GenLauncher.exe"), dest);
        Log(string.Format(Loc.T("m1move.done"), dest));
        LoadMods();
    }

    /// <summary>Met à jour (ou crée) le raccourci Bureau « GenLauncher.lnk » vers un nouvel exe/dossier, marqué
    /// « Exécuter en tant qu'administrateur ». Best-effort (WScript.Shell COM).</summary>
    private void UpdateGenLauncherShortcut(string exePath, string workingDir)
    {
        try
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var t = Type.GetTypeFromProgID("WScript.Shell");
            if (t == null) return;
            dynamic shell = Activator.CreateInstance(t)!;
            string lnkPath = Path.Combine(desktop, "GenLauncher.lnk");
            dynamic lnk = shell.CreateShortcut(lnkPath);
            lnk.TargetPath = exePath;
            lnk.WorkingDirectory = workingDir;
            lnk.IconLocation = exePath + ",0";
            lnk.Description = "GenLauncher";
            lnk.Save();
            try { var b = File.ReadAllBytes(lnkPath); if (b.Length > 0x15) { b[0x15] = (byte)(b[0x15] | 0x20); File.WriteAllBytes(lnkPath, b); } } catch { }
        }
        catch { }
    }
}
