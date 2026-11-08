using System.Diagnostics;
using System.Security;
using System.Text;

namespace BoosterAI;

/// <summary>
/// System notifications without extra dependencies: a Windows toast through Windows PowerShell's
/// WinRT access (hidden, no console window), or notify-send on Linux when it is installed.
/// Clicking a notification shows the main window: on Windows the toast launches the <c>boosterai:</c>
/// protocol, whose new process hands the request to the running GUI through <see cref="SingleInstance"/>;
/// on Linux a notify-send with actions reports the click back to this process.
/// </summary>
public static class Notifier
{
    /// <summary>The AppUserModelID the toast is shown under; registered in HKCU on first use.</summary>
    public const string AppId = "BoosterAI.App";

    /// <summary>The URL scheme a toast click launches; registered in HKCU next to the AppUserModelID.</summary>
    public const string Protocol = "boosterai";

    /// <summary>The argument a toast click starts BoosterAI with; any GUI launch activates the running window.</summary>
    public const string ShowUri = Protocol + ":show";

    private static bool? _linuxActions;

    public static void Show(string title, string body)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                Register();
                Run("powershell.exe", ToastArgs(title, body));
            }
            else if (Launcher.OnPath("notify-send"))
            {
                if (_linuxActions ??= NotifySendHasActions()) ShowLinuxClickable(title, body);
                else Run("notify-send", ["-a", "BoosterAI", title, body]);
            }
        }
        catch (Exception)
        {
            // A notification is a courtesy; the status column still shows what happened.
        }
    }

    /// <summary>The powershell.exe arguments that show the toast (the script is passed encoded).</summary>
    public static string[] ToastArgs(string title, string body) =>
        ["-NoProfile", "-NonInteractive", "-WindowStyle", "Hidden", "-EncodedCommand",
            Convert.ToBase64String(Encoding.Unicode.GetBytes(ToastScript(title, body)))];

    public static string ToastScript(string title, string body)
    {
        var xml = $"<toast activationType=\"protocol\" launch=\"{ShowUri}\"><visual><binding template=\"ToastGeneric\"><text>{SecurityElement.Escape(title)}</text>" +
                  $"<text>{SecurityElement.Escape(body)}</text></binding></visual></toast>";
        return string.Join('\n',
            "[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] > $null",
            "[Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom.XmlDocument, ContentType = WindowsRuntime] > $null",
            "$x = New-Object Windows.Data.Xml.Dom.XmlDocument",
            $"$x.LoadXml('{xml.Replace("'", "''")}')",
            $"[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('{AppId}').Show([Windows.UI.Notifications.ToastNotification]::new($x))");
    }

    /// <summary>An unpackaged app needs its AppUserModelID registered for toasts to show its name and icon.</summary>
    private static void Register()
    {
        if (!OperatingSystem.IsWindows()) return;
        Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\AppUserModelId\" + LegacyNames.OldAppId, throwOnMissingSubKey: false);
        using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Classes\AppUserModelId\" + AppId);
        key.SetValue("DisplayName", "BoosterAI");
        var icon = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BoosterAI", "boosterai.png");
        LegacyNames.RemoveOldLocalData(LegacyNames.OldLocalAppDir);
        if (!File.Exists(icon))
        {
            using var src = Avalonia.Platform.AssetLoader.Open(new Uri("avares://BoosterAI/Assets/boosterai.png"));
            Directory.CreateDirectory(Path.GetDirectoryName(icon)!);
            using var dst = File.Create(icon);
            src.CopyTo(dst);
        }
        key.SetValue("IconUri", icon);
        if (LaunchCommand() is { } command)
        {
            using var proto = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + Protocol);
            proto.SetValue("", "URL:BoosterAI");
            proto.SetValue("URL Protocol", "");
            using var open = proto.CreateSubKey(@"shell\open\command");
            open.SetValue("", command);
        }
    }

    /// <summary>The protocol's open command for this copy of BoosterAI, or null when the executable is unknown.</summary>
    public static string? LaunchCommand(string? processPath = null, string? entryAssembly = null)
    {
        processPath ??= Environment.ProcessPath;
        if (string.IsNullOrEmpty(processPath)) return null;
        // Under `dotnet BoosterAI.dll` the process is the host; the click must start the app, not bare dotnet.
        var exe = processPath[(processPath.LastIndexOfAny(['/', '\\']) + 1)..];
        if (exe.Equals("dotnet", StringComparison.OrdinalIgnoreCase) || exe.Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase))
        {
            entryAssembly ??= System.Reflection.Assembly.GetEntryAssembly()?.Location;
            return string.IsNullOrEmpty(entryAssembly) ? null : $"\"{processPath}\" \"{entryAssembly}\" \"%1\"";
        }
        return $"\"{processPath}\" \"%1\"";
    }

    /// <summary>The notify-send arguments whose default action (a click on the notification) is printed on exit.</summary>
    public static string[] LinuxClickableArgs(string title, string body) =>
        ["-a", "BoosterAI", "--action=default=" + Loc.Get("tray.show"), "--wait", title, body];

    /// <summary>libnotify 0.7.9 and later; older notify-send rejects --action and shows nothing.</summary>
    private static bool NotifySendHasActions()
    {
        try
        {
            var psi = new ProcessStartInfo("notify-send", "--help") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using var p = Process.Start(psi);
            if (p is null) return false;
            var help = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit(2000);
            return help.Contains("--action");
        }
        catch (Exception) { return false; }
    }

    private static void ShowLinuxClickable(string title, string body)
    {
        var psi = new ProcessStartInfo("notify-send") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        foreach (var a in LinuxClickableArgs(title, body)) psi.ArgumentList.Add(a);
        var p = Process.Start(psi);
        if (p is null) return;
        // --wait keeps notify-send alive until the notification closes; it prints the chosen action, if any.
        _ = Task.Run(async () =>
        {
            using (p)
            {
                try
                {
                    var action = await p.StandardOutput.ReadToEndAsync();
                    await p.WaitForExitAsync();
                    if (action.Trim() == "default") Program.Instance?.RequestActivation();
                }
                catch (Exception) { }
            }
        });
    }

    private static void Run(string exe, IEnumerable<string> args)
    {
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        Process.Start(psi)?.Dispose();
    }
}
