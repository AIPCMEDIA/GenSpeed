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
