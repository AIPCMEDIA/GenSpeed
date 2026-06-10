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

public partial class MainWindow
{
    // ===== Menu Config =====
    private void OnCfgReset()
    {
        SpeedSlider.Value = Math.Min(2, Speeds.Count - 1);
        ApplySpeedPreset(SpeedIdx);
        int camInit = _camNames.IndexOf("Cam haute");
        if (camInit < 0) camInit = _camNames.Count > 1 ? 1 : 0;
        _camIdx = camInit;
        if (CamLabel != null) CamLabel.Text = camInit == 0 ? Loc.T("cam.default") : _camNames[camInit];
        CamSlider.Value = camInit;
        ApplyCamPreset(camInit);
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

    /// <summary>Aide globale de l'application.</summary>
    private void OnHelp(object sender, RoutedEventArgs e) => HelpWindow.Show(this);

    /// <summary>Re-choisir le dossier du jeu à tout moment (corrige un dossier mal sélectionné au 1er lancement).</summary>
    private void OnCfgGameDir()
    {
        Dialogs.Info(this, "GenSpeed", Loc.T("pick.game.help"));
        var dir = AskGameDir();
        if (dir == null) return;
        _config.GameDir = dir;
        ConfigStore.Save(_config);
        Log(string.Format(Loc.T("log.gamedir"), dir));
        LoadMods();   // ré-détecte mods + rafraîchit la grille
    }

    /// <summary>Pointer le dossier des mods (GLM) si GenLauncher est installé hors du dossier du jeu.</summary>
    private void OnCfgModsDir()
    {
        Dialogs.Info(this, "GenSpeed", Loc.T("pick.mods.help"));
        var dlg = new OpenFolderDialog { Title = Loc.T("pick.mods.title") };
        if (dlg.ShowDialog() != true) return;
        var glm = ModDetection.ResolveGlmDir(dlg.FolderName);
        if (glm == null) { Dialogs.Info(this, "GenSpeed", Loc.T("pick.mods.invalid")); return; }
        _config.ModsDir = glm;
        ConfigStore.Save(_config);
        Log(string.Format(Loc.T("cfg.modsdir.set"), glm));
        LoadMods();
    }

    private static string InstallLabel(string dir) => Path.GetFileName(dir.TrimEnd('\\', '/'));

    /// <summary>Type d'install (pour bien différencier) : 🎮 Jeu d'origine / 🧩 GenLauncher / 🔧 Mod autonome (fork).</summary>
    /// <summary>Un fork ships son PROPRE exe de jeu (ex. « Reborn Omega 1.01.exe »), souvent avec dossier renommé.
    /// GenLauncher.exe n'en est pas un : c'est le monde GenLauncher dans le dossier ZH d'origine.</summary>
    private static bool IsForkExe(string file)
        => IsLauncherCandidate(file) && Path.GetFileName(file).ToLowerInvariant() != "genlauncher.exe";

    private static string InstallType(string dir)
    {
        // 1) Fork = exe de jeu propre au mod présent à la racine (signal fiable, indépendant du nom du dossier).
        try
        {
            if (Directory.EnumerateFiles(dir, "*.exe").Any(IsForkExe))
                return Loc.T("inst.type.fork");
        }
        catch { }
        // 2) Monde GenLauncher = dossier GLM contenant au moins un mod (reste dans le dossier ZH d'origine).
        try
        {
            string g = Path.Combine(dir, "GLM");
            if (Directory.Exists(g) && Directory.EnumerateDirectories(g)
                  .Any(d => Path.GetFileName(d) is not ("Addons" or "Patches" or "Tools")))
                return Loc.T("inst.type.genl");
        }
        catch { }
        // 3) Sinon : jeu Zero Hour d'origine, sans mods.
        return Loc.T("inst.type.orig");
    }

    private void EnsureInstallListed(string? dir)
    {
        if (string.IsNullOrEmpty(dir)) return;
        if (!_config.KnownInstalls.Any(p => string.Equals(p, dir, StringComparison.OrdinalIgnoreCase)))
        { _config.KnownInstalls.Add(dir); ConfigStore.Save(_config); }
    }

    private void SwitchInstall(string dir)
    {
        if (string.Equals(dir, _gameDir, StringComparison.OrdinalIgnoreCase)) return;
        _config.GameDir = dir;
        ConfigStore.Save(_config);
        Log(string.Format(Loc.T("inst.switched"), InstallLabel(dir)));
        LoadMods();   // recharge la liste des mods de l'install choisie
    }

    /// <summary>Sélecteur d'installations : jeu de base + mods autonomes (ajout / bascule).</summary>
    private void OnCfgInstalls()
    {
        EnsureInstallListed(_gameDir);
        var installs = _config.KnownInstalls.ToList();
        var options = installs.Select(p =>
            (string.Equals(p, _gameDir, StringComparison.OrdinalIgnoreCase) ? "● " : "    ") + InstallLabel(p)).ToList();
        options.Add(Loc.T("inst.add"));

        string? pick = Dialogs.Choose(this, Loc.T("inst.title"), Loc.T("inst.msg"), options);
        if (pick == null) return;

        if (pick == Loc.T("inst.add"))
        {
            var dir = AskGameDir();           // sélecteur + validation IsZhFolder (existant)
            if (dir == null) return;
            EnsureInstallListed(dir);
            SwitchInstall(dir);
            return;
        }
        int idx = options.IndexOf(pick);
        if (idx >= 0 && idx < installs.Count) SwitchInstall(installs[idx]);
    }
}
