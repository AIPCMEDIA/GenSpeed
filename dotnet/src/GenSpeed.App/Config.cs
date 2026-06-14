using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GenSpeed.App;

/// <summary>Un preset de vitesse (nom, verrou, facteur global, surcharges par catégorie).</summary>
public sealed class SpeedPreset
{
    [JsonPropertyName("name")]   public string Name { get; set; } = "";
    [JsonPropertyName("locked")] public bool Locked { get; set; }
    [JsonPropertyName("factor")] public double Factor { get; set; } = 1.0;
    [JsonPropertyName("cats")]   public Dictionary<string, double>? Cats { get; set; }
}

/// <summary>Cache du code LAN d'une cible : hash + signature (fichier -> [ticks, taille]) pour éviter de re-hacher des Go.</summary>
public sealed class HashCacheEntry
{
    [JsonPropertyName("hash")] public string Hash { get; set; } = "";
    [JsonPropertyName("sig")]  public Dictionary<string, long[]> Sig { get; set; } = new();
}

/// <summary>État appliqué à un mod (pour réafficher vitesse/caméra après redémarrage).</summary>
public sealed class PatchedInfo
{
    [JsonPropertyName("speed")]  public string Speed { get; set; } = "";
    [JsonPropertyName("camera")] public string Camera { get; set; } = "";
    // chemin -> SHA du dernier patch : permet de détecter une MAJ externe du mod après redémarrage.
    [JsonPropertyName("files")]  public Dictionary<string, string> Files { get; set; } = new();
}

/// <summary>Configuration persistée de GenSpeed (.NET).</summary>
public sealed class GenConfig
{
    [JsonPropertyName("game_dir")]       public string? GameDir { get; set; }
    [JsonPropertyName("mods_dir")]       public string? ModsDir { get; set; }   // dossier GLM si GenLauncher est installé ailleurs
    // Master M1 : copie VIERGE de sauvegarde (jamais patchée) servant de source aux forks (M2).
    // Volontairement HORS KnownInstalls → jamais affiché dans le tableau ni l'assistant (anti-bêtise),
    // mais GenSpeed garde son emplacement pour les copies. Re-pointable via ⚙ Config.
    [JsonPropertyName("m1_dir")]         public string? M1Dir { get; set; }
    // Lien de téléchargement DIRECT de GenLauncher (ModDB). Direct = facile pour un utilisateur non technique ;
    // mais épingle une version → ÉDITABLE (⚙ Config) quand une plus récente sort. La page de listing ModDB
    // reste le secours toujours-à-jour. (Le GitHub p0ls3r n'a pas de release binaire.)
    [JsonPropertyName("genlauncher_url")] public string GenLauncherUrl { get; set; } = "https://www.moddb.com/downloads/start/277509";
    // Installations connues (jeu de base + mods autonomes type Reborn Omega). GameDir = active.
    [JsonPropertyName("known_installs")] public List<string> KnownInstalls { get; set; } = new();
    // Dossier d'install -> exe de lancement mémorisé (résout l'ambiguïté GenLauncher vs exe du mod).
    [JsonPropertyName("launch_exes")]    public Dictionary<string, string> LaunchExes { get; set; } = new();
    // Label du mod -> vitesse/caméra appliquées (réaffichées au prochain démarrage).
    [JsonPropertyName("patched_state")]  public Dictionary<string, PatchedInfo> PatchedState { get; set; } = new();
    // "dossier::label" -> code LAN mis en cache (évite de re-hacher des Go à chaque chargement).
    [JsonPropertyName("hash_cache")]     public Dictionary<string, HashCacheEntry> HashCache { get; set; } = new();
    // "dossier::label" -> nom d'affichage personnalisé (renommage non destructif, n'affecte pas le patch).
    [JsonPropertyName("mod_aliases")]    public Dictionary<string, string> ModAliases { get; set; } = new();
    [JsonPropertyName("speed_presets")]  public List<SpeedPreset> SpeedPresets { get; set; } = new();
    // nom -> { variable -> valeur } ; "Reset camera" = {} est conservé.
    [JsonPropertyName("camera_presets")] public Dictionary<string, Dictionary<string, string>> CameraPresets { get; set; } = new();
    [JsonPropertyName("last_lang")]      public int LastLang { get; set; } = 0;
    [JsonPropertyName("last_theme")]     public string LastTheme { get; set; } = "Eva";

