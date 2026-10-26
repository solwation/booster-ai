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
            // Each Start gets a session directory with a status log per tab; shown here with a placeholder.
            var root = OperatingSystem.IsWindows() ? PathMapping.ToWslPath(TabSession.DefaultRoot) : TabSession.DefaultRoot;
            var dir = root.Replace('\\', '/') + "/<session>";
            var tracks = tabs.Select((_, i) => (TabTrack?)new TabTrack(dir + "/tab-lib.sh", dir, "tab" + (i + 1))).ToList();
            for (var i = 0; i < tabs.Count; i++) Console.WriteLine($"{tabs[i].Name}: bash -lic {Launcher.TabScript(tabs[i], null, tracks[i])}");
            Console.WriteLine("wt.exe " + string.Join(' ', Launcher.WindowsTerminalArgs(tabs, cfg.WslDistro, null, tracks, "BoosterStart-<session>", cfg.ZoomPercent)
                .Select(a => a.Contains(' ') ? $"\"{a}\"" : a)));
            if (cfg.ZoomPercent > 0)
            {
                Console.WriteLine($"zoom {cfg.ZoomPercent}%: Windows writes {Launcher.ZoomFragmentPath}:");
                Console.Write(Launcher.ZoomFragmentJson(cfg.ZoomPercent));
                foreach (var term in new[] { "gnome-terminal", "kitty", "wezterm" })
                    Console.WriteLine($"zoom {cfg.ZoomPercent}%: {term} {string.Join(' ', Launcher.LinuxZoomArgs(term, cfg.ZoomPercent))}");
            }
            return;
        }
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
