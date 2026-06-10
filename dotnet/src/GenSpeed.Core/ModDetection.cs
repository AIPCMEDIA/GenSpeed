using System.Buffers.Binary;
using System.Text;

namespace GenSpeed.Core;

public enum TargetType { Big, Ini, Gib }

/// <summary>Une cible patchable : Vanilla, overrides Data/INI, ou un mod (.gib).</summary>
public sealed class Target
{
    public required string Label { get; init; }
    public required TargetType Type { get; init; }
    public required List<string> Files { get; init; }
    /// <summary>Dossier d'installation auquel appartient la cible (multi-installs : plus d'« install active »).</summary>
    public string InstallDir { get; init; } = "";

    /// <summary>Nb d'archives (= nb de fichiers).</summary>
    public int ArchiveCount => Files.Count;

    /// <summary>Nb de .ini : pour un mod (.gib) = somme des entrées .ini ; sinon = nb de fichiers.</summary>
    public int IniCount()
    {
        if (Type != TargetType.Gib)
            return Files.Count;
        int n = 0;
        foreach (var fp in Files)
            foreach (var nm in ModDetection.ArchiveNames(fp))
                if (nm.EndsWith(".ini", StringComparison.OrdinalIgnoreCase)) n++;
        return n;
    }
}

/// <summary>Détection des cibles (port fidèle de core.ModDetector / detect_targets).</summary>
public static class ModDetection
{
    private static readonly Encoding Latin1 = Encoding.Latin1;

    /// <summary>Noms des entrées d'une archive BIGF (lecture de la table seule, rapide).</summary>
    public static List<string> ArchiveNames(string path)
    {
        var names = new List<string>();
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                                          bufferSize: 64 * 1024);
            Span<byte> head = stackalloc byte[16];
            if (fs.Read(head) < 16) return names;
            if (!(head[0] == (byte)'B' && head[1] == (byte)'I' && head[2] == (byte)'G' && head[3] == (byte)'F'))
                return names;
            uint num = BinaryPrimitives.ReadUInt32BigEndian(head.Slice(8, 4));
            if (num > 100000) return names;

