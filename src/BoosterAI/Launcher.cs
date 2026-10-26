using System.Globalization;
using System.Diagnostics;

namespace BoosterAI;

/// <summary>Opens the enabled tabs in a terminal and starts each tab's AI CLI.</summary>
public static class Launcher
{
    /// <summary>
    /// The bash command a tab runs: cd into its directory, start the CLI through an
    /// interactive login shell (so ~/.bashrc aliases and PATH apply), then leave a shell.
    /// It contains no ';' because Windows Terminal splits its command line on it.
    /// With a <paramref name="track"/>, the shell first sources tab-lib.sh, which logs start, CLI
    /// start and exit code, and the shell's own exit (see TabSession); the closing shell is then a
    /// child instead of exec so that the exit trap still fires when the tab is closed. Every logging
    /// step is "|| true", so a missing lib never stops the CLI.
    /// </summary>
    public static string TabScript(TabConfig tab, string? prompt = null, TabTrack? track = null)
    {
        var cd = tab.Directory.Trim() switch
        {
            "" or "~" => "cd ~",
            var d when d.StartsWith("~/") => "cd ~/" + Quote(d[2..]),
            var d => "cd " + Quote(d),
        };
        var lib = track is null ? "" : $". {Quote(track.Lib)} {Quote(track.Dir)} {Quote(track.Id)} 2>/dev/null || true && ";
        var shell = track is null ? "exec bash" : "bash";
        if (tab.Llm == Catalog.ShellOnly) return $"{lib}{cd} || true && {shell}";

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
        if (track is null) return $"{cd} && {cli} || true && exec bash";
        return $"{lib}{cd} && {Hook("bs_cli")} && {cli} && {Hook("bs_done 0")} || {Hook("bs_done $?")} && bash";
    }

    /// <summary>A logging call that can neither fail the chain nor print "command not found".</summary>
    private static string Hook(string call) => $"({call} 2>/dev/null || true)";

    public static string Validate(IEnumerable<TabConfig> tabs)
    {
        foreach (var t in tabs.Where(t => t.Enabled))
        {
            if (!Catalog.IsSafeModel(t.Model)) return Loc.Format("err.model", t.Name, t.Model);
            if (t.Directory.IndexOfAny([';', '\n', '\r']) >= 0) return Loc.Format("err.directory", t.Name);
            if (t.Name.Contains(';')) return Loc.Format("err.name", t.Name);
        }
        return "";
    }

    /// <summary>First prompt for the "Install" link in a project without the orchestra, in the UI language. No ';' (see TabScript).</summary>
    public static string InstallOrkesterPrompt => Loc.Get("prompt.install");

    /// <summary>
    /// Starts the terminal; returns an error text in the UI language, or "" on success.
    /// <paramref name="track"/> gives each tab its status log; <paramref name="window"/> is the
    /// Windows Terminal window to open in ("new", or a session's name so restarts join it).
    /// </summary>
    public static string Launch(AppConfig cfg, string? prompt = null, Func<TabConfig, TabTrack?>? track = null, string window = "new")
    {
        var tabs = cfg.Tabs.Where(t => t.Enabled).ToList();
        if (tabs.Count == 0) return Loc.Get("err.noTab");
        var error = Validate(tabs);
        if (error != "") return error;
        try
        {
            var tracks = tabs.Select(t => track?.Invoke(t)).ToList();
            if (OperatingSystem.IsWindows()) LaunchWindowsTerminal(tabs, cfg.WslDistro, prompt, tracks, window, cfg.ZoomPercent);
            else LaunchLinux(tabs, cfg.LinuxTerminal, prompt, tracks, cfg.ZoomPercent);
            return "";
        }
        catch (Exception e)
        {
            return Loc.Format("err.launch", e.Message);
        }
    }

    /// <summary>The wt.exe arguments, exposed for --dry-run.</summary>
    public static List<string> WindowsTerminalArgs(IReadOnlyList<TabConfig> tabs, string distro, string? prompt = null,
        IReadOnlyList<TabTrack?>? tracks = null, string window = "new", int zoomPercent = 0)
    {
        // wt has no font-size flag: a zoomed run selects the profile from ZoomFragmentJson.
        string[] profile = zoomPercent > 0 ? ["-p", ZoomProfileName] : [];
        var a = new List<string> { "-w", window };
        for (var i = 0; i < tabs.Count; i++)
        {
            var t = tabs[i];
            if (a.Count > 2) a.Add(";");
            a.AddRange(["new-tab", .. profile, "--title", t.Name, "--tabColor", t.Color, "--suppressApplicationTitle",
                "wsl.exe", "-d", distro, "--exec", "bash", "-lic", TabScript(InWsl(t), prompt, tracks?[i])]);
        }
        a.AddRange([";", "focus-tab", "-t", "0"]);
        return a;
    }

