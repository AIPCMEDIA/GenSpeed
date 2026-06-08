using System.Globalization;
using System.Text.Json;
using GenSpeed.Core;

// Outil de vérification : reproduit les opérations du cœur pour comparaison
// octet-pour-octet avec la version Python.
//
// Usage :
//   CoreCheck patchbig   <in.big> <out.big> <factorsJson>
//   CoreCheck applytext  <in.txt> <out.txt> <factorsJson>   (latin-1)
//   CoreCheck installhash <gameDir> <file...>
//   CoreCheck filesha    <path>

if (args.Length == 0)
{
    Console.Error.WriteLine("commande manquante");
    return 2;
}

static Dictionary<string, double> ParseFactors(string json)
{
    var raw = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)
              ?? new Dictionary<string, JsonElement>();
    var d = new Dictionary<string, double>();
    foreach (var (k, v) in raw)
        d[k] = v.ValueKind == JsonValueKind.String
            ? double.Parse(v.GetString()!, CultureInfo.InvariantCulture)
            : v.GetDouble();
    return d;
}

switch (args[0])
{
    case "patchbig":
    {
        var entries = BigArchive.Read(args[1]);
        Dictionary<string, string?>? cam = null;
        if (args.Length > 4)
            cam = JsonSerializer.Deserialize<Dictionary<string, string?>>(args[4]);
        IniScaler.PatchBigEntries(entries, ParseFactors(args[3]), cam);
        BigArchive.Write(args[2], entries);
        Console.WriteLine(Hashing.FileSha256(args[2]));
        return 0;
    }
    case "applytext":
    {
        byte[] inBytes = File.ReadAllBytes(args[1]);
        string original = System.Text.Encoding.Latin1.GetString(inBytes);
        string patched = IniScaler.ApplyText(original, ParseFactors(args[3]));
        File.WriteAllBytes(args[2], System.Text.Encoding.Latin1.GetBytes(patched));
        Console.WriteLine(Hashing.FileSha256(args[2]));
        return 0;
    }
    case "installhash":
    {
        string gameDir = args[1];
        var files = args.Skip(2).ToArray();
        var r = Hashing.InstallHash(gameDir, files);
        Console.WriteLine($"{r.Hash} {r.FileCount} {r.TotalBytes}");
        return 0;
    }
    case "filesha":
        Console.WriteLine(Hashing.FileSha256(args[1]));
        return 0;
    case "detect":
    {
        foreach (var t in ModDetection.DetectTargets(args[1]))
            Console.WriteLine($"{t.Label}\t{t.Type.ToString().ToLowerInvariant()}\t{t.ArchiveCount}\t{t.IniCount()}");
        return 0;
    }
    case "locate":
        Console.WriteLine(GameLocator.Detect() ?? "(introuvable)");
        return 0;
    case "components":
        foreach (var (k, v) in Diagnostics.CollectComponents(args[1]))
            Console.WriteLine($"{k}  =  {v}");
        return 0;
    case "fingerprint":   // <gameDir> <outfile>
    {
        var mods = ModDetection.DetectTargets(args[1]).Where(t => t.Type == TargetType.Gib);
        File.WriteAllText(args[2], Diagnostics.ExportJson(Diagnostics.Build(args[1], mods)));
        Console.WriteLine("écrit: " + args[2]);
        return 0;
    }
    case "diff":          // <gameDir> <otherFingerprint.json>
    {
        var mods = ModDetection.DetectTargets(args[1]).Where(t => t.Type == TargetType.Gib);
        var mine = Diagnostics.Build(args[1], mods);
        var other = Diagnostics.Parse(File.ReadAllText(args[2]));
        foreach (var d in Diagnostics.Diff(mine, other))
            Console.WriteLine($"[{d.Severity,-9}] {d.SectionKey,-16} {d.Item}  |  {d.Status}  {(d.Mine ?? "")} ↔ {(d.Other ?? "")}");
        return 0;
    }
    case "roundtrip":   // <bigfile> <factorsJson> : patch -> backup -> restore
    {
        string file = args[1];
        var t = new Target { Label = "test", Type = TargetType.Big, Files = new() { file } };
        string sha0 = Hashing.FileSha256(file)!;
        Patcher.PatchTarget(t, ParseFactors(args[2]), null, new Dictionary<string, string>());
        string sha1 = Hashing.FileSha256(file)!;
        bool bak1 = File.Exists(file + ".speedbak");
        Patcher.RestoreFiles(new[] { file }, Array.Empty<string>());
        string sha2 = Hashing.FileSha256(file)!;
        bool bak2 = File.Exists(file + ".speedbak");
        Console.WriteLine($"orig={sha0[..12]} patched={sha1[..12]} restored={sha2[..12]}");
        Console.WriteLine($"backup_créé={bak1} backup_supprimé={!bak2}");
        Console.WriteLine((sha1 != sha0 && sha2 == sha0 && bak1 && !bak2)
            ? "✅ ROUND-TRIP OK (patché puis restauré à l'identique)"
            : "❌ ROUND-TRIP ÉCHEC");
        return 0;
    }
    default:
        Console.Error.WriteLine($"commande inconnue: {args[0]}");
        return 2;
}
