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

/// <summary>Configuration persistée de GenSpeed (.NET).</summary>
public sealed class GenConfig
{
    [JsonPropertyName("game_dir")]       public string? GameDir { get; set; }
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

    public static void Save(GenConfig cfg)
    {
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
