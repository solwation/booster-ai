using Avalonia;

namespace BoosterAI;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // --dry-run prints what "Starta" would run, without opening anything.
        if (args.Contains("--dry-run"))
        {
            var at = Array.IndexOf(args, "--config");
            if (at >= 0 && at + 1 < args.Length) ConfigStore.UseFile(args[at + 1]);
            var cfg = ConfigStore.Load();
            var tabs = cfg.Tabs.Where(t => t.Enabled).ToList();
            // Each Start gets a session directory with a status log per tab; shown here with a placeholder.
            var root = OperatingSystem.IsWindows() ? PathMapping.ToWslPath(TabSession.DefaultRoot) : TabSession.DefaultRoot;
            var dir = root.Replace('\\', '/') + "/<session>";
            var tracks = tabs.Select((_, i) => (TabTrack?)new TabTrack(dir + "/tab-lib.sh", dir, "tab" + (i + 1))).ToList();
            for (var i = 0; i < tabs.Count; i++) Console.WriteLine($"{tabs[i].Name}: bash -lic {Launcher.TabScript(tabs[i], null, tracks[i])}");
            Console.WriteLine("wt.exe " + string.Join(' ', Launcher.WindowsTerminalArgs(tabs, cfg.WslDistro, null, tracks, "BoosterAI-<session>", cfg.ZoomPercent)
                .Select(a => a.Contains(' ') ? $"\"{a}\"" : a)));
            if (!OperatingSystem.IsWindows())
                Console.WriteLine($"linux terminal: {cfg.LinuxTerminal} -> " + (cfg.LinuxTerminal != "auto" ? cfg.LinuxTerminal
                    : Launcher.LinuxTerminals.Skip(1).FirstOrDefault(Launcher.OnPath) ?? "(none found)"));
            foreach (var term in new[] { "gnome-terminal", "ptyxis" })
                foreach (var call in Launcher.LinuxCommands(tabs, term, null, tracks, cfg.ZoomPercent))
                    Console.WriteLine($"linux {term}: {call.Exe} " + string.Join(' ', call.Args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a)));
            if (tabs.Count > 0)
                Console.WriteLine("linux ptyxis cold start (no instance yet): restore-session is off until the tabs are open; if it cannot be, the first call uses --tab instead of --new-window");
            if (cfg.ZoomPercent > 0)
            {
                Console.WriteLine($"zoom {cfg.ZoomPercent}%: Windows writes {Launcher.ZoomFragmentPath}:");
                Console.Write(Launcher.ZoomFragmentJson(cfg.ZoomPercent));
                foreach (var term in new[] { "gnome-terminal", "kitty", "wezterm" })
                    Console.WriteLine($"zoom {cfg.ZoomPercent}%: {term} {string.Join(' ', Launcher.LinuxZoomArgs(term, cfg.ZoomPercent))}");
            }
            return;
        }
        var shot = Screenshot.Parse(args);
        if (shot is not null)
        {
            Environment.ExitCode = Screenshot.Run(shot);
            return;
        }
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
