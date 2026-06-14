using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace GenSpeed.Core;

/// <summary>Résultat de la pose de GenLauncher (extraction de l'exe depuis le zip ModDB téléchargé).</summary>
public sealed record GenLauncherResult(bool Ok, string? ExePath, string? Error);

/// <summary>État des prérequis système du jeu (indépendants du dossier — GenPatcher les installerait,
/// mais ils sont SYSTÈME et ne touchent pas M0). Le jeu a besoin de VC++ 2005 + DirectX 9 (d3dx9).</summary>
public sealed record PrereqStatus(bool VcRedist, bool DirectX9)
{
    public bool AllOk => VcRedist && DirectX9;
}

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

    /// <summary>Nom de dossier standard de l'install M1 (copie de M0 + GenLauncher). Constant → cohérent avec
    /// le raccourci Bureau « GenLauncher » que GenSpeed utilise pour re-découvrir M1.</summary>
    public const string GenLauncherFolderName = "GenLauncher";

    /// <summary>Emplacement PARENT suggéré par défaut pour une nouvelle install : le disque fixe avec le plus
    /// d'espace libre, sous-dossier « Jeux » (ex. « G:\Jeux »). Éditable par l'utilisateur.</summary>
    public static string SuggestInstallParent()
    {
        try
        {
            var best = DriveInfo.GetDrives()
                .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
                .OrderByDescending(d => d.AvailableFreeSpace)
                .FirstOrDefault();
            string root = best?.RootDirectory.FullName ?? @"C:\";
            return Path.Combine(root, "Jeux");
        }
        catch { return @"C:\Jeux"; }
    }

    /// <summary>URL du manifeste public de GenLauncher (catalogue + lien d'installeur, tenu à jour par p0ls3r).</summary>
    public const string GenLauncherManifestUrl =
        "https://raw.githubusercontent.com/p0ls3r/GenLauncherModsData/master/ReposModificationDataZH4.yaml";

    /// <summary>Lit le `DownloadLink` (zip installeur GenLauncher, TOUJOURS à jour) dans le manifeste public
    /// de p0ls3r. Lecture seule d'un petit yaml (pas de téléchargement de binaire). Null si échec/réseau →
    /// l'appelant retombe sur le lien éditable de la config, puis sur la page ModDB.</summary>
    public static async System.Threading.Tasks.Task<string?> FetchGenLauncherDownloadLinkAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("GenSpeed");
            string yaml = await http.GetStringAsync(GenLauncherManifestUrl);
            var m = Regex.Match(yaml, "DownloadLink:\\s*\"?(https?://\\S+?)\"?\\s*(?:\\r|\\n|$)");
            string? url = m.Success ? m.Groups[1].Value.Trim() : null;
            return url is { Length: > 0 } && url.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? url : null;
        }
        catch { return null; }
    }

    /// <summary>Vrai dossier Téléchargements de l'utilisateur (respecte un Downloads DÉPLACÉ, via le registre).
    /// Repli sur `%USERPROFILE%\Downloads`. On ne présume donc pas l'emplacement par défaut.</summary>
    public static string DownloadsFolder()
    {
        if (OperatingSystem.IsWindows())
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders");
                if (k?.GetValue("{374DE290-123F-4565-9164-39C4925E467B}") is string s && s.Length > 0)
                {
                    string p = Environment.ExpandEnvironmentVariables(s);
                    if (Directory.Exists(p)) return p;
                }
            }
            catch { }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }

    /// <summary>Télécharge un fichier (le zip GenLauncher direct gen.insave.ovh) vers <paramref name="destPath"/>.
    /// Réservé aux liens DIRECTS (pas ModDB/Cloudflare). Timeout 120 s.</summary>
    public static async System.Threading.Tasks.Task<CopyResult> DownloadToFileAsync(string url, string destPath)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("GenSpeed");
            using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            resp.EnsureSuccessStatusCode();
            await using (var fs = File.Create(destPath))
                await resp.Content.CopyToAsync(fs);
            return new CopyResult(true, new FileInfo(destPath).Length, null);
        }
        catch (Exception ex) { return new CopyResult(false, 0, ex.Message); }
    }

    /// <summary>Cherche le zip GenLauncher le plus récent dans Téléchargements (vrai dossier) + le Bureau.
    /// Retourne null si rien trouvé → l'appelant propose alors un sélecteur de fichier (repli universel).</summary>
    public static string? FindDownloadedGenLauncherZip()
    {
        try
        {
            var roots = new[] { DownloadsFolder(), Environment.GetFolderPath(Environment.SpecialFolder.Desktop) };
            return roots.Where(Directory.Exists)
                .SelectMany(d => Directory.EnumerateFiles(d, "GenLauncher*.zip"))
                .OrderByDescending(f => { try { return new FileInfo(f).LastWriteTimeUtc; } catch { return DateTime.MinValue; } })
                .FirstOrDefault();
        }
        catch { return null; }
    }

    /// <summary>Extrait `GenLauncher.exe` d'un zip téléchargé et le pose dans le dossier de l'install (la copie).
    /// 100% local (pas de téléchargement web → pas de comportement « downloader » suspect pour l'antivirus).</summary>
    public static GenLauncherResult InstallGenLauncherFromZip(string zipPath, string destDir)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(zipPath) || !File.Exists(zipPath)) return new(false, null, "Zip introuvable : " + zipPath);
            if (string.IsNullOrWhiteSpace(destDir) || !Directory.Exists(destDir)) return new(false, null, "Dossier cible introuvable : " + destDir);
            using var zip = ZipFile.OpenRead(zipPath);
            var entry = zip.Entries.FirstOrDefault(e => e.Name.Equals("GenLauncher.exe", StringComparison.OrdinalIgnoreCase));
            if (entry == null) return new(false, null, "GenLauncher.exe introuvable dans le zip.");
            string exePath = Path.Combine(destDir, "GenLauncher.exe");
            entry.ExtractToFile(exePath, overwrite: true);
            return new(true, exePath, null);
        }
        catch (Exception ex) { return new(false, null, ex.Message); }
    }

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

    /// <summary>Vérifie les prérequis SYSTÈME du jeu (n'altèrent pas M0) : VC++ 2005 (clé de uninstall)
    /// et DirectX 9 hérité (`d3dx9_*.dll` dans SysWOW64). Si absents (Windows neuf), GenSpeed guide leur
    /// installation depuis Microsoft — légal, système, ne touche pas le dossier du jeu.</summary>
    public static PrereqStatus CheckPrereqs()
    {
        bool dx = false, vc = false;
        try
        {
            string sysWow = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SysWOW64");
            dx = Directory.Exists(sysWow) && Directory.EnumerateFiles(sysWow, "d3dx9_*.dll").Any();
        }
        catch { }
        if (OperatingSystem.IsWindows())
            try
            {
                foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
                {
                    using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                    using var unins = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                    if (unins == null) continue;
                    foreach (var sub in unins.GetSubKeyNames())
                    {
                        using var k = unins.OpenSubKey(sub);
                        if (k?.GetValue("DisplayName") is string dn && dn.Contains("Visual C++ 2005", StringComparison.OrdinalIgnoreCase))
                        { vc = true; break; }
                    }
                    if (vc) break;
                }
            }
            catch { }
        return new PrereqStatus(vc, dx);
    }

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
