using System.Runtime.InteropServices;

namespace BoosterAI;

/// <summary>
/// Whether the system allows decorative animation (#222): on Windows the "Show animations in Windows" setting
/// (SPI_GETCLIENTAREAANIMATION); elsewhere animation is on. Read once at start, like the theme's other system settings.
/// </summary>
internal static class Motion
{
    private const uint SpiGetClientAreaAnimation = 0x1042;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint param, out bool value, uint winIni);

    public static bool Animate { get; } = Read();

    private static bool Read()
    {
        if (!OperatingSystem.IsWindows()) return true;
        try { return !SystemParametersInfo(SpiGetClientAreaAnimation, 0, out var on, 0) || on; }
        catch (Exception) { return true; }
    }
}
