using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace GenSpeed.Core;

public enum CleanupCategory { Jeu, Mods, GenTool, GenLauncher, GenPatcher, Registre, Raccourcis, AppCompat, Steam, GenSpeed, Systeme, Joueur, TracesWin }
public enum CleanupRisk { Sur, Attention, Danger }
public enum CleanupMethod { Laisser, Desactiver, SauvegarderSupprimer, SupprimerDirect }
public enum CleanupKind { Fichier, Dossier, CleRegistre, ValeurRegistre, Info }

/// <summary>Un élément détecté par le scanner de désinstallation (fichier, dossier, clé/valeur registre, info).</summary>
public sealed class CleanupItem
{
    public CleanupCategory Category { get; set; }
    public CleanupKind Kind { get; set; }
    public string Path { get; set; } = "";          // chemin fichier/dossier OU clé registre ("HKLM\\…")
    public string? ValueName { get; set; }            // nom de valeur (ValeurRegistre)
    public string Display { get; set; } = "";         // libellé brut (nom de fichier / clé)
    public string ExplainKey { get; set; } = "";      // clé i18n d'explication (résolue côté UI)
    public long SizeBytes { get; set; }
    public CleanupRisk Risk { get; set; }
    public bool Reversible { get; set; } = true;
    public bool DefaultChecked { get; set; }
    public bool Removable { get; set; } = true;       // false = info seule (ex. redists système)
    public string? Extra { get; set; }                // donnée libre (ex. appId Steam, version)
    public List<string>? Kind2Files { get; set; }     // lot de fichiers regroupés sous un item Info (ex. .speedbak)
    // Cohérence GenLauncher : si renseigné, après suppression on passe l'entrée à Installed:false dans le YAML.
    public string? GlYaml { get; set; }               // chemin du GenLauncherCfg.yaml de l'install
    public string? GlName { get; set; }               // nom du mod/addon tel que dans le YAML
    public string? GlDependence { get; set; }         // mod parent (addon/patch) pour lever l'ambiguïté de noms ; null = mod
    public List<CleanupMethod> AllowedMethods { get; set; } = new();
    // Renseignés par l'UI avant exécution :
    public bool Selected { get; set; }
    public CleanupMethod ChosenMethod { get; set; } = CleanupMethod.SauvegarderSupprimer;
}

/// <summary>Job transmis au process élevé : éléments choisis + destination de secours.</summary>
public sealed class CleanupJob
{
    public string BackupDir { get; set; } = "";
    public List<CleanupItem> Items { get; set; } = new();
    public string ResultPath { get; set; } = "";
}

/// <summary>Résultat renvoyé par le process élevé.</summary>
public sealed class CleanupResult
{
    public string BackupDir { get; set; } = "";
    public List<string> Done { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public long FreedBytes { get; set; }
}

/// <summary>Désinstalleur propre : détection (lecture seule) + exécution (sauvegarde puis suppression).</summary>
public static class Cleanup
{
    // ===================================================================
    //  Détection (lecture seule, sans admin)
    // ===================================================================

    private static IEnumerable<string> UserDataDirs()
    {
        string up = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        yield return Path.Combine(up, "Documents", "Command and Conquer Generals Zero Hour Data");
        yield return Path.Combine(up, "OneDrive", "Documents", "Command and Conquer Generals Zero Hour Data");
    }

    private static long FileSize(string p) { try { return new FileInfo(p).Length; } catch { return 0; } }

    private static long DirSize(string d)
    {
        try { return Directory.EnumerateFiles(d, "*", SearchOption.AllDirectories).Sum(FileSize); }
        catch { return 0; }
    }

    private static string? PeVersion(string path)
    {
        try
        {
            var fi = FileVersionInfo.GetVersionInfo(path);
            return !string.IsNullOrWhiteSpace(fi.ProductVersion) ? fi.ProductVersion?.Trim()
                 : !string.IsNullOrWhiteSpace(fi.FileVersion) ? fi.FileVersion?.Trim() : null;
        }
        catch { return null; }
    }