    public static List<SpeedPreset> DefaultSpeedPresets() => new()
    {
        new() { Name = "Original", Locked = true,  Factor = 1.0 },
        new() { Name = "Cool",     Locked = false, Factor = 1.5 },
        new() { Name = "Énervé",   Locked = false, Factor = 2.0 },
        new() { Name = "Déchaîné", Locked = false, Factor = 3.0 },
    };

    public static Dictionary<string, Dictionary<string, string>> DefaultCameraPresets() => new()
    {
        ["Cam haute"]     = new() { ["CameraPitch"]="31", ["CameraYaw"]="", ["CameraHeight"]="600",  ["MaxCameraHeight"]="800",  ["MinCameraHeight"]="120", ["DrawEntireTerrain"]="Yes" },
        ["Cam max"]       = new() { ["CameraPitch"]="30", ["CameraYaw"]="", ["CameraHeight"]="800",  ["MaxCameraHeight"]="1200", ["MinCameraHeight"]="120", ["DrawEntireTerrain"]="Yes" },
        ["Cam eloignee"]  = new() { ["CameraPitch"]="29", ["CameraYaw"]="", ["CameraHeight"]="1000", ["MaxCameraHeight"]="1500", ["MinCameraHeight"]="120", ["DrawEntireTerrain"]="Yes" },
        ["Vue satellite"] = new() { ["CameraPitch"]="28", ["CameraYaw"]="", ["CameraHeight"]="1200", ["MaxCameraHeight"]="2000", ["MinCameraHeight"]="120", ["DrawEntireTerrain"]="Yes" },
        ["Reset camera"]  = new(),
    };
}

/// <summary>Lecture/écriture de la config (JSON dans %LOCALAPPDATA%\GenSpeed).</summary>
public static class ConfigStore
{
    private static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static string Dir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GenSpeed");
    private static string NetFile => Path.Combine(Dir, "genspeed-net.json");
    private static string PyFile  => Path.Combine(Dir, "genspeed-config.json");

    public static GenConfig Load()
    {
        // 1) Config .NET existante.
        if (File.Exists(NetFile))
        {
            try
            {
                var c = JsonSerializer.Deserialize<GenConfig>(File.ReadAllText(NetFile), Opts);
                if (c != null) { Normalize(c); return c; }
            }
            catch { }
        }

        var cfg = new GenConfig
        {
            SpeedPresets = GenConfig.DefaultSpeedPresets(),
            CameraPresets = GenConfig.DefaultCameraPresets(),
        };

        // 2) Premier lancement : importer les presets de la config Python si présente.
        if (File.Exists(PyFile))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(PyFile));
                var root = doc.RootElement;
                if (root.TryGetProperty("speed_presets", out var sp) && sp.ValueKind == JsonValueKind.Array)
                {
                    var imported = sp.Deserialize<List<SpeedPreset>>(Opts);
                    if (imported is { Count: > 0 }) cfg.SpeedPresets = imported;
                }
                if (root.TryGetProperty("camera_presets", out var cp) && cp.ValueKind == JsonValueKind.Object)
                {
                    var imported = cp.Deserialize<Dictionary<string, Dictionary<string, string>>>(Opts);
                    if (imported is { Count: > 0 }) cfg.CameraPresets = imported;
                }
                if (root.TryGetProperty("game_dir", out var gd) && gd.ValueKind == JsonValueKind.String)
                    cfg.GameDir = gd.GetString();
            }
            catch { }
        }

        Normalize(cfg);
        return cfg;
    }

    private static void Normalize(GenConfig c)
    {
        if (c.SpeedPresets.Count == 0) c.SpeedPresets = GenConfig.DefaultSpeedPresets();
        if (c.CameraPresets.Count == 0) c.CameraPresets = GenConfig.DefaultCameraPresets();
    }

    public static void ExportTo(string path, GenConfig cfg) =>
        File.WriteAllText(path, JsonSerializer.Serialize(cfg, Opts));

    public static GenConfig? ImportFrom(string path)
    {
        try
        {
            var c = JsonSerializer.Deserialize<GenConfig>(File.ReadAllText(path), Opts);
            if (c != null) { Normalize(c); return c; }
        }
        catch { }
        return null;
    }

    /// <summary>Si vrai, Save() ne fait rien : empêche GenSpeed de recréer sa config qu'on est en train
    /// de supprimer (auto-désinstallation — « ne pas scier la branche »).</summary>
    public static bool Suppressed { get; set; }

    public static void Save(GenConfig cfg)
    {
        if (Suppressed) return;
        try
        {
            Directory.CreateDirectory(Dir);
            string tmp = NetFile + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(cfg, Opts));
            File.Move(tmp, NetFile, overwrite: true);
        }
        catch { }
    }
}
