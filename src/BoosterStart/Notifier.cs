using System.Diagnostics;
using System.Security;
using System.Text;

namespace BoosterStart;

/// <summary>
/// System notifications without extra dependencies: a Windows toast through Windows PowerShell's
/// WinRT access (hidden, no console window), or notify-send on Linux when it is installed.
/// </summary>
public static class Notifier
{
    /// <summary>The AppUserModelID the toast is shown under; registered in HKCU on first use.</summary>
    public const string AppId = "BoosterStart.App";

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
                Run("notify-send", ["-a", "BoosterStart", title, body]);
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
        var xml = $"<toast><visual><binding template=\"ToastGeneric\"><text>{SecurityElement.Escape(title)}</text>" +
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
        using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Classes\AppUserModelId\" + AppId);
        key.SetValue("DisplayName", "BoosterStart");
        var icon = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BoosterStart", "boosterstart.png");
        if (!File.Exists(icon))
        {
            using var src = Avalonia.Platform.AssetLoader.Open(new Uri("avares://BoosterStart/Assets/boosterstart.png"));
            Directory.CreateDirectory(Path.GetDirectoryName(icon)!);
            using var dst = File.Create(icon);
            src.CopyTo(dst);
        }
        key.SetValue("IconUri", icon);
    }

    private static void Run(string exe, IEnumerable<string> args)
    {
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        Process.Start(psi)?.Dispose();
    }
}
