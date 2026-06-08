using System.Text;
using System.Text.RegularExpressions;

namespace GenSpeed.Core;

public sealed record PreviewRow(string Var, string Orig, string Current, string Location, bool Modified);

/// <summary>Aperçu des variables INI d'une cible (original via .speedbak, actuel). Port de gui/preview.py.</summary>
public static class Preview
{
    /// <summary>Variables « clés » affichées par défaut.</summary>
    public static readonly HashSet<string> KeyVars = new(StringComparer.Ordinal)
    {
        "Speed", "TurnRate", "InitialVelocity", "WeaponSpeed", "TurretTurnRate", "BuildTime",
        "DelayBetweenShots", "ClipReloadTime", "ReloadTime", "UnpackTime", "SupplyWarehouseActionDelay",
        "ValuePerSupplyBox", "VisionRange", "ShroudClearingRange", "HealingAmount", "HealingDelay",
        "ExperienceValue", "CameraHeight", "MaxCameraHeight", "MinCameraHeight", "CameraPitch", "DrawEntireTerrain",
    };

    private static readonly Regex VarRe =
        new(@"^[ \t]*([A-Za-z][A-Za-z0-9_]*)\s*=\s*([^\s;]+)", RegexOptions.Multiline | RegexOptions.Compiled);

    private static IEnumerable<(string Label, string Text)> IterInis(string gameDir, string fp, bool isGib)
    {
        string rel;
        try { rel = Path.GetRelativePath(gameDir, fp); } catch { rel = Path.GetFileName(fp); }
        if (rel.EndsWith(".speedbak", StringComparison.OrdinalIgnoreCase)) rel = rel[..^9];

        if (isGib)
        {
            List<BigEntry> entries;
            try { entries = BigArchive.Read(fp); } catch { yield break; }
            foreach (var e in entries)
                if (e.Name.EndsWith(".ini", StringComparison.OrdinalIgnoreCase))
                    yield return ($"{rel} › {e.Name}", Encoding.Latin1.GetString(e.Data));
        }
        else
        {
            string? txt = null;
            try { txt = Encoding.Latin1.GetString(File.ReadAllBytes(fp)); } catch { }
            if (txt != null) yield return (rel, txt);
        }
    }

    private static Dictionary<string, (string Val, string Label, int Line)> LocatedVars(
        string gameDir, IEnumerable<string> paths, bool isGib, ISet<string>? wanted)
    {
        var outd = new Dictionary<string, (string, string, int)>(StringComparer.Ordinal);
        foreach (var fp in paths)
            foreach (var (label, text) in IterInis(gameDir, fp, isGib))
                foreach (Match m in VarRe.Matches(text))
                {
                    string n = m.Groups[1].Value;
                    if (wanted != null && !wanted.Contains(n)) continue;
                    if (outd.ContainsKey(n)) continue;
                    int line = text.AsSpan(0, m.Index).Count('\n') + 1;
                    outd[n] = (m.Groups[2].Value, label, line);
                }
        return outd;
    }

    /// <summary>Construit les lignes d'aperçu. wanted=null → toutes les variables ; onlyChanged → modifiées seulement.</summary>
    public static (List<PreviewRow> Rows, bool Patched, int Changed) Gather(
        string gameDir, Target target, ISet<string>? wanted, bool onlyChanged)
    {
        bool isGib = target.Type is TargetType.Gib or TargetType.Big;
        bool patched = target.Files.Any(fp => File.Exists(fp + ".speedbak"));
        var origPaths = target.Files.Select(fp => File.Exists(fp + ".speedbak") ? fp + ".speedbak" : fp);
        var orig = LocatedVars(gameDir, origPaths, isGib, wanted);
        var cur = LocatedVars(gameDir, target.Files, isGib, wanted);

        var names = orig.Keys.Union(cur.Keys).OrderBy(x => x, StringComparer.Ordinal);
        var rows = new List<PreviewRow>();
        int changed = 0;
        foreach (var n in names)
        {
            string? ov = orig.TryGetValue(n, out var oe) ? oe.Val : null;
            string? cv = cur.TryGetValue(n, out var ce) ? ce.Val : null;
            var refe = orig.TryGetValue(n, out var r1) ? r1 : (cur.TryGetValue(n, out var r2) ? r2 : default);
            string loc = refe.Label != null ? $"{refe.Label}:{refe.Line}" : "";
            bool modified = patched && cv != null && cv != ov;
            if (modified) changed++;
            if (onlyChanged && !modified) continue;
            rows.Add(new PreviewRow(n, ov ?? "", patched ? (cv ?? "") : "", loc, modified));
        }
        return (rows, patched, changed);
    }
}
