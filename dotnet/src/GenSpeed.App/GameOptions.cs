using System.Collections.Generic;
using System.Linq;
using GenSpeed.Core;

namespace GenSpeed.App;

/// <summary>Définition d'une option de jeu pour le sélecteur. Group : "free" (zéro risque LAN) / "match"
/// (à aligner entre joueurs) / "adv" (Vulkan). Kind : "toggle" (oui/non) / "res" / "lod" / "tex" / "particles".
/// Les libellés + aides sont dans la loc : go.&lt;Key&gt;.l (libellé) et go.&lt;Key&gt;.h (explication brève).</summary>
internal sealed record GOpt(string Key, bool Yaml, string Group, string Kind, string[]? Choices = null);

/// <summary>Modèle des options de jeu : liste, valeurs recommandées (pré-cochage PC-aware) et application
/// vers Options.ini + YAML. Tout est modifiable par l'utilisateur (stocké dans Config.GameOptions).</summary>
internal static class GameOptions
{
    public static readonly GOpt[] Defs =
    {
        // 🟢 Libres — aucun impact LAN
        new("Resolution",        false, "free",  "res"),
        new("UseAlternateMouse", false, "free",  "toggle"),
        new("StaticGameLOD",     false, "free",  "lod",       new[] { "Low", "Medium", "High" }),
        new("TextureReduction",  false, "free",  "tex",       new[] { "0", "1", "2" }),
        new("UseShadowVolumes",  false, "free",  "toggle"),
        new("UseShadowDecals",   false, "free",  "toggle"),
        new("UseCloudMap",       false, "free",  "toggle"),
        new("UseLightMap",       false, "free",  "toggle"),
        new("BuildingOcclusion", false, "free",  "toggle"),
        // 🔴 À aligner avec l'ami (pré-cochés sûrs)
        new("HeatEffects",       false, "match", "toggle"),
        new("ExtraAnimations",   false, "match", "toggle"),
        new("ShowTrees",         false, "match", "toggle"),
        new("ShowSoftWaterEdge", false, "match", "toggle"),
        new("DynamicLOD",        false, "match", "toggle"),
        new("SendDelay",         false, "match", "toggle"),
        new("MaxParticleCount",  false, "match", "particles", new[] { "500", "1000", "1500" }),
        // ⚡ Avancé
        new("UseVulkan",         true,  "adv",   "toggle"),
    };

    /// <summary>Valeurs recommandées (pré-cochage) : anti-mismatch sûrs, graphismes selon la puissance du PC,
    /// résolution native, souris alternative ON, Vulkan OFF.</summary>
    public static Dictionary<string, string> Recommended()
    {
        string gfx = PcInfo.RecommendedGraphics();           // light / balanced / high
        string yn(bool b) => b ? "yes" : "no";
        bool eff = gfx == "high";                            // effets cosmétiques ON seulement sur PC costaud
        return new()
        {
            ["Resolution"] = "native",
            ["UseAlternateMouse"] = "yes",
            ["StaticGameLOD"] = gfx == "light" ? "Medium" : "High",
            ["TextureReduction"] = gfx == "high" ? "0" : gfx == "balanced" ? "1" : "2",
            ["UseShadowVolumes"] = yn(eff), ["UseShadowDecals"] = yn(eff), ["UseCloudMap"] = yn(eff),
            ["UseLightMap"] = yn(eff), ["BuildingOcclusion"] = yn(eff),
            ["HeatEffects"] = "no", ["ExtraAnimations"] = "no", ["ShowTrees"] = "no", ["ShowSoftWaterEdge"] = "no",
            ["DynamicLOD"] = "no", ["SendDelay"] = "no", ["MaxParticleCount"] = "1000",
            ["UseVulkan"] = "no",
        };
    }

    /// <summary>Valeur effective d'une clé : choix de l'utilisateur s'il existe, sinon la recommandation.</summary>
    public static string Value(GenConfig c, string key, Dictionary<string, string>? reco = null)
        => (c.GameOptions.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v))
            ? v : ((reco ?? Recommended()).TryGetValue(key, out var d) ? d : "");

    /// <summary>Construit la liste (clé,valeur) Options.ini effective (résolution « native » → résolution réelle).</summary>
    public static List<(string Key, string Value)> EffectiveIni(GenConfig c)
    {
        var reco = Recommended();
        var ini = new List<(string, string)>();
        foreach (var o in Defs.Where(o => !o.Yaml))
        {
            string val = Value(c, o.Key, reco);
            if (o.Key == "Resolution" && (val == "native" || string.IsNullOrWhiteSpace(val)))
                val = ScreenInfo.NativeResolution() ?? "1920 1080";
            if (o.Key == "StaticGameLOD") ini.Add(("IdealStaticGameLOD", val));   // les deux ensemble
            ini.Add((o.Key, val));
        }
        return ini;
    }

    /// <summary>Vrai si l'utilisateur a choisi Vulkan ON (sinon OFF par défaut).</summary>
    public static bool Vulkan(GenConfig c) => Value(c, "UseVulkan").Equals("yes", System.StringComparison.OrdinalIgnoreCase);

    /// <summary>Applique les choix à Options.ini (un seul, partagé). Le YAML/UseVulkan est appliqué par AutoTune
    /// (qui itère les installs GenLauncher).</summary>
    public static void ApplyIni(GenConfig c)
        => MultiplayerTuning.ApplyOptionsValues(MultiplayerTuning.DefaultOptionsIniPath(), EffectiveIni(c));

    /// <summary>Les clés qui DOIVENT être identiques entre joueurs LAN : groupe "match" + Vulkan (impacte la
    /// désync). Ce sont elles, et elles seules, que le code de synchro transporte.</summary>
    public static IEnumerable<string> SyncKeys =>
        Defs.Where(o => o.Group == "match").Select(o => o.Key).Append("UseVulkan");

    /// <summary>Encode les réglages anti-désync en un code court partageable (« GS1-… », base64). Zéro réseau :
    /// l'utilisateur le copie et l'envoie (Discord…), l'ami le colle.</summary>
    public static string ExportMatchCode(GenConfig c)
    {
        var reco = Recommended();
        var raw = string.Join(";", SyncKeys.Select(k => $"{k}={Value(c, k, reco)}"));
        return "GS1-" + System.Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(raw));
    }

    /// <summary>Applique un code reçu d'un ami : ne touche QUE les clés anti-désync connues. Retourne le nombre
    /// de réglages appliqués, ou -1 si le code est invalide.</summary>
    public static int ImportMatchCode(GenConfig c, string? code)
    {
        try
        {
            code = (code ?? "").Trim();
            if (code.StartsWith("GS1-", System.StringComparison.OrdinalIgnoreCase)) code = code.Substring(4);
            if (string.IsNullOrWhiteSpace(code)) return -1;
            string raw = System.Text.Encoding.UTF8.GetString(System.Convert.FromBase64String(code));
            var valid = new HashSet<string>(SyncKeys);
            int n = 0;
            foreach (var pair in raw.Split(';', System.StringSplitOptions.RemoveEmptyEntries))
            {
                int i = pair.IndexOf('=');
                if (i <= 0) continue;
                string k = pair.Substring(0, i).Trim(), v = pair.Substring(i + 1).Trim();
                if (valid.Contains(k)) { c.GameOptions[k] = v; n++; }
            }
            return n > 0 ? n : -1;
        }
        catch { return -1; }
    }
}
