using System.Text.Json.Serialization;

namespace GenSpeed.Core;

/// <summary>Définition d'un fork « autonome » (moteur SAGE recompilé : exe custom + fichiers Data\INI en vrac,
/// PAS de .gib, donc HORS catalogue GenLauncher). Ex. Reborn Omega, plus tard Generals X. Distribué via une
/// release GitHub (un ou plusieurs .exe + une archive de données). Voir [[zh-ecosystem-knowledge-base]].
///
/// Un fork s'installe sur une COPIE de M0 vierge (jamais sur M0) : voir <see cref="ForkInstaller"/>.</summary>
public sealed class ForkDef
{
    /// <summary>Slug stable (clé) — sert d'identité de l'install et de nom de dossier par défaut.</summary>
    [JsonPropertyName("id")]   public string Id { get; set; } = "";
    /// <summary>Nom affiché (« Reborn Omega »).</summary>
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    /// <summary>Dépôt GitHub « owner/name » d'où lire la dernière release (ex. « gamezerve/Reborn-Omega »).
    /// Utilisé seulement si <see cref="DataUrl"/> est vide.</summary>
    [JsonPropertyName("repo")] public string Repo { get; set; } = "";
    /// <summary>URL de téléchargement de la distribution OFFICIELLE qui fonctionne (page ModDB
    /// « downloads/start/… », lien mirror ModDB, ou URL directe d'une archive). PRIORITAIRE sur GitHub : pour
    /// Reborn Omega, le packaging ModDB (.pak) est celui qui produit un jeu jouable, pas le zip GitHub.</summary>
    [JsonPropertyName("data_url")] public string DataUrl { get; set; } = "";
    /// <summary>Regex (insensible casse) qui sélectionne l'archive de DONNÉES dans les assets de la release.
    /// .zip uniquement (extraction native) — un fork qui ne livre que du .rar passe par l'overlay manuel.</summary>
    [JsonPropertyName("data_asset_regex")] public string DataAssetRegex { get; set; } = @"\.zip$";
    /// <summary>Note libre affichée à l'utilisateur (consignes d'install du fork, avertissements…).</summary>
    [JsonPropertyName("notes")] public string Notes { get; set; } = "";

    public ForkDef Clone() => new() { Id = Id, Name = Name, Repo = Repo, DataUrl = DataUrl, DataAssetRegex = DataAssetRegex, Notes = Notes };
}

/// <summary>Catalogue de forks ÉDITABLE : défauts codés + surcharges/ajouts de la config (comme
/// <see cref="DownloadLinks"/>). Un nouveau fork (Generals X…) s'ajoute SANS recompiler, via le panneau in-app
/// ou le JSON. La liste de la config, si non vide, REMPLACE les défauts (l'utilisateur garde la main).</summary>
public static class ForkCatalog
{
    /// <summary>Forks connus livrés avec GenSpeed. Generals X n'y est pas encore (pas de release publique au
    /// moment d'écrire) — il s'ajoutera ici, ou via la config dès sa sortie.</summary>
    public static IReadOnlyList<ForkDef> Defaults() => new List<ForkDef>
    {
        new()
        {
            Id   = "reborn-omega",
            Name = "Reborn Omega",
            Repo = "gamezerve/Reborn-Omega",
            // Archive de données type « RebornOmegaData.zip » ; on cible large (toute .zip) et on
            // privilégiera la plus grosse à l'install si plusieurs.
            DataAssetRegex = @"\.zip$",
            Notes = "Gros mod « total » sur moteur communautaire 2025 (nouvelles unités, factions, équilibrage).",
        },
    };

    /// <summary>Catalogue effectif : la liste de la config si elle contient au moins une entrée valide,
    /// sinon les défauts. Toujours au moins les défauts (jamais vide).</summary>
    public static List<ForkDef> Effective(IEnumerable<ForkDef>? configForks)
    {
        var fromCfg = (configForks ?? Enumerable.Empty<ForkDef>())
            .Where(f => f != null && !string.IsNullOrWhiteSpace(f.Id) && !string.IsNullOrWhiteSpace(f.Repo))
            .ToList();
        return fromCfg.Count > 0 ? fromCfg : Defaults().Select(d => d.Clone()).ToList();
    }

    /// <summary>Cherche un fork par id (insensible casse) dans le catalogue effectif.</summary>
    public static ForkDef? Find(IEnumerable<ForkDef>? configForks, string id)
        => Effective(configForks).FirstOrDefault(f => string.Equals(f.Id, id, StringComparison.OrdinalIgnoreCase));
}
