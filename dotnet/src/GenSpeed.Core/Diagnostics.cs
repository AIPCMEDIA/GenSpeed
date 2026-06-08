using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace GenSpeed.Core;

public enum Severity { Critique, Attention, Info }
public enum DiffStatus { Different, AbsentMine, AbsentOther }

/// <summary>Une différence entre deux empreintes (section localisable côté UI).</summary>
public sealed record DiffEntry(string SectionKey, string Item, DiffStatus Status, Severity Severity,
                               string? Mine = null, string? Other = null);

/// <summary>Une entrée fichier d'empreinte : hash court + taille (+ source pour les maps).</summary>
public sealed record FpEntry(string? Sha, long Size, string? Source = null)
{
    public string Canon => $"{Sha}|{Size}";          // pour comparaison (ignore Source)
}

/// <summary>Empreinte de synchro complète d'une machine.</summary>
public sealed class Fingerprint
{
    public Dictionary<string, FpEntry> Base { get; set; } = new();
    public Dictionary<string, Dictionary<string, FpEntry>> Mods { get; set; } = new();
    public Dictionary<string, FpEntry> LooseIni { get; set; } = new();
    public Dictionary<string, FpEntry> Maps { get; set; } = new();
    public Dictionary<string, FpEntry> Gentool { get; set; } = new();
    /// <summary>Composants installés NOMMÉS : "Mod: Contra" -> "10.0.2 Beta 2 Patch 1", etc.</summary>
    public Dictionary<string, string> Components { get; set; } = new();
    public bool HasComponents { get; set; } = true;   // false si chargé depuis un ancien rapport
}

/// <summary>Diagnostic mismatch : empreinte de synchro + comparaison nommée entre machines.</summary>
public static class Diagnostics
{
    private static IEnumerable<string> UserDataDirs()
    {
        string up = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        yield return Path.Combine(up, "Documents", "Command and Conquer Generals Zero Hour Data");
        yield return Path.Combine(up, "OneDrive", "Documents", "Command and Conquer Generals Zero Hour Data");
    }

    private static FpEntry Entry(string fp)
    {
        string? h = Hashing.FileSha256(fp);
        long size = File.Exists(fp) ? new FileInfo(fp).Length : 0;
        return new FpEntry(h?[..Math.Min(12, h.Length)], size);
    }

    private static Dictionary<string, FpEntry> Entries(string gameDir, IEnumerable<string> files)
    {
        var d = new Dictionary<string, FpEntry>();
        foreach (var fp in files)
            d[Path.GetRelativePath(gameDir, fp).Replace('\\', '/')] = Entry(fp);
        return d;
    }

    private static Dictionary<string, FpEntry> CollectLooseIni(string gameDir)
    {
        var d = new Dictionary<string, FpEntry>();
        string baseDir = Path.Combine(gameDir, "Data", "INI");
        if (!Directory.Exists(baseDir)) return d;
        foreach (var fp in Directory.EnumerateFiles(baseDir, "*", SearchOption.AllDirectories))
        {
            if (fp.EndsWith(".speedbak", StringComparison.OrdinalIgnoreCase)) continue;
            d[Path.GetRelativePath(gameDir, fp).Replace('\\', '/')] = Entry(fp);
        }
        return d;
    }

    private static Dictionary<string, FpEntry> CollectMaps(string gameDir)
    {
        var d = new Dictionary<string, FpEntry>();
        var roots = new List<(string Root, string Source)> { (Path.Combine(gameDir, "Maps"), "install") };
        foreach (var ud in UserDataDirs()) roots.Add((Path.Combine(ud, "Maps"), "utilisateur"));
        foreach (var (root, source) in roots)
        {
            if (!Directory.Exists(root)) continue;
            foreach (var fp in Directory.EnumerateFiles(root, "*.map", SearchOption.AllDirectories))
            {
                string key = Path.GetRelativePath(root, fp).Replace('\\', '/');
                if (d.ContainsKey(key)) continue;     // setdefault : premier gagne
                string? h = Hashing.FileSha256(fp);
                d[key] = new FpEntry(h?[..Math.Min(12, h.Length)], new FileInfo(fp).Length, source);
            }
        }
        return d;
    }

