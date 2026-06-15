using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace GenSpeed.App;

/// <summary>Énumère les IP des cartes réseau pour que l'utilisateur CHOISISSE celle que le jeu utilise — comme
/// dans Options → Réseau du jeu, qui écrit deux clés d'Options.ini : « IPAddress » (LAN) et « GameSpyIPAddress »
/// (en ligne), au format pointé (192.168.x ; « 0.0.0.0 » = auto). C'est la bonne approche pour le souci « le jeu
/// montre une IP 172.x (Hyper-V) en LAN » : on lui dit d'utiliser le 192.168.x, sans toucher au système.
/// 100% local (System.Net.NetworkInformation). Voir [[lan-mismatch-problem]].</summary>
internal static class NetInfo
{
    public const string Auto = "0.0.0.0";

    /// <summary>Une IP candidate proposée à l'utilisateur, avec de quoi l'étiqueter (réseau local vs virtuelle).</summary>
    public sealed record IpCandidate(string Ip, string AdapterName, bool IsLan, bool IsParasite);

    /// <summary>IPv4 utilisables (cartes Up, hors loopback et hors APIPA 169.254). Triées : le LAN 192.168 d'abord.</summary>
    public static List<IpCandidate> Candidates()
    {
        var list = new List<IpCandidate>();
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                string d = (ni.Description + " " + ni.Name).ToLowerInvariant();
                bool virt = d.Contains("hyper-v") || d.Contains("vethernet") || d.Contains("virtual") || d.Contains("wsl")
                         || d.Contains("vmware") || d.Contains("virtualbox") || d.Contains("default switch")
                         || d.Contains("tap-") || d.Contains("tunnel") || d.Contains("vpn");
                IPInterfaceProperties props;
                try { props = ni.GetIPProperties(); } catch { continue; }
                foreach (var ua in props.UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    string ip = ua.Address.ToString();
                    if (ip.StartsWith("127.") || ip.StartsWith("169.254.")) continue;   // loopback / APIPA
                    bool isLan = ip.StartsWith("192.168.");
                    list.Add(new(ip, ni.Name, isLan, virt && IsHijackIp(ip)));
                }
            }
        }
        catch { }
        return list.OrderByDescending(c => c.IsLan).ThenBy(c => c.IsParasite).ToList();
    }

    /// <summary>IP « pirate » pour un LAN domestique (192.168.x) : 172.16/12 ou 10.x — typiquement Hyper-V/VPN.</summary>
    private static bool IsHijackIp(string ip)
    {
        var p = ip.Split('.');
        if (p.Length != 4 || !int.TryParse(p[0], out int a) || !int.TryParse(p[1], out int b)) return false;
        if (a == 10) return true;
        if (a == 172 && b >= 16 && b <= 31) return true;
        return false;
    }

    /// <summary>IP du vrai LAN domestique (192.168.x) si présente, sinon null — le bon défaut pour le LAN.</summary>
    public static string? LanIp() => Candidates().FirstOrDefault(c => c.IsLan)?.Ip;
}