            var sb = new List<byte>(64);
            Span<byte> entry = stackalloc byte[8];
            for (uint i = 0; i < num; i++)
            {
                if (fs.Read(entry) < 8) break;
                sb.Clear();
                int b;
                while ((b = fs.ReadByte()) > 0) sb.Add((byte)b);
                if (b < 0) break;
                names.Add(Latin1.GetString(sb.ToArray()));
            }
        }
        catch { /* archive illisible → aucun nom */ }
        return names;
    }

    public static bool ArchiveHasIni(string path)
    {
        foreach (var n in ArchiveNames(path))
            if (n.EndsWith(".ini", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>Archives racine (.big/.gib contenant des .ini) + l'exe — base LAN. Tri ordinal.</summary>
    public static List<string> BaseInstallFiles(string gameDir)
    {
        var outp = new List<string>();
        try
        {
            foreach (var fp in Directory.EnumerateFiles(gameDir))
            {
                string low = Path.GetFileName(fp).ToLowerInvariant();
                if ((low.EndsWith(".big") || low.EndsWith(".gib")) && ArchiveHasIni(fp))
                    outp.Add(fp);
                else if (low is "generals.exe" or "generalszh.exe")
                    outp.Add(fp);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        outp.Sort(StringComparer.Ordinal);
        return outp;
    }

    private static IEnumerable<string> GibsIn(string dir)
    {
        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(dir); }
        catch { yield break; }
        foreach (var f in files)
            if (f.EndsWith(".gib", StringComparison.OrdinalIgnoreCase))
                yield return f;
    }

    /// <summary>Toutes les archives de STATS d'un mod (hors Addons). Tri ordinal.</summary>
    public static List<string> CollectStatArchives(string modPath)
    {
        var outp = new List<string>();
        foreach (var root in EnumerateDirsInclusive(modPath))
        {
            string rel = Path.GetRelativePath(modPath, root);
            bool inAddons = rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                               .Any(p => p.Equals("Addons", StringComparison.OrdinalIgnoreCase));
            if (inAddons) continue;
            foreach (var g in GibsIn(root))
                if (ArchiveHasIni(g)) outp.Add(g);
        }
        outp.Sort(StringComparer.Ordinal);
        return outp;
    }

    private static IEnumerable<string> EnumerateDirsInclusive(string root)
    {
        yield return root;
        IEnumerable<string> subs;
        try { subs = Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories); }
        catch { yield break; }
        foreach (var d in subs) yield return d;
    }

    /// <summary>Détecte toutes les cibles : Vanilla (.big), Data/INI, et les mods GLM (.gib).</summary>
    public static List<Target> DetectTargets(string gameDir)
    {
        var targets = new List<Target>();

        var bigFiles = BaseInstallFiles(gameDir)
            .Where(f => f.EndsWith(".big", StringComparison.OrdinalIgnoreCase)
                     || f.EndsWith(".gib", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (bigFiles.Count > 0)
            targets.Add(new Target { Label = "🎮 Vanilla", Type = TargetType.Big, Files = bigFiles, InstallDir = gameDir });

        string iniDir = Path.Combine(gameDir, "Data", "INI");
        if (Directory.Exists(iniDir))
        {
            try
            {
                // Récursif : certains mods (ex. Reborn Omega) rangent leurs unités dans
                // des sous-dossiers (Data\INI\Object\, Default\…). Cohérent avec CollectLooseIni.
                var inis = Directory.EnumerateFiles(iniDir, "*", SearchOption.AllDirectories)
                    .Where(f => f.EndsWith(".ini", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(p => p, StringComparer.Ordinal).ToList();
                if (inis.Count > 0)
                    targets.Add(new Target { Label = "VANILLA (Data/INI)", Type = TargetType.Ini, Files = inis, InstallDir = gameDir });
            }
            catch { }
        }

        targets.AddRange(DetectGlmMods(Path.Combine(gameDir, "GLM")));

        return targets;
    }

    /// <summary>Détecte les mods d'un dossier GLM donné (GenLauncher pouvant être installé ailleurs). Tri ordinal.</summary>
    public static List<Target> DetectGlmMods(string glmDir)
    {
        var targets = new List<Target>();
        if (string.IsNullOrEmpty(glmDir) || !Directory.Exists(glmDir)) return targets;
        // L'install propriétaire = le parent du dossier GLM (GLM externe : son parent fait office d'install).
        string installDir = Path.GetDirectoryName(glmDir.TrimEnd('\\', '/')) ?? glmDir;
        try
        {
            foreach (var modPath in Directory.EnumerateDirectories(glmDir)
                         .OrderBy(p => Path.GetFileName(p), StringComparer.Ordinal))
            {
                string mod = Path.GetFileName(modPath);
                if (mod is "Addons" or "Patches" or "Tools") continue;
                var arch = CollectStatArchives(modPath);
                if (arch.Count > 0)
                    targets.Add(new Target { Label = mod, Type = TargetType.Gib, Files = arch, InstallDir = installDir });
            }
        }
        catch { }
        return targets;
    }

    /// <summary>À partir d'un dossier choisi par l'utilisateur, localise le dossier GLM des mods (ou null).</summary>
    public static string? ResolveGlmDir(string picked)
    {
        if (string.IsNullOrEmpty(picked) || !Directory.Exists(picked)) return null;
        // 1) le dossier choisi EST le dossier GLM
        if (Path.GetFileName(picked.TrimEnd('\\', '/')).Equals("GLM", StringComparison.OrdinalIgnoreCase))
            return picked;
        // 2) il contient un sous-dossier GLM
        string sub = Path.Combine(picked, "GLM");
        if (Directory.Exists(sub)) return sub;
        // 3) il contient directement des mods (sous-dossiers avec des .gib)
        try
        {
            foreach (var d in Directory.EnumerateDirectories(picked))
                if (Directory.EnumerateFiles(d, "*.gib", SearchOption.AllDirectories).Any())
                    return picked;
        }
        catch { }
        return null;
    }
}
