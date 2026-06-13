namespace GenSpeed.Core;

/// <summary>Référence « known-good » des binaires tiers de l'écosystème ZH (table versionnée — voir la base
/// de connaissance §10, relevé 2026-06-11).
///
/// CONÇU NEUTRE, PAS ALARMISTE (leçon du serial LAN) : un hash absent de la table = le plus souvent une
/// simple version PLUS RÉCENTE (GenTool/GenLauncher se mettent à jour), PAS une menace. On annote donc
/// « référence connue » / « non répertorié » sans jamais crier au loup. Pour TRANCHER la sécurité d'un
/// binaire, on renvoie vers VirusTotal (70 antivirus, base toujours à jour) plutôt que de juger localement
/// sur une liste figée qui deviendrait obsolète.</summary>
public static class KnownBinaries
{
    public enum Status { NotTracked, Known, Unlisted }

    // Clé = nom de fichier (insensible à la casse) ; valeurs = (préfixe SHA-256 connu-sain, libellé version).
    // Plusieurs entrées possibles par fichier au fil des versions vérifiées.
    private static readonly Dictionary<string, List<(string ShaPrefix, string Label)>> Table =
        new(StringComparer.OrdinalIgnoreCase)
    {
        ["d3d8.dll"]           = new() { ("be5276180d04b3de", "GenTool 8.9") },
        ["GenLauncher.exe"]    = new() { ("636f7d03f0fc36cf", "GenLauncher 2025-02-28") },
        ["GenToolUpdater.exe"] = new() { ("77dc12afd049db59", "GenTool updater") },
        ["modded.exe"]         = new() { ("4ea7e86baffbc949", "modded.exe (xezon)") },
        ["EdgeScroller.exe"]   = new() { ("021f2db9ef1564a8", "EdgeScroller 2026-01-21") },
        ["GenPatcher.exe"]     = new() { ("9c940f59503e099b", "GenPatcher v2.14") },
        // Game.dat : binaire EA OFFICIEL signé, variable selon langue/édition → vérifié par signature
        //            Authenticode (cf. diagnostic), pas par hash figé.
    };

    /// <summary>Vrai si ce nom de fichier fait partie des binaires tiers qu'on sait identifier.</summary>
    public static bool IsTracked(string fileName) => Table.ContainsKey(System.IO.Path.GetFileName(fileName));

    /// <summary>Statut NEUTRE d'un binaire : non suivi / référence connue (+ libellé) / répertorié mais hash
    /// non listé (souvent une version plus récente — à vérifier sur VirusTotal, pas une alarme).</summary>
    public static (Status Status, string? Label) Identify(string path)
    {
        string name = System.IO.Path.GetFileName(path);
        if (!Table.TryGetValue(name, out var list)) return (Status.NotTracked, null);
        string? sha = Hashing.FileSha256(path);
        if (sha == null) return (Status.Unlisted, null);
        foreach (var (prefix, label) in list)
            if (sha.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
                return (Status.Known, label);
        return (Status.Unlisted, null);
    }

    /// <summary>Libellé court et neutre prêt à afficher (jamais une alarme).</summary>
    public static string Describe(string path)
    {
        var (status, label) = Identify(path);
        return status switch
        {
            Status.Known     => $"✓ référence connue ({label})",
            Status.Unlisted  => "version non répertoriée — vérifiable sur VirusTotal",
            _                => "non suivi",
        };
    }

    /// <summary>URL VirusTotal pour le SHA-256 du fichier (recherche par hash : aucun fichier n'est envoyé,
    /// seul le hash — déjà public pour ces binaires communautaires — sert de clé de recherche).</summary>
    public static string? VirusTotalUrl(string path)
    {
        string? sha = Hashing.FileSha256(path);
        return sha == null ? null : $"https://www.virustotal.com/gui/file/{sha}";
    }
}