    /// <summary>Steam-managed UNIQUEMENT si un appmanifest référence ce dossier. Une simple copie
    /// posée sous steamapps\common (ex. fork Reborn Omega) n'est PAS gérée par Steam → supprimable.</summary>
    private static (bool steam, string? appId) SteamInfo(string gameDir)
    {
        int idx = gameDir.IndexOf(@"\steamapps\common\", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return (false, null);
        string steamapps = gameDir.Substring(0, idx) + @"\steamapps";
        try
        {
            foreach (var acf in Directory.EnumerateFiles(steamapps, "appmanifest_*.acf"))
            {
                string txt = File.ReadAllText(acf);
                var m = Regex.Match(txt, "\"installdir\"\\s*\"([^\"]+)\"");
                if (m.Success && string.Equals(m.Groups[1].Value, Path.GetFileName(gameDir), StringComparison.OrdinalIgnoreCase))
                {
                    var a = Regex.Match(Path.GetFileName(acf), @"appmanifest_(\d+)\.acf");
                    if (a.Success) return (true, a.Groups[1].Value);
                }
            }
        }
        catch { }
        return (false, null);   // sous steamapps\common mais sans manifest = copie manuelle, supprimable
    }

    private static CleanupItem FileItem(string path, CleanupCategory cat, CleanupRisk risk, string explainKey,
        bool defaultChecked, bool reversible = true, List<CleanupMethod>? methods = null)
        => new()
        {
            Category = cat, Kind = CleanupKind.Fichier, Path = path, Display = Path.GetFileName(path),
            ExplainKey = explainKey, SizeBytes = FileSize(path), Risk = risk, Reversible = reversible,
            DefaultChecked = defaultChecked,
            AllowedMethods = methods ?? new() { CleanupMethod.SauvegarderSupprimer, CleanupMethod.Desactiver, CleanupMethod.SupprimerDirect },
            ChosenMethod = CleanupMethod.SauvegarderSupprimer,
        };

    private static CleanupItem DirItem(string path, CleanupCategory cat, CleanupRisk risk, string explainKey, bool defaultChecked)
        => new()
        {
            Category = cat, Kind = CleanupKind.Dossier, Path = path, Display = Path.GetFileName(path.TrimEnd('\\')),
            ExplainKey = explainKey, SizeBytes = DirSize(path), Risk = risk, Reversible = true,
            DefaultChecked = defaultChecked,
            AllowedMethods = new() { CleanupMethod.SauvegarderSupprimer, CleanupMethod.Desactiver, CleanupMethod.SupprimerDirect },
            ChosenMethod = CleanupMethod.SauvegarderSupprimer,
        };

    /// <summary>Ordre SÛR (exécution ET affichage) : contenu interne du dossier jeu d'abord (avec ses
    /// sauvegardes), puis l'indépendant, puis le dossier jeu lui-même, puis Steam en tout dernier.
    /// Évite de supprimer le dossier jeu avant d'avoir sauvegardé ce qu'il contient.</summary>
    public static int CategoryRank(CleanupCategory c) => c switch
    {
        CleanupCategory.Mods => 10,           // le contenu géré AVANT l'outil qui le gère
        CleanupCategory.GenLauncher => 20,
        CleanupCategory.GenTool => 30,
        CleanupCategory.GenPatcher => 40,
        CleanupCategory.GenSpeed => 50,
        CleanupCategory.Joueur => 60,
        CleanupCategory.Raccourcis => 70,
        CleanupCategory.Registre => 80,
        CleanupCategory.AppCompat => 90,
        CleanupCategory.TracesWin => 95,
        CleanupCategory.Jeu => 100,
        CleanupCategory.Steam => 110,
        CleanupCategory.Systeme => 120,
        _ => 200,
    };

    /// <summary>Scanne une install + les traces globales. Aucune écriture.</summary>
    public static List<CleanupItem> Scan(string gameDir)
    {
        var items = new List<CleanupItem>();
        if (string.IsNullOrWhiteSpace(gameDir) || !Directory.Exists(gameDir)) return items;

        // ── 🛠 GenTool : d3d8.dll (proxy DirectX 8 — DANGER) + variantes ───
        foreach (var loc in new[] { gameDir }.Concat(UserDataDirs()).Where(Directory.Exists).Distinct())
        {
            string d3d8 = Path.Combine(loc, "d3d8.dll");
            if (System.IO.File.Exists(d3d8))
            {
                var it = FileItem(d3d8, CleanupCategory.GenTool, CleanupRisk.Danger, "clean.explain.d3d8",
                    defaultChecked: false, reversible: true,
                    methods: new() { CleanupMethod.Desactiver, CleanupMethod.SauvegarderSupprimer, CleanupMethod.Laisser });
                it.ChosenMethod = CleanupMethod.Desactiver;     // le plus prudent par défaut
                it.Extra = PeVersion(d3d8);
                items.Add(it);
            }
            foreach (var name in new[] { "d3d8x.dll", "gentool.dll", "GenTool.dll" })
            {
                string p = Path.Combine(loc, name);
                if (System.IO.File.Exists(p))
                    items.Add(FileItem(p, CleanupCategory.GenTool, CleanupRisk.Attention, "clean.explain.gentool", defaultChecked: false));
            }
        }
        // dbghelp.dll (crash handler GenTool, OU renommé par GenPatcher) : signal, à manipuler avec soin.
        string dbg = Path.Combine(gameDir, "dbghelp.dll");
        if (System.IO.File.Exists(dbg))
            items.Add(FileItem(dbg, CleanupCategory.GenTool, CleanupRisk.Attention, "clean.explain.dbghelp", defaultChecked: false));

        // ── 🩹 GenPatcher : dossiers "GenPatcher*" + exe (jeu/Bureau/Téléchargements/Documents) ──
        string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var gpRoots = new[]
        {
            gameDir,
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
            docs,
        };
        var seenGp = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in gpRoots.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
            try
            {
                foreach (var dir in Directory.EnumerateDirectories(root, "GenPatcher*"))
                    if (seenGp.Add(dir))
                    {
                        var gi = DirItem(dir, CleanupCategory.GenPatcher, CleanupRisk.Sur, "clean.explain.genpatcher", defaultChecked: false);
                        gi.ChosenMethod = CleanupMethod.SupprimerDirect;   // dossier téléchargé, re-téléchargeable
                        items.Add(gi);
                    }
                foreach (var exe in Directory.EnumerateFiles(root, "GenPatcher*.exe"))
                    if (seenGp.Add(exe))
                        items.Add(FileItem(exe, CleanupCategory.GenPatcher, CleanupRisk.Sur, "clean.explain.genpatcher", defaultChecked: false,
                            methods: new() { CleanupMethod.SauvegarderSupprimer, CleanupMethod.SupprimerDirect }));
            }
            catch { }
        // GenPatcher se loge AUSSI dans les dossiers Data du jeu (Generals ET Zero Hour).
        foreach (var data in new[]
        {
            Path.Combine(docs, "Command and Conquer Generals Data", "GenPatcher"),
            Path.Combine(docs, "Command and Conquer Generals Zero Hour Data", "GenPatcher"),
        })
            if (Directory.Exists(data) && seenGp.Add(data))
            {
                var gd = DirItem(data, CleanupCategory.GenPatcher, CleanupRisk.Sur, "clean.explain.genpatcher.data", defaultChecked: false);
                gd.ChosenMethod = CleanupMethod.SupprimerDirect;
                items.Add(gd);
            }

        string browser = Path.Combine(gameDir, "BrowserEngine.dll");
        if (System.IO.File.Exists(browser))
            items.Add(FileItem(browser, CleanupCategory.GenPatcher, CleanupRisk.Attention, "clean.explain.browserengine", defaultChecked: false));

        // ── 🚀 GenLauncher : dossiers + exe + config YAML ──────────────────
        string glFolder = Path.Combine(gameDir, ".GenLauncherFolder");
        if (Directory.Exists(glFolder))
            items.Add(DirItem(glFolder, CleanupCategory.GenLauncher, CleanupRisk.Sur, "clean.explain.glfolder", defaultChecked: false));
        foreach (var name in new[] { "GenLauncher.exe", "launcher.bmp", "Launcher.txt" })
        {
            string p = Path.Combine(gameDir, name);
            if (System.IO.File.Exists(p))
                items.Add(FileItem(p, CleanupCategory.GenLauncher, CleanupRisk.Sur, "clean.explain.genlauncher", defaultChecked: false));
        }
        string launcherDir = Path.Combine(gameDir, "launcher");
        if (Directory.Exists(launcherDir))
            items.Add(DirItem(launcherDir, CleanupCategory.GenLauncher, CleanupRisk.Sur, "clean.explain.launcherdir", defaultChecked: false));

        // ── 🧩 Mods (GLM) : gros volume, décoché par défaut ────────────────
        string glm = Path.Combine(gameDir, "GLM");
        if (Directory.Exists(glm))
        {
            var skipRoot = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Addons", "Patches", "Tools" };
            string glYaml = Path.Combine(gameDir, ".GenLauncherFolder", "GenLauncherCfg.yaml");
            string? glYamlOrNull = System.IO.File.Exists(glYaml) ? glYaml : null;
            bool anyMod = false;
            // Un item PAR MOD ; puis un item PAR ADDON / PATCH du mod (sous-dossiers Addons/Patches).
            try
            {
                foreach (var modDir in Directory.EnumerateDirectories(glm).OrderBy(p => Path.GetFileName(p), StringComparer.Ordinal))
                {
                    string modName = Path.GetFileName(modDir);
                    if (skipRoot.Contains(modName)) continue;
                    var mi = DirItem(modDir, CleanupCategory.Mods, CleanupRisk.Attention, "clean.explain.mod", defaultChecked: false);
                    mi.ChosenMethod = CleanupMethod.SupprimerDirect;   // re-téléchargeable
                    mi.GlYaml = glYamlOrNull; mi.GlName = modName;     // cohérence GenLauncher
                    items.Add(mi);
                    anyMod = true;

                    // Addons / Patches de CE mod (chacun = un dossier de .gib).
                    foreach (var (sub, kind) in new[] { ("Addons", "addon"), ("Patches", "patch") })
                    {
                        string subDir = Path.Combine(modDir, sub);
                        if (!Directory.Exists(subDir)) continue;
                        foreach (var aDir in Directory.EnumerateDirectories(subDir).OrderBy(p => Path.GetFileName(p), StringComparer.Ordinal))
                        {
                            string aName = Path.GetFileName(aDir);
                            var ai = DirItem(aDir, CleanupCategory.Mods, CleanupRisk.Sur, $"clean.explain.{kind}", defaultChecked: false);
                            ai.Display = $"   ↳ {modName} ▸ {aName} ({kind})";
                            ai.ChosenMethod = CleanupMethod.SupprimerDirect;
                            ai.GlYaml = glYamlOrNull; ai.GlName = aName; ai.GlDependence = modName;   // lève l'ambiguïté de noms d'addon
                            items.Add(ai);
                        }
                    }
                }
            }
            catch { }
            // Dossiers partagés Addons/Patches/Tools à la RACINE de GLM (rares — listés s'ils contiennent des données).
            foreach (var shared in skipRoot)
            {
                string sp = Path.Combine(glm, shared);
                if (Directory.Exists(sp) && Directory.EnumerateFileSystemEntries(sp).Any())
                {
                    var si = DirItem(sp, CleanupCategory.Mods, CleanupRisk.Attention, "clean.explain.modshared", defaultChecked: false);
                    si.Display = "GLM/" + shared;
                    si.ChosenMethod = CleanupMethod.SupprimerDirect;
                    items.Add(si);
                    anyMod = true;
                }
            }
            // Repli : GLM présent mais vide de mods → proposer le dossier entier.
            if (!anyMod)
            {
                var it = DirItem(glm, CleanupCategory.Mods, CleanupRisk.Attention, "clean.explain.glm", defaultChecked: false);
                it.ChosenMethod = CleanupMethod.SupprimerDirect;
                items.Add(it);
            }
        }

        // ── 🟦 GenSpeed : ses propres sauvegardes .speedbak + config ───────
        try
        {
            var speedbaks = Directory.EnumerateFiles(gameDir, "*.speedbak", SearchOption.AllDirectories).ToList();
            if (speedbaks.Count > 0)
            {
                long sz = speedbaks.Sum(FileSize);
                items.Add(new CleanupItem
                {
                    Category = CleanupCategory.GenSpeed, Kind = CleanupKind.Info, Path = gameDir,
                    Display = $"{speedbaks.Count} × .speedbak", ExplainKey = "clean.explain.speedbak",
                    SizeBytes = sz, Risk = CleanupRisk.Attention, Reversible = false, DefaultChecked = false,
                    Removable = true, Kind2Files = speedbaks,
                    AllowedMethods = new() { CleanupMethod.SupprimerDirect, CleanupMethod.Laisser },
                    ChosenMethod = CleanupMethod.SupprimerDirect,
                });
            }
        }
        catch { }
        // GenSpeed n'est PAS désinstallé par lui-même (il sert aussi à faire des installs propres),
        // mais on permet de le RÉINITIALISER : supprimer sa config = retour aux réglages par défaut.
        string gsCfg = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GenSpeed");
        if (Directory.Exists(gsCfg))
        {
            var it = DirItem(gsCfg, CleanupCategory.GenSpeed, CleanupRisk.Sur, "clean.explain.gscfg", defaultChecked: false);
            it.Display = "↺ Réinitialiser GenSpeed (config)";
            it.AllowedMethods = new() { CleanupMethod.SauvegarderSupprimer, CleanupMethod.SupprimerDirect };
            items.Add(it);
        }

        // ── 📁 Données joueur (Documents) : LE résidu classique, jamais retiré par Steam. ──
        // Tout DÉCOCHÉ par défaut : ce sont des données personnelles.
        foreach (var ud in UserDataDirs().Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var (sub, key) in new[] { ("Maps", "usermaps"), ("Replays", "replays"), ("Save", "saves"), ("Screenshots", "screenshots"),
                                               ("MapPreviews", "mappreviews"), ("CrashDumps", "crashdumps") })
            {
                string p = Path.Combine(ud, sub);
                if (Directory.Exists(p))
                    items.Add(DirItem(p, CleanupCategory.Joueur, CleanupRisk.Attention, $"clean.explain.{key}", defaultChecked: false));
            }
            foreach (var ini in new[] { "options.ini", "Network.ini" })
            {
                string opt = Path.Combine(ud, ini);
                if (System.IO.File.Exists(opt))
                    items.Add(FileItem(opt, CleanupCategory.Joueur, CleanupRisk.Attention, "clean.explain.options", defaultChecked: false,
                        methods: new() { CleanupMethod.SauvegarderSupprimer, CleanupMethod.SupprimerDirect }));
            }
        }

        // ── 🖇 Raccourcis bureau / menu démarrer pointant vers le jeu ──────
        foreach (var lnk in FindGameShortcuts(gameDir))
            items.Add(FileItem(lnk, CleanupCategory.Raccourcis, CleanupRisk.Sur, "clean.explain.shortcut", defaultChecked: false,
                methods: new() { CleanupMethod.SauvegarderSupprimer, CleanupMethod.SupprimerDirect }));

        // ── 🗝 Registre EA (installpath) ───────────────────────────────────
        if (OperatingSystem.IsWindows())
        {
            foreach (var key in new[]
            {
                @"HKLM\SOFTWARE\WOW6432Node\Electronic Arts\EA Games\Command and Conquer Generals Zero Hour",
                @"HKLM\SOFTWARE\WOW6432Node\Electronic Arts\EA Games\Generals",
                @"HKLM\SOFTWARE\Electronic Arts\EA Games\Command and Conquer Generals Zero Hour",
            })
                if (RegKeyExists(key))
                    items.Add(new CleanupItem
                    {
                        Category = CleanupCategory.Registre, Kind = CleanupKind.CleRegistre, Path = key,
                        Display = key.Replace(@"HKLM\SOFTWARE\", ""), ExplainKey = "clean.explain.eareg",
                        Risk = CleanupRisk.Attention, Reversible = true, DefaultChecked = false,
                        AllowedMethods = new() { CleanupMethod.SauvegarderSupprimer, CleanupMethod.SupprimerDirect },
                        ChosenMethod = CleanupMethod.SauvegarderSupprimer,
                    });

            // ── 🧭 AppCompatFlags\Layers : valeurs orphelines / liées au jeu ──
            foreach (var it in ScanAppCompat(gameDir)) items.Add(it);

            // ── 🧹 Traces Windows (journaux de diag auto-créés par Windows pour les exe du jeu).
            //     Inoffensif à retirer : Windows les recrée au besoin. Décoché par défaut (bruit).
            foreach (var it in ScanWinTraces()) items.Add(it);
        }

        // ── 🗺 Maps installées dans le dossier jeu (officielles + ajoutées) ─
        string installMaps = Path.Combine(gameDir, "Maps");
        if (Directory.Exists(installMaps))
        {
            var im = DirItem(installMaps, CleanupCategory.Jeu, CleanupRisk.Attention, "clean.explain.installmaps", defaultChecked: false);
            im.Display = "Maps (dossier du jeu)";
            items.Add(im);
        }

        // ── 💨 Steam : ne pas supprimer à la main → info + désinstall Steam ─
        var (steam, appId) = SteamInfo(gameDir);
        if (steam)
            items.Add(new CleanupItem
            {
                Category = CleanupCategory.Steam, Kind = CleanupKind.Info, Path = gameDir,
                Display = appId != null ? $"AppID {appId}" : "Steam", ExplainKey = "clean.explain.steam",
                Risk = CleanupRisk.Attention, Removable = false, DefaultChecked = false, Extra = appId,
                AllowedMethods = new(),
            });
        else
        {
            // Install hors Steam (retail / copie) : suppression du dossier jeu possible.
            var gj = DirItem(gameDir, CleanupCategory.Jeu, CleanupRisk.Danger, "clean.explain.gamedir", defaultChecked: false);
            gj.ChosenMethod = CleanupMethod.SupprimerDirect;   // réinstallable : pas de sauvegarde de 11 Go par défaut
            items.Add(gj);
        }

        // ── ⚙ Système : redists VC++ (NON supprimables ici) ───────────────
        if (OperatingSystem.IsWindows())
            foreach (var vc in DetectVcRedists())
                items.Add(new CleanupItem
                {
                    Category = CleanupCategory.Systeme, Kind = CleanupKind.Info, Path = vc,
                    Display = vc, ExplainKey = "clean.explain.vcredist", Risk = CleanupRisk.Sur,
                    Removable = false, DefaultChecked = false, AllowedMethods = new(),
                });

        return items;
    }

    private static IEnumerable<string> FindGameShortcuts(string gameDir)
    {
        var dirs = new List<string>
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
        };
        string needle = Path.GetFileName(gameDir);
        foreach (var dir in dirs.Where(Directory.Exists).Distinct())
        {
            IEnumerable<string> lnks;
            try { lnks = Directory.EnumerateFiles(dir, "*.lnk", SearchOption.AllDirectories); }
            catch { continue; }
            foreach (var lnk in lnks)
            {
                string name = Path.GetFileNameWithoutExtension(lnk);
                // Heuristique nom : "Generals", "Zero Hour", nom du dossier, mods connus.
                if (Regex.IsMatch(name, @"generals|zero\s*hour|genlauncher|reborn|contra|shockwave", RegexOptions.IgnoreCase)
                    || name.Contains(needle, StringComparison.OrdinalIgnoreCase))
                    yield return lnk;
            }
        }
    }

