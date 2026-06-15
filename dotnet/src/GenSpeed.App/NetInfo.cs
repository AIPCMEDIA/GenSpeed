using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace GenSpeed.App;

/// <summary>Détecte les adaptateurs réseau « parasites » qui font que C&C Generals ZH choisit la MAUVAISE IP
/// en LAN (ex. Hyper-V « Default Switch » en 172.x, WSL, VPN, VirtualBox…). Le jeu énumère les cartes et peut
/// se lier à une virtuelle (172.16/12 ou 10.x) au lieu du vrai LAN 192.168.x → les autres joueurs ne le voient
/// pas. 100% local (System.Net.NetworkInformation), aucun changement sans action explicite de l'utilisateur.</summary>
internal static class NetInfo
{
    public sealed record Adapter(string Name, string Desc, string Ipv4, bool Up, bool Virtual, bool Parasitic);

    /// <summary>Tous les adaptateurs (hors loopback) avec leur IPv4, statut, et s'ils sont « parasites » LAN.</summary>
    public static List<Adapter> All()
    {
        var list = new List<Adapter>();
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                string ip = "";
                try { ip = ni.GetIPProperties().UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)?.Address.ToString() ?? ""; }
                catch { }
                bool up = ni.OperationalStatus == OperationalStatus.Up;
                string d = (ni.Description + " " + ni.Name).ToLowerInvariant();
                bool virt = d.Contains("hyper-v") || d.Contains("vethernet") || d.Contains("virtual") || d.Contains("wsl")
                         || d.Contains("vmware") || d.Contains("virtualbox") || d.Contains("default switch")
                         || d.Contains("tap-") || d.Contains("tunnel") || d.Contains("vpn");
                bool parasitic = up && virt && IsHijackIp(ip);
                list.Add(new(ni.Name, ni.Description, ip, up, virt, parasitic));
            }
        }
        catch { }
        return list;
    }

    /// <summary>IP « pirate » du point de vue d'un LAN domestique (qui est en 192.168.x) : 172.16.0.0/12 ou 10.0.0.0/8.</summary>
    private static bool IsHijackIp(string ip)
    {
        if (string.IsNullOrEmpty(ip)) return false;
        var p = ip.Split('.');
        if (p.Length != 4 || !int.TryParse(p[0], out int a) || !int.TryParse(p[1], out int b)) return false;
        if (a == 10) return true;
        if (a == 172 && b >= 16 && b <= 31) return true;
        return false;
    }

    /// <summary>IP du vrai LAN domestique (192.168.x) si présente, sinon null.</summary>
    public static string? LanIp() => All().FirstOrDefault(x => x.Up && x.Ipv4.StartsWith("192.168."))?.Ipv4;

    /// <summary>Adaptateurs parasites actuellement actifs (Up + virtuel + IP 172/10).</summary>
    public static List<Adapter> Parasites() => All().Where(x => x.Parasitic).ToList();

    /// <summary>Désactive un adaptateur via PowerShell ÉLEVÉ (UAC) — l'utilisateur valide la fenêtre.
    /// Réversible (Enable-NetAdapter). Renvoie true si la commande a fini avec succès (code 0).</summary>
    public static bool DisableElevated(string name)
    {
        try
        {
            string safe = name.Replace("'", "''");
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"Disable-NetAdapter -Name '{safe}' -Confirm:$false\"",
                UseShellExecute = true,
                Verb = "runas",
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            var p = Process.Start(psi);
            if (p == null) return false;
            p.WaitForExit(20000);
            return p.ExitCode == 0;
        }
        catch { return false; }   // UAC refusé (Win32Exception) ou autre → échec propre
    }
}
