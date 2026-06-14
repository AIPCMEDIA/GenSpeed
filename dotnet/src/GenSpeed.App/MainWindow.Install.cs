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
    private bool _m1Checked;   // la vérif du master M1 ne se fait qu'une fois par session (au démarrage)

    // ===== Assistant d'installation propre (wizard) =====
    /// <summary>Ouvre le wizard d'installation. callbacks : enregistrer une install normale (tableau)
    /// ou définir le master M1 (caché du tableau, mémorisé pour les copies).</summary>
    private void OnInstallWizard()
        => InstallWizardWindow.Show(this, _config, Log,
               dir => { EnsureInstallListed(dir); LoadMods(); },   // install normale → tableau
               SetMaster);                                         // master M1 → variable cachée

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

    /// <summary>⚙ Config → caler Options.ini (anti-mismatch + perf 7290) + GenLauncherCfg.yaml (GenSpeed-safe).
    /// Édition en place, sauvegardes .gsbak. À lancer GenLauncher fermé (il réécrit son YAML à la fermeture).</summary>
    private void OnCfgTuneMultiplayer()
    {
        if (!Dialogs.Confirm(this, Loc.T("tune.title"), Loc.T("tune.confirm"))) return;

        var log = new List<string>();

        // 1) Options.ini (un seul — dossier Documents partagé).
        string? opt = MultiplayerTuning.FindOptionsIni();
        if (opt == null) log.Add(Loc.T("tune.noopt"));
        else
        {
            var r = MultiplayerTuning.ApplyOptions(opt, ScreenInfo.NativeResolution());
            log.Add(r.Ok ? string.Format(Loc.T("tune.opt.ok"), r.Applied) : "⚠ " + r.Error);
        }

        // 2) GenLauncherCfg.yaml par install — cale l'existant, ou le CRÉE (baseline) si GenLauncher.exe est là
        //    mais pas encore lancé (pré-empte l'install auto de GenTool + le setup de 1er lancement).
        //    Pas touché si GenLauncher est ouvert (il l'écraserait à la fermeture).
        bool glRunning = RunningGameProcs().Contains("GenLauncher");
        bool anyYaml = false;
        foreach (var dir in _installs)
        {
            bool hasYaml = MultiplayerTuning.FindGenLauncherYaml(dir) != null;
            bool hasExe = File.Exists(Path.Combine(dir, "GenLauncher.exe"));
            if (!hasYaml && !hasExe) continue;   // pas une install GenLauncher
            anyYaml = true;
            if (glRunning && hasYaml) { log.Add(Loc.T("tune.glrunning")); break; }
            var r = MultiplayerTuning.SeedOrTuneYaml(dir);
            if (!r.Ok) { log.Add("⚠ " + r.Error); continue; }
            log.Add(r.Applied < 0 ? string.Format(Loc.T("gl.seeded"), r.Path)
                                  : string.Format(Loc.T("tune.yaml.ok"), InstallLabel(dir), r.Applied));
        }
        if (!anyYaml) log.Add(Loc.T("tune.noyaml"));

        foreach (var l in log) Log(l);
        Dialogs.Info(this, Loc.T("tune.title"), string.Join("\n", log));
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
            if (!string.IsNullOrEmpty(_config.M1Dir) && string.Equals(folder.TrimEnd('\\', '/'), _config.M1Dir!.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)) continue;
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

        // 1) Options.ini (unique, Documents) — uniquement s'il existe déjà (créé par le jeu/wizard ; on n'invente
        //    pas un fichier de zéro ici, le wizard s'en charge à l'install).
        string? opt = MultiplayerTuning.FindOptionsIni();
        if (opt != null)
        {
            var r = MultiplayerTuning.ApplyOptions(opt, ScreenInfo.NativeResolution());
            if (r.Ok && r.Applied != 0) Log(string.Format(Loc.T("tune.auto.opt"), r.Applied));
            else if (!r.Ok) Log("⚠ " + r.Error);
        }

        // 2) GenLauncherCfg.yaml par install — pas touché si GenLauncher est ouvert (il l'écraserait).
        if (RunningGameProcs().Contains("GenLauncher")) return;
        foreach (var dir in _installs)
        {
            bool hasYaml = MultiplayerTuning.FindGenLauncherYaml(dir) != null;
            bool hasExe = File.Exists(Path.Combine(dir, "GenLauncher.exe"));
            if (!hasYaml && !hasExe) continue;
            var yr = MultiplayerTuning.SeedOrTuneYaml(dir);
            if (yr.Ok && yr.Applied != 0) Log(string.Format(Loc.T("tune.auto.yaml"), InstallLabel(dir)));
        }
    }

    /// <summary>⚙ Config → modifier le lien de téléchargement direct de GenLauncher (utile quand une version
    /// plus récente sort : on remplace l'id du fichier ModDB). La page de listing reste le secours.</summary>
    private void OnCfgGenLauncherUrl()
    {
        string? url = Dialogs.Prompt(this, Loc.T("gllink.title"), Loc.T("gllink.msg"), _config.GenLauncherUrl);
        if (string.IsNullOrWhiteSpace(url)) return;
        _config.GenLauncherUrl = url.Trim();
        ConfigStore.Save(_config);
        Log(string.Format(Loc.T("gllink.set"), _config.GenLauncherUrl));
    }

    /// <summary>Définit le master M1 (copie vierge de sauvegarde) : mémorisé dans la config, RETIRÉ des
    /// installs connues → jamais affiché dans le tableau ni l'assistant, mais GenSpeed garde son chemin.</summary>
    private void SetMaster(string dir)
    {
        _config.M1Dir = dir;
        _config.KnownInstalls.RemoveAll(p => string.Equals(p, dir, StringComparison.OrdinalIgnoreCase));
        ConfigStore.Save(_config);
        Log(string.Format(Loc.T("m1.set"), dir));
        LoadMods();   // si M1 était listé, il disparaît du tableau
    }

    /// <summary>⚙ Config → gérer le master M1. Comportement adapté à l'état :
    /// présent → DÉPLACER le dossier ; absent/non défini → RENSEIGNER l'emplacement ; + oublier.</summary>
    private async void OnCfgMaster()
    {
        bool set = !string.IsNullOrEmpty(_config.M1Dir);
        bool present = set && Directory.Exists(_config.M1Dir!);
        string cur = set ? _config.M1Dir! : Loc.T("m1.none");

        string relocate = present ? Loc.T("m1.move") : Loc.T("m1.repoint");   // déplacer (présent) ou renseigner (absent)
        string clear = Loc.T("m1.clear");
        var opts = new List<string> { relocate };
        if (set) opts.Add(clear);

        string msgKey = present ? "m1.msg" : (set ? "m1.msg.missing" : "m1.msg.none");
        string? pick = Dialogs.Choose(this, Loc.T("m1.title"), string.Format(Loc.T(msgKey), cur), opts);
        if (pick == relocate) { if (present) await MoveMaster(); else RepointMaster(); }
        else if (pick == clear) { _config.M1Dir = null; ConfigStore.Save(_config); Log(Loc.T("m1.cleared")); LoadMods(); }
    }

    /// <summary>Déplace physiquement le dossier master M1 vers un nouvel emplacement (même volume = instantané ;
    /// autre volume = copie+suppression). Le dossier garde le nom standard « Master ZH ».</summary>
    private async System.Threading.Tasks.Task MoveMaster()
    {
        var dlg = new OpenFolderDialog { Title = Loc.T("m1.move.pick") };
        if (dlg.ShowDialog() != true) return;
        string dest = System.IO.Path.Combine(dlg.FolderName, GenSpeed.Core.InstallManager.MasterFolderName);
        Log(string.Format(Loc.T("m1.moving"), _config.M1Dir, dest));
        var res = await System.Threading.Tasks.Task.Run(() => GenSpeed.Core.InstallManager.MoveInstall(_config.M1Dir!, dest));
        if (!res.Ok) { Dialogs.Info(this, Loc.T("m1.title"), string.Format(Loc.T("m1.move.fail"), res.Error)); Log("⚠ " + res.Error); return; }
        _config.M1Dir = dest; ConfigStore.Save(_config);
        Log(string.Format(Loc.T("m1.moved"), dest));
    }

    /// <summary>Renseigne l'emplacement du master M1 (cas où l'utilisateur l'a déplacé hors de GenSpeed).</summary>
    private void RepointMaster()
    {
        Dialogs.Info(this, Loc.T("m1.title"), Loc.T("m1.repoint.help"));
        var dlg = new OpenFolderDialog { Title = Loc.T("m1.title") };
        if (dlg.ShowDialog() != true) return;
        if (!GameLocator.IsZhFolder(dlg.FolderName)) { Dialogs.Info(this, "GenSpeed", Loc.T("wiz.s1.invalid")); return; }
        SetMaster(dlg.FolderName);
    }

    /// <summary>Au démarrage : si un master M1 est défini mais introuvable (déplacé/supprimé hors GenSpeed),
    /// alerter et proposer de renseigner son emplacement. Si aucun M1 mais une install initialisée existe,
    /// suggérer d'en créer un (astuce non bloquante dans le journal).</summary>
    private void CheckMasterM1()
    {
        if (!string.IsNullOrEmpty(_config.M1Dir))
        {
            if (!Directory.Exists(_config.M1Dir))
            {
                Log("⚠ " + string.Format(Loc.T("m1.missing.log"), _config.M1Dir));
                if (Dialogs.Confirm(this, Loc.T("m1.title"), string.Format(Loc.T("m1.missing.msg"), _config.M1Dir)))
                    RepointMaster();
            }
            return;
        }
        if (_installs.Any(d => !GenSpeed.Core.InstallManager.NeedsInit(d)))
            Log("ℹ " + Loc.T("m1.suggest.log"));
    }
}