    private static Dictionary<string, FpEntry> CollectGentool(string gameDir)
    {
        var d = new Dictionary<string, FpEntry>();
        foreach (var name in new[] { "d3d8.dll", "gentool.dll", "GenTool.dll", "dbghelp.dll" })
        {
            string fp = Path.Combine(gameDir, name);
            if (File.Exists(fp)) d[name] = Entry(fp);
        }
        return d;
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

    private static string Sha8(string fp) => Hashing.FileSha256(fp)?[..8] ?? "présent";

    // Noms lisibles pour les addons numérotés connus.
    private static string FriendlyAddon(string fileNoExt)
    {
        string s = Regex.Replace(fileNoExt, @"^\d+_", "");
        s = s.Replace("ControlBarProArt", "Control Bar Pro (art) ").Replace("ControlBarProData", "Control Bar Pro (data) ")
             .Replace("ControlBarPro", "Control Bar Pro ").Replace("ControlBarHD", "Control Bar HD ")
             .Replace("ExpandedLANLobbyMenu", "Expanded LAN Lobby").Replace("Decals", "Decals ");
        return s.Trim();
    }

    /// <summary>Inventaire COMPLET et NOMMÉ de tout l'écosystème C&C, trié par couche (préfixe emoji).</summary>
    public static Dictionary<string, string> CollectComponents(string gameDir)
    {
        var d = new Dictionary<string, string>();
        var skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Addons", "Patches", "Tools" };

        // ── 🎮 Jeu & patch ──────────────────────────────────────────────
        foreach (var exe in new[] { "GeneralsZH.exe", "Generals.exe" })
        {
            string p = Path.Combine(gameDir, exe);
            if (File.Exists(p)) { d[$"🎮 Jeu: {exe}"] = $"{PeVersion(p) ?? "?"}  ({Sha8(p)})"; break; }
        }
        string lang = DetectLanguage(gameDir);
        if (lang.Length > 0) d["🎮 Jeu: langue"] = lang;

        // ── 🧩 Mods (GLM) : version = nom du sous-dossier ───────────────
        string glm = Path.Combine(gameDir, "GLM");
        if (Directory.Exists(glm))
            foreach (var modPath in Directory.EnumerateDirectories(glm).OrderBy(p => Path.GetFileName(p), StringComparer.Ordinal))
            {
                string mod = Path.GetFileName(modPath);
                if (skip.Contains(mod)) continue;
                List<string> subs;
                try { subs = Directory.EnumerateDirectories(modPath).Select(p => Path.GetFileName(p)!).ToList(); }
                catch { continue; }
                var versions = subs.Where(n => !skip.Contains(n)).OrderBy(n => n, StringComparer.Ordinal).ToList();
                var extras = subs.Where(n => skip.Contains(n) && !n.Equals("Tools", StringComparison.OrdinalIgnoreCase)).ToList();
                string ver = versions.Count > 0 ? string.Join(", ", versions) : "(?)";
                if (extras.Count > 0) ver += " +" + string.Join("+", extras);
                d[$"🧩 Mod: {mod}"] = ver;
            }

        // ── 📦 Addons de contenu : .big numérotés à la racine ───────────
        try
        {
            foreach (var fp in Directory.EnumerateFiles(gameDir))
            {
                string name = Path.GetFileName(fp);
                if (Regex.IsMatch(name, @"^\d+_.*\.big$", RegexOptions.IgnoreCase))
                    d[$"📦 Addon: {FriendlyAddon(Path.GetFileNameWithoutExtension(name))}"] = Sha8(fp);
            }
        }
        catch { }

        // ── 🛠 Outils & overlays ────────────────────────────────────────
        string d3d8 = Path.Combine(gameDir, "d3d8.dll");
        d["🛠 GenTool (d3d8.dll)"] = File.Exists(d3d8) ? (PeVersion(d3d8) ?? Sha8(d3d8)) : "absent";
        foreach (var (file, label) in new[]
                 { ("GenLauncher.exe", "GenLauncher"), ("EdgeScroller.exe", "EdgeScroller"),
                   ("WorldBuilder.exe", "WorldBuilder"), ("modded.exe", "modded.exe") })
        {
            string p = Path.Combine(gameDir, file);
            if (File.Exists(p)) d[$"🛠 {label}"] = PeVersion(p) ?? Sha8(p);
        }
        // dbghelp.dll est souvent renommé par GenPatcher → sa présence/absence est un signal.
        d["🛠 dbghelp.dll"] = File.Exists(Path.Combine(gameDir, "dbghelp.dll")) ? "présent" : "absent (renommé)";

        // ── ⚙ Système & en ligne ───────────────────────────────────────
        foreach (var vc in DetectVcRedists()) d[$"⚙ Système: {vc}"] = "installé";
        string dx = DetectDirectX();
        if (dx.Length > 0) d["⚙ Système: DirectX"] = dx;
        string res = DetectResolution(gameDir);
        if (res.Length > 0) d["⚙ Système: résolution"] = res;
        if (DetectGameRanger()) d["⚙ En ligne: GameRanger"] = "installé";

        return d;
    }

    // ── Détecteurs système (best-effort, Windows) ───────────────────────
    private static string DetectLanguage(string gameDir)
    {
        // 1) Registre EA, 2) heuristique fichiers *French*.
        if (OperatingSystem.IsWindows())
            foreach (var key in new[]
            {
                @"SOFTWARE\WOW6432Node\Electronic Arts\EA Games\Command and Conquer Generals Zero Hour",
                @"SOFTWARE\Electronic Arts\EA Games\Command and Conquer Generals Zero Hour",
            })
                try
                {
                    using var k = Registry.LocalMachine.OpenSubKey(key);
                    if (k?.GetValue("Language") is string s && s.Length > 0) return s;
                }
                catch { }
        try
        {
            var names = Directory.EnumerateFiles(gameDir, "*.big").Select(p => Path.GetFileName(p)!).ToList();
            if (names.Any(n => n.Contains("French", StringComparison.OrdinalIgnoreCase))) return "French";
            if (names.Any(n => n.Contains("German", StringComparison.OrdinalIgnoreCase))) return "German";
        }
        catch { }
        return "";
    }

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
                    using var k = unins.OpenSubKey(sub);
                    if (k?.GetValue("DisplayName") is string dn &&
                        dn.Contains("Visual C++", StringComparison.OrdinalIgnoreCase) &&
                        dn.Contains("Redistributable", StringComparison.OrdinalIgnoreCase) &&
                        dn.Contains("x86", StringComparison.OrdinalIgnoreCase))
                    {
                        // Garde "Visual C++ 2010 x86"
                        var m = Regex.Match(dn, @"Visual C\+\+ (\d{4})");
                        string label = m.Success ? $"VC++ {m.Groups[1].Value} x86" : dn;
                        if (!found.Contains(label)) found.Add(label);
                    }
                }
            }
            catch { }
        found.Sort(StringComparer.Ordinal);
        return found;
    }

    private static string DetectDirectX()
    {
        if (!OperatingSystem.IsWindows()) return "";
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\DirectX");
            if (k?.GetValue("Version") is string v) return v;
        }
        catch { }
        return "";
    }

    private static string DetectResolution(string gameDir)
    {
        foreach (var ud in UserDataDirs())
        {
            string opt = Path.Combine(ud, "options.ini");
            if (!File.Exists(opt)) continue;
            try
            {
                foreach (var line in File.ReadLines(opt))
                {
                    var m = Regex.Match(line, @"^\s*Resolution\s*=\s*(\d+)\s+(\d+)", RegexOptions.IgnoreCase);
                    if (m.Success) return $"{m.Groups[1].Value}×{m.Groups[2].Value}";
                }
            }
            catch { }
        }
        return "";
    }

    private static bool DetectGameRanger()
    {
        if (OperatingSystem.IsWindows())
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\GameRanger\GameRanger");
                if (k != null) return true;
            }
            catch { }
        string p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GameRanger");
        return Directory.Exists(p);
    }

    /// <summary>Construit l'empreinte de synchro complète de la machine.</summary>
    public static Fingerprint Build(string gameDir, IEnumerable<Target> targets)
    {
        var fp = new Fingerprint
        {
            Base = Entries(gameDir, ModDetection.BaseInstallFiles(gameDir)),
            LooseIni = CollectLooseIni(gameDir),
            Maps = CollectMaps(gameDir),
            Gentool = CollectGentool(gameDir),
            Components = CollectComponents(gameDir),
        };
        foreach (var t in targets)
            fp.Mods[t.Label] = Entries(gameDir, t.Files);
        return fp;
    }

    // ===== Export / import JSON (compatible avec la version Python) =====
    public static string ExportJson(Fingerprint fp)
    {
        static object ArrOf(FpEntry e) =>
            e.Source == null ? new object?[] { e.Sha, e.Size } : new object?[] { e.Sha, e.Size, e.Source };
        static Dictionary<string, object> Sec(Dictionary<string, FpEntry> s) =>
            s.ToDictionary(kv => kv.Key, kv => ArrOf(kv.Value));

        var root = new Dictionary<string, object?>
        {
            ["kind"] = "genspeed-sync-fingerprint",
            ["v"] = 2,
            ["generated"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            ["base"] = Sec(fp.Base),
            ["mods"] = fp.Mods.ToDictionary(kv => kv.Key, kv => Sec(kv.Value)),
            ["loose_ini"] = Sec(fp.LooseIni),
            ["maps"] = Sec(fp.Maps),
            ["gentool"] = Sec(fp.Gentool),
            ["components"] = fp.Components,
        };
        return JsonSerializer.Serialize(root, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
    }

    public static Fingerprint Parse(string json)
    {
        var fp = new Fingerprint();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        static FpEntry ReadEntry(JsonElement arr)
        {
            string? sha = arr.GetArrayLength() > 0 && arr[0].ValueKind == JsonValueKind.String ? arr[0].GetString() : null;
            long size = arr.GetArrayLength() > 1 && arr[1].ValueKind == JsonValueKind.Number ? arr[1].GetInt64() : 0;
            string? src = arr.GetArrayLength() > 2 && arr[2].ValueKind == JsonValueKind.String ? arr[2].GetString() : null;
            return new FpEntry(sha, size, src);
        }
        static Dictionary<string, FpEntry> ReadSec(JsonElement obj)
        {
            var d = new Dictionary<string, FpEntry>();
            if (obj.ValueKind == JsonValueKind.Object)
                foreach (var p in obj.EnumerateObject())
                    if (p.Value.ValueKind == JsonValueKind.Array) d[p.Name] = ReadEntry(p.Value);
            return d;
        }

        if (root.TryGetProperty("base", out var b)) fp.Base = ReadSec(b);
        if (root.TryGetProperty("loose_ini", out var li)) fp.LooseIni = ReadSec(li);
        if (root.TryGetProperty("maps", out var mp)) fp.Maps = ReadSec(mp);
        if (root.TryGetProperty("gentool", out var gt)) fp.Gentool = ReadSec(gt);
        if (root.TryGetProperty("mods", out var mods) && mods.ValueKind == JsonValueKind.Object)
            foreach (var p in mods.EnumerateObject()) fp.Mods[p.Name] = ReadSec(p.Value);
        if (root.TryGetProperty("components", out var comp) && comp.ValueKind == JsonValueKind.Object)
            fp.Components = comp.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "");
        else
            fp.HasComponents = false;
        return fp;
    }

    public static bool IsSyncFingerprint(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("kind", out var k)
                   && k.GetString() == "genspeed-sync-fingerprint";
        }
        catch { return false; }
    }

    // ===== Comparaison =====
    public static List<DiffEntry> Diff(Fingerprint mine, Fingerprint other)
    {
        var diffs = new List<DiffEntry>();

        void Cmp(string sectionKey, Dictionary<string, FpEntry> a, Dictionary<string, FpEntry> b, Severity sev)
        {
            foreach (var k in a.Keys.Union(b.Keys).OrderBy(x => x, StringComparer.Ordinal))
            {
                bool ina = a.TryGetValue(k, out var va), inb = b.TryGetValue(k, out var vb);
                if (!ina) diffs.Add(new DiffEntry(sectionKey, k, DiffStatus.AbsentMine, sev));
                else if (!inb) diffs.Add(new DiffEntry(sectionKey, k, DiffStatus.AbsentOther, sev));
                else if (va!.Canon != vb!.Canon) diffs.Add(new DiffEntry(sectionKey, k, DiffStatus.Different, sev));
            }
        }

        // Composants NOMMÉS d'abord (le plus lisible) — seulement si les deux côtés en ont.
        if (mine.HasComponents && other.HasComponents)
        {
            foreach (var k in mine.Components.Keys.Union(other.Components.Keys).OrderBy(x => x, StringComparer.Ordinal))
            {
                bool ina = mine.Components.TryGetValue(k, out var va), inb = other.Components.TryGetValue(k, out var vb);
                // Gravité par couche : 🎮 jeu / 🧩 mods = critique ; 📦 addons = attention ; 🛠 ⚙ = info.
                Severity sev = (k.StartsWith("🎮") || k.StartsWith("🧩")) ? Severity.Critique
                             : k.StartsWith("📦") ? Severity.Attention : Severity.Info;
                if (!ina) diffs.Add(new DiffEntry("sec.components", k, DiffStatus.AbsentMine, sev, null, vb));
                else if (!inb) diffs.Add(new DiffEntry("sec.components", k, DiffStatus.AbsentOther, sev, va, null));
                else if (va != vb) diffs.Add(new DiffEntry("sec.components", k, DiffStatus.Different, sev, va, vb));
            }
        }

        Cmp("sec.base", mine.Base, other.Base, Severity.Critique);
        Cmp("sec.ini", mine.LooseIni, other.LooseIni, Severity.Critique);
        foreach (var label in mine.Mods.Keys.Union(other.Mods.Keys).OrderBy(x => x, StringComparer.Ordinal))
        {
            mine.Mods.TryGetValue(label, out var ma);
            other.Mods.TryGetValue(label, out var mb);
            if (ma == null) diffs.Add(new DiffEntry("mod:" + label, "(tout le mod)", DiffStatus.AbsentMine, Severity.Critique));
            else if (mb == null) diffs.Add(new DiffEntry("mod:" + label, "(tout le mod)", DiffStatus.AbsentOther, Severity.Critique));
            else Cmp("mod:" + label, ma, mb, Severity.Critique);
        }
        Cmp("sec.maps", mine.Maps, other.Maps, Severity.Attention);
        Cmp("sec.gentool", mine.Gentool, other.Gentool, Severity.Info);

        return diffs;
    }
}
