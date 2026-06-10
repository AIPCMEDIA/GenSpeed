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
    // ===== Lancer GenLauncher =====
    private void OnLaunchGenLauncher(object sender, RoutedEventArgs e) => LaunchGenLauncher();

    // Exe qui NE sont pas des lanceurs utilisateur (moteur/outils).
    private static bool IsLauncherCandidate(string file)
    {
        string n = Path.GetFileName(file).ToLowerInvariant();
        if (n is "generals.exe" or "generalszh.exe" or "modded.exe" or "edgescroller.exe" or "genpatcher.exe") return false;
        if (n.Contains("worldbuilder") || n.StartsWith("unins") || n.StartsWith("gentool")
            || n.Contains("vcredist") || n.Contains("dxsetup") || n.Contains("redist")) return false;
        return true;
    }

    private static List<string> LaunchCandidates(string dir)
    {
        try { return Directory.EnumerateFiles(dir, "*.exe").Where(IsLauncherCandidate).OrderBy(f => f, StringComparer.Ordinal).ToList(); }
        catch { return new List<string>(); }
    }

    /// <summary>Install à lancer : celle du PREMIER mod coché, sinon la première découverte.</summary>
    private string? LaunchDir()
    {
        var first = CheckedTargets().FirstOrDefault();
        if (first != null && !string.IsNullOrEmpty(first.InstallDir)) return first.InstallDir;
        return _installs.FirstOrDefault();
    }

    /// <summary>Lance l'install du mod coché : exe mémorisé &gt; candidat unique &gt; sinon demande (et mémorise).</summary>
    private void LaunchGenLauncher()
    {
        string? dir = LaunchDir();
        if (dir == null) { Log(Loc.T("log.nogame")); return; }
        string key = LaunchKey(dir);       // dossier + mod coché : le lanceur est mémorisé PAR mod
        string? exe = null;
        if (_config.LaunchExes.TryGetValue(key, out var saved))
        {
            string sp = Path.Combine(dir, saved);
            if (File.Exists(sp)) exe = sp;
        }
        if (exe == null)
        {
            var cands = LaunchCandidates(dir);
            if (cands.Count == 0) { Dialogs.Info(this, "GenSpeed", Loc.T("genl.notfound")); Log(Loc.T("genl.notfound")); return; }
            if (cands.Count == 1) exe = cands[0];
            else
            {
                string? pick = Dialogs.Choose(this, Loc.T("launch.pick.title"), Loc.T("launch.pick.msg"),
                                              cands.Select(c => Path.GetFileName(c)!).ToList());
                if (pick == null) return;
                exe = Path.Combine(dir, pick);
            }
        }
        _config.LaunchExes[key] = Path.GetFileName(exe);
        ConfigStore.Save(_config);
        try
        {
            Process.Start(new ProcessStartInfo { FileName = exe, WorkingDirectory = dir, UseShellExecute = true, Verb = "runas" });
            Log(string.Format(Loc.T("launch.started"), Path.GetFileName(exe)));
        }
        catch (System.ComponentModel.Win32Exception) { Log(Loc.T("genl.cancel")); }
    }

    /// <summary>Clé du lanceur mémorisé : dossier d'install + mod coché (le 1er). Le bon lanceur dépend du mod joué.</summary>
    private string LaunchKey(string dir)
    {
        var first = CheckedTargets().FirstOrDefault();
        return dir + "::" + (first?.Label ?? "");
    }

    /// <summary>Changer le lanceur mémorisé pour le mod actuellement coché (re-demande + ré-enregistre).</summary>
    private void OnCfgLauncher()
    {
        string? dir = LaunchDir();
        if (dir == null) { Log(Loc.T("log.nogame")); return; }
        var cands = LaunchCandidates(dir);
        if (cands.Count == 0) { Dialogs.Info(this, "GenSpeed", Loc.T("genl.notfound")); return; }
        var first = CheckedTargets().FirstOrDefault();
        string forMod = first != null ? FriendlyLabel(first.Label) : Loc.T("launch.nomod");
        string? pick = Dialogs.Choose(this, Loc.T("launch.pick.title"),
                                      string.Format(Loc.T("launch.change.msg"), forMod),
                                      cands.Select(c => Path.GetFileName(c)!).ToList());
        if (pick == null) return;
        _config.LaunchExes[LaunchKey(dir)] = pick;
        ConfigStore.Save(_config);
        Log(string.Format(Loc.T("launch.set"), pick, forMod));
    }
}
