using System.ComponentModel;
using System.Diagnostics;

namespace BoosterAI;

/// <summary>
/// Opens a web page in the system's default handler. A missing browser or URL handler must never end the app
/// (#175): the failure is swallowed and reported as false, so a button simply does nothing.
/// </summary>
public static class ExternalLink
{
    /// <summary>Headless screenshots never start a browser.</summary>
    public static bool Open(string url) => !Screenshot.Active && Open(url, psi => Process.Start(psi));

    internal static bool Open(string url, Func<ProcessStartInfo, Process?> start)
    {
        try
        {
            start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            return false;
        }
    }
}
