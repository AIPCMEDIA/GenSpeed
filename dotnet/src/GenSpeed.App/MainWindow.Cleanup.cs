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
    // ===== Désinstalleur propre =====
    /// <summary>Process du jeu/outils potentiellement en cours (verrous fichiers + symlinks GenLauncher actifs).</summary>
    private static List<string> RunningGameProcs()
    {
        var found = new List<string>();
        foreach (var n in new[] { "generals", "generalszh", "modded", "GenLauncher", "WorldBuilder", "GenTool", "GenPatcher" })
            try { if (Process.GetProcessesByName(n).Length > 0) found.Add(n); } catch { }
        return found;
    }

    private static string FmtBytes(long b)
    {
        if (b <= 0) return "0";
        string[] u = { "o", "Ko", "Mo", "Go" };
        double v = b; int i = 0;
        while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
        return $"{v:0.#} {u[i]}";
    }

    private async void OnCfgUninstall()
    {
        if (_gameDir == null) { Dialogs.Info(this, "GenSpeed", Loc.T("log.nogame")); return; }
        string gameDir = _gameDir;

        Log(Loc.T("clean.scanning"));
        List<CleanupItem> items;
        try { items = await Task.Run(() => Cleanup.Scan(gameDir)); }
        catch (Exception ex) { Log("⚠ " + ex.Message); return; }
        if (!IsLoaded) return;   // fenêtre principale fermée pendant l'analyse : abandonner proprement
        if (items.Count == 0) { Dialogs.Info(this, Loc.T("clean.title"), Loc.T("clean.nothing")); return; }

        // Dossier dédié (pas le Bureau) : toutes les sauvegardes GenSpeed regroupées au même endroit.
        string backupDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "GenSpeed", "Backups", $"Cleanup-{DateTime.Now:yyyyMMdd-HHmmss}");

        var (action, result) = CleanupWindow.Show(this, items, backupDir, gameDir);
        if (action == CleanupAction.Cancel) return;

        var chosen = result.Where(i => i.Selected && i.Removable && i.ChosenMethod != CleanupMethod.Laisser).ToList();
        if (chosen.Count == 0) { Log(Loc.T("clean.none.sel")); return; }

        if (action == CleanupAction.Simulate)
        {
            Log(Loc.T("clean.sim.head"));
            // Même ordre par étapes que la fenêtre ET que la suppression réelle (CategoryRank).
            int simStep = 0;
            foreach (var g in chosen.GroupBy(i => i.Category).OrderBy(x => Cleanup.CategoryRank(x.Key)))
            {
                simStep++;
                Log("  " + string.Format(Loc.T("clean.step"), simStep, Loc.T($"clean.cat.{g.Key}")));
                foreach (var it in g)
                    Log($"     • [{Loc.T($"clean.method.{it.ChosenMethod}")}] {it.Display}");
            }
            Log(string.Format(Loc.T("clean.sim.foot"), backupDir));
            return;
        }

        // Étape 0 : vérifier qu'aucun process du jeu/outil ne tourne (verrous + symlinks GenLauncher actifs).
        var running = RunningGameProcs();
        if (running.Count > 0 &&
            !Dialogs.Confirm(this, Loc.T("clean.title"), string.Format(Loc.T("clean.proc.warn"), string.Join(", ", running))))
            return;

        // Exécution réelle : confirmation forte.
        if (!Dialogs.Confirm(this, Loc.T("clean.title"), string.Format(Loc.T("clean.confirm"), chosen.Count, backupDir)))
            return;

        // Réinitialisation GenSpeed : si on supprime sa config, empêcher qu'il la réécrive à la
        // fermeture (sinon le reset serait annulé — « ne pas scier la branche »). GenSpeed reste installé.
        string gsCfgPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GenSpeed");
        if (chosen.Any(i => string.Equals(i.Path.TrimEnd('\\'), gsCfgPath, StringComparison.OrdinalIgnoreCase)))
            ConfigStore.Suppressed = true;

        var job = new CleanupJob
        {
            BackupDir = backupDir, Items = chosen,
            ResultPath = Path.Combine(Path.GetTempPath(), $"genspeed_cleanup_result_{Guid.NewGuid():N}.json"),
        };
        string jobPath = Path.Combine(Path.GetTempPath(), $"genspeed_cleanup_job_{Guid.NewGuid():N}.json");
        File.WriteAllText(jobPath, JsonSerializer.Serialize(job));

        Log(Loc.T("clean.running"));
        try
        {
            int code = await RunElevated("--cleanup", jobPath);
            if (code < 0) { Log(Loc.T("log.uaccancel")); ConfigStore.Suppressed = false; return; }
            CleanupResult? res = File.Exists(job.ResultPath)
                ? JsonSerializer.Deserialize<CleanupResult>(File.ReadAllText(job.ResultPath)) : null;
            if (res == null) { Log("⚠ " + Loc.T("log.noresult")); return; }
            foreach (var d in res.Done) Log("   " + d);
            foreach (var er in res.Errors) Log("⚠ " + er);
            if (!IsLoaded) return;   // fenêtre fermée pendant le nettoyage : le travail est fait, pas de dialogues
            Dialogs.Info(this, Loc.T("clean.title"),
                string.Format(Loc.T("clean.report"), res.Done.Count, res.Errors.Count, FmtBytes(res.FreedBytes), res.BackupDir));

            // Désinstallation profonde (clés EA / dossier jeu) : proposer la désinstall Steam propre.
            var steam = result.FirstOrDefault(i => i.Category == CleanupCategory.Steam && !string.IsNullOrEmpty(i.Extra));
            bool deep = chosen.Any(i => i.Category == CleanupCategory.Registre || i.Category == CleanupCategory.Jeu);
            if (steam != null && deep && Dialogs.Confirm(this, Loc.T("clean.title"), string.Format(Loc.T("clean.steam.ask"), steam.Extra)))
                try { Process.Start(new ProcessStartInfo { FileName = $"steam://uninstall/{steam.Extra}", UseShellExecute = true }); } catch { }

            // GenTool d3d8.dll retiré/désactivé → vérifier que le DirectX 8 SYSTÈME (Windows) est sain.
            // Lecture seule ; si anomalie, proposer la réparation officielle de Windows (sfc /scannow).
            if (chosen.Any(i => i.Category == CleanupCategory.GenTool &&
                                i.Path.EndsWith("d3d8.dll", StringComparison.OrdinalIgnoreCase)))
            {
                Log(Loc.T("clean.dx.checking"));
                var (ok, detail) = await Task.Run(Cleanup.VerifySystemDirectX8);
                if (ok)
                {
                    Log("✅ " + string.Format(Loc.T("clean.dx.ok"), detail));
                }
                else
                {
                    Log("⚠ " + string.Format(Loc.T("clean.dx.bad"), detail));
                    if (IsLoaded && Dialogs.Confirm(this, Loc.T("clean.title"), Loc.T("clean.dx.sfc.ask")))
                        try
                        {
                            Process.Start(new ProcessStartInfo
                            { FileName = "cmd.exe", Arguments = "/k sfc /scannow", Verb = "runas", UseShellExecute = true });
                            Log(Loc.T("clean.dx.sfc.started"));
                        }
                        catch (System.ComponentModel.Win32Exception) { Log(Loc.T("log.uaccancel")); }
                }
            }
        }
        finally
        {
            try { File.Delete(jobPath); File.Delete(job.ResultPath); } catch { }
        }
    }
}
