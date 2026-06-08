using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace GenSpeed.Core;

/// <summary>Localisation automatique de l'installation Zero Hour (port de config.detect_zh_install).</summary>
public static class GameLocator
{
    private static readonly string[] ZhFolderNames =
    {
        "Command & Conquer Generals - Zero Hour",
        "Command and Conquer Generals Zero Hour",
        "CnC Generals Zero Hour",
    };

    private static string? SteamPath()
    {
        if (!OperatingSystem.IsWindows()) return null;
        (RegistryHive Hive, string Key, string Value)[] cands =
        {
            (RegistryHive.CurrentUser,  @"Software\Valve\Steam",               "SteamPath"),
            (RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam",   "InstallPath"),
            (RegistryHive.LocalMachine, @"SOFTWARE\Valve\Steam",               "InstallPath"),
        };
        foreach (var (hive, key, value) in cands)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
                using var k = baseKey.OpenSubKey(key);
                if (k?.GetValue(value) is string s && s.Length > 0)
                    return s.Replace('/', '\\');
            }
            catch { }
        }
        return null;
    }

    private static IEnumerable<string> SteamLibraries()
    {
        var libs = new List<string>();
        var sp = SteamPath();
        if (sp != null && Directory.Exists(sp))
        {
            libs.Add(sp);
            string vdf = Path.Combine(sp, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf))
            {
                try
                {
                    string txt = File.ReadAllText(vdf);
                    foreach (Match m in Regex.Matches(txt, "\"path\"\\s*\"([^\"]+)\""))
                        libs.Add(m.Groups[1].Value.Replace("\\\\", "\\"));
                }
                catch { }
            }
        }
        return libs;
    }

    /// <summary>Vrai si le dossier ressemble à une installation Zero Hour valide.</summary>
    public static bool IsZhFolder(string path) => LooksLikeZh(path);

    private static bool LooksLikeZh(string path)
    {
        if (!Directory.Exists(path)) return false;
        string[] markers =
        {
            Path.Combine(path, "GLM"),
            Path.Combine(path, "Data", "INI"),
            Path.Combine(path, "generals.exe"),
            Path.Combine(path, "GeneralsZH.exe"),
        };
        return markers.Any(m => File.Exists(m) || Directory.Exists(m));
    }

    /// <summary>Retourne le dossier d'install ZH détecté, ou null.</summary>
    public static string? Detect()
    {
        var roots = new List<string>(SteamLibraries())
        {
            @"C:\Program Files (x86)\Steam",
            @"C:\Program Files\Steam",
        };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            string common = Path.Combine(root, "steamapps", "common");
            foreach (var name in ZhFolderNames)
            {
                string p = Path.Combine(common, name);
                if (!seen.Add(p)) continue;
                if (LooksLikeZh(p)) return p;
            }
        }
        return null;
    }
}
