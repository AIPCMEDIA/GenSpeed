using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using YamlDotNet.RepresentationModel;

namespace GenSpeed.Core;

/// <summary>Cohérence GenLauncher : après suppression des fichiers d'un mod/addon, passe son entrée
/// à Installed:false (et IsSelected:false) dans GenLauncherCfg.yaml. On édite UNIQUEMENT les lignes
/// ciblées (le reste du fichier reste identique au caractère près) — pas de re-sérialisation qui
/// polluerait le YAML d'ancres parasites. Le YAML est sauvegardé AVANT toute modification.</summary>
public static class GenLauncherYaml
{
    /// <param name="dependence">Mod parent (pour addon/patch) afin d'éviter de toucher un homonyme d'un
    /// autre mod ; null = c'est un mod (le mod lui-même ET ses addons/patches).</param>
    public static void MarkUninstalled(string yamlPath, string name, string? dependence, string backupDir)
    {
        if (!File.Exists(yamlPath) || string.IsNullOrEmpty(name)) return;

        // Sauvegarde (une seule fois par exécution).
        try
        {
            string bdir = Path.Combine(backupDir, "genlauncher-yaml");
            Directory.CreateDirectory(bdir);
            string bak = Path.Combine(bdir, Path.GetFileName(yamlPath));
            if (!File.Exists(bak)) File.Copy(yamlPath, bak, overwrite: true);
        }
        catch { }

        // 1) Parse pour LOCALISER les lignes à modifier (sans réécrire le document).
        var stream = new YamlStream();
        using (var rdr = new StreamReader(yamlPath)) stream.Load(rdr);
        if (stream.Documents.Count == 0) return;

        var targetLines = new HashSet<int>();   // indices 0-based de lignes "...: true" à passer à false
        Collect(stream.Documents[0].RootNode, name, dependence, targetLines);
        if (targetLines.Count == 0) return;

        // 2) Édition ciblée des seules lignes repérées (le reste est préservé tel quel).
        var lines = File.ReadAllLines(yamlPath).ToList();
        bool any = false;
        foreach (int ln in targetLines)
        {
            if (ln < 0 || ln >= lines.Count) continue;
            string updated = Regex.Replace(lines[ln], @"(:\s*)true(\s*)$", "${1}false${2}");
            if (updated != lines[ln]) { lines[ln] = updated; any = true; }
        }
        if (any) File.WriteAllLines(yamlPath, lines, new UTF8Encoding(false));
    }

    /// <summary>Collecte les n° de ligne des valeurs Installed/IsSelected (==true) à basculer,
    /// sur toute entrée dont Name == name (et DependenceName == dependence si fourni ;
    /// pour un mod, on prend aussi ses addons via DependenceName == name).</summary>
    private static void Collect(YamlNode node, string name, string? dependence, HashSet<int> lines)
    {
        if (node is YamlMappingNode map)
        {
            string? mName = GetScalar(map, "Name");
            string? mDep = GetScalar(map, "DependenceName");
            bool match = dependence == null
                ? (mName == name || mDep == name)
                : (mName == name && mDep == dependence);
            if (match)
            {
                AddTrueLine(map, "Installed", lines);
                AddTrueLine(map, "IsSelected", lines);
            }
            foreach (var v in map.Children.Values.ToList()) Collect(v, name, dependence, lines);
        }
        else if (node is YamlSequenceNode seq)
        {
            foreach (var c in seq.Children.ToList()) Collect(c, name, dependence, lines);
        }
    }

    private static string? GetScalar(YamlMappingNode map, string key)
    {
        foreach (var kv in map.Children)
            if (kv.Key is YamlScalarNode k && k.Value == key && kv.Value is YamlScalarNode v) return v.Value;
        return null;
    }

    private static void AddTrueLine(YamlMappingNode map, string key, HashSet<int> lines)
    {
        foreach (var kv in map.Children)
            if (kv.Key is YamlScalarNode k && k.Value == key && kv.Value is YamlScalarNode v && v.Value == "true")
            {
                lines.Add((int)v.Start.Line - 1);   // Mark.Line est 1-based ; clé+valeur sur la même ligne
                return;
            }
    }
}
