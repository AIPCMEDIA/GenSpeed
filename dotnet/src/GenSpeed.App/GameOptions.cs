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
        new("Resolution",             false, "free",  "res"),
        new("UseAlternateMouse",      false, "free",  "toggle"),
        new("ScrollFactor",           false, "free",  "choice", new[] { "10", "20", "30", "40", "50", "60", "80", "100" }),
        new("UseDoubleClickAttackMove", false, "free", "toggle"),
        new("Retaliation",            false, "free",  "toggle"),
        new("StaticGameLOD",          false, "free",  "lod",    new[] { "Low", "Medium", "High" }),
        new("TextureReduction",       false, "free",  "tex",    new[] { "0", "1", "2" }),
        new("AntiAliasing",           false, "free",  "choice", new[] { "0", "2", "4" }),
        new("UseShadowVolumes",       false, "free",  "toggle"),
        new("UseShadowDecals",        false, "free",  "toggle"),
        new("UseCloudMap",            false, "free",  "toggle"),
        new("UseLightMap",            false, "free",  "toggle"),
        new("BuildingOcclusion",      false, "free",  "toggle"),
        // 🔴 À aligner avec l'ami (pré-cochés sûrs) — + Vulkan (doit matcher aussi, fusionné ici sans bloc à part)
        new("HeatEffects",            false, "match", "toggle"),
        new("ExtraAnimations",        false, "match", "toggle"),
        new("ShowTrees",              false, "match", "toggle"),
        new("ShowSoftWaterEdge",      false, "match", "toggle"),
        new("DynamicLOD",             false, "match", "toggle"),
        new("MaxParticleCount",       false, "match", "particles", new[] { "500", "1000", "1500" }),
        new("UseVulkan",              true,  "match", "toggle"),
        // 🌐 Réseau — SendDelay (doit s'aligner aussi : porté par le code de synchro). IP & pare-feu sont gérés
        // hors Defs (contrôles dynamiques) dans GameOptionsWindow.
        new("SendDelay",              false, "net",   "toggle"),
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
            ["ScrollFactor"] = "50",
            ["UseDoubleClickAttackMove"] = "no",
            ["Retaliation"] = "yes",
            ["StaticGameLOD"] = gfx == "light" ? "Medium" : "High",
            ["TextureReduction"] = gfx == "high" ? "0" : gfx == "balanced" ? "1" : "2",
            ["AntiAliasing"] = gfx == "high" ? "4" : gfx == "balanced" ? "2" : "0",
            ["UseShadowVolumes"] = yn(eff), ["UseShadowDecals"] = yn(eff), ["UseCloudMap"] = yn(eff),
            ["UseLightMap"] = yn(eff), ["BuildingOcclusion"] = yn(eff),
            ["HeatEffects"] = "no", ["ExtraAnimations"] = "no", ["ShowTrees"] = "no", ["ShowSoftWaterEdge"] = "no",
            ["DynamicLOD"] = "no", ["SendDelay"] = "yes", ["MaxParticleCount"] = "1000",
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

    /// <summary>Les 7 booléens anti-désync (les 6 du groupe "match" + Vulkan), dans un ORDRE FIXE : leur position
    /// est le numéro de bit dans le code court. NE JAMAIS réordonner (casserait les codes existants).</summary>
    private static readonly string[] BoolSync =
        { "HeatEffects", "ExtraAnimations", "ShowTrees", "ShowSoftWaterEdge", "DynamicLOD", "SendDelay", "UseVulkan" };

    /// <summary>Toutes les clés que le code de synchro transporte : les 7 booléens + MaxParticleCount (nombre).</summary>
    public static IEnumerable<string> SyncKeys => BoolSync.Append("MaxParticleCount");

    /// <summary>Encode les réglages anti-désync en un code COURT (« GS2-7F-1000 ») : 7 oui/non packés en un octet
    /// hexa + le nombre de particules. Zéro réseau : l'utilisateur le copie, l'ami le colle.</summary>
    public static string ExportMatchCode(GenConfig c)
    {
        var reco = Recommended();
        int bits = 0;
        for (int i = 0; i < BoolSync.Length; i++)
            if (Value(c, BoolSync[i], reco).Equals("yes", System.StringComparison.OrdinalIgnoreCase)) bits |= 1 << i;
        return $"GS2-{bits:X2}-{Value(c, "MaxParticleCount", reco)}";
    }

    /// <summary>Applique un code « GS2-… » reçu d'un ami : ne touche QUE les clés anti-désync. Retourne le nombre
    /// de réglages appliqués, ou -1 si le code est invalide.</summary>
    public static int ImportMatchCode(GenConfig c, string? code)
    {
        try
        {
            code = (code ?? "").Trim().ToUpperInvariant();
            if (!code.StartsWith("GS2-")) return -1;
            var p = code.Substring(4).Split('-');
            if (p.Length < 2) return -1;
            int bits = System.Convert.ToInt32(p[0], 16);
            int n = 0;
            for (int i = 0; i < BoolSync.Length; i++) { c.GameOptions[BoolSync[i]] = (bits & (1 << i)) != 0 ? "yes" : "no"; n++; }
            if (int.TryParse(p[1], out var part)) { c.GameOptions["MaxParticleCount"] = System.Math.Clamp(part, 100, 50000).ToString(); n++; }
            return n;
        }
        catch { return -1; }
    }
}
