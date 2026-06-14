using System.Runtime.InteropServices;

namespace GenSpeed.App;

/// <summary>Infos écran via Win32. Sert à poser la résolution NATIVE dans Options.ini.</summary>
internal static class ScreenInfo
{
    /// <summary>Résolution physique de l'écran principal sous la forme « L H » (ex. « 1920 1080 »),
    /// INDÉPENDANTE du DPI/mise à l'échelle Windows (via EnumDisplaySettings), ou null si indéterminée.</summary>
    public static string? NativeResolution()
    {
        try
        {
            var dm = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
            if (EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref dm) && dm.dmPelsWidth > 0 && dm.dmPelsHeight > 0)
                return $"{dm.dmPelsWidth} {dm.dmPelsHeight}";
        }
        catch { }
        return null;
    }

    private const int ENUM_CURRENT_SETTINGS = -1;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettings(string? deviceName, int modeNum, ref DEVMODE devMode);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public int dmFields;
        public int dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType;
        public int dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }
}
