namespace GenSpeed.Core;

/// <summary>Registre central des liens de telechargement dont GenSpeed a besoin, avec leurs DEFAUTS.
/// Surchargeables via la config (section "links" du JSON) ou le panneau in-app -> un lien casse (URL
/// Microsoft qui bouge, etc.) se corrige SANS recompiler. Tout le code de download lit via GenConfig.Link(cle).</summary>
public static class DownloadLinks
{
    public sealed record Entry(string Key, string Label, string DefaultUrl);

    /// <summary>Liste ordonnee (sert aussi a l'affichage du panneau d'edition).</summary>
    public static readonly Entry[] All =
    {
        new("genlauncher_zip",      "GenLauncher - zip direct",             "https://gen.insave.ovh/genlauncher/launcher/GenLauncher1010.zip"),
        new("genlauncher_manifest", "GenLauncher - manifeste (catalogue)",  "https://raw.githubusercontent.com/p0ls3r/GenLauncherModsData/master/ReposModificationDataZH4.yaml"),
        new("genlauncher_moddb",    "GenLauncher - page ModDB (secours)",   "https://www.moddb.com/mods/genlauncher/downloads"),
        new("vcredist_2005_x86",    "VC++ 2005 SP1 (x86)",                  "https://download.microsoft.com/download/8/B/4/8B42259F-5D70-43F4-AC2E-4B208FD8D66A/vcredist_x86.EXE"),
        new("vcredist_2008_x86",    "VC++ 2008 SP1 (x86)",                  "https://download.microsoft.com/download/5/D/8/5D8C65CB-C849-4025-8E95-C3966CAFD8AE/vcredist_x86.exe"),
        new("vcredist_2010_x86",    "VC++ 2010 SP1 (x86)",                  "https://download.microsoft.com/download/1/6/5/165255E7-1014-4D0A-B094-B6A430A6BFFC/vcredist_x86.exe"),
        new("directx_redist",       "DirectX June 2010 (redist hors-ligne)","https://download.microsoft.com/download/8/4/A/84A35BF1-DAFE-4AE8-82AF-AD2AE20B6B14/directx_Jun2010_redist.exe"),
        new("directx_page",         "DirectX End-User Runtime (page MS)",   "https://www.microsoft.com/en-us/download/details.aspx?id=35"),
    };

    /// <summary>Defaut code pour une cle (chaine vide si cle inconnue).</summary>
    public static string DefaultFor(string key)
    {
        foreach (var e in All) if (e.Key == key) return e.DefaultUrl;
        return "";
    }
}
