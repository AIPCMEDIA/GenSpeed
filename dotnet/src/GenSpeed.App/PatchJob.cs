using System.IO;
using System.Text.Json;
using GenSpeed.Core;

namespace GenSpeed.App;

/// <summary>Paramètres d'une opération patch/restore transmis au process élevé (JSON).</summary>
public sealed class PatchJob
{
    public string Mode { get; set; } = "apply";          // "apply" | "restore"
    public string GameDir { get; set; } = "";
    public string? ModsDir { get; set; }                 // dossier GLM externe (GenLauncher ailleurs)
    public Dictionary<string, double> Factors { get; set; } = new();
    public Dictionary<string, string?> Cam { get; set; } = new();
    public List<string> Labels { get; set; } = new();
    // label -> (chemin -> sha du dernier patch) : pour détecter les MAJ externes.
    public Dictionary<string, Dictionary<string, string>> PrevHashes { get; set; } = new();
    public string ResultPath { get; set; } = "";
}

/// <summary>Résultat renvoyé par le process élevé.</summary>
public sealed class PatchResult
{
    // label -> (chemin -> sha) des fichiers réellement patchés (vide = restauré).
    public Dictionary<string, Dictionary<string, string>> Patched { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}

/// <summary>Exécuté DANS le process élevé (admin) : effectue les écritures dans le dossier du jeu.</summary>
public static class ElevatedRunner
{
    public static int Run(string mode, string jobPath)
    {
        var result = new PatchResult();
        try
        {
            var job = JsonSerializer.Deserialize<PatchJob>(File.ReadAllText(jobPath))!;
            var byLabel = ModDetection.DetectTargets(job.GameDir).ToDictionary(t => t.Label);

            // Mods GenLauncher installés ailleurs : mêmes cibles que la fenêtre principale.
            if (!string.IsNullOrEmpty(job.ModsDir) && Directory.Exists(job.ModsDir)
                && !string.Equals(job.ModsDir, Path.Combine(job.GameDir, "GLM"), StringComparison.OrdinalIgnoreCase))
                foreach (var t in ModDetection.DetectGlmMods(job.ModsDir))
                    byLabel.TryAdd(t.Label, t);

            foreach (var label in job.Labels)
            {
                if (!byLabel.TryGetValue(label, out var t)) continue;
                var prev = job.PrevHashes.TryGetValue(label, out var p) ? p
                           : new Dictionary<string, string>();
                try
                {
                    if (mode == "apply")
                    {
                        var outcome = Patcher.PatchTarget(t, job.Factors, job.Cam, prev);
                        result.Patched[label] = outcome.PatchedFiles;
                    }
                    else // restore
                    {
                        var (toRestore, stale) = Patcher.ClassifyRestore(t, prev);
                        Patcher.RestoreFiles(toRestore, stale);
                        result.Patched[label] = new Dictionary<string, string>();
                    }
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"{label}: {ex.Message}");
                }
            }
            File.WriteAllText(job.ResultPath, JsonSerializer.Serialize(result));
            return result.Errors.Count == 0 ? 0 : 1;
        }
        catch (Exception ex)
        {
            try
            {
                var job = JsonSerializer.Deserialize<PatchJob>(File.ReadAllText(jobPath));
                if (job != null)
                {
                    result.Errors.Add(ex.Message);
                    File.WriteAllText(job.ResultPath, JsonSerializer.Serialize(result));
                }
            }
            catch { }
            return 2;
        }
    }
}

/// <summary>Exécuté DANS le process élevé (admin) : sauvegarde + suppression du désinstalleur propre.</summary>
public static class CleanupRunner
{
    public static int Run(string jobPath)
    {
        CleanupJob? job = null;
        try
        {
            job = JsonSerializer.Deserialize<CleanupJob>(File.ReadAllText(jobPath))!;
            var result = Cleanup.Execute(job);
            File.WriteAllText(job.ResultPath, JsonSerializer.Serialize(result));
            return result.Errors.Count == 0 ? 0 : 1;
        }
        catch (Exception ex)
        {
            try
            {
                if (job != null)
                {
                    var result = new CleanupResult { BackupDir = job.BackupDir, Errors = { ex.Message } };
                    File.WriteAllText(job.ResultPath, JsonSerializer.Serialize(result));
                }
            }
            catch { }
            return 2;
        }
    }
}
