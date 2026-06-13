using System.Diagnostics;
using System.Text.RegularExpressions;

namespace GenSpeed.Core;

/// <summary>Résultat d'une copie d'install (assistant d'installation propre).</summary>
public sealed record CopyResult(bool Ok, long Bytes, string? Error);

/// <summary>Briques de l'assistant d'installation propre : copier une base vierge vers un dossier neutre
/// (compartimentation) + garde-fous (vierge ? moddée ? NTFS ? espace ?). 100% local, non destructif pour la source.
///
/// Modèle (voir [[install-assistant-design]]) : M0 vierge (Steam, gardé) → copie → M1 (base saine + GenPatcher)
/// → M2 (jouable + outils/mods). FORK = copie de M0 VIERGE (consigne Reborn Omega : « install onto a copy of
/// VANILLA Zero Hour, GenTool can cause issues ») + contenu du fork collé dessus.</summary>
public static class InstallManager
{
    // AppIDs Steam (Ultimate Collection).
    public const string AppIdZeroHour = "2732960";
    public const string AppIdGenerals = "2229870";

    /// <summary>Nom de dossier standard du master M1 (copie vierge de sauvegarde).</summary>
    public const string MasterFolderName = "Master ZH";

    /// <summary>Déclenche une action du cycle de vie Steam via son protocole — l'utilisateur valide DANS Steam
    /// (GenSpeed ne télécharge rien lui-même). verb = "install" | "run" | "uninstall". Pendant du désinstall « juste valider ».</summary>
    public static bool SteamLifecycle(string verb, string appId)
    {
        try { System.Diagnostics.Process.Start(new ProcessStartInfo($"steam://{verb}/{appId}") { UseShellExecute = true }); return true; }
        catch { return false; }
    }

    // Fichiers/dossiers qui signent un outil tiers ou un mod (= PAS vierge).
    private static readonly string[] ToolFiles =
        { "d3d8.dll", "d3d8x.dll", "gentool.dll", "d3d8.cfg", "GenToolUpdater.exe",
          "GenLauncher.exe", "modded.exe", "EdgeScroller.exe" };
    private static readonly string[] ToolDirs =
        { ".GenLauncherFolder", "GLM", "launcher", "x64", "x86" };
    // Exes considérés « standard » à la racine d'une install ZH (tout autre = mod/fork).
    private static readonly HashSet<string> StdExes =
        new(StringComparer.OrdinalIgnoreCase) { "generals", "worldbuilder", "edgescroller", "genlauncher", "gentoolupdater", "modded" };

    /// <summary>Liste les indices qu'une install n'est PAS vierge (outils, addons, mods, exe de fork).
    /// Vide = vierge/vanilla. Sert au garde-fou « installer un fork sur une base vraiment vanilla ».</summary>
    public static List<string> NonVanillaItems(string dir)
    {
        var found = new List<string>();
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return found;
        foreach (var f in ToolFiles)
            if (File.Exists(Path.Combine(dir, f))) found.Add(f);
        foreach (var d in ToolDirs)
            if (Directory.Exists(Path.Combine(dir, d))) found.Add(d + "\\");
        try
        {
            // Addons GenPatcher (.big préfixés) + .bak de patch.
            foreach (var big in Directory.EnumerateFiles(dir, "*.big"))
            {
                string bn = Path.GetFileName(big);
                if (Regex.IsMatch(bn, @"^\d+_") || bn.StartsWith("!") || bn.Equals("CustomContentMaps.big", StringComparison.OrdinalIgnoreCase))
                    found.Add(bn);
            }
            foreach (var bak in Directory.EnumerateFiles(dir, "*.bak")) found.Add(Path.GetFileName(bak));
            // Exe non standard à la racine = mod/fork (ex. « RebornOmega 1.01.exe »).
            foreach (var exe in Directory.EnumerateFiles(dir, "*.exe"))
                if (!StdExes.Contains(Path.GetFileNameWithoutExtension(exe)))
                    found.Add(Path.GetFileName(exe) + "  (exe non standard / fork ?)");
        }
        catch { }
        return found;
    }

    /// <summary>Vierge (vanilla) = aucun indice d'outil/mod. Requis pour installer un fork dessus.</summary>
    public static bool IsVanilla(string dir) => NonVanillaItems(dir).Count == 0;

    /// <summary>Moddée = GLM, modded.exe, ou exe de fork présent.</summary>
    public static bool IsModded(string dir)
        => Directory.Exists(Path.Combine(dir, "GLM"))
        || File.Exists(Path.Combine(dir, "modded.exe"))
        || NonVanillaItems(dir).Any(s => s.Contains("fork", StringComparison.OrdinalIgnoreCase));

    /// <summary>Le jeu n'a pas encore été initialisé (jamais lancé) : `Data\INI\INIZH.big` encore présent.
    /// Le 1er lancement (ou GenPatcher) le supprime → présent = fraîchement installé, pas encore lancé.
    /// (Vérifié sur machine réelle : install Steam fraîche = INIZH.big présent + clés EA vides.)</summary>
    public static bool NeedsInit(string dir)
        => !string.IsNullOrWhiteSpace(dir) && File.Exists(Path.Combine(dir, "Data", "INI", "INIZH.big"));

