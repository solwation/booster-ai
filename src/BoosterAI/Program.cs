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
            var tracks = tabs.Select((_, i) => (TabTrack?)new TabTrack(dir + "/tab-lib.sh", dir, "tab" + (i + 1), cfg.AutoUpdateClis, cfg.AutoClearIdle)).ToList();
            for (var i = 0; i < tabs.Count; i++) Console.WriteLine($"{tabs[i].Name}: bash -lic {Launcher.TabScript(tabs[i], null, tracks[i])}");
            // bs_update in tab-lib.sh, before the CLI and before each in-place restart.
            Console.WriteLine(cfg.AutoUpdateClis
                ? "cli update (bs_update, at most every 6 h per CLI, state in ~/.local/share/BoosterAI/cli-update of the tab's Linux):"
                : "cli update: off (AutoUpdateClis false)");
            if (cfg.AutoUpdateClis)
                foreach (var (llm, how) in Launcher.CliUpdateCommands) Console.WriteLine($"  {llm}: {how}");
            Console.WriteLine(cfg.AutoClearIdle
                ? $"auto-clear: after {AutoClear.Minutes(cfg.AutoClearMinutes)} min idle, claude tabs run under bs_pty (bs_run ... input) and get /clear once per idle period"
                : "auto-clear: off (AutoClearIdle false)");
            Console.WriteLine("wt.exe " + string.Join(' ', Launcher.WindowsTerminalArgs(tabs, cfg.WslDistro, null, tracks, "BoosterAI-<session>", cfg.ZoomPercent)
                .Select(a => a.Contains(' ') ? $"\"{a}\"" : a)));
            if (!OperatingSystem.IsWindows())
                Console.WriteLine($"linux terminal: {cfg.LinuxTerminal} -> " + (cfg.LinuxTerminal != "auto" ? cfg.LinuxTerminal
                    : Launcher.LinuxTerminals.Skip(1).FirstOrDefault(Launcher.OnPath) ?? "(none found)"));
            foreach (var term in new[] { "gnome-terminal", "ptyxis" })
                foreach (var call in Launcher.LinuxCommands(tabs, term, null, tracks, cfg.ZoomPercent))
                    Console.WriteLine($"linux {term}: {call.Exe} " + string.Join(' ', call.Args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a)));
            foreach (var term in Launcher.LinuxTerminals.Skip(1))
                foreach (var call in Launcher.LinuxRestartCommands(tabs, term, null, tracks, cfg.ZoomPercent))
                    Console.WriteLine($"linux restart {term} (safe new-window fallback): {call.Exe} " + string.Join(' ', call.Args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a)));
            if (tabs.Count > 0)
                Console.WriteLine("linux ptyxis cold start (no instance yet): restore-session is off until the tabs are open; if it cannot be, the first call uses --tab instead of --tab-with-profile=default --new-window");
            PrintSetup(cfg, tabs);
            Console.WriteLine($"Windows profile (zoom {cfg.ZoomPercent}%): {Launcher.ZoomFragmentPath}:");
            Console.Write(Launcher.ZoomFragmentJson(cfg.ZoomPercent));
            if (cfg.ZoomPercent > 0)
            {
                foreach (var term in new[] { "gnome-terminal", "kitty", "wezterm" })
                    Console.WriteLine($"zoom {cfg.ZoomPercent}%: {term} {string.Join(' ', Launcher.LinuxZoomArgs(term, cfg.ZoomPercent))}");
            }
            return;
        }
        if (args.Contains("--dry-run-close"))
        {
            foreach (var llm in new[] { "claude", "codex", "agy" })
                Console.WriteLine(llm + ": " + Launcher.SaveAndCloseScript(new TabConfig { Llm = llm }, "example-token"));
            Console.WriteLine("Close recorded tab (example PID/starttime):\n" + Launcher.CloseTabScript(12345, 67890, exitCleanly: true));
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

    /// <summary>The setup check and the install and alias commands it can offer (#74); nothing is run.</summary>
    private static void PrintSetup(AppConfig cfg, List<TabConfig> tabs)
    {
        Console.WriteLine("setup check (background, no window): Windows: wsl.exe -l -q (WSL_UTF8=1), then wsl.exe -d " + cfg.WslDistro
            + " --exec bash -lic <check>; Linux: bash -lic <check>; <check> = " + Prerequisites.DetectScript);
        Console.WriteLine("setup install WSL (Windows, after confirmation, elevated and visible): " + Prerequisites.WslInstall.Exe + " "
            + string.Join(' ', Prerequisites.WslInstall.Args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a)));
        foreach (var cli in Prerequisites.Clis)
        {
            var tab = new TabConfig { Name = "Install " + cli, Directory = "~", Llm = Catalog.ShellOnly, Script = Prerequisites.InstallScript(cli) };
            Console.WriteLine($"setup install {cli} (visible tab): bash -lic {tab.Script}");
            Console.WriteLine($"setup install {cli} (Windows): wt.exe " + string.Join(' ', Launcher.WindowsTerminalArgs([tab], cfg.WslDistro, null, null, "new", cfg.ZoomPercent)
                .Select(a => a.Contains(' ') ? $"\"{a}\"" : a)));
        }
        Console.WriteLine("setup full-auto aliases (after confirmation, only for installed CLIs without an alias or function; block appended to ~/.bashrc):");
        Console.Write(Prerequisites.AddAliases(null, Prerequisites.Clis).Replace("\n", "\n  ").Insert(0, "  ").TrimEnd(' '));
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