    /// <summary>The zoom levels offered in Settings, in percent; 0 = terminal default.</summary>
    public static readonly IReadOnlyList<int> ZoomChoices = [0, 80, 90, 110, 125, 150, 175, 200];

    public const string ZoomProfileName = "BoosterAI";
    private const string ZoomProfileGuid = "{b005e57a-2a27-4c00-9a0e-5b0057a27001}";
    /// <summary>Windows Terminal's built-in default font size in points; Windows zoom is a percentage of it.</summary>
    private const double WtDefaultPoints = 12;

    private static string Num(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>Windows Terminal JSON fragment with a hidden profile that only sets the font size; it inherits the user's defaults.</summary>
    public static string ZoomFragmentJson(int zoomPercent) =>
        "{\n  \"profiles\": [\n    {\n      \"name\": \"" + ZoomProfileName + "\",\n      \"guid\": \"" + ZoomProfileGuid +
        "\",\n      \"hidden\": true,\n      \"font\": { \"size\": " + Num(WtDefaultPoints * zoomPercent / 100) + " }\n    }\n  ]\n}\n";

    /// <summary>%LOCALAPPDATA%\Microsoft\Windows Terminal\Fragments\BoosterAI\boosterai.json, the only file written for zoom.</summary>
    public static string ZoomFragmentPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Microsoft", "Windows Terminal", "Fragments", "BoosterAI", "boosterai.json");

    /// <summary>Writes the fragment for a non-default zoom, or removes it for the default.</summary>
    private static void SyncZoomFragment(int zoomPercent)
    {
        LegacyNames.RemoveOldFragment(LegacyNames.OldFragmentDir);
        if (zoomPercent > 0)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ZoomFragmentPath)!);
            File.WriteAllText(ZoomFragmentPath, ZoomFragmentJson(zoomPercent));
        }
        else if (File.Exists(ZoomFragmentPath)) File.Delete(ZoomFragmentPath);
    }

    /// <summary>Terminal arguments that apply the zoom, or none where the terminal has no such option.</summary>
    public static string[] LinuxZoomArgs(string term, int zoomPercent)
    {
        if (zoomPercent <= 0) return [];
        var f = zoomPercent / 100.0;
        return term switch
        {
            "gnome-terminal" => [$"--zoom={Num(f)}"],
            "kitty" => ["-o", $"font_size={Num(11 * f)}"],
            // --config is a global option, so it goes before "start".
            "wezterm" => ["--config", $"font_size={Num(12 * f)}"],
            _ => [],
        };
    }

    /// <summary>The tab as its WSL shell sees it: a Windows folder becomes /mnt/c/… or /home/….</summary>
    private static TabConfig InWsl(TabConfig t) => new()
    {
        Name = t.Name, Color = t.Color, ColorIndex = t.ColorIndex, Enabled = t.Enabled,
        Llm = t.Llm, Model = t.Model, Orkester = t.Orkester, Directory = PathMapping.ToWslPath(t.Directory),
    };

    private static void LaunchWindowsTerminal(IReadOnlyList<TabConfig> tabs, string distro, string? prompt,
        IReadOnlyList<TabTrack?> tracks, string window, int zoomPercent)
    {
        SyncZoomFragment(zoomPercent);
        var psi = new ProcessStartInfo("wt.exe") { UseShellExecute = false };
        foreach (var arg in WindowsTerminalArgs(tabs, distro, prompt, tracks, window, zoomPercent)) psi.ArgumentList.Add(arg);
        Process.Start(psi);
    }

    public static readonly IReadOnlyList<string> LinuxTerminals =
        ["auto", "gnome-terminal", "konsole", "xfce4-terminal", "kitty", "wezterm", "x-terminal-emulator", "xterm"];

    private static void LaunchLinux(IReadOnlyList<TabConfig> tabs, string preferred, string? prompt,
        IReadOnlyList<TabTrack?> tracks, int zoomPercent)
    {
        var term = preferred != "auto" ? preferred
            : LinuxTerminals.Skip(1).FirstOrDefault(OnPath)
              ?? throw new InvalidOperationException(Loc.Get("err.noTerminal"));
        for (var i = 0; i < tabs.Count; i++)
        {
            var t = tabs[i];
            string[] shell = ["bash", "-lic", TabScript(t, prompt, tracks[i])];
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
            args = [.. LinuxZoomArgs(term, zoomPercent), .. args];
            var psi = new ProcessStartInfo(term) { UseShellExecute = false };
            foreach (var a in args) psi.ArgumentList.Add(a);
            Process.Start(psi);
            if (term is "gnome-terminal" or "xfce4-terminal" && i == 0) Thread.Sleep(600);
        }
    }

    public static bool OnPath(string exe) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':')
            .Any(dir => dir != "" && File.Exists(Path.Combine(dir, exe)));

    private static string Quote(string s) => "'" + s.Replace("'", "'\\''") + "'";
}
