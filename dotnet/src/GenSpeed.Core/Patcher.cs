using System.Text;

namespace GenSpeed.Core;

/// <summary>Patch / restauration des cibles (port fidèle de core.patch_target & co).</summary>
public static class Patcher
{
    private static readonly Encoding Latin1 = Encoding.Latin1;

    public sealed class PatchOutcome
    {
        public Dictionary<string, string> PatchedFiles { get; } = new();  // fp -> sha256
        public int Skipped { get; set; }
    }

    /// <summary>Garantit que .speedbak = version pristine et que fp est prêt à patcher.</summary>
    public static void EnsurePristineBackup(string fp, string? prevHash)
    {
        string bak = fp + ".speedbak";
        if (!File.Exists(bak)) { File.Copy(fp, bak, true); return; }
        string? cur = Hashing.FileSha256(fp);
        if (prevHash == null || cur == prevHash)
            File.Copy(bak, fp, true);   // backup de confiance -> on restaure le pristine
        else
            File.Copy(fp, bak, true);   // fp modifié hors GenSpeed -> nouveau pristine
    }

    public static PatchOutcome PatchTarget(Target t, IReadOnlyDictionary<string, double> factors,
        IReadOnlyDictionary<string, string?>? cam, IReadOnlyDictionary<string, string> prevHashes)
    {
        var res = new PatchOutcome();
        foreach (var fp in t.Files)
        {
            EnsurePristineBackup(fp, prevHashes.TryGetValue(fp, out var ph) ? ph : null);
            bool changed;

            if (t.Type == TargetType.Ini)
            {
                string original = Latin1.GetString(File.ReadAllBytes(fp));
                // Reproduit le mode texte Python : lecture universelle (\r\n,\r -> \n),
                // écriture \n -> \r\n.
                string norm = original.Replace("\r\n", "\n").Replace("\r", "\n");
                string outText = IniScaler.ApplyText(norm, factors, cam).Replace("\n", "\r\n");
                changed = outText != original;
                if (changed) File.WriteAllBytes(fp, Latin1.GetBytes(outText));
            }
            else
            {
                List<BigEntry> entries;
                try { entries = BigArchive.Read(fp); }
                catch (BigFileException)
                {
                    string b = fp + ".speedbak";
                    if (File.Exists(b)) File.Delete(b);
                    res.Skipped++;
                    continue;
                }
                changed = false;
                foreach (var e in entries)
                {
                    if (!e.Name.EndsWith(".ini", StringComparison.OrdinalIgnoreCase)) continue;
                    string orig = Latin1.GetString(e.Data);
                    string np = IniScaler.ApplyText(orig, factors, cam);
                    if (np != orig) { changed = true; e.Data = Latin1.GetBytes(np); }
                }
                if (changed) BigArchive.Write(fp, entries);
            }

            if (changed)
            {
                res.PatchedFiles[fp] = Hashing.FileSha256(fp)!;
            }
            else
            {
                string bak = fp + ".speedbak";
                if (File.Exists(bak)) File.Delete(bak);
                res.Skipped++;
            }
        }
        return res;
    }

    /// <summary>Classe les fichiers d'une cible : restaurables vs backup périmé (stale).</summary>
    public static (List<string> ToRestore, List<string> Stale) ClassifyRestore(
        Target t, IReadOnlyDictionary<string, string>? expected)
    {
        var toRestore = new List<string>();
        var stale = new List<string>();
        foreach (var fp in t.Files)
        {
            string bak = fp + ".speedbak";
            if (!File.Exists(bak)) continue;
            if (expected != null && expected.TryGetValue(fp, out var exp) && exp != null
                && Hashing.FileSha256(fp) != exp)
            {
                stale.Add(fp);
                continue;
            }
            toRestore.Add(fp);
        }
        return (toRestore, stale);
    }

    /// <summary>Restaure (.speedbak -> fichier puis suppression) et/ou supprime les backups périmés.</summary>
    public static void RestoreFiles(IEnumerable<string> restore, IEnumerable<string> delbak)
    {
        foreach (var fp in restore)
        {
            string bak = fp + ".speedbak";
            if (File.Exists(bak)) { File.Copy(bak, fp, true); File.Delete(bak); }
        }
        foreach (var fp in delbak)
        {
            string bak = fp + ".speedbak";
            if (File.Exists(bak)) File.Delete(bak);
        }
    }
}
