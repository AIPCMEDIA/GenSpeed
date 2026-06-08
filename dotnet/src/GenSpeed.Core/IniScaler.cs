using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace GenSpeed.Core;

/// <summary>
/// Mise à l'échelle des variables INI (port fidèle de core.scale / apply_text).
/// Reproduit EXACTEMENT le formatage des nombres Python pour garantir
/// l'égalité octet-pour-octet du texte patché.
/// </summary>
public static class IniScaler
{
    // Catégories MULTIPLICATIVES (cat, variables) — ordre conservé (= dict Python).
    public static readonly (string Cat, string[] Vars)[] Mult =
    {
        ("deplacement", new[] { "Speed","SpeedDamaged","MinSpeed","Acceleration","AccelerationDamaged",
                                "Braking","TurnRate","TurnRateDamaged","MinTurnSpeed" }),
        ("projectiles", new[] { "InitialVelocity","WeaponSpeed" }),
        ("visee",       new[] { "TurretTurnRate","TurretPitchRate" }),
        ("economie_gain", new[] { "ValuePerSupplyBox" }),
        ("detection",   new[] { "VisionRange","ShroudClearingRange" }),
        ("soin",        new[] { "HealingAmount" }),
        ("merite",      new[] { "ExperienceValue" }),
    };

    // Catégories DIVISIVES (le facteur est inversé).
    public static readonly (string Cat, string[] Vars)[] Div =
    {
        ("construction", new[] { "BuildTime" }),
        ("tir",          new[] { "DelayBetweenShots","ClipReloadTime" }),
        ("pouvoirs",     new[] { "ReloadTime" }),
        ("deploiement",  new[] { "UnpackTime","PreparationTime" }),
        ("economie_collecte", new[] { "SupplyWarehouseActionDelay" }),
        ("soin",         new[] { "HealingDelay" }),
    };

    private static readonly Dictionary<string, Regex> RegexCache = new();

    private static Regex VarRegex(string var)
    {
        if (!RegexCache.TryGetValue(var, out var rx))
        {
            // ^(\s*VAR\s*=\s*)(-?\d+\.?\d*)(.*)$  — Multiline + IgnoreCase
            rx = new Regex(
                $@"^(\s*{Regex.Escape(var)}\s*=\s*)(-?\d+\.?\d*)(.*)$",
                RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
            RegexCache[var] = rx;
        }
        return rx;
    }

    /// <summary>Formate la nouvelle valeur exactement comme Python (str(int(round)) ou %.4f+strip).</summary>
    private static string FormatScaled(string original, double newValue)
    {
        if (!original.Contains('.'))
        {
            // Entier : round() Python = half-to-even.
            long r = (long)Math.Round(newValue, MidpointRounding.ToEven);
            return r.ToString(CultureInfo.InvariantCulture);
        }
        // Décimal : "%.4f" puis suppression des zéros (et du point) en fin.
        string s = newValue.ToString("F4", CultureInfo.InvariantCulture);
        s = s.TrimEnd('0').TrimEnd('.');
        return s;
    }

    /// <summary>Applique un facteur multiplicatif aux variables données. Retourne (texte, nbRemplacements).</summary>
    public static (string Text, int Count) Scale(string text, string[] varnames, double factor)
    {
        int count = 0;
        foreach (var var in varnames)
        {
            text = VarRegex(var).Replace(text, m =>
            {
                count++;
                double v = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                double nv = v * factor;
                return m.Groups[1].Value + FormatScaled(m.Groups[2].Value, nv) + m.Groups[3].Value;
            });
        }
        return (text, count);
    }

    /// <summary>Applique tous les facteurs (MULT puis DIV) et, optionnellement, la caméra.</summary>
    public static string ApplyText(string text, IReadOnlyDictionary<string, double> factors,
                                   IReadOnlyDictionary<string, string?>? cam = null)
    {
        foreach (var (cat, vars) in Mult)
        {
            double fac = factors.TryGetValue(cat, out var f) ? f : 1.0;
            if (fac != 1.0)
                text = Scale(text, vars, fac).Text;
        }
        foreach (var (cat, vars) in Div)
        {
            double fac = factors.TryGetValue(cat, out var f) ? f : 1.0;
            if (fac != 1.0)
                text = Scale(text, vars, 1.0 / fac).Text;
        }
        if (cam != null)
            text = SetCamera(text, cam);
        return text;
    }

    private static readonly string[] CamOrder =
        { "CameraPitch", "CameraYaw", "CameraHeight", "MaxCameraHeight", "MinCameraHeight", "DrawEntireTerrain" };

    /// <summary>Applique les réglages caméra (port fidèle de core.set_camera).</summary>
    public static string SetCamera(string text, IReadOnlyDictionary<string, string?> cam)
    {
        foreach (var var in CamOrder)
        {
            if (!cam.TryGetValue(var, out var val) || string.IsNullOrEmpty(val))
                continue;
            string repl = var == "DrawEntireTerrain"
                ? val
                : FormatG(double.Parse(val, CultureInfo.InvariantCulture));
            text = Regex.Replace(text, $@"^(\s*{Regex.Escape(var)}\s*=\s*)\S+",
                m => m.Groups[1].Value + repl,
                RegexOptions.Multiline | RegexOptions.IgnoreCase);
        }
        if (cam.TryGetValue("MaxCameraHeight", out var mx) && !string.IsNullOrEmpty(mx))
            text = Regex.Replace(text, @"^(\s*EnforceMaxCameraHeight\s*=\s*)\S+", "${1}No",
                RegexOptions.Multiline | RegexOptions.IgnoreCase);
        return text;
    }

    // Équivalent de Python "%g" pour les valeurs caméra.
    private static string FormatG(double v)
    {
        string s = v.ToString("G6", CultureInfo.InvariantCulture);
        return s.Contains('E') ? s.Replace("E", "e") : s;
    }

    /// <summary>Patche le contenu d'une archive BIG en place (mêmes règles que patch_target : .ini latin-1).</summary>
    public static void PatchBigEntries(List<BigEntry> entries, IReadOnlyDictionary<string, double> factors,
                                       IReadOnlyDictionary<string, string?>? cam = null)
    {
        foreach (var e in entries)
        {
            if (!e.Name.EndsWith(".ini", StringComparison.OrdinalIgnoreCase))
                continue;
            string original = Encoding.Latin1.GetString(e.Data);
            string patched = ApplyText(original, factors, cam);
            if (patched != original)
                e.Data = Encoding.Latin1.GetBytes(patched);
        }
    }
}