    // ===================================================================
    //  Registre — helpers (lecture)
    // ===================================================================

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static (RegistryHive hive, RegistryView view, string sub)? ParseRegPath(string full)
    {
        int i = full.IndexOf('\\');
        if (i < 0) return null;
        string root = full.Substring(0, i).ToUpperInvariant();
        string sub = full.Substring(i + 1);
        RegistryHive hive = root switch
        {
            "HKLM" or "HKEY_LOCAL_MACHINE" => RegistryHive.LocalMachine,
            "HKCU" or "HKEY_CURRENT_USER" => RegistryHive.CurrentUser,
            "HKCR" or "HKEY_CLASSES_ROOT" => RegistryHive.ClassesRoot,
            _ => RegistryHive.LocalMachine,
        };
        var view = sub.Contains("WOW6432Node", StringComparison.OrdinalIgnoreCase) ? RegistryView.Registry32 : RegistryView.Registry64;
        return (hive, view, sub);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static bool RegKeyExists(string full)
    {
        if (!OperatingSystem.IsWindows()) return false;
        var p = ParseRegPath(full);
        if (p == null) return false;
        try
        {
            using var bk = RegistryKey.OpenBaseKey(p.Value.hive, p.Value.view);
            using var k = bk.OpenSubKey(p.Value.sub);
            if (k != null) return true;
            // Réessaye en vue 64 si 32 a échoué (et inversement).
            using var bk2 = RegistryKey.OpenBaseKey(p.Value.hive, RegistryView.Registry64);
            using var k2 = bk2.OpenSubKey(p.Value.sub);
            return k2 != null;
        }
        catch { return false; }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static IEnumerable<CleanupItem> ScanAppCompat(string gameDir)
    {
        const string layers = @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";
        const string fullKey = @"HKCU\" + layers;
        RegistryKey? k = null;
        try { k = Registry.CurrentUser.OpenSubKey(layers); } catch { }
        if (k == null) yield break;
        string[] names;
        try { names = k.GetValueNames(); } catch { k.Dispose(); yield break; }
        foreach (var exePath in names)
        {
            if (string.IsNullOrWhiteSpace(exePath)) continue;
            bool exists = System.IO.File.Exists(exePath);
            bool looksGame = Regex.IsMatch(exePath, @"generals|zero\s*hour|genlauncher|modded|worldbuilder|reborn|contra|shockwave|gentool", RegexOptions.IgnoreCase)
                             || exePath.Contains(Path.GetFileName(gameDir), StringComparison.OrdinalIgnoreCase);
            if (!looksGame) continue;                    // ne touche QU'AUX entrées liées au jeu (pas les orphelins d'autres logiciels)
            // Orpheline (chemin inexistant) = sûr à retirer ; existante liée au jeu = attention.
            yield return new CleanupItem
            {
                Category = CleanupCategory.AppCompat, Kind = CleanupKind.ValeurRegistre, Path = fullKey,
                ValueName = exePath, Display = exePath, ExplainKey = exists ? "clean.explain.appcompat" : "clean.explain.appcompat.orphan",
                Risk = exists ? CleanupRisk.Attention : CleanupRisk.Sur, Reversible = true,
                DefaultChecked = !exists,                 // orpheline cochée par défaut
                AllowedMethods = new() { CleanupMethod.SauvegarderSupprimer, CleanupMethod.SupprimerDirect },
                ChosenMethod = CleanupMethod.SauvegarderSupprimer,
            };
        }
        k.Dispose();
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static IEnumerable<CleanupItem> ScanWinTraces()
    {
        CleanupItem RegTrace(string key, string explainKey) => new()
        {
            Category = CleanupCategory.TracesWin, Kind = CleanupKind.CleRegistre, Path = key,
            Display = key.Replace(@"HKLM\SOFTWARE\", "").Replace(@"WOW6432Node\", ""),
            ExplainKey = explainKey, Risk = CleanupRisk.Sur, Reversible = true, DefaultChecked = false,
            AllowedMethods = new() { CleanupMethod.SauvegarderSupprimer, CleanupMethod.SupprimerDirect },
            ChosenMethod = CleanupMethod.SauvegarderSupprimer,
        };

        // Journaux Windows par exe (diagnostic fuite mémoire + traçage réseau RAS).
        var gameExes = new[] { "GenLauncher", "GenPatcher", "generals", "GeneralsZH", "modded", "WorldBuilder", "GenTool" };
        foreach (var exe in gameExes)
        {
            string radar = $@"HKLM\SOFTWARE\Microsoft\RADAR\HeapLeakDetection\DiagnosedApplications\{exe}.exe";
            if (RegKeyExists(radar)) yield return RegTrace(radar, "clean.explain.wintrace");
            foreach (var sfx in new[] { "_RASAPI32", "_RASMANCS" })
            {
                foreach (var root in new[] { @"HKLM\SOFTWARE\WOW6432Node\Microsoft\Tracing\", @"HKLM\SOFTWARE\Microsoft\Tracing\" })
                {
                    string tr = root + exe + sfx;
                    if (RegKeyExists(tr)) { yield return RegTrace(tr, "clean.explain.wintrace"); break; }   // une seule vue suffit
                }
            }
        }

        // Association de type de fichier .gib (auto-créée à l'ouverture d'un .gib).
        if (RegKeyExists(@"HKCR\.gib"))
            yield return RegTrace(@"HKCR\.gib", "clean.explain.gibassoc");

        // Liens « fichiers récents » Windows pointant vers GenLauncher (gérés par Windows, inoffensifs).
        string recent = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                     "Microsoft", "Windows", "Recent");
        if (Directory.Exists(recent))
        {
            IEnumerable<string> lnks;
            try { lnks = Directory.EnumerateFiles(recent, "*.lnk").Where(f =>
                      Path.GetFileName(f).Contains("GenLauncher", StringComparison.OrdinalIgnoreCase)).ToList(); }
            catch { lnks = Enumerable.Empty<string>(); }
            foreach (var lnk in lnks)
                yield return FileItem(lnk, CleanupCategory.TracesWin, CleanupRisk.Sur, "clean.explain.recent", defaultChecked: false,
                    methods: new() { CleanupMethod.SupprimerDirect, CleanupMethod.SauvegarderSupprimer });
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static List<string> DetectVcRedists()
    {
        var found = new List<string>();
        if (!OperatingSystem.IsWindows()) return found;
        foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var unins = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if (unins == null) continue;
                foreach (var sub in unins.GetSubKeyNames())
                {
                    using var kk = unins.OpenSubKey(sub);
                    if (kk?.GetValue("DisplayName") is string dn &&
                        dn.Contains("Visual C++", StringComparison.OrdinalIgnoreCase) &&
                        dn.Contains("Redistributable", StringComparison.OrdinalIgnoreCase))
                    {
                        var m = Regex.Match(dn, @"Visual C\+\+ (\d{4})");
                        string label = m.Success ? $"VC++ {m.Groups[1].Value}" : dn;
                        if (!found.Contains(label)) found.Add(label);
                    }
                }
            }
            catch { }
        found.Sort(StringComparer.Ordinal);
        return found;
    }

    // ===================================================================
    //  Exécution (DANS le process élevé)
    // ===================================================================

    public static CleanupResult Execute(CleanupJob job)
    {
        var res = new CleanupResult { BackupDir = job.BackupDir };
        try { Directory.CreateDirectory(job.BackupDir); } catch { }
        var manifest = new List<object>();

        // Ordre SÛR : contenu interne (et ses sauvegardes) AVANT le dossier jeu / Steam.
        foreach (var it in job.Items.OrderBy(i => CategoryRank(i.Category)))
        {
            if (it.ChosenMethod == CleanupMethod.Laisser) continue;
            try
            {
                switch (it.Kind)
                {
                    case CleanupKind.Fichier:
                    case CleanupKind.Dossier:
                        ExecFileOrDir(it, job, res, manifest);
                        break;
                    case CleanupKind.ValeurRegistre:
                    case CleanupKind.CleRegistre:
                        ExecRegistry(it, job, res, manifest);
                        break;
                    case CleanupKind.Info:
                        if (it.Kind2Files is { Count: > 0 })       // ex. lot de .speedbak
                            ExecFileBatch(it, job, res, manifest);
                        break;
                }

                // Cohérence GenLauncher : marquer le mod/addon non installé dans le YAML (s'il existe encore).
                if (!string.IsNullOrEmpty(it.GlYaml) && !string.IsNullOrEmpty(it.GlName) && System.IO.File.Exists(it.GlYaml))
                    try
                    {
                        GenLauncherYaml.MarkUninstalled(it.GlYaml!, it.GlName!, it.GlDependence, job.BackupDir);
                        res.Done.Add($"↺ GenLauncher : « {it.GlName} » marqué non installé");
                    }
                    catch (Exception ex) { res.Errors.Add($"YAML {it.GlName}: {ex.Message}"); }
            }
            catch (Exception ex) { res.Errors.Add($"{it.Display}: {ex.Message}"); }
        }

        // Manifeste + mode d'emploi de restauration.
        try
        {
            System.IO.File.WriteAllText(Path.Combine(job.BackupDir, "manifest.json"),
                JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
            System.IO.File.WriteAllText(Path.Combine(job.BackupDir, "RESTORE.txt"), RestoreReadme(), new UTF8Encoding(false));
        }
        catch { }
        return res;
    }

    private static string SanitizeForBackup(string fullPath)
    {
        string s = fullPath.Replace(":", "").Replace('\\', '/');
        foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s.Replace('/', Path.DirectorySeparatorChar);
    }

    /// <summary>Retire lecture-seule/caché/système (récursif pour un dossier) — sinon Delete échoue en
    /// « Access denied » même élevé (cas réel : contenus extraits de ModDB marqués lecture seule).</summary>
    private static void ClearAttributes(string path, bool isDir)
    {
        try
        {
            if (isDir)
                foreach (var e in Directory.EnumerateFileSystemEntries(path, "*", SearchOption.AllDirectories))
                    try { System.IO.File.SetAttributes(e, FileAttributes.Normal); } catch { }
            System.IO.File.SetAttributes(path, FileAttributes.Normal);
        }
        catch { }
    }

    private static void ExecFileOrDir(CleanupItem it, CleanupJob job, CleanupResult res, List<object> manifest)
    {
        bool isDir = it.Kind == CleanupKind.Dossier;
        if (!(isDir ? Directory.Exists(it.Path) : System.IO.File.Exists(it.Path))) return;
        long size = isDir ? DirSize(it.Path) : FileSize(it.Path);

        if (it.ChosenMethod == CleanupMethod.Desactiver)
        {
            string off = it.Path.TrimEnd('\\') + ".off";
            int n = 1; while (System.IO.File.Exists(off) || Directory.Exists(off)) off = it.Path.TrimEnd('\\') + $".off{n++}";
            if (isDir) Directory.Move(it.Path, off); else System.IO.File.Move(it.Path, off);
            manifest.Add(new { action = "rename", kind = it.Kind.ToString(), from = it.Path, to = off });
            res.Done.Add($"↳ {it.Display} → {Path.GetFileName(off)}");
            return;
        }

        if (it.ChosenMethod == CleanupMethod.SauvegarderSupprimer)
        {
            string dest = Path.Combine(job.BackupDir, "files", SanitizeForBackup(it.Path));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            if (isDir) CopyDir(it.Path, dest); else System.IO.File.Copy(it.Path, dest, overwrite: true);
            manifest.Add(new { action = "backup+delete", kind = it.Kind.ToString(), original = it.Path, backup = dest });
        }

        ClearAttributes(it.Path, isDir);
        if (isDir) Directory.Delete(it.Path, recursive: true); else System.IO.File.Delete(it.Path);
        res.FreedBytes += size;
        res.Done.Add($"🗑 {it.Display}");
    }

    private static void ExecFileBatch(CleanupItem it, CleanupJob job, CleanupResult res, List<object> manifest)
    {
        foreach (var fp in it.Kind2Files!)
        {
            if (!System.IO.File.Exists(fp)) continue;
            long size = FileSize(fp);
            if (it.ChosenMethod == CleanupMethod.SauvegarderSupprimer)
            {
                string dest = Path.Combine(job.BackupDir, "files", SanitizeForBackup(fp));
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                System.IO.File.Copy(fp, dest, overwrite: true);
                manifest.Add(new { action = "backup+delete", kind = "Fichier", original = fp, backup = dest });
            }
            ClearAttributes(fp, isDir: false);
            System.IO.File.Delete(fp);
            res.FreedBytes += size;
        }
        res.Done.Add($"🗑 {it.Display}");
    }

    private static void ExecRegistry(CleanupItem it, CleanupJob job, CleanupResult res, List<object> manifest)
    {
        if (it.ChosenMethod == CleanupMethod.SauvegarderSupprimer)
        {
            string regFile = Path.Combine(job.BackupDir, "registry", SanitizeForBackup(it.Path) + ".reg");
            Directory.CreateDirectory(Path.GetDirectoryName(regFile)!);
            RunReg($"export \"{it.Path}\" \"{regFile}\" /y");
            manifest.Add(new { action = "backup+delete", kind = it.Kind.ToString(), key = it.Path, value = it.ValueName, backup = regFile });
        }
        if (it.Kind == CleanupKind.ValeurRegistre && it.ValueName != null)
        {
            RunReg($"delete \"{it.Path}\" /v \"{it.ValueName}\" /f");
            res.Done.Add($"🗑 [registre] {it.ValueName}");
        }
        else
        {
            RunReg($"delete \"{it.Path}\" /f");
            res.Done.Add($"🗑 [registre] {it.Display}");
        }
    }

    private static void RunReg(string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "reg.exe", Arguments = args, UseShellExecute = false,
            CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        using var p = Process.Start(psi);
        p?.WaitForExit();
        if (p != null && p.ExitCode != 0)
            throw new Exception($"reg.exe ({p.ExitCode}) {p.StandardError.ReadToEnd().Trim()}");
    }

    private static void CopyDir(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(src, f);
            string target = Path.Combine(dst, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            System.IO.File.Copy(f, target, overwrite: true);
        }
    }

    // ===================================================================
    //  Vérification DirectX 8 système (après retrait du proxy GenTool)
    // ===================================================================

    /// <summary>Vérifie que le d3d8.dll SYSTÈME (celui de Windows, pas le proxy GenTool) est présent
    /// et authentique (signé Microsoft). Lecture seule. Valable Windows 10 et 11 (x64 : SysWOW64 ;
    /// x86 : System32). Si KO, la réparation officielle est « sfc /scannow » — jamais un téléchargement.</summary>
    public static (bool Ok, string Detail) VerifySystemDirectX8()
    {
        string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        // Le jeu est 32 bits : sur Windows x64 il charge SysWOW64\d3d8.dll, sur x86 System32\d3d8.dll.
        string p = Path.Combine(win, "SysWOW64", "d3d8.dll");
        if (!System.IO.File.Exists(p)) p = Path.Combine(win, "System32", "d3d8.dll");
        if (!System.IO.File.Exists(p)) return (false, "d3d8.dll absent (SysWOW64/System32)");

        string ver = PeVersion(p) ?? "?";
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -ExecutionPolicy Bypass -Command \"$s = Get-AuthenticodeSignature '" + p +
                            "'; Write-Output ($s.Status.ToString() + '|' + $s.IsOSBinary)\"",
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
            };
            using var proc = Process.Start(psi);
            string outp = proc!.StandardOutput.ReadToEnd().Trim();
            proc.WaitForExit(30000);
            var parts = outp.Split('|');
            bool valid = parts.Length >= 1 && parts[0].Equals("Valid", StringComparison.OrdinalIgnoreCase);
            bool osBin = parts.Length >= 2 && parts[1].Equals("True", StringComparison.OrdinalIgnoreCase);
            if (valid && osBin) return (true, $"{p} · v{ver} · signé Microsoft / Microsoft-signed");
            return (false, $"{p} · v{ver} · signature: {outp}");
        }
        catch (Exception ex)
        {
            // Impossible de vérifier la signature : on reporte la présence + version sans conclure.
            return (false, $"{p} · v{ver} · vérification signature impossible: {ex.Message}");
        }
    }

    private static string RestoreReadme() =>
        "=== GenSpeed — sauvegarde de désinstallation / cleanup backup ===\n\n" +
        "FR : Cette sauvegarde a été créée AVANT suppression. Pour restaurer :\n" +
        " • Fichiers/dossiers : voir le sous-dossier 'files\\'. Recopier chaque élément\n" +
        "   à son emplacement d'origine (le chemin est encodé dans le nom du dossier).\n" +
        " • Registre : voir 'registry\\'. Double-cliquer chaque fichier .reg pour réimporter.\n" +
        " • Éléments 'désactivés' (.off) : renommer en retirant le suffixe .off.\n" +
        " • manifest.json liste précisément chaque action effectuée.\n\n" +
        "EN : This backup was made BEFORE deletion. To restore:\n" +
        " • Files/folders: see 'files\\'. Copy each item back to its original path\n" +
        "   (the original path is encoded in the folder name).\n" +
        " • Registry: see 'registry\\'. Double-click each .reg file to re-import.\n" +
        " • 'Disabled' (.off) items: rename by removing the .off suffix.\n" +
        " • manifest.json lists every action performed.\n";
}
