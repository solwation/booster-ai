using Avalonia;

namespace BoosterStart;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // --dry-run prints what "Starta" would run, without opening anything.
        if (args.Contains("--dry-run"))
        {
            var cfg = ConfigStore.Load();
            var tabs = cfg.Tabs.Where(t => t.Enabled).ToList();
            foreach (var t in tabs) Console.WriteLine($"{t.Name}: bash -lic {Launcher.TabScript(t)}");
            Console.WriteLine("wt.exe " + string.Join(' ', Launcher.WindowsTerminalArgs(tabs, cfg.WslDistro)
                .Select(a => a.Contains(' ') ? $"\"{a}\"" : a)));
            return;
        }
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
