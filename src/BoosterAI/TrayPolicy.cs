using Avalonia.Controls;

namespace BoosterAI;

/// <summary>Decides when the main window goes to the tray instead of closing or minimizing to the taskbar.</summary>
public static class TrayPolicy
{
    /// <summary>
    /// True when a close request should hide the window: only the user's own close (X, Alt+F4), never a
    /// programmatic Close (Close after Start), an application or OS shutdown, or a requested real quit.
    /// </summary>
    public static bool HideOnClose(bool minimizeToTray, bool trayAvailable, WindowCloseReason reason, bool programmatic, bool quitRequested) =>
        minimizeToTray && trayAvailable && !quitRequested && !programmatic && reason == WindowCloseReason.WindowClosing;

    /// <summary>True when minimizing should hide the window from the taskbar and leave only the tray icon.</summary>
    public static bool HideOnMinimize(bool minimizeToTray, bool trayAvailable) => minimizeToTray && trayAvailable;
}
