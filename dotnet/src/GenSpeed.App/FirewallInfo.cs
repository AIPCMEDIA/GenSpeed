using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using GenSpeed.Core;

namespace GenSpeed.App;

/// <summary>Autorise le(s) exe du jeu dans le pare-feu Windows (entrant) pour que l'hébergement LAN/online marche
/// sans que Windows bloque. Comme un installeur de jeu. AJOUT uniquement, ÉLEVÉ (UAC) — donc déclenché par un clic
/// explicite de l'utilisateur (case à cocher), jamais en silence. Règles préfixées « GenSpeed - ZH » pour pouvoir
/// détecter qu'on les a déjà posées.</summary>
internal static class FirewallInfo
{
    private const string Prefix = "GenSpeed - ZH";

    /// <summary>Exes du jeu à autoriser : Generals.exe + modded.exe de chaque install connue (dédupliqués).</summary>
    public static List<string> GameExes(GenConfig c)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in c.KnownInstalls)
            foreach (var n in new[] { "Generals.exe", "modded.exe" })
            {
                try { string p = Path.Combine(d, n); if (File.Exists(p)) set.Add(p); } catch { }
            }
        return set.ToList();
    }

    /// <summary>Vrai si NOS règles pare-feu (« GenSpeed - ZH … ») existent déjà → on pré-décoche par défaut.</summary>
    public static bool RuleExists()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"if (Get-NetFirewallRule -DisplayName '{Prefix}*' -ErrorAction SilentlyContinue) {{ exit 0 }} else {{ exit 1 }}\"",
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            };
            var p = Process.Start(psi);
            if (p == null) return false;
            p.WaitForExit(8000);
            return p.ExitCode == 0;
        }
        catch { return false; }
    }

    /// <summary>Ajoute (ÉLEVÉ/UAC) une règle entrante « Autoriser » par exe pour les profils choisis (Privé/Public).
    /// Idempotent côté affichage (nos règles sont nommées). Renvoie faux si rien à faire / UAC refusé / échec.</summary>
    public static bool AddElevated(IEnumerable<string> exes, bool priv, bool pub)
    {
        var profiles = new List<string>();
        if (priv) profiles.Add("Private");
        if (pub) profiles.Add("Public");
        if (profiles.Count == 0) return false;
        string prof = string.Join(",", profiles);

        var sb = new StringBuilder();
        int n = 0;
        foreach (var e in exes)
        {
            string ee = e.Replace("'", "''");
            string label = $"{Prefix} {Path.GetFileName(Path.GetDirectoryName(e) ?? "")} {Path.GetFileName(e)}".Replace("'", "''");
            sb.Append($"New-NetFirewallRule -DisplayName '{label}' -Direction Inbound -Program '{ee}' -Action Allow -Profile {prof} -ErrorAction SilentlyContinue | Out-Null; ");
            n++;
        }
        if (n == 0) return false;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{sb}\"",
                UseShellExecute = true, Verb = "runas", CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            };
            var p = Process.Start(psi);
            if (p == null) return false;
            p.WaitForExit(20000);
            return p.ExitCode == 0;
        }
        catch { return false; }   // UAC refusé / erreur
    }
}
