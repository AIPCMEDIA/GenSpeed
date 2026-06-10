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
    // ===== Appliquer / Annuler (élévation UAC) =====
    private async void OnApply(object sender, RoutedEventArgs e) => await RunPatch("apply");
    private async void OnRestore(object sender, RoutedEventArgs e) => await RunPatch("restore");

    private async Task RunPatch(string mode)
    {
        var rows = _rows.Where(r => r.Sel && r.Target != null).ToList();
        if (rows.Count == 0) { Log(Loc.T("log.nosel")); return; }

        // Dépatch : ne garder que les mods réellement patchés (présence d'un .speedbak).
        if (mode == "restore")
        {
            var patchedRows = rows.Where(r => r.Target!.Files.Any(fp => File.Exists(fp + ".speedbak"))).ToList();
            if (patchedRows.Count == 0)
            {
                Dialogs.Info(this, "GenSpeed", Loc.T("restore.nothing"));
                Log(Loc.T("restore.nothing"));
                return;
            }
            rows = patchedRows;   // on ignore les cibles non patchées (pas d'UAC inutile, pas de faux « restauré »)
        }

        if (mode == "apply" &&
            !Dialogs.ConfirmApply(this, rows.Select(r => $"{FriendlyLabel(r.Mod)}  ({InstallLabel(r.InstallDir)})"),
                                  BuildChangeSummary(),
                                  string.Join(" · ", rows.Select(r => r.InstallDir).Distinct(StringComparer.OrdinalIgnoreCase))))
            return;

        // Multi-installs : un sous-job par install, UNE seule élévation UAC pour le tout.
        var job = new PatchJob
        {
            Mode = mode, Factors = ReadFactors(), Cam = ReadCam(),
            ResultPath = Path.Combine(Path.GetTempPath(), $"genspeed_result_{Guid.NewGuid():N}.json"),
        };
        foreach (var g in rows.GroupBy(r => r.InstallDir, StringComparer.OrdinalIgnoreCase))
            job.Installs.Add(new InstallPatch
            {
                GameDir = g.Key,
                ModsDir = (!string.IsNullOrEmpty(_config.ModsDir) &&
                           string.Equals(Path.GetDirectoryName(_config.ModsDir.TrimEnd('\\', '/')), g.Key, StringComparison.OrdinalIgnoreCase))
                          ? _config.ModsDir : null,
                Labels = g.Select(r => r.Mod).ToList(),
                PrevHashes = g.ToDictionary(r => r.Mod, r => r.PatchedFiles),
            });
        string jobPath = Path.Combine(Path.GetTempPath(), $"genspeed_job_{Guid.NewGuid():N}.json");
        File.WriteAllText(jobPath, JsonSerializer.Serialize(job));

        ApplyBtn.IsEnabled = RestoreBtn.IsEnabled = false;
        Log(Loc.T(mode == "apply" ? "log.applying" : "log.restoring"));
        if (mode == "apply")
            Log($"   ⚡ {SpeedLabel.Text}  ·  📷 {CamLabel.Text}  →  {string.Join(", ", rows.Select(r => FriendlyLabel(r.Mod)))}");
        try
        {
            int code = await RunElevated(mode == "apply" ? "--apply" : "--restore", jobPath);
            if (code < 0) { Log(Loc.T("log.uaccancel")); return; }
            PatchResult? res = File.Exists(job.ResultPath)
                ? JsonSerializer.Deserialize<PatchResult>(File.ReadAllText(job.ResultPath)) : null;
            if (res == null) { Log("⚠ " + Loc.T("log.noresult")); return; }
            foreach (var err in res.Errors) Log("⚠ " + err);
            bool camApplied = mode == "apply" &&
                ReadCam().Any(kv => kv.Key != "CameraYaw" && !string.IsNullOrEmpty(kv.Value));
            foreach (var r in rows)
            {
                if (!res.Patched.TryGetValue(r.StateKey, out var pf)) continue;
                r.PatchedFiles = pf;
                if (mode == "apply")
                {
                    r.Patched = $"{pf.Count}/{r.Target!.ArchiveCount}";
                    r.Vitesse = SpeedLabel.Text;
                    r.Camera = camApplied ? (_camIdx > 0 ? CamLabel.Text : Loc.T("cam.custom")) : Loc.T("orig");
                    _config.PatchedState[r.StateKey] = new PatchedInfo { Speed = r.Vitesse, Camera = r.Camera, Files = pf };
                    Log($"   • {FriendlyLabel(r.Mod)} : {pf.Count}/{r.Target!.ArchiveCount} " + Loc.T("log.filespatched"));
                }
                else
                {
                    r.Patched = "—"; r.Vitesse = Loc.T("orig"); r.Camera = Loc.T("orig");
                    _config.PatchedState.Remove(r.StateKey);
                    Log($"   • {FriendlyLabel(r.Mod)} : " + Loc.T("log.restoredmod"));
                }
            }
            ConfigStore.Save(_config);   // persiste l'état patché (vitesse/caméra) pour le prochain démarrage
            Log(Loc.T(mode == "apply" ? "log.applied" : "log.restored"));
            foreach (var r in rows)
                r.Code = await CachedLanCode(r.Target!);   // recalcule + met à jour le cache (fichiers changés par le patch)
            if (_hashCacheDirty) { ConfigStore.Save(_config); _hashCacheDirty = false; }

            if (mode == "apply")
            {
                // Code LAN affiché : celui de l'install du PREMIER mod coché (celle qu'on va lancer).
                string firstDir = rows[0].InstallDir;
                var inDir = rows.Where(r => string.Equals(r.InstallDir, firstDir, StringComparison.OrdinalIgnoreCase)).ToList();
                var lan = await Task.Run(() =>
                {
                    var files = ModDetection.BaseInstallFiles(firstDir).ToList();
                    foreach (var r in inDir) files.AddRange(r.Target!.Files);
                    return Hashing.InstallHash(firstDir, files);
                });
                LanCodeLabel.Text = lan.Hash;
                Dialogs.ApplyResult(this, Loc.T("result.body"), lan.Hash, LaunchGenLauncher);
            }
        }
        finally
        {
            ApplyBtn.IsEnabled = RestoreBtn.IsEnabled = true;
            try { File.Delete(jobPath); File.Delete(job.ResultPath); } catch { }
        }
    }

    private static Task<int> RunElevated(string verbArg, string jobPath) => Task.Run(() =>
    {
        try
        {
            var psi = new ProcessStartInfo { FileName = Environment.ProcessPath!, UseShellExecute = true, Verb = "runas" };
            psi.ArgumentList.Add(verbArg);
            psi.ArgumentList.Add(jobPath);
            var p = Process.Start(psi);
            if (p == null) return -1;
            p.WaitForExit();
            return p.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception) { return -1; }
    });

    /// <summary>Résumé « joueur » de ce que le patch va changer (par catégorie + caméra).</summary>
    private List<string> BuildChangeSummary()
    {
        var lines = new List<string>();
        foreach (var (key, _, _) in Cats)
        {
            if (_catBoxes.TryGetValue(key, out var box) &&
                double.TryParse(box.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var f) &&
                Math.Abs(f - 1) > 0.001)
                lines.Add(string.Format(Loc.T("fx." + key), Fmt(f)));
        }
        var cam = ReadCam();
        if (cam.Any(kv => kv.Key != "CameraYaw" && !string.IsNullOrEmpty(kv.Value)))
            lines.Add(string.Format(Loc.T("fx.camera"), _camIdx > 0 ? CamLabel.Text : Loc.T("cam.custom")));
        if (lines.Count == 0) lines.Add(Loc.T("fx.none"));
        return lines;
    }

    private Dictionary<string, double> ReadFactors()
    {
        var d = new Dictionary<string, double>();
        foreach (var (key, _, _) in Cats)
            if (_catBoxes.TryGetValue(key, out var box) &&
                double.TryParse(box.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                d[key] = v;
        return d;
    }

    private Dictionary<string, string?> ReadCam()
    {
        var d = new Dictionary<string, string?> { ["CameraYaw"] = "" };
        foreach (var (var, _) in CamVars)
            if (_camControls.TryGetValue(var, out var c) && c is TextBox tb) d[var] = tb.Text.Trim();
        if (_camControls.TryGetValue("DrawEntireTerrain", out var cc) && cc is ComboBox cb)
            d["DrawEntireTerrain"] = cb.SelectedItem as string ?? "";
        return d;
    }
}