    /// <summary>AppID Steam qui gère ce dossier (via son appmanifest), ou null si hors Steam (copie manuelle / fork).
    /// Sert au « lancer une fois pour initialiser » (steam://run/&lt;appId&gt;).</summary>
    public static string? SteamAppId(string gameDir)
    {
        if (string.IsNullOrWhiteSpace(gameDir)) return null;
        int idx = gameDir.IndexOf(@"\steamapps\common\", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        string steamapps = gameDir.Substring(0, idx) + @"\steamapps";
        try
        {
            foreach (var acf in Directory.EnumerateFiles(steamapps, "appmanifest_*.acf"))
            {
                var m = Regex.Match(File.ReadAllText(acf), "\"installdir\"\\s*\"([^\"]+)\"");
                if (m.Success && string.Equals(m.Groups[1].Value, Path.GetFileName(gameDir), StringComparison.OrdinalIgnoreCase))
                {
                    var a = Regex.Match(Path.GetFileName(acf), @"appmanifest_(\d+)\.acf");
                    if (a.Success) return a.Groups[1].Value;
                }
            }
        }
        catch { }
        return null;
    }

    /// <summary>Le volume du chemin est-il NTFS (requis pour les symlinks de GenLauncher) ?</summary>
    public static bool IsNtfs(string path)
    {
        try { return new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path))!).DriveFormat.Equals("NTFS", StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    /// <summary>Octets libres sur le volume du chemin (-1 si inconnu).</summary>
    public static long FreeSpaceBytes(string path)
    {
        try { return new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path))!).AvailableFreeSpace; }
        catch { return -1; }
    }

    /// <summary>Taille totale d'un dossier (octets), tolérante aux fichiers illisibles.</summary>
    public static long DirSizeBytes(string dir)
    {
        try { return Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Sum(f => { try { return new FileInfo(f).Length; } catch { return 0L; } }); }
        catch { return 0; }
    }

    /// <summary>Copie robuste d'une install vers un dossier neutre (robocopy multi-thread ; `/XJ` exclut les
    /// jonctions/symlinks pour éviter les boucles). Vérifie l'espace AVANT (marge 200 Mo). Source intacte.</summary>
    public static CopyResult CopyInstall(string src, string dest)
    {
        if (string.IsNullOrWhiteSpace(src) || !Directory.Exists(src))
            return new CopyResult(false, 0, "Source introuvable : " + src);
        if (string.Equals(Path.GetFullPath(src).TrimEnd('\\'), Path.GetFullPath(dest).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            return new CopyResult(false, 0, "La source et la destination sont identiques.");

        long need = DirSizeBytes(src);
        long free = FreeSpaceBytes(dest);
        if (free >= 0 && free < need + (200L << 20))
            return new CopyResult(false, 0, $"Espace insuffisant : {free >> 20} Mo libres, ~{need >> 20} Mo requis.");

        try { Directory.CreateDirectory(dest); }
        catch (Exception ex) { return new CopyResult(false, 0, "Création de la destination : " + ex.Message); }

        try
        {
            var psi = new ProcessStartInfo("robocopy",
                $"\"{src.TrimEnd('\\')}\" \"{dest.TrimEnd('\\')}\" /E /COPY:DAT /DCOPY:DAT /R:1 /W:1 /XJ /NP /NFL /NDL /MT:8")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using var p = Process.Start(psi)!;
            p.WaitForExit();
            // robocopy : 0–7 = succès (≥8 = erreur réelle).
            if (p.ExitCode >= 8)
                return new CopyResult(false, DirSizeBytes(dest), $"robocopy a échoué (code {p.ExitCode}).");
            return new CopyResult(true, DirSizeBytes(dest), null);
        }
        catch (Exception ex) { return new CopyResult(false, 0, ex.Message); }
    }

    /// <summary>Déplace une install (ex. le master M1) vers un nouveau dossier. Même volume = renommage
    /// instantané ; volume différent = robocopy /MOVE (copie puis supprime la source). La destination ne
    /// doit pas déjà exister.</summary>
    public static CopyResult MoveInstall(string src, string dest)
    {
        if (string.IsNullOrWhiteSpace(src) || !Directory.Exists(src))
            return new CopyResult(false, 0, "Source introuvable : " + src);
        if (string.Equals(Path.GetFullPath(src).TrimEnd('\\'), Path.GetFullPath(dest).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            return new CopyResult(true, 0, null);   // déjà au bon endroit
        if (Directory.Exists(dest))
            return new CopyResult(false, 0, "La destination existe déjà : " + dest);

        string? rootSrc = Path.GetPathRoot(Path.GetFullPath(src));
        string? rootDst = Path.GetPathRoot(Path.GetFullPath(dest));
        if (string.Equals(rootSrc, rootDst, StringComparison.OrdinalIgnoreCase))
        {
            // Même volume : renommage atomique, instantané, pas de besoin d'espace.
            try { Directory.Move(src, dest); return new CopyResult(true, DirSizeBytes(dest), null); }
            catch (Exception ex) { return new CopyResult(false, 0, ex.Message); }
        }

        // Volume différent : robocopy /MOVE (copie + supprime la source). Vérifie l'espace AVANT.
        long need = DirSizeBytes(src);
        long free = FreeSpaceBytes(dest);
        if (free >= 0 && free < need + (200L << 20))
            return new CopyResult(false, 0, $"Espace insuffisant : {free >> 20} Mo libres, ~{need >> 20} Mo requis.");
        try { Directory.CreateDirectory(dest); }
        catch (Exception ex) { return new CopyResult(false, 0, "Création de la destination : " + ex.Message); }
        try
        {
            var psi = new ProcessStartInfo("robocopy",
                $"\"{src.TrimEnd('\\')}\" \"{dest.TrimEnd('\\')}\" /E /MOVE /COPY:DAT /DCOPY:DAT /R:1 /W:1 /XJ /NP /NFL /NDL /MT:8")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using var p = Process.Start(psi)!;
            p.WaitForExit();
            if (p.ExitCode >= 8)
                return new CopyResult(false, DirSizeBytes(dest), $"robocopy a échoué (code {p.ExitCode}).");
            return new CopyResult(true, DirSizeBytes(dest), null);
        }
        catch (Exception ex) { return new CopyResult(false, 0, ex.Message); }
    }
}
