using System.Text;
using System.Text.RegularExpressions;

namespace GenSpeed.Core;

/// <summary>Résultat d'un calage (Options.ini ou GenLauncherCfg.yaml).</summary>
public sealed record TuningResult(bool Ok, int Applied, string? Path, string? Error);

/// <summary>Calage « multi / GenSpeed » : applique la base anti-mismatch + perf (7290-optimal) dans
/// Options.ini, et les réglages GenSpeed-safe dans GenLauncherCfg.yaml. ÉDITION EN PLACE des seules clés
/// visées (le reste des fichiers est préservé), avec sauvegarde .gsbak. Voir [[genlauncher-sources]].</summary>
public static class MultiplayerTuning
{
    /// <summary>Options.ini : anti-mismatch (🔴, doivent être identiques entre joueurs) + perf (libre).
    /// Resolution et UseAlternateMouse VOLONTAIREMENT exclus (native par machine / préférence).</summary>
    public static readonly (string Key, string Value)[] OptionsBaseline =
    {
        ("IdealStaticGameLOD", "High"),
        ("StaticGameLOD",      "High"),
        ("TextureReduction",   "2"),
        ("MaxParticleCount",   "1000"),   // 🔴
        ("DynamicLOD",         "no"),     // 🔴
        ("HeatEffects",        "no"),     // 🔴
        ("ExtraAnimations",    "no"),     // 🔴
        ("ShowSoftWaterEdge",  "no"),     // 🔴
        ("ShowTrees",          "no"),     // 🔴
        ("SendDelay",          "no"),     // 🔴
        ("UseShadowVolumes",   "no"),
        ("UseShadowDecals",    "no"),
        ("UseCloudMap",        "no"),
        ("UseLightMap",        "no"),
        ("BuildingOcclusion",  "no"),
    };

    /// <summary>GenLauncherCfg.yaml : réglages CRITIQUES pour GenSpeed / anti-mismatch (clés root-level).
    /// On ne touche PAS aux préférences (Windowed, QuickStart, AutoUpdateGentool, HideLauncher…).</summary>
    public static readonly (string Key, string Value)[] YamlSafe =
    {
        ("CheckModFiles",         "false"),  // sinon GenLauncher restaure les fichiers → annule les patches GenSpeed
        ("AskBeforeCheck",        "false"),  // cohérence (plus de vérif → rien à demander)
        ("CameraHeight",          "0"),      // GenSpeed seul maître de la caméra
        ("ModdedExe",             "true"),   // exe xezon : caméra/zoom débloqués en LAN
        ("UseVulkan",             "false"),  // renderer Vulkan signalé affectant le pathfinding → desync
        ("AutoDeleteOldVersions", "false"),  // garder la flexibilité de version (anti-mismatch)
        ("AutoUpdateGentool",     "false"),  // pas de GenTool (choix utilisateur ; n'est pas requis)
        ("FirstStart",            "false"),  // setup déjà fait → pas de ré-exécution du 1er lancement GenLauncher
        // NON touchés (préférences / machine-spécifique) : Windowed (7290=false, workstation=true), QuickStart,
        // HideLauncherAfterGameStart, GameParams, LaunchesCount.
    };

