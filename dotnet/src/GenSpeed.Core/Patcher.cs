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

    /// <summary>Patch d'une cible FORK (.pak) par OVERLAY LOOSE : on extrait les .ini du .pak (source PRISTINE,
    /// jamais modifiée), on les scale/règle la caméra, et on écrit le résultat en LOOSE dans Data\INI — que le
    /// moteur SAGE charge PAR-DESSUS l'archive. Avantages : aucune réécriture des centaines de Mo du .pak, et
    /// re-patch toujours sûr (on repart du .pak pristine à chaque fois). Le « backup » = ne rien écrire / supprimer
    /// les loose (cf. <see cref="RestorePakLoose"/>). <paramref name="prevLoose"/> = loose du patch précédent à
    /// nettoyer d'abord (sinon un ancien override resterait si la nouvelle valeur ne change plus ce fichier).</summary>
    public static PatchOutcome PatchPakLoose(Target t, IReadOnlyDictionary<string, double> factors,
        IReadOnlyDictionary<string, string?>? cam, IEnumerable<string> prevLoose)
    {
        var res = new PatchOutcome();
        DeleteLoose(prevLoose);                                  // repart d'un état propre
        var camFork = ForkCam(cam);                             // jamais de pleine-carte sur un fork (FPS)
        foreach (var pak in t.Files)
        {
            List<BigEntry> inis;
            try { inis = BigArchive.ReadIniEntries(pak); }
            catch (BigFileException) { res.Skipped++; continue; }
            foreach (var e in inis)
            {
                string orig = Latin1.GetString(e.Data);
                string np = IniScaler.ApplyText(orig, factors, camFork);
                if (np == orig) continue;
                string loose = Path.Combine(t.InstallDir, e.Name);
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(loose)!);
                    File.WriteAllBytes(loose, Latin1.GetBytes(np));
                    res.PatchedFiles[loose] = Hashing.FileSha256(loose)!;
                }
                catch { res.Skipped++; }
            }
        }
        return res;
    }

    /// <summary>Dépatch d'un fork (.pak) : supprime les fichiers loose écrits par le patch → le moteur relit
    /// l'archive d'origine. Réversible à 100 %, le .pak n'a jamais été touché.</summary>
    public static void RestorePakLoose(IEnumerable<string> looseFiles) => DeleteLoose(looseFiles);

    private static void DeleteLoose(IEnumerable<string> files)
    {
        foreach (var f in files)
            try { if (File.Exists(f)) File.Delete(f); } catch { }
    }

    /// <summary>Copie de la caméra forçant DrawEntireTerrain=No DÈS qu'une caméra est réglée : un fork tourne sur
    /// le moteur recompilé lourd, où le rendu pleine-carte écroule les FPS (cause du « jeu ralenti »).</summary>
    private static IReadOnlyDictionary<string, string?>? ForkCam(IReadOnlyDictionary<string, string?>? cam)
    {
        if (cam == null) return null;
        var d = new Dictionary<string, string?>(cam);
        bool camSet = d.Any(kv => kv.Key is not ("DrawEntireTerrain" or "CameraYaw") && !string.IsNullOrEmpty(kv.Value));
        if (camSet) d["DrawEntireTerrain"] = "No";
        return d;
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
