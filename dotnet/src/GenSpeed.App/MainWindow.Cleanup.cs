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

    /// <summary>Lance la désinstallation OFFICIELLE Steam (steam://uninstall) pour chaque jeu coché —
    /// avec confirmation par jeu. La fenêtre de confirmation Steam prend ensuite le relais.</summary>
    private void LaunchSteamUninstalls(List<CleanupItem> steamChosen)
    {
        foreach (var s in steamChosen)
            if (Dialogs.Confirm(this, Loc.T("clean.title"), string.Format(Loc.T("clean.steam.ask"), s.Extra)))
                try
                {
                    Process.Start(new ProcessStartInfo { FileName = $"steam://uninstall/{s.Extra}", UseShellExecute = true });
                    Log(string.Format(Loc.T("clean.steam.started"), s.Extra));
                }
                catch { }
        // Steam ne supprime que SES fichiers : les ajouts tiers (addons GenPatcher, DXVK, .bak…)
        // peuvent rester dans le dossier. Au prochain scan, ce dossier (devenu non-Steam) sera
        // proposé en suppression normale — on prévient l'utilisateur.
        if (steamChosen.Count > 0) Log(Loc.T("clean.steam.residue"));
    }

    private async void OnCfgUninstall()
    {
        // MACHINE ENTIÈRE : toutes les installs découvertes + traces globales (plus d'« install active »).
        var installs = _installs.ToList();
        if (installs.Count == 0) { Dialogs.Info(this, "GenSpeed", Loc.T("log.nogame")); return; }

        Log(Loc.T("clean.scanning"));
        List<CleanupItem> items;
        try { items = await Task.Run(() => Cleanup.Scan(installs)); }
        catch (Exception ex) { Log("⚠ " + ex.Message); return; }
        if (!IsLoaded) return;   // fenêtre principale fermée pendant l'analyse : abandonner proprement
        if (items.Count == 0) { Dialogs.Info(this, Loc.T("clean.title"), Loc.T("clean.nothing")); return; }

        // Dossier dédié (pas le Bureau) : toutes les sauvegardes GenSpeed regroupées au même endroit.
        string backupDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "GenSpeed", "Backups", $"Cleanup-{DateTime.Now:yyyyMMdd-HHmmss}");

        var headers = installs.ToDictionary(d => d, d => $"🖥 {InstallLabel(d)}   ·   {InstallType(d)}",
                                            StringComparer.OrdinalIgnoreCase);
        var (action, result) = CleanupWindow.Show(this, items, backupDir, headers);
        if (action == CleanupAction.Cancel) return;

        var chosen = result.Where(i => i.Selected && i.Removable && i.ChosenMethod != CleanupMethod.Laisser).ToList();
        if (chosen.Count == 0) { Log(Loc.T("clean.none.sel")); return; }
        // Les jeux Steam cochés ne passent PAS par le job élevé : on lance la désinstallation
        // OFFICIELLE via Steam à la fin (GenSpeed ne supprime jamais ces fichiers à la main).
        var steamChosen = chosen.Where(i => i.Category == CleanupCategory.Steam && !string.IsNullOrEmpty(i.Extra)).ToList();
        var jobChosen = chosen.Where(i => i.Category != CleanupCategory.Steam).ToList();

        // Description groupée (même ordre que la fenêtre ET que la suppression réelle) :
        // sections par install puis traces globales ; à l'intérieur, l'ordre des étapes.
        IEnumerable<string> DescribeChosen()
        {
            var dirs = chosen.Where(i => i.InstallDir != null).Select(i => i.InstallDir!)
                             .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            int step = 0;
            foreach (var dirOrNull in dirs.Cast<string?>().Append(null))
            {
                var sec = chosen.Where(i => string.Equals(i.InstallDir, dirOrNull, StringComparison.OrdinalIgnoreCase)
                                            || (i.InstallDir == null && dirOrNull == null)).ToList();
                if (sec.Count == 0) continue;
                yield return dirOrNull == null ? Loc.T("clean.sec.global")
                    : (headers.TryGetValue(dirOrNull, out var h) ? h : dirOrNull);
                foreach (var g in sec.GroupBy(i => i.Category).OrderBy(x => Cleanup.CategoryRank(x.Key)))
                {
                    step++;
                    yield return "  " + string.Format(Loc.T("clean.step"), step, Loc.T($"clean.cat.{g.Key}"));
                    foreach (var it in g)
                        yield return $"     • [{Loc.T($"clean.method.{it.ChosenMethod}")}] {it.Display}";
                }
            }
        }

        if (action == CleanupAction.Simulate)
        {
            Log(Loc.T("clean.sim.head"));
            foreach (var line in DescribeChosen()) Log(line);
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

        if (jobChosen.Count == 0)
        {
            // Seuls des jeux Steam sont cochés : pas d'élévation nécessaire, désinstallation Steam directe.
            LaunchSteamUninstalls(steamChosen);
            _config.KnownInstalls.RemoveAll(p => !Directory.Exists(p));
            LoadMods();
            return;
        }

        var job = new CleanupJob
        {
            BackupDir = backupDir, Items = jobChosen,
            ResultPath = Path.Combine(Path.GetTempPath(), $"genspeed_cleanup_result_{Guid.NewGuid():N}.json"),
        };
        string jobPath = Path.Combine(Path.GetTempPath(), $"genspeed_cleanup_job_{Guid.NewGuid():N}.json");
        File.WriteAllText(jobPath, JsonSerializer.Serialize(job));

        Log(Loc.T("clean.running"));
        try
        {
            // Tout le job dans le périmètre utilisateur (HKCU / VirtualStore) → exécution directe,
            // sans invite UAC. Sinon, process élevé comme avant.
            int code = Cleanup.NeedsElevation(jobChosen)
                ? await RunElevated("--cleanup", jobPath)
                : await Task.Run(() => CleanupRunner.Run(jobPath));
            if (code < 0) { Log(Loc.T("log.uaccancel")); ConfigStore.Suppressed = false; return; }
            CleanupResult? res = File.Exists(job.ResultPath)
                ? JsonSerializer.Deserialize<CleanupResult>(File.ReadAllText(job.ResultPath)) : null;
            if (res == null) { Log("⚠ " + Loc.T("log.noresult")); return; }
            foreach (var d in res.Done) Log("   " + d);
            foreach (var er in res.Errors) Log("⚠ " + er);

            // Journal écrit DANS le dossier de sauvegarde (utile pour relire/diagnostiquer après coup).
            var jl = new System.Text.StringBuilder();
            jl.AppendLine("=== GenSpeed — journal de nettoyage / cleanup log ===");
            jl.AppendLine($"Date     : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            jl.AppendLine($"Installs : {string.Join("  |  ", installs)}");
            jl.AppendLine();
            jl.AppendLine($"--- Éléments choisis / selected items ({chosen.Count}) ---");
            foreach (var line in DescribeChosen()) jl.AppendLine(line);
            jl.AppendLine();
            jl.AppendLine($"--- Effectué / done ({res.Done.Count}) ---");
            foreach (var d in res.Done) jl.AppendLine(d);
            jl.AppendLine();
            jl.AppendLine($"--- Erreurs / errors ({res.Errors.Count}) ---");
            foreach (var er in res.Errors) jl.AppendLine(er);
            jl.AppendLine();
            jl.AppendLine($"Espace libéré / freed : {FmtBytes(res.FreedBytes)}");
            jl.AppendLine($"Sauvegarde / backup   : {res.BackupDir}");
            void SaveJournal()
            {
                try
                {
                    Directory.CreateDirectory(backupDir);
                    File.WriteAllText(Path.Combine(backupDir, "cleanup.log"), jl.ToString(), System.Text.Encoding.UTF8);
                    Log(string.Format(Loc.T("clean.log.saved"), Path.Combine(backupDir, "cleanup.log")));
                }
                catch { }
            }

            if (!IsLoaded) { SaveJournal(); return; }   // fenêtre fermée pendant le nettoyage : journal quand même
            Dialogs.Info(this, Loc.T("clean.title"),
                string.Format(Loc.T("clean.report"), res.Done.Count, res.Errors.Count, FmtBytes(res.FreedBytes), res.BackupDir));

            // Jeux Steam COCHÉS : lancer la désinstallation officielle via Steam (jamais de suppression manuelle).
            LaunchSteamUninstalls(steamChosen);

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
                    jl.AppendLine($"DirectX 8 système : OK — {detail}");
                }
                else
                {
                    Log("⚠ " + string.Format(Loc.T("clean.dx.bad"), detail));
                    jl.AppendLine($"DirectX 8 système : ANORMAL — {detail}");
                    if (IsLoaded && Dialogs.Confirm(this, Loc.T("clean.title"), Loc.T("clean.dx.sfc.ask")))
                        try
                        {
                            Process.Start(new ProcessStartInfo
                            { FileName = "cmd.exe", Arguments = "/k sfc /scannow", Verb = "runas", UseShellExecute = true });
                            Log(Loc.T("clean.dx.sfc.started"));
                            jl.AppendLine("Réparation lancée : sfc /scannow");
                        }
                        catch (System.ComponentModel.Win32Exception) { Log(Loc.T("log.uaccancel")); }
                }
            }

            SaveJournal();

            // Rafraîchir l'application : l'install active ou ses mods ont pu disparaître.
            // LoadMods re-détecte (et bascule automatiquement sur une install existante si besoin).
            _config.KnownInstalls.RemoveAll(p => !Directory.Exists(p));
            LoadMods();
        }
        finally
        {
            try { File.Delete(jobPath); File.Delete(job.ResultPath); } catch { }
        }
    }
}
