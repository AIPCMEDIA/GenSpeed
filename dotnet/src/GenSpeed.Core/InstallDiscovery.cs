using Microsoft.Win32;

namespace GenSpeed.Core;

/// <summary>Découverte AUTOMATIQUE de toutes les installations ZH-like de la machine.
/// Remplace le concept d'« install active » : plus d'état caché, plus d'angle mort
/// (cas réel : un « Reborn Omega 1.00 » jamais ajouté manuellement restait invisible).</summary>
public static class InstallDiscovery
{
    /// <summary>Toutes les installs : bibliothèques Steam + registre EA + ajouts manuels (purgés des
    /// chemins morts). Dédup insensible à la casse, ordre stable (base Steam d'abord, puis le reste trié).</summary>
    public static List<string> DiscoverAll(IEnumerable<string>? known = null)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var steamBase = new List<string>();   // dossiers ZH "officiels" Steam (noms standard)
        var others = new List<string>();

        void Add(string? dir, bool preferred = false)
        {
            if (string.IsNullOrWhiteSpace(dir)) return;
            string norm = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dir));
            if (!Directory.Exists(norm) || !GameLocator.IsZhFolder(norm)) return;
            if (!seen.Add(norm)) return;
            (preferred ? steamBase : others).Add(norm);
        }

        // 1) Toutes les bibliothèques Steam : chaque dossier de steamapps\common qui ressemble à une install.
        foreach (var lib in GameLocator.SteamLibraries())
        {
            string common = Path.Combine(lib, "steamapps", "common");
            if (!Directory.Exists(common)) continue;
            IEnumerable<string> dirs;
            try { dirs = Directory.EnumerateDirectories(common); }
            catch { continue; }
            foreach (var d in dirs)
            {
                string name = Path.GetFileName(d);
                bool official = name.Contains("Conquer", StringComparison.OrdinalIgnoreCase)
                             || name.Contains("Generals", StringComparison.OrdinalIgnoreCase)
                             || name.Contains("Zero Hour", StringComparison.OrdinalIgnoreCase);
                // Les dossiers sans rapport avec C&C ne sont même pas testés (perf + zéro faux positif).
                bool related = official
                            || name.Contains("Reborn", StringComparison.OrdinalIgnoreCase)
                            || name.Contains("Omega", StringComparison.OrdinalIgnoreCase)
                            || name.Contains("Contra", StringComparison.OrdinalIgnoreCase)
                            || name.Contains("Shockwave", StringComparison.OrdinalIgnoreCase)
                            || HasGameExe(d);
                if (related) Add(d, preferred: official);
            }
        }

        // 2) Registre EA (installpath) — couvre les installs EA App / retail.
        if (OperatingSystem.IsWindows())
            foreach (var key in new[]
            {
                @"SOFTWARE\WOW6432Node\Electronic Arts\EA Games\Command and Conquer Generals Zero Hour",
                @"SOFTWARE\Electronic Arts\EA Games\Command and Conquer Generals Zero Hour",
                @"SOFTWARE\WOW6432Node\Electronic Arts\EA Games\Generals",
            })
                try
                {
                    using var k = Registry.LocalMachine.OpenSubKey(key);
                    if (k?.GetValue("installpath") is string p) Add(p);
                    if (k?.GetValue("InstallPath") is string p2) Add(p2);
                }
                catch { }

        // 3) Installs ajoutées manuellement (hors Steam / hors registre).
        if (known != null)
            foreach (var p in known) Add(p);

        others.Sort(StringComparer.OrdinalIgnoreCase);
        steamBase.Sort(StringComparer.OrdinalIgnoreCase);
        steamBase.AddRange(others);
        return steamBase;
    }

    /// <summary>Vrai si le dossier contient un exe du jeu (generals.exe / GeneralsZH.exe / game.dat).</summary>
    private static bool HasGameExe(string dir)
    {
        foreach (var n in new[] { "generals.exe", "GeneralsZH.exe", "Game.dat", "game.dat" })
            if (File.Exists(Path.Combine(dir, n))) return true;
        return false;
    }
}