    /// <summary>Premier Options.ini trouvé (dossier de données du jeu : Documents ou OneDrive\Documents).</summary>
    public static string? FindOptionsIni()
    {
        string up = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var d in new[]
        {
            Path.Combine(up, "Documents", "Command and Conquer Generals Zero Hour Data"),
            Path.Combine(up, "OneDrive", "Documents", "Command and Conquer Generals Zero Hour Data"),
        })
        {
            string opt = Path.Combine(d, "Options.ini");
            if (File.Exists(opt)) return opt;
        }
        return null;
    }

    /// <summary>YAML baseline « sans mod » écrit par GenSpeed AVANT le 1er lancement de GenLauncher, pour
    /// que GenLauncher démarre déjà calé : GenTool OFF (AutoUpdateGentool=false → pas d'install GenTool
    /// automatique) et FirstStart=false (pas de setup/proposition de 1er lancement). Catalogue vide →
    /// GenLauncher le re-remplit depuis le manifeste au 1er run. Windowed=true = défaut sûr (préférence/machine).</summary>
    public const string BaselineYaml =
        "FirstStart: false\r\n" +
        "UseVulkan: false\r\n" +
        "Modifications: []\r\n" +
        "Addons: []\r\n" +
        "Patches: []\r\n" +
        "ModdedExe: true\r\n" +
        "Windowed: true\r\n" +
        "QuickStart: true\r\n" +
        "CameraHeight: 0\r\n" +
        "LaunchesCount: 0\r\n" +
        "AutoUpdateGentool: false\r\n" +
        "AutoDeleteOldVersions: false\r\n" +
        "GameParams: ''\r\n" +
        "CheckModFiles: false\r\n" +
        "AskBeforeCheck: false\r\n" +
        "HideLauncherAfterGameStart: false\r\n";

    /// <summary>Pré-configure GenLauncher : si le YAML existe → le cale (ApplyYaml) ; s'il n'existe pas ENCORE
    /// (GenLauncher pas lancé) mais que GenLauncher.exe est là → crée `.GenLauncherFolder\GenLauncherCfg.yaml`
    /// avec la baseline (Applied=-1 = créé). Empêche l'install auto de GenTool + le setup de 1er lancement.</summary>
    public static TuningResult SeedOrTuneYaml(string installDir)
    {
        string yamlPath = Path.Combine(installDir, ".GenLauncherFolder", "GenLauncherCfg.yaml");
        if (File.Exists(yamlPath)) return ApplyYaml(yamlPath);
        if (!File.Exists(Path.Combine(installDir, "GenLauncher.exe")))
            return new(false, 0, yamlPath, "Pas d'install GenLauncher ici");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(yamlPath)!);
            File.WriteAllText(yamlPath, BaselineYaml, new UTF8Encoding(false));
            return new(true, -1, yamlPath, null);   // -1 = créé (seedé)
        }
        catch (Exception ex) { return new(false, 0, yamlPath, ex.Message); }
    }

    /// <summary>GenLauncherCfg.yaml d'une install (dans .GenLauncherFolder), ou null.</summary>
    public static string? FindGenLauncherYaml(string installDir)
    {
        try
        {
            string p = Path.Combine(installDir, ".GenLauncherFolder", "GenLauncherCfg.yaml");
            return File.Exists(p) ? p : null;
        }
        catch { return null; }
    }

    /// <summary>Applique la base Options.ini en place (remplace les lignes existantes, ajoute les manquantes ;
    /// le reste — audio, luminosité, calibrage — est préservé). Sauvegarde .gsbak avant.</summary>
    public static TuningResult ApplyOptions(string optionsIniPath)
    {
        try
        {
            if (!File.Exists(optionsIniPath)) return new(false, 0, optionsIniPath, "Options.ini introuvable");
            try { File.Copy(optionsIniPath, optionsIniPath + ".gsbak", overwrite: true); } catch { }

            var lines = File.ReadAllLines(optionsIniPath).ToList();
            int applied = 0;
            foreach (var (key, val) in OptionsBaseline)
            {
                var rx = new Regex(@"^\s*" + Regex.Escape(key) + @"\s*=.*$", RegexOptions.IgnoreCase);
                int idx = lines.FindIndex(l => rx.IsMatch(l));
                string line = $"{key} = {val}";
                if (idx >= 0) { if (lines[idx] != line) { lines[idx] = line; applied++; } }
                else { lines.Add(line); applied++; }
            }
            File.WriteAllLines(optionsIniPath, lines, new UTF8Encoding(false));
            return new(true, applied, optionsIniPath, null);
        }
        catch (Exception ex) { return new(false, 0, optionsIniPath, ex.Message); }
    }

    /// <summary>Applique les réglages GenSpeed-safe (root-level) dans GenLauncherCfg.yaml par édition ciblée
    /// ligne par ligne (PAS de re-sérialisation — sinon YamlDotNet pollue d'ancres). Sauvegarde .gsbak avant.
    /// À faire GENLAUNCHER FERMÉ (il réécrit le YAML à la fermeture).</summary>
    public static TuningResult ApplyYaml(string yamlPath)
    {
        try
        {
            if (!File.Exists(yamlPath)) return new(false, 0, yamlPath, "GenLauncherCfg.yaml introuvable");
            try { File.Copy(yamlPath, yamlPath + ".gsbak", overwrite: true); } catch { }

            string text = File.ReadAllText(yamlPath);
            int applied = 0;
            foreach (var (key, val) in YamlSafe)
            {
                // Clé root-level uniquement (début de ligne, sans indentation) → ne touche pas les entrées de mods.
                var rx = new Regex(@"(?m)^" + Regex.Escape(key) + @":[ \t].*$");
                if (!rx.IsMatch(text)) continue;
                string updated = rx.Replace(text, $"{key}: {val}", 1);
                if (updated != text) { text = updated; applied++; }
            }
            File.WriteAllText(yamlPath, text, new UTF8Encoding(false));
            return new(true, applied, yamlPath, null);
        }
        catch (Exception ex) { return new(false, 0, yamlPath, ex.Message); }
    }
}
