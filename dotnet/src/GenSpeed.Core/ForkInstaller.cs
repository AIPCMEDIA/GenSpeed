using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace GenSpeed.Core;

/// <summary>Un asset d'une release GitHub (nom + URL de téléchargement direct + taille).</summary>
public sealed record ForkAsset(string Name, string Url, long Size);

/// <summary>Une release GitHub d'un fork (tag + assets).</summary>
public sealed record ForkRelease(string Tag, string Name, IReadOnlyList<ForkAsset> Assets)
{
    public IEnumerable<ForkAsset> Exes => Assets.Where(a => a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
    public IEnumerable<ForkAsset> Zips => Assets.Where(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
    public IEnumerable<ForkAsset> Rars => Assets.Where(a => a.Name.EndsWith(".rar", StringComparison.OrdinalIgnoreCase)
                                                         || a.Name.EndsWith(".7z", StringComparison.OrdinalIgnoreCase));
}

/// <summary>Progression d'une install de fork (phase humaine + % global 0–100 + détail).</summary>
public sealed record ForkProgress(string Phase, int Percent, string Detail = "");

/// <summary>Résultat final d'une install de fork.</summary>
public sealed record ForkInstallResult(bool Ok, string? InstallDir, string? PrimaryExe, string? Error);

/// <summary>Installe un fork autonome (Reborn Omega, Generals X…) sur une COPIE de M0 vierge, en gardant M0
/// intact à 100 % :
///   1. COPIE RÉELLE de M0 → dossier du fork (robocopy, identique à l'install M1 déjà éprouvée). M0 jamais touché.
///   2. Téléchargement de l'archive de données de la dernière release GitHub.
///   3. Extraction native (.zip).
///   4. OVERLAY « supprimer-puis-écrire » : chaque fichier du fork remplace celui de la copie en SUPPRIMANT d'abord
///      la cible (jamais d'écriture en place). Inoffensif en copie réelle ; et déjà correct si un jour on partage
///      la base par liens physiques (supprimer un lien ne touche pas M0).
///   5. Pose des .exe du fork à la racine.
/// Voir [[install-assistant-design]] et <see cref="ForkCatalog"/>.</summary>
public static class ForkInstaller
{
    private static HttpClient NewHttp(TimeSpan timeout)
    {
        var h = new HttpClient { Timeout = timeout };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("GenSpeed");
        return h;
    }

    // ===== 1. Lire la dernière release GitHub =====

    /// <summary>Lit la dernière release publiée d'un dépôt « owner/name ». Tente /releases/latest (exclut les
    /// pré-releases) puis retombe sur la 1re de /releases. null si réseau/dépôt introuvable.</summary>
    public static async Task<ForkRelease?> FetchLatestReleaseAsync(string repo)
    {
        repo = repo?.Trim().Trim('/') ?? "";
        if (repo.Length == 0 || !Regex.IsMatch(repo, @"^[\w.-]+/[\w.-]+$")) return null;
        using var http = NewHttp(TimeSpan.FromSeconds(15));
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

        var r = await TryReleaseAsync(http, $"https://api.github.com/repos/{repo}/releases/latest");
        if (r != null) return r;

        // Repli : pas de release « latest » (que des pré-releases) → on prend la première de la liste.
        try
        {
            string json = await http.GetStringAsync($"https://api.github.com/repos/{repo}/releases");
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
                return ParseRelease(doc.RootElement[0]);
        }
        catch { }
        return null;
    }

    private static async Task<ForkRelease?> TryReleaseAsync(HttpClient http, string url)
    {
        try
        {
            using var resp = await http.GetAsync(url);
            if (!resp.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            return ParseRelease(doc.RootElement);
        }
        catch { return null; }
    }

    private static ForkRelease ParseRelease(JsonElement el)
    {
        string tag = el.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
        string name = el.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
        var assets = new List<ForkAsset>();
        if (el.TryGetProperty("assets", out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var a in arr.EnumerateArray())
            {
                string an = a.TryGetProperty("name", out var anv) ? anv.GetString() ?? "" : "";
                string au = a.TryGetProperty("browser_download_url", out var auv) ? auv.GetString() ?? "" : "";
                long sz = a.TryGetProperty("size", out var asz) && asz.TryGetInt64(out var s) ? s : 0;
                if (an.Length > 0 && au.Length > 0) assets.Add(new ForkAsset(an, au, sz));
            }
        return new ForkRelease(tag, name, assets);
    }

    /// <summary>Sélectionne l'archive de DONNÉES (.zip) à extraire : parmi les assets correspondant au regex du
    /// fork, la PLUS GROSSE (les données pèsent ~1 Go vs un éventuel petit zip de patch). null si aucune .zip.</summary>
    public static ForkAsset? PickDataAsset(ForkRelease rel, string regex)
    {
        Regex? rx = null;
        try { if (!string.IsNullOrWhiteSpace(regex)) rx = new Regex(regex, RegexOptions.IgnoreCase); } catch { }
        var zips = rel.Zips.Where(a => rx == null || rx.IsMatch(a.Name)).ToList();
        if (zips.Count == 0) zips = rel.Zips.ToList();           // regex trop strict → toute .zip
        return zips.OrderByDescending(a => a.Size).FirstOrDefault();
    }

    // ===== 2. Télécharger un asset (avec progression) =====

    /// <summary>Télécharge <paramref name="url"/> vers <paramref name="dest"/> en streaming, en signalant
    /// (octets reçus, total) — total = -1 si inconnu. Pas de timeout sur le corps (gros fichiers ~1 Go).</summary>
    public static async Task<CopyResult> DownloadAssetAsync(string url, string dest,
        Action<long, long>? onProgress = null, CancellationToken ct = default)
    {
        try
        {
            using var http = NewHttp(TimeSpan.FromMinutes(30));
            using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();
            long total = resp.Content.Headers.ContentLength ?? -1;
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            await using var src = await resp.Content.ReadAsStreamAsync(ct);
            await using var fs = File.Create(dest);
            var buf = new byte[1 << 20];
            long done = 0; int read;
            while ((read = await src.ReadAsync(buf, ct)) > 0)
            {
                await fs.WriteAsync(buf.AsMemory(0, read), ct);
                done += read;
                onProgress?.Invoke(done, total);
            }
            return new CopyResult(true, done, null);
        }
        catch (Exception ex) { return new CopyResult(false, 0, ex.Message); }
    }

    // ===== 3. Extraire (.zip natif) =====

    /// <summary>Extrait un .zip dans un dossier neuf. Renvoie le dossier d'extraction ou une erreur.</summary>
    public static (bool Ok, string? Dir, string? Error) ExtractZip(string zipPath, string extractDir)
    {
        try
        {
            Directory.CreateDirectory(extractDir);
            ZipFile.ExtractToDirectory(zipPath, extractDir, overwriteFiles: true);
            return (true, extractDir, null);
        }
        catch (Exception ex) { return (false, null, ex.Message); }
    }

    /// <summary>Trouve la « racine d'overlay » à poser sur le dossier de jeu, à partir d'un dossier extrait.
    /// RÈGLE PRINCIPALE : la racine d'install = LE DOSSIER OÙ SE TROUVE L'EXE DU JEU (tout est relatif à l'exe).
    /// On copie alors ce dossier TEL QUEL (les sous-dossiers du mod, ex. « RebornOmegaData\ » avec ses .pak,
    /// restent des sous-dossiers — comme une install manuelle « copie les fichiers dans ZH »).
    /// Replis : si aucun exe (ex. archive de DONNÉES seule, exe livré à part), on descend les wrappers à enfant
    /// unique, puis on choisit le sous-dossier le plus « jeu » (max .big/.gib, bonus Data\INI).</summary>
    public static string FindOverlayRoot(string extractedDir)
    {
        // 1) Le dossier le moins profond contenant un exe de jeu → racine d'install (copie telle quelle).
        string? exeRoot = ShallowestWithGameExe(extractedDir);
        if (exeRoot != null) return exeRoot;

        // 2) Pas d'exe : descendre un wrapper à enfant unique SEULEMENT si ce child est une racine de jeu à
        //    APLATIR (.big / Data\INI). On NE descend PAS dans un dossier « payload » à PRÉSERVER comme
        //    sous-dossier (ex. « RebornOmegaData\ » avec ses .pak : le jeu charge RebornOmegaData\*.pak par nom).
        string cur = extractedDir;
        for (int guard = 0; guard < 16; guard++)
        {
            if (IsGameLayoutRoot(cur)) return cur;
            string[] dirs, files;
            try { dirs = Directory.GetDirectories(cur); files = Directory.GetFiles(cur); }
            catch { return cur; }
            if (files.Length == 0 && dirs.Length == 1 && IsGameLayoutRoot(dirs[0])) { cur = dirs[0]; continue; }
            break;
        }
        if (IsGameLayoutRoot(cur)) return cur;

        // 3) Plusieurs dossiers, pas de .big à ce niveau : le sous-dossier le plus « jeu ».
        string best = cur; int bestScore = GameScore(cur);
        try
        {
            foreach (var d in Directory.EnumerateDirectories(cur, "*", SearchOption.AllDirectories).Take(2000))
            {
                int s = GameScore(d);
                if (s > bestScore) { bestScore = s; best = d; }
            }
        }
        catch { }
        return best;
    }

    // Marqueurs d'exe « outil » (pas l'exe de jeu) : ils ne définissent pas la racine d'install.
    private static readonly string[] ToolExeMarkers =
        { "worldbuilder", "genlauncher", "gentool", "edgescroller", "unins", "vcredist", "dxsetup", "redist", "setup" };

    private static bool IsGameExe(string file)
    {
        string n = Path.GetFileName(file).ToLowerInvariant();
        if (!n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return false;
        return !ToolExeMarkers.Any(m => n.Contains(m));
    }

    private static bool HasGameExe(string dir)
    {
        try { return Directory.EnumerateFiles(dir, "*.exe").Any(IsGameExe); } catch { return false; }
    }

    private static bool HasBig(string dir)
    {
        try
        {
            return Directory.EnumerateFiles(dir).Any(f => f.EndsWith(".big", StringComparison.OrdinalIgnoreCase)
                                                       || f.EndsWith(".gib", StringComparison.OrdinalIgnoreCase));
        }
        catch { return false; }
    }

    /// <summary>Dossier qui EST une racine de jeu à aplatir (ses fichiers vont directement dans le dossier ZH) :
    /// présence de .big/.gib, d'un Data\INI, ou d'un exe de jeu. À l'inverse, un dossier « payload » (RebornOmegaData
    /// avec ses .pak) n'en est pas un → on le garde comme sous-dossier.</summary>
    private static bool IsGameLayoutRoot(string dir)
        => HasBig(dir) || HasGameExe(dir) || Directory.Exists(Path.Combine(dir, "Data", "INI"));

    /// <summary>Dossier le MOINS profond (parcours en largeur) contenant un exe de jeu, à partir de
    /// <paramref name="start"/>. null si aucun.</summary>
    private static string? ShallowestWithGameExe(string start)
    {
        var q = new Queue<string>();
        q.Enqueue(start);
        int guard = 0;
        while (q.Count > 0 && guard++ < 5000)
        {
            string d = q.Dequeue();
            if (HasGameExe(d)) return d;
            try { foreach (var sub in Directory.GetDirectories(d)) q.Enqueue(sub); } catch { }
        }
        return null;
    }

    /// <summary>Note « ressemblance dossier de jeu » : nb d'archives .big/.gib (+ bonus Data\INI). Pour le repli
    /// sur une archive de DONNÉES sans exe.</summary>
    private static int GameScore(string dir)
    {
        try
        {
            int big = Directory.EnumerateFiles(dir).Count(f => f.EndsWith(".big", StringComparison.OrdinalIgnoreCase)
                                                            || f.EndsWith(".gib", StringComparison.OrdinalIgnoreCase));
            return big + (Directory.Exists(Path.Combine(dir, "Data", "INI")) ? 5 : 0);
        }
        catch { return 0; }
    }

    // ===== 4. Overlay « supprimer-puis-écrire » (garde M0 intact) =====

    /// <summary>Pose tous les fichiers de <paramref name="srcRoot"/> par-dessus <paramref name="destDir"/> en
    /// SUPPRIMANT d'abord chaque cible existante (jamais d'écriture en place → si la cible était un lien vers M0,
    /// on casse le lien sans toucher M0). Renvoie (ok, nb fichiers, erreur).</summary>
    public static (bool Ok, int Files, string? Error) OverlayFromFolder(string srcRoot, string destDir,
        Action<int, int>? onProgress = null)
    {
        try
        {
            if (!Directory.Exists(srcRoot)) return (false, 0, "Source d'overlay introuvable : " + srcRoot);
            var all = Directory.GetFiles(srcRoot, "*", SearchOption.AllDirectories);
            int n = 0;
            foreach (var src in all)
            {
                string rel = Path.GetRelativePath(srcRoot, src);
                string dst = Path.Combine(destDir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                DeleteIfExists(dst);                       // casse l'éventuel lien, M0 préservé
                File.Copy(src, dst, overwrite: false);
                onProgress?.Invoke(++n, all.Length);
            }
            return (true, n, null);
        }
        catch (Exception ex) { return (false, 0, ex.Message); }
    }

    /// <summary>Supprime un fichier même en lecture seule (retire l'attribut d'abord).</summary>
    private static void DeleteIfExists(string path)
    {
        if (!File.Exists(path)) return;
        try
        {
            var attr = File.GetAttributes(path);
            if (attr.HasFlag(FileAttributes.ReadOnly))
                File.SetAttributes(path, attr & ~FileAttributes.ReadOnly);
        }
        catch { }
        File.Delete(path);
    }

    // ===== 5. Orchestration complète =====

    /// <summary>Installe un fork de bout en bout sur une COPIE de M0. Étapes : copie base → DL données →
    /// extraction → overlay → pose des exes. <paramref name="release"/> est déjà récupérée (UI l'a affichée).
    /// <paramref name="tmpDir"/> = dossier de travail (téléchargement+extraction), nettoyé en fin.</summary>
    public static async Task<ForkInstallResult> InstallAsync(
        string m0Dir, string destDir, ForkDef fork, ForkRelease release, string tmpDir,
        IProgress<ForkProgress>? progress = null, CancellationToken ct = default)
    {
        try
        {
            // --- 1. Copie réelle de M0 (M0 intact). ~0–35 % ---
            progress?.Report(new ForkProgress("Copie de la base ZH (M0 → copie du fork)", 2, "robocopy…"));
            var copy = await Task.Run(() => InstallManager.CopyInstall(m0Dir, destDir), ct);
            if (!copy.Ok) return new(false, null, null, "Copie de la base : " + copy.Error);

            // --- 2. Données : choisir + télécharger. 35–80 % ---
            var data = PickDataAsset(release, fork.DataAssetRegex);
            string? extractRoot = null;
            if (data != null)
            {
                Directory.CreateDirectory(tmpDir);
                string zip = Path.Combine(tmpDir, data.Name);
                progress?.Report(new ForkProgress($"Téléchargement des données ({data.Size >> 20} Mo)", 35, data.Name));
                var dl = await DownloadAssetAsync(data.Url, zip, (d, t) =>
                {
                    int pct = t > 0 ? 35 + (int)(d * 45 / t) : 35;
                    progress?.Report(new ForkProgress("Téléchargement des données", pct, $"{d >> 20}/{(t > 0 ? t >> 20 : 0)} Mo"));
                }, ct);
                if (!dl.Ok) return new(false, null, null, "Téléchargement des données : " + dl.Error);

                // --- 3. Extraction. 80–85 % ---
                progress?.Report(new ForkProgress("Extraction des données", 80, data.Name));
                var ex = ExtractZip(zip, Path.Combine(tmpDir, "extract"));
                if (!ex.Ok) return new(false, null, null, "Extraction : " + ex.Error);
                extractRoot = FindOverlayRoot(ex.Dir!);
            }

            // --- 4. Overlay des données sur la copie. 85–92 % ---
            if (extractRoot != null)
            {
                progress?.Report(new ForkProgress("Application des fichiers du fork", 85));
                var ov = OverlayFromFolder(extractRoot, destDir, (d, t) =>
                {
                    int pct = t > 0 ? 85 + (int)(d * 7L / t) : 85;
                    progress?.Report(new ForkProgress("Application des fichiers du fork", pct, $"{d}/{t}"));
                });
                if (!ov.Ok) return new(false, null, null, "Overlay : " + ov.Error);
            }

            // --- 5. Pose des exes du fork (assets de la release) + choix de l'exe primaire. 92–100 % ---
            // L'exe primaire est l'exe DU FORK (asset de la release), JAMAIS un exe résiduel de la copie M0
            // (le generals.exe d'origine est encore là). On privilégie donc les assets ; on ne retombe sur un
            // scan du dossier que si la release n'apporte aucun exe (exe inclus dans l'archive de données).
            string? primaryExe = null; long primarySize = -1;
            var exeAssets = release.Exes.ToList();
            for (int i = 0; i < exeAssets.Count; i++)
            {
                var a = exeAssets[i];
                progress?.Report(new ForkProgress("Pose de l'exécutable du fork", 92 + i * 8 / Math.Max(1, exeAssets.Count), a.Name));
                string exeDst = Path.Combine(destDir, a.Name);
                var dl = await DownloadAssetAsync(a.Url, exeDst + ".tmp", null, ct);
                if (!dl.Ok) continue;                                  // best-effort : exe optionnel si data l'inclut déjà
                DeleteIfExists(exeDst);
                File.Move(exeDst + ".tmp", exeDst, overwrite: true);
                // WorldBuilder (éditeur de cartes) n'est PAS l'exe de jeu : ne jamais le retenir comme primaire,
                // même si sa taille égale celle du jeu (cas Reborn Omega : les deux font 6,5 Mo).
                bool isWb = a.Name.Contains("worldbuilder", StringComparison.OrdinalIgnoreCase);
                if (!isWb && a.Size > primarySize) { primarySize = a.Size; primaryExe = exeDst; }
            }
            // Repli : aucun exe livré en asset → l'exe du fork est dans l'archive de données.
            primaryExe ??= PickPrimaryExeInDir(destDir);

            // Nettoyage du dossier de travail.
            try { Directory.Delete(tmpDir, recursive: true); } catch { }

            progress?.Report(new ForkProgress("Terminé", 100));
            return new(true, destDir, primaryExe, null);
        }
        catch (OperationCanceledException) { return new(false, null, null, "Annulé."); }
        catch (Exception ex) { return new(false, null, null, ex.Message); }
    }

    /// <summary>Installe un fork depuis une SOURCE LOCALE (dossier déjà extrait, ou archive .zip/.rar/.7z) au lieu
    /// d'une release GitHub. Indispensable pour les forks dont les données ne sont pas sur GitHub (ex. Reborn Omega
    /// v1.01 : exe sur GitHub mais données dans un .rar à part). Copie M0 → extrait si besoin → overlay → exe primaire.</summary>
    public static async Task<ForkInstallResult> InstallFromLocalAsync(
        string m0Dir, string destDir, ForkDef fork, string localSource, string tmpDir,
        IProgress<ForkProgress>? progress = null, CancellationToken ct = default)
    {
        try
        {
            progress?.Report(new ForkProgress("Copie de la base ZH (M0 → copie du fork)", 2, "robocopy…"));
            var copy = await Task.Run(() => InstallManager.CopyInstall(m0Dir, destDir), ct);
            if (!copy.Ok) return new(false, null, null, "Copie de la base : " + copy.Error);

            string overlayRoot;
            if (Directory.Exists(localSource))
            {
                progress?.Report(new ForkProgress("Lecture du dossier source", 45));
                overlayRoot = FindOverlayRoot(localSource);
            }
            else if (File.Exists(localSource))
            {
                Directory.CreateDirectory(tmpDir);
                progress?.Report(new ForkProgress("Extraction de l'archive", 45, Path.GetFileName(localSource)));
                var ex = await Task.Run(() => ExtractArchive(localSource, Path.Combine(tmpDir, "extract")), ct);
                if (!ex.Ok) return new(false, null, null, "Extraction : " + ex.Error);
                overlayRoot = FindOverlayRoot(ex.Dir!);
            }
            else return new(false, null, null, "Source introuvable : " + localSource);

            progress?.Report(new ForkProgress("Application des fichiers du fork", 80));
            var ov = OverlayFromFolder(overlayRoot, destDir, (d, t) =>
            {
                int pct = t > 0 ? 80 + (int)(d * 18L / t) : 80;
                progress?.Report(new ForkProgress("Application des fichiers du fork", pct, $"{d}/{t}"));
            });
            if (!ov.Ok) return new(false, null, null, "Overlay : " + ov.Error);

            string? primaryExe = PickPrimaryExeInDir(destDir);
            try { Directory.Delete(tmpDir, recursive: true); } catch { }
            progress?.Report(new ForkProgress("Terminé", 100));
            return new(true, destDir, primaryExe, null);
        }
        catch (OperationCanceledException) { return new(false, null, null, "Annulé."); }
        catch (Exception ex) { return new(false, null, null, ex.Message); }
    }

    /// <summary>Exe « de jeu » primaire d'un dossier : plus gros .exe non-outil, en évitant le generals.exe résiduel
    /// de la copie M0 s'il existe un autre exe (= l'exe du fork). WorldBuilder et outils exclus.</summary>
    public static string? PickPrimaryExeInDir(string destDir)
    {
        try
        {
            var exes = Directory.EnumerateFiles(destDir, "*.exe")
                .Where(e =>
                {
                    string n = Path.GetFileName(e).ToLowerInvariant();
                    return !n.Contains("worldbuilder")
                        && n is not ("genlauncher.exe" or "gentoolupdater.exe" or "edgescroller.exe");
                })
                .ToList();
            var nonVanilla = exes.Where(e => !Path.GetFileName(e).Equals("generals.exe", StringComparison.OrdinalIgnoreCase)).ToList();
            return (nonVanilla.Count > 0 ? nonVanilla : exes)
                .OrderByDescending(e => { try { return new FileInfo(e).Length; } catch { return 0L; } })
                .FirstOrDefault();
        }
        catch { return null; }
    }

    /// <summary>Extrait une archive : .zip en natif, .rar/.7z via 7-Zip (s'il est installé). Renvoie le dossier
    /// d'extraction ou une erreur (avec un message clair si 7-Zip manque pour un .rar).</summary>
    public static (bool Ok, string? Dir, string? Error) ExtractArchive(string archivePath, string extractDir)
    {
        string ext = Path.GetExtension(archivePath).ToLowerInvariant();
        if (ext == ".zip") return ExtractZip(archivePath, extractDir);
        if (ext is ".rar" or ".7z")
        {
            string? sz = SevenZipPath();
            if (sz == null) return (false, null, "7-Zip est requis pour un .rar/.7z. Installe 7-Zip, ou extrais l'archive à la main puis choisis le dossier obtenu.");
            try
            {
                Directory.CreateDirectory(extractDir);
                var psi = new ProcessStartInfo(sz, $"x \"{archivePath}\" -o\"{extractDir}\" -y -bso0 -bsp0")
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                using var p = Process.Start(psi)!;
                p.WaitForExit();
                if (p.ExitCode != 0) return (false, null, $"7-Zip a échoué (code {p.ExitCode}).");
                return (true, extractDir, null);
            }
            catch (Exception ex) { return (false, null, ex.Message); }
        }
        return (false, null, "Format d'archive non géré : " + ext);
    }

    /// <summary>Chemin de 7z.exe (Program Files puis registre), ou null si 7-Zip n'est pas installé.</summary>
    public static string? SevenZipPath()
    {
        foreach (var c in new[] { @"C:\Program Files\7-Zip\7z.exe", @"C:\Program Files (x86)\7-Zip\7z.exe" })
            if (File.Exists(c)) return c;
        try
        {
            foreach (var key in new[] { @"HKEY_LOCAL_MACHINE\SOFTWARE\7-Zip", @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\7-Zip" })
                if (Registry.GetValue(key, "Path", null) is string p)
                { string e = Path.Combine(p, "7z.exe"); if (File.Exists(e)) return e; }
        }
        catch { }
        return null;
    }
}
