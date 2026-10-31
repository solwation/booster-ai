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

/// <summary>The "After Start" setting: what the main window does once Start has opened the tabs.</summary>
public static class AfterLaunchPolicy
{
    public const string Keep = "keep", Minimize = "minimize", Close = "close";
    public static readonly string[] All = [Keep, Minimize, Close];

    /// <summary>An unknown or missing value reads as <see cref="Keep"/>.</summary>
    public static string Normalize(string? mode) => mode is Minimize or Close ? mode : Keep;

    /// <summary>The window actions after Start. Windows shows the tray icon unless the app closes.</summary>
    public static (bool Close, bool ShowTray, bool Minimize) Decide(string? mode, bool windows)
    {
        mode = Normalize(mode);
        return (mode == Close, windows && mode != Close, mode == Minimize);
    }
}
