using System.Diagnostics;

namespace BoosterStart;

/// <summary>Opens the enabled tabs in a terminal and starts each tab's AI CLI.</summary>
public static class Launcher
{
    /// <summary>
    /// The bash command a tab runs: cd into its directory, start the CLI through an
    /// interactive login shell (so ~/.bashrc aliases and PATH apply), then leave a shell.
    /// It contains no ';' because Windows Terminal splits its command line on it.
    /// </summary>
    public static string TabScript(TabConfig tab, string? prompt = null)
    {
        var cd = tab.Directory.Trim() switch
        {
            "" or "~" => "cd ~",
            var d when d.StartsWith("~/") => "cd ~/" + Quote(d[2..]),
            var d => "cd " + Quote(d),
        };
        if (tab.Llm == Catalog.ShellOnly) return $"{cd} || true && exec bash";

        var args = new List<string>();
        if (tab.Model != Catalog.StandardModel) args.Add("--model " + Quote(tab.Model));
        if (prompt is not null)
            args.Add((tab.Llm == "agy" ? "-i " : "") + Quote(prompt));
        else if (tab.Orkester)
            args.Add(tab.Llm switch
            {
                "agy" => "-i " + Quote("/orkester"),
                // Codex invokes repository skills as $name; repositories without one get the slash text.
                "codex" => "$(test -d .agents/skills/orkester && echo '$orkester' || echo /orkester)",
                _ => Quote("/orkester"),
            });
        var cli = string.Join(' ', new[] { tab.Llm }.Concat(args));
        return $"{cd} && {cli} || true && exec bash";
    }

    public static string Validate(IEnumerable<TabConfig> tabs)
    {
        foreach (var t in tabs.Where(t => t.Enabled))
        {
            if (!Catalog.IsSafeModel(t.Model)) return $"{t.Name}: ogiltigt modell-id \"{t.Model}\".";
            if (t.Directory.IndexOfAny([';', '\n', '\r']) >= 0) return $"{t.Name}: katalogen får inte innehålla ; eller radbrytning.";
            if (t.Name.Contains(';')) return $"{t.Name}: fliknamnet får inte innehålla ;.";
        }
        return "";
    }

    /// <summary>First prompt for the "Installera" link in a project without the orchestra. No ';' (see TabScript).</summary>
    public const string InstallOrkesterPrompt =
        "Installera Dirigenten och orkestern i det här projektet. Följ INSTALL.md i " +
        "https://github.com/solwation/dirigenten-och-orkestern: senaste release som git subtree under " +
        ".orchestra/shared, en projektpolicy i .orchestra/project.md utifrån PROJECT.example.md, " +
        "kommandot /orkester för Claude (.claude/commands/orkester.md) och Codex-skillen " +
        "(.agents/skills/orkester/SKILL.md), länkade från AGENTS.md och CLAUDE.md. " +
        "Läs projektet först och fråga mig om språk, publicering, kapacitet och timers innan du " +
        "skriver policyn. Committa när jag har godkänt.";

    /// <summary>Starts the terminal; returns a Swedish error text, or "" on success.</summary>
    public static string Launch(AppConfig cfg, string? prompt = null)
    {
        var tabs = cfg.Tabs.Where(t => t.Enabled).ToList();
        if (tabs.Count == 0) return "Ingen flik är vald.";
        var error = Validate(tabs);
        if (error != "") return error;
        try
        {
            if (OperatingSystem.IsWindows()) LaunchWindowsTerminal(tabs, cfg.WslDistro, prompt);
            else LaunchLinux(tabs, cfg.LinuxTerminal, prompt);
            return "";
        }
        catch (Exception e)
        {
            return "Kunde inte starta terminalen: " + e.Message;
        }
    }

    /// <summary>The wt.exe arguments, exposed for --dry-run.</summary>
    public static List<string> WindowsTerminalArgs(IReadOnlyList<TabConfig> tabs, string distro, string? prompt = null)
    {
        var a = new List<string> { "-w", "new" };
        foreach (var t in tabs)
        {
            if (a.Count > 2) a.Add(";");
            a.AddRange(["new-tab", "--title", t.Name, "--tabColor", t.Color, "--suppressApplicationTitle",
                "wsl.exe", "-d", distro, "--exec", "bash", "-lic", TabScript(InWsl(t), prompt)]);
        }
        a.AddRange([";", "focus-tab", "-t", "0"]);
        return a;
    }

    /// <summary>The tab as its WSL shell sees it: a Windows folder becomes /mnt/c/… or /home/….</summary>
    private static TabConfig InWsl(TabConfig t) => new()
    {
        Name = t.Name, Color = t.Color, ColorIndex = t.ColorIndex, Enabled = t.Enabled,
        Llm = t.Llm, Model = t.Model, Orkester = t.Orkester, Directory = PathMapping.ToWslPath(t.Directory),
    };

    private static void LaunchWindowsTerminal(IReadOnlyList<TabConfig> tabs, string distro, string? prompt)
    {
        var psi = new ProcessStartInfo("wt.exe") { UseShellExecute = false };
        foreach (var arg in WindowsTerminalArgs(tabs, distro, prompt)) psi.ArgumentList.Add(arg);
        Process.Start(psi);
    }

    public static readonly IReadOnlyList<string> LinuxTerminals =
        ["auto", "gnome-terminal", "konsole", "xfce4-terminal", "kitty", "wezterm", "x-terminal-emulator", "xterm"];

    private static void LaunchLinux(IReadOnlyList<TabConfig> tabs, string preferred, string? prompt)
    {
        var term = preferred != "auto" ? preferred
            : LinuxTerminals.Skip(1).FirstOrDefault(OnPath)
              ?? throw new InvalidOperationException("hittade ingen terminal (gnome-terminal, konsole, kitty …).");
        for (var i = 0; i < tabs.Count; i++)
        {
            var t = tabs[i];
            string[] shell = ["bash", "-lic", TabScript(t, prompt)];
            string[] args = term switch
            {
                // Later tabs open in the window the first call created.
                "gnome-terminal" => [i == 0 ? "--window" : "--tab", $"--title={t.Name}", "--", .. shell],
                "konsole" => ["--new-tab", "-p", $"tabtitle={t.Name}", "-e", .. shell],
                "xfce4-terminal" => [i == 0 ? "--window" : "--tab", "--title", t.Name, "-x", .. shell],
                "kitty" => ["--title", t.Name, .. shell],
                "wezterm" => ["start", "--", .. shell],
                _ => ["-T", t.Name, "-e", .. shell],
            };
            var psi = new ProcessStartInfo(term) { UseShellExecute = false };
            foreach (var a in args) psi.ArgumentList.Add(a);
            Process.Start(psi);
            if (term is "gnome-terminal" or "xfce4-terminal" && i == 0) Thread.Sleep(600);
        }
    }

    private static bool OnPath(string exe) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':')
            .Any(dir => dir != "" && File.Exists(Path.Combine(dir, exe)));

    private static string Quote(string s) => "'" + s.Replace("'", "'\\''") + "'";
}
