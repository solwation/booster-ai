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

    /// <summary>A status channel notification's click (#249): shows the main window with the agent's open question.</summary>
    public const string QuestionUri = Protocol + ":question";

    private static bool? _linuxActions;

    /// <summary>
    /// Where <see cref="Show(string, string, bool)"/> sends a notification; the system notification by default.
    /// Tests replace it with a recording fake, so a test run never writes the user's HKCU registration and never
    /// shows a real toast (#269: a Windows test run once pointed the boosterai: protocol at testhost.exe).
    /// </summary>
    internal static Action<string, string, bool> Post { get; set; } = (title, body, openQuestion) => ShowOnSystem(title, body, openQuestion);

    public static void Show(string title, string body) => Show(title, body, openQuestion: false);

    /// <param name="openQuestion">A click opens the open question of the tab that asked (#249), not only the window.</param>
    public static void Show(string title, string body, bool openQuestion)
    {
        try { Post(title, body, openQuestion); }
        catch (Exception)
        {
            // A notification is a courtesy; the status column still shows what happened.
        }
    }

    /// <summary>The real toast or notify-send; false when this process is not BoosterAI itself and nothing is shown.</summary>
    internal static bool ShowOnSystem(string title, string body, bool openQuestion)
    {
        // Only BoosterAI shows notifications: a test host or another tool that reaches this code neither toasts nor registers.
        if (!IsAppProcess(Environment.ProcessPath, EntryAssemblyPath())) return false;
        if (OperatingSystem.IsWindows())
        {
            Register();
            Run("powershell.exe", ToastArgs(title, body, openQuestion));
            return true;
        }
        if (!Launcher.OnPath("notify-send")) return false;
        if (_linuxActions ??= NotifySendHasActions()) ShowLinuxClickable(title, body, openQuestion);
        else Run("notify-send", ["-a", "BoosterAI", title, body]);
        return true;
    }

    /// <summary>The powershell.exe arguments that show the toast (the script is passed encoded).</summary>
    public static string[] ToastArgs(string title, string body, bool openQuestion = false) =>
        ["-NoProfile", "-NonInteractive", "-WindowStyle", "Hidden", "-EncodedCommand",
            Convert.ToBase64String(Encoding.Unicode.GetBytes(ToastScript(title, body, openQuestion)))];

    public static string ToastScript(string title, string body, bool openQuestion = false)
    {
        var xml = $"<toast activationType=\"protocol\" launch=\"{(openQuestion ? QuestionUri : ShowUri)}\"><visual><binding template=\"ToastGeneric\"><text>{SecurityElement.Escape(title)}</text>" +
                  $"<text>{SecurityElement.Escape(body)}</text></binding></visual></toast>";
        return string.Join('\n',
            "[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] > $null",
            "[Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom.XmlDocument, ContentType = WindowsRuntime] > $null",
            "$x = New-Object Windows.Data.Xml.Dom.XmlDocument",
            $"$x.LoadXml('{xml.Replace("'", "''")}')",
            $"[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('{AppId}').Show([Windows.UI.Notifications.ToastNotification]::new($x))");
    }

    /// <summary>
    /// An unpackaged app needs its AppUserModelID registered for toasts to show its name and icon, and the
    /// <c>boosterai:</c> protocol registered for a click to start BoosterAI. Writes nothing (false) unless
    /// <see cref="RegistrationCommand"/> accepts this process; the decision is documented there.
    /// </summary>
    internal static bool Register()
    {
        if (!OperatingSystem.IsWindows()) return false;
        if (RegistrationCommand(Environment.ProcessPath, EntryAssemblyPath(), InstalledExe(), File.Exists) is not { } command) return false;
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
        using var proto = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + Protocol);
        proto.SetValue("", "URL:BoosterAI");
        proto.SetValue("URL Protocol", "");
        using var open = proto.CreateSubKey(@"shell\open\command");
        if (open.GetValue("") as string != command) open.SetValue("", command);
        return true;
    }

    /// <summary>
    /// The protocol command to register, or null to leave the registry alone. The decision (#269):
    /// only a real BoosterAI run registers (<see cref="IsAppProcess"/>), never testhost, a setup copy
    /// (BoosterAI-Setup*.exe) or another tool that reaches this code. The installed copy takes precedence when
    /// it exists, also over a development or portable build: a build folder can be cleaned or moved, and any copy
    /// hands the click to the running GUI through <see cref="SingleInstance"/>, so the installed one reaches a
    /// running development build as well. Without an installed copy this copy is registered.
    /// </summary>
    public static string? RegistrationCommand(string? processPath, string? entryAssembly, string? installedExe, Func<string, bool> exists)
    {
        if (!IsAppProcess(processPath, entryAssembly)) return null;
        if (!string.IsNullOrEmpty(installedExe) && exists(installedExe)) return LaunchCommand(installedExe);
        return LaunchCommand(processPath, entryAssembly);
    }

    /// <summary>True for BoosterAI(.exe) itself or <c>dotnet BoosterAI.dll</c>; false for any other host or name.</summary>
    public static bool IsAppProcess(string? processPath, string? entryAssembly)
    {
        var exe = FileName(processPath);
        if (exe.Equals("dotnet", StringComparison.OrdinalIgnoreCase) || exe.Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase))
            return FileName(entryAssembly).Equals(Installer.AppName + ".dll", StringComparison.OrdinalIgnoreCase);
        return exe.Equals(Installer.AppName, StringComparison.OrdinalIgnoreCase) || exe.Equals(Installer.AppName + ".exe", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True when the start arguments carry the click that asks for the open question (#249); a trailing slash is tolerated.</summary>
    public static bool IsQuestionLaunch(IEnumerable<string> args) =>
        args.Any(a => a.TrimEnd('/').Equals(QuestionUri, StringComparison.OrdinalIgnoreCase));

    private static string FileName(string? path) => string.IsNullOrEmpty(path) ? "" : path[(path.LastIndexOfAny(['/', '\\']) + 1)..];

    private static string? EntryAssemblyPath() => System.Reflection.Assembly.GetEntryAssembly()?.Location;

    private static string? InstalledExe()
    {
        try { return Installer.CurrentPlan().Exe; }
        catch (Exception) { return null; }
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

    private static void ShowLinuxClickable(string title, string body, bool openQuestion)
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
                    if (action.Trim() == "default") Program.Instance?.RequestActivation(openQuestion);
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
