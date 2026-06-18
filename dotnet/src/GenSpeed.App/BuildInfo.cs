using System;
using System.IO;

namespace GenSpeed.App;

/// <summary>Version + horodatage du build = date de l'EXE en cours d'exécution. Affiché dans l'en-tête : permet de
/// vérifier d'un coup d'œil qu'on lance bien le dernier build. Un raccourci pointant vers un exe périmé affichera
/// une date ancienne → fin des doutes « est-ce le bon build ? ».</summary>
internal static class BuildInfo
{
    /// <summary>Version « produit » (à bumper manuellement aux jalons).</summary>
    public const string Version = "v2.5-beta";

    /// <summary>Date/heure de l'exe en cours (jj/MM HH:mm), ou « ? » si indéterminable.</summary>
    public static string Stamp()
    {
        try
        {
            string? path = Environment.ProcessPath;   // .NET 6+ : chemin de l'exe lancé (Debug ET single-file)
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
                return File.GetLastWriteTime(path).ToString("dd/MM HH:mm");
        }
        catch { }
        return "?";
    }

    /// <summary>Libellé complet, ex. « v2.4-beta · build 18/06 09:12 ».</summary>
    public static string Label() => $"{Version}  ·  build {Stamp()}";
}
