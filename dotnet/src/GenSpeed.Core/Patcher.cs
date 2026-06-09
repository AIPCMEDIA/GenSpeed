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

    /// <summary>
    /// Garantit que .speedbak = version pristine et que fp est DÉPATCHÉ avant d'être (re)patché.
    /// Retourne false s'il faut SAUTER ce fichier : déjà patché mais backup perdu → re-scaler
    /// sur-patcherait le mod. On refuse plutôt que d'abîmer les données.
    /// </summary>
    public static bool EnsurePristineBackup(string fp, string? prevHash)
    {
        string bak = fp + ".speedbak";
        if (!File.Exists(bak))
        {
            // Pas de backup, mais le fichier EST la version déjà patchée connue → impossible de
            // récupérer le pristine ; re-scaler doublerait le patch. On saute.
            if (prevHash != null && Hashing.FileSha256(fp) == prevHash) return false;
            File.Copy(fp, bak, true);   // 1er patch : on sauvegarde le pristine
            return true;
        }
        string? cur = Hashing.FileSha256(fp);
        if (prevHash == null || cur == prevHash)
            File.Copy(bak, fp, true);   // DÉPATCH : restaure le pristine AVANT de (re)patcher
        else
            File.Copy(fp, bak, true);   // fp modifié hors GenSpeed -> nouveau pristine
        return true;
    }

    public static PatchOutcome PatchTarget(Target t, IReadOnlyDictionary<string, double> factors,
        IReadOnlyDictionary<string, string?>? cam, IReadOnlyDictionary<string, string> prevHashes)
    {
        var res = new PatchOutcome();
        foreach (var fp in t.Files)
        {
            // Dépatch préalable obligatoire (évite de sur-patcher un mod déjà patché).
            if (!EnsurePristineBackup(fp, prevHashes.TryGetValue(fp, out var ph) ? ph : null))
            {
                res.Skipped++;   // déjà patché + backup perdu → on ne double-patche pas
                continue;
            }
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
                    // Archive illisible (transitoire ?) → on saute SANS toucher au backup existant
                    // (le supprimer ferait perdre le point de restauration). Fix D.
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
