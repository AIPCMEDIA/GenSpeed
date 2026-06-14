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

    /// <summary>Ajouter une installation hors Steam / hors registre (copie manuelle, fork…).
    /// Les installs Steam et registre EA sont découvertes AUTOMATIQUEMENT — pas besoin de les ajouter.</summary>
    private void OnCfgAddInstall()
    {
        Dialogs.Info(this, "GenSpeed", Loc.T("inst.add.help"));
        var dir = AskGameDir();
        if (dir == null) return;
        EnsureInstallListed(dir);
        Log(string.Format(Loc.T("inst.added"), InstallLabel(dir)));
        LoadMods();   // re-découvre tout + rafraîchit la grille groupée
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

    /// <summary>Étiquette M0/M1/Mx par chemin d'install : M0 = vierge (sans GenLauncher), M1 = GenLauncher,
    /// M2, M3… = forks (le reste), dans l'ordre. Sert au panneau « Mes installs ».</summary>
    private Dictionary<string, string> MLabels(List<string> installs)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        bool HasGl(string d) { try { return File.Exists(Path.Combine(d, "GenLauncher.exe")); } catch { return false; } }
        foreach (var d in installs) if (InstallManager.IsVanilla(d) && !HasGl(d)) dict[d] = "M0";
        foreach (var d in installs) if (HasGl(d)) dict[d] = "M1";
        int n = 2;
        foreach (var d in installs) if (!dict.ContainsKey(d)) dict[d] = "M" + n++;
        return dict;
    }

    /// <summary>⚙ Config → « Mes installs » : panneau listant M0/M1/Mx + leurs emplacements (le JSON éditable),
    /// avec corriger / retirer / ajouter. Rafraîchit le tableau à chaque changement.</summary>
    private void OnCfgInstalls()
        => InstallsWindow.Show(this, _config, MLabels, LoadMods, MoveInstallInteractive);

}
