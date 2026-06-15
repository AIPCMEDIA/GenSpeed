using System;
using System.Runtime.InteropServices;

namespace GenSpeed.App;

/// <summary>Infos matériel LOCALES (GPU + RAM via Win32, AUCUN envoi réseau — GenSpeed reste sans télémétrie)
/// pour RECOMMANDER un niveau graphique. L'utilisateur reste libre de changer.</summary>
internal static class PcInfo
{
    /// <summary>Nom de la carte graphique principale (ex. « Intel(R) UHD Graphics 620 », « NVIDIA GeForce… »).</summary>
    public static string Gpu()
    {
        try
        {
            var d = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
            if (EnumDisplayDevices(null, 0, ref d, 0)) return d.DeviceString ?? "";
        }
        catch { }
        return "";
    }

    /// <summary>RAM physique totale en octets (0 si indéterminé).</summary>
    public static long RamBytes()
    {
        try { var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() }; if (GlobalMemoryStatusEx(ref m)) return (long)m.ullTotalPhys; }
        catch { }
        return 0;
    }

    /// <summary>Niveau graphique recommandé : « light » (iGPU / peu de RAM), « high » (carte dédiée + RAM ok),
    /// sinon « balanced ». Heuristique douce — c'est une SUGGESTION, l'utilisateur tranche.</summary>
    public static string RecommendedGraphics()
    {
        string g = Gpu().ToLowerInvariant();
        long ram = RamBytes();
        const long GB = 1024L * 1024 * 1024;
        bool dedicated = g.Contains("nvidia") || g.Contains("geforce") || g.Contains("quadro") || g.Contains("rtx") || g.Contains("gtx") || g.Contains("radeon") || g.Contains("amd");
        bool iGpu = g.Contains("intel") && (g.Contains("uhd") || g.Contains("hd ") || g.Contains("iris") || g.Contains("graphics"));
        if (dedicated && ram >= 8 * GB) return "high";
        if (iGpu || (ram > 0 && ram < 8 * GB)) return "light";
        return "balanced";
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplayDevices(string? device, uint iDevNum, ref DISPLAY_DEVICE info, uint flags);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }
}
