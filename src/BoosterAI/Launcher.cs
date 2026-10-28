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
    /// start and exit code, tracks terminal output timing and captures a bounded output tail (bs_run, bs_cap), formats the run-once event stream
    /// (bs_stream), and logs the shell's own exit (see TabSession); the closing shell is then a
    /// child instead of exec so that the exit trap still fires when the tab is closed. Every logging
    /// step is "|| true", so a missing lib never stops the CLI.
    /// </summary>
    public static string TabScript(TabConfig tab, string? prompt = null, TabTrack? track = null, bool resume = false)
    {
        var cd = Cd(tab);
        var lib = track is null ? "" : $". {Quote(track.Lib)} {Quote(track.Dir)} {Quote(track.Id)} 2>/dev/null || true && ";
        var shell = track is null ? "exec bash" : "bash";
        if (tab.Llm == Catalog.ShellOnly)
            return track is null ? $"{cd} || true && {shell}"
                : $"{lib}{cd} || true && {Hook("bs_cli")} && bs_run {Quote("bash")}";
        var cli = CliCommand(tab, prompt, track, resume && CanContinue(tab));
        if (track is null) return $"{cd} && {cli} || true && exec bash";
        // bs_next runs what an in-place restart queued (see RestartScript) before the shell is left.
        return $"{lib}{cd} && {Hook("bs_cli")} && {TrackedCommand(cli)} && {Hook("bs_done 0")} || {Hook("bs_done $?")} && {Next} && bash";
    }

    // In the tab's own shell, not a subshell: stopping a restarted CLI's process group must not take bs_next's loop with it.
    private const string Next = "(type bs_next >/dev/null 2>&1) && bs_next || true";

    private static string TrackedCommand(string command) =>
        command.StartsWith("bs_stream ", StringComparison.Ordinal) ? command : "bs_run " + Quote(command);

    /// <summary>
    /// What tab-lib.sh's bs_next runs in a tab whose CLI BoosterAI stopped for an in-place restart: the CLI
    /// with the new choices, logged like the first one. <paramref name="resume"/> keeps the conversation
    /// (claude --continue, codex resume --last) for a model change.
    /// </summary>
    public static string RestartScript(TabConfig tab, TabTrack track, bool resume) =>
        tab.Llm == Catalog.ShellOnly ? $"{Cd(InWsl(tab))} || true"
            : $"{Cd(InWsl(tab))} && {Hook("bs_cli")} && {TrackedCommand(CliCommand(tab, null, track, resume))} && {Hook("bs_done 0")} || {Hook("bs_done $?")}";

    /// <summary>A model change on claude or codex restarts with the same conversation; anything else starts fresh.</summary>
    public static bool CanResume(TabConfig from, TabConfig to) =>
        from.Llm == to.Llm && to.Llm is "claude" or "codex" && !(to.NonInteractive && to.Orkester);

    /// <summary>Explicit continuation is interactive; Run once always starts a new orchestra pass.</summary>
    public static bool CanContinue(TabConfig tab) =>
        tab.Llm is "claude" or "codex" or "agy" && !(tab.NonInteractive && tab.Orkester);

    /// <summary>The settings file whose hooks log busy/idle for an interactive claude tab (written by TabSession.NewTrack).</summary>
    public static string HooksFile(TabTrack track) => $"{track.Dir}/{track.Id}.claude.json";

    private static string Cd(TabConfig tab) => tab.Directory.Trim() switch
    {
        "" or "~" => "cd ~",
        var d when d.StartsWith("~/") => "cd ~/" + Quote(d[2..]),
        var d => "cd " + Quote(d),
    };

    private const string CodexOrkester = "$(test -d .agents/skills/orkester && echo '$orkester' || echo /orkester)";

    /// <summary>
    /// Appended to the orchestra command of a Run once tab with codex or agy (#49). Those CLIs end when the
    /// model ends its turn (codex exec at once, agy -p after at most 30 minutes of background tasks), so an
    /// idle watch left in a background poll loop would be dropped silently. The note asks the session to keep
    /// polling and idle waiting in the foreground of its turn, with the core's waits of at most 60 seconds.
    /// claude -p waits for background tasks and is re-invoked when they finish, so it gets no note.
    /// No ';' (see TabScript), no quote or '$' (it is quoted for bash and read by the model).
    /// </summary>
    public const string ForegroundWatchNote =
        "(BoosterAI Run once host note: this CLI exits as soon as your turn ends and does not keep background tasks or wake you when they finish. "
        + "Run every queue poll and the whole idle watch in the foreground of this turn, as blocking commands that wait at most 60 seconds each, for example sleep 60, "
        + "never as a background loop or job. End your turn only when the session is finished.)";

    private static string CliCommand(TabConfig tab, string? prompt, TabTrack? track, bool resume)
    {
        var args = new List<string>();
        if (resume && tab.Llm is "claude" or "agy") args.Add("--continue");
        if (tab.Model != Catalog.StandardModel) args.Add("--model " + Quote(tab.Model));
        // One-shot mode applies to the orchestra command only; a given prompt (the install) stays interactive.
        var once = tab.NonInteractive && tab.Orkester && prompt is null && !resume;
        // A resumed conversation gets no first prompt: it already ran /orkester or the install.
        if (resume) { }
        else if (prompt is not null)
            args.Add((tab.Llm == "agy" ? "-i " : "") + Quote(prompt));
        else if (tab.Orkester)
            args.Add(tab.Llm switch
            {
                "agy" => once ? "-p " + Quote("/orkester " + ForegroundWatchNote) : "-i " + Quote("/orkester"),
                // Codex invokes repository skills as $name; repositories without one get the slash text.
                "codex" when once => "\"" + CodexOrkester + "\"" + Quote(" " + ForegroundWatchNote),
                "codex" => CodexOrkester,
                _ when once => "-p " + Quote("/orkester"),
                _ => Quote("/orkester"),
            });
        // An interactive claude reports busy/idle through hooks; a one-shot run is busy until it exits.
        if (track is not null && tab.Llm == "claude" && !once) args.Add("--settings " + Quote(HooksFile(track)));
        var cli = string.Join(' ', new[] { tab.Llm }.Concat(args));
        if (once && tab.Llm == "codex") cli = "codex exec" + cli["codex".Length..];
        if (resume && tab.Llm == "codex") cli = "codex resume --last" + cli["codex".Length..];
        // claude -p and agy -p only print the final answer; their event streams, read by tab-lib.sh's
        // bs_stream, show the text and tool calls as they happen. codex exec already prints progress.
        // Without a lib (no track) they stay in text mode.
        if (once && track is not null && tab.Llm != "codex")
            cli = "bs_stream " + cli + (tab.Llm == "claude" ? " --output-format stream-json --verbose" : " --output-format stream-json");
        return cli;
    }

    /// <summary>
    /// Stops what runs in the foreground of the tab whose shell is <paramref name="shellPid"/> (the CLI's process
    /// group, found through the tty's foreground group), never the shell itself; its chain then runs bs_next.
    /// </summary>
    public static string StopForegroundScript(int shellPid) =>
        $"g=$(cut -d' ' -f8 /proc/{shellPid}/stat) && [ \"$g\" -gt 0 ] && [ \"$g\" != {shellPid} ] && kill -TERM -- -$g";

    public static void StopForeground(int shellPid, string wslDistro)
    {
        var psi = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("wsl.exe") { ArgumentList = { "-d", wslDistro, "-e", "bash", "-c", StopForegroundScript(shellPid) } }
            : new ProcessStartInfo("bash") { ArgumentList = { "-c", StopForegroundScript(shellPid) } };
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        using var p = Process.Start(psi);
        p?.WaitForExit(10000);
    }

    public const string SaveAndClosePrompt = "Stop now. Save your work, commit and push what is complete as quickly as possible, then end this turn. Start no new work. Respect repository policy and report anything you could not save or push.";

    /// <summary>A distinct turn after stopping the old CLI; the token prevents an old exit/idle from completing it.</summary>
    public static string SaveAndCloseScript(TabConfig tab, string token)
    {
        var model = tab.Model == Catalog.StandardModel ? "" : " --model " + Quote(tab.Model);
        var command = tab.Llm switch
        {
            "claude" => "bs_stream claude --continue" + model + " -p " + Quote(SaveAndClosePrompt) + " --output-format stream-json --verbose",
            "agy" => "bs_stream agy --continue" + model + " -p " + Quote(SaveAndClosePrompt) + " --output-format stream-json",
            "codex" => "bs_run " + Quote("codex exec resume --last" + model + " " + Quote(SaveAndClosePrompt)),
            _ => throw new ArgumentException("A save turn requires an AI CLI."),
        };
        return $"{Cd(InWsl(tab))} && bs_cli && bs_ev close-save token={Quote(token)} && {command} && bs_done 0 || bs_done $?";
    }

    internal static string StopTrackedForegroundScript(int pid, long startTime) => $$"""
bs_stat=$(cat /proc/{{pid}}/stat 2>/dev/null) || exit 0
bs_stat=${bs_stat##*) }
read -ra bs_fields <<< "$bs_stat"
[ "${bs_fields[19]}" = {{startTime}} ] || exit 0
g=${bs_fields[5]}
[ "$g" -gt 0 ] && [ "$g" != {{pid}} ] && kill -TERM -- -$g
""";

    /// <summary>Only the recorded shell and its descendants are closed, never the terminal server or another tab.</summary>
    public static string CloseTabScript(int shellPid, long startTime) => $$"""
bs_close_ids=()
bs_start() {
  local stat
  local -a fields
  stat=$(cat /proc/$1/stat 2>/dev/null) || return 1
  stat=${stat##*) }
  read -ra fields <<< "$stat"
  printf '%s' "${fields[19]}"
}
[ "$(bs_start {{shellPid}})" = {{startTime}} ] || exit 0
bs_collect() {
  local c st
  for c in $(cat /proc/$1/task/$1/children 2>/dev/null)
  do
    bs_collect "$c"
  done
  st=$(bs_start "$1")
  [ -z "$st" ] || bs_close_ids+=("$1:$st")
}
for c in $(cat /proc/{{shellPid}}/task/{{shellPid}}/children 2>/dev/null)
do
  bs_collect "$c"
done
if [ "$(bs_start {{shellPid}})" = {{startTime}} ]
then
  {{StopTrackedForegroundScript(shellPid, startTime)}} || true
fi
bs_signal() {
  local item p st
  for item in "${bs_close_ids[@]}"
  do
    p=${item%:*}
    st=${item#*:}
    if [ "$(bs_start "$p")" = "$st" ]
    then
      kill -"$1" "$p" 2>/dev/null || true
    fi
  done
}
bs_signal TERM
sleep 0.2
bs_signal KILL
if [ "$(bs_start {{shellPid}})" = {{startTime}} ]
then
  kill -HUP {{shellPid}} 2>/dev/null || true
fi
sleep 0.2
if [ "$(bs_start {{shellPid}})" = {{startTime}} ]
then
  kill -KILL {{shellPid}} 2>/dev/null || true
fi
""";

    internal static Process StartTabControl(string script, string wslDistro)
    {
        var psi = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("wsl.exe") { ArgumentList = { "-d", wslDistro, "-e", "bash", "-c", script } }
            : new ProcessStartInfo("bash") { ArgumentList = { "-c", script } };
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        return Process.Start(psi) ?? throw new IOException("Could not start tab control.");
    }

    internal static void WaitTabControl(Process p)
    {
        if (!p.WaitForExit(10000)) { p.Kill(); throw new IOException("Tab control timed out."); }
        if (p.ExitCode != 0) throw new IOException("Tab control failed.");
    }

    internal static void RunTabControl(string script, string wslDistro)
    {
        using var p = StartTabControl(script, wslDistro);
        WaitTabControl(p);
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
    public static string Launch(AppConfig cfg, string? prompt = null, Func<TabConfig, TabTrack?>? track = null, string window = "new", bool resume = false)
    {
        var tabs = cfg.Tabs.Where(t => t.Enabled).ToList();
        if (tabs.Count == 0) return Loc.Get("err.noTab");
        var error = Validate(tabs);
        if (error != "") return error;
        try
        {
            var tracks = tabs.Select(t => track?.Invoke(t)).ToList();
            if (OperatingSystem.IsWindows()) LaunchWindowsTerminal(tabs, cfg.WslDistro, prompt, tracks, window, cfg.ZoomPercent, resume);
            else LaunchLinux(tabs, cfg.LinuxTerminal, prompt, tracks, cfg.ZoomPercent, resume);
            return "";
        }
        catch (Exception e)
        {
            return Loc.Format("err.launch", e.Message);
        }
    }

    /// <summary>The wt.exe arguments, exposed for --dry-run.</summary>
    public static List<string> WindowsTerminalArgs(IReadOnlyList<TabConfig> tabs, string distro, string? prompt = null,
        IReadOnlyList<TabTrack?>? tracks = null, string window = "new", int zoomPercent = 0, bool resume = false)
    {
        // wt has no font-size flag: a zoomed run selects the profile from ZoomFragmentJson.
        string[] profile = zoomPercent > 0 ? ["-p", ZoomProfileName] : [];
        var a = new List<string> { "-w", window };
        for (var i = 0; i < tabs.Count; i++)
        {
            var t = tabs[i];
            if (a.Count > 2) a.Add(";");
            a.AddRange(["new-tab", .. profile, "--title", t.Name, "--tabColor", t.Color, "--suppressApplicationTitle",
                "wsl.exe", "-d", distro, "--exec", "bash", "-lic", TabScript(InWsl(t), prompt, tracks?[i], resume)]);
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

    /// <summary>Writes the fragment for a non-default zoom, or removes it for the default; true when the file changed.</summary>
    private static bool SyncZoomFragment(int zoomPercent)
    {
        LegacyNames.RemoveOldFragment(LegacyNames.OldFragmentDir);
        if (zoomPercent > 0)
        {
            var json = ZoomFragmentJson(zoomPercent);
            if (File.Exists(ZoomFragmentPath) && File.ReadAllText(ZoomFragmentPath) == json) return false;
            Directory.CreateDirectory(Path.GetDirectoryName(ZoomFragmentPath)!);
            File.WriteAllText(ZoomFragmentPath, json);
            return true;
        }
        if (!File.Exists(ZoomFragmentPath)) return false;
        File.Delete(ZoomFragmentPath);
        return true;
    }

    /// <summary>settings.json of Windows Terminal (Store, Preview and unpackaged installs).</summary>
    private static IEnumerable<string> WtSettingsFiles()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new[]
        {
            Path.Combine(local, "Packages", "Microsoft.WindowsTerminal_8wekyb3d8bbwe", "LocalState", "settings.json"),
            Path.Combine(local, "Packages", "Microsoft.WindowsTerminalPreview_8wekyb3d8bbwe", "LocalState", "settings.json"),
            Path.Combine(local, "Microsoft", "Windows Terminal", "settings.json"),
        }.Where(File.Exists);
    }

    /// <summary>
    /// The settings.json content with one trailing newline added, or removed again if the last nudge added it.
    /// A running Windows Terminal reads fragments only when it (re)loads settings, and it reloads only when
    /// settings.json changes; whitespace after the closing brace changes nothing else.
    /// </summary>
    public static byte[] NudgedSettings(byte[] settings) =>
        settings.Length >= 2 && settings[^1] == '\n' && settings[^2] == '\n' ? settings[..^1] : [.. settings, (byte)'\n'];

    /// <summary>Makes a running Windows Terminal reload its settings, so a new zoom fragment applies to the tabs about to open.</summary>
    private static void ReloadRunningWindowsTerminal()
    {
        if (Process.GetProcessesByName("WindowsTerminal").Length == 0) return;
        var nudged = false;
        foreach (var file in WtSettingsFiles())
        {
            try { File.WriteAllBytes(file, NudgedSettings(File.ReadAllBytes(file))); nudged = true; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        // The reload is asynchronous; without the wait the new tabs can still get the old profile set.
        if (nudged) Thread.Sleep(2000);
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
        Llm = t.Llm, Model = t.Model, Orkester = t.Orkester, NonInteractive = t.NonInteractive, Directory = PathMapping.ToWslPath(t.Directory),
    };

    private static void LaunchWindowsTerminal(IReadOnlyList<TabConfig> tabs, string distro, string? prompt,
        IReadOnlyList<TabTrack?> tracks, string window, int zoomPercent, bool resume)
    {
        if (SyncZoomFragment(zoomPercent)) ReloadRunningWindowsTerminal();
        var psi = new ProcessStartInfo("wt.exe") { UseShellExecute = false };
        foreach (var arg in WindowsTerminalArgs(tabs, distro, prompt, tracks, window, zoomPercent, resume)) psi.ArgumentList.Add(arg);
        Process.Start(psi);
    }

    /// <summary>"auto" tries the rest in this order. Ptyxis is first: it is the default on Ubuntu 25.10+ and Fedora.</summary>
    public static readonly IReadOnlyList<string> LinuxTerminals =
        ["auto", "ptyxis", "gnome-terminal", "konsole", "xfce4-terminal", "kitty", "wezterm", "x-terminal-emulator", "xterm"];

    /// <summary>
    /// One terminal process to start; <paramref name="SettleAfter"/> = give it a moment to open its window before the next one.
    /// With a <paramref name="BusName"/> the wait is for the terminal's single-instance D-Bus name instead (see WaitForInstance).
    /// </summary>
    public sealed record TermCall(string Exe, IReadOnlyList<string> Args, bool SettleAfter = false, string? BusName = null);

    /// <summary>Ptyxis' application id, which it owns on the session bus while an instance runs.</summary>
    public const string PtyxisBusName = "org.gnome.Ptyxis";

    /// <summary>
    /// The terminal processes that open the tabs, in order; used by Start and --dry-run.
    /// gnome-terminal and xfce4-terminal take every tab in ONE call (--window, then --tab groups), so all tabs share one
    /// window even when BoosterAI is not started from that terminal. Ptyxis and konsole need one call per tab, the later
    /// ones joining the window the first opened. The other terminals have no tabs: one window per tab.
    /// <paramref name="ptyxisRestores"/> true means these calls start Ptyxis and it restores its saved session, see the ptyxis case.
    /// </summary>
    public static List<TermCall> LinuxCommands(IReadOnlyList<TabConfig> tabs, string term, string? prompt,
        IReadOnlyList<TabTrack?> tracks, int zoomPercent, bool ptyxisRestores = false, bool resume = false)
    {
        var zoom = LinuxZoomArgs(term, zoomPercent);
        // --command / -e take ONE string that the terminal splits like a shell.
        string Cmd(int i) => "bash -lic " + Quote(TabScript(tabs[i], prompt, tracks[i], resume));
        var calls = new List<TermCall>();
        switch (term)
        {
            case "gnome-terminal" or "xfce4-terminal":
                // gnome-terminal: --zoom before the first --window is the default for every tab.
                // -- / -x must be last in the whole call, so each tab uses --command.
                var a = new List<string>(zoom);
                for (var i = 0; i < tabs.Count; i++)
                    a.AddRange([i == 0 ? "--window" : "--tab", $"--title={tabs[i].Name}", $"--command={Cmd(i)}"]);
                calls.Add(new TermCall(term, a));
                break;
            case "ptyxis":
                // --tab joins the active window; Ptyxis has no zoom option. Each call waits until the instance has
                // handled the previous one (BusName), so the tabs keep their order and the first call owns the instance.
                // A new window uses --tab-with-profile=default together with --new-window; without a profile option,
                // Ptyxis enters single-terminal mode, which hides the tab bar and makes every later --tab invisible.
                // A cold start normally restores the saved session (restore-session) first; LaunchLinux turns that off
                // for the start (see SuppressPtyxisRestore). When it cannot, the restored windows take the active window
                // from the new one, so the first tab also uses --tab: the tabs then at least join a restored window.
                for (var i = 0; i < tabs.Count; i++)
                {
                    List<string> args = i == 0 && !ptyxisRestores
                        ? ["--tab-with-profile=default", "--new-window", "-T", tabs[i].Name, "-x", Cmd(i)]
                        : ["--tab", "-T", tabs[i].Name, "-x", Cmd(i)];
                    calls.Add(new TermCall(term, args, i == 0, PtyxisBusName));
                }
                break;
            default:
                for (var i = 0; i < tabs.Count; i++)
                {
                    string[] shell = ["bash", "-lic", TabScript(tabs[i], prompt, tracks[i], resume)];
                    string[] args = term switch
                    {
                        "konsole" => ["--new-tab", "-p", $"tabtitle={tabs[i].Name}", "-e", .. shell],
                        "kitty" => ["--title", tabs[i].Name, .. shell],
                        "wezterm" => ["start", "--", .. shell],
                        _ => ["-T", tabs[i].Name, "-e", .. shell],
                    };
                    calls.Add(new TermCall(term, [.. zoom, .. args], term == "konsole" && i == 0));
                }
                break;
        }
        return calls;
    }

    private static void LaunchLinux(IReadOnlyList<TabConfig> tabs, string preferred, string? prompt,
        IReadOnlyList<TabTrack?> tracks, int zoomPercent, bool resume)
    {
        var term = preferred != "auto" ? preferred
            : LinuxTerminals.Skip(1).FirstOrDefault(OnPath)
              ?? throw new InvalidOperationException(Loc.Get("err.noTerminal"));
        // Without gdbus the state is unknown: assume a running instance, the --new-window behaviour.
        var coldPtyxis = term == "ptyxis" && BusNameOwned(PtyxisBusName) == false;
        using var restore = coldPtyxis ? SuppressPtyxisRestore() : null;
        foreach (var call in LinuxCommands(tabs, term, prompt, tracks, zoomPercent, coldPtyxis && restore is null, resume))
        {
            var psi = new ProcessStartInfo(call.Exe) { UseShellExecute = false };
            foreach (var a in call.Args) psi.ArgumentList.Add(a);
            var ownedBefore = call.BusName is not null && BusNameOwned(call.BusName) == true;
            using var proc = Process.Start(psi)
                ?? throw new InvalidOperationException(Loc.Format("err.processFailed", call.Exe));
            if (call.BusName is not null && OnPath("gdbus"))
            {
                var ready = WaitForInstance(() => proc.HasExited, () => BusNameOwned(call.BusName) == true, ownedBefore,
                    TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(100));
                if (!ready) throw new InvalidOperationException(Loc.Format("err.terminalTimeout", call.Exe));
                if (proc.HasExited && proc.ExitCode != 0)
                    throw new InvalidOperationException(Loc.Format("err.tabFailed", call.Exe, proc.ExitCode));
            }
            else if (call.SettleAfter) Thread.Sleep(600);
        }
        // The new instance reads the setting while it handles the first call; a single call is not waited for that long.
        if (restore is not null) Thread.Sleep(1000);
    }

    /// <summary>
    /// Turns off Ptyxis' restore-session for a cold start and returns what turns it back on, or null when it was off or
    /// gsettings is missing. A restored session reopens the last run's tabs as plain shells, spread over their old windows,
    /// and whichever of those gets focus takes the --tab calls, so the tabs ended up in several windows next to shells
    /// without a CLI. Without the restore the first call's window is the only one, and every tab joins it.
    /// </summary>
    private static IDisposable? SuppressPtyxisRestore()
    {
        if (!OnPath("gsettings") || Gsettings("get", "org.gnome.Ptyxis", "restore-session") != "true") return null;
        if (Gsettings("set", "org.gnome.Ptyxis", "restore-session", "false") is null) return null;
        return new Undo(() => Gsettings("set", "org.gnome.Ptyxis", "restore-session", "true"));
    }

    private sealed class Undo(Action undo) : IDisposable
    {
        public void Dispose() => undo();
    }

    /// <summary>Runs gsettings; its trimmed output, or null when it fails.</summary>
    private static string? Gsettings(params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo("gsettings") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi);
            if (p is null) return null;
            var output = p.StandardOutput.ReadToEnd();
            return p.WaitForExit(2000) && p.ExitCode == 0 ? output.Trim() : null;
        }
        catch (Exception) { return null; }
    }

    /// <summary>
    /// Waits until a single-instance (GApplication) terminal has taken the call, so the next call joins its window.
    /// A remote call (instance already running, <paramref name="ownedBefore"/>) is done when its process exits: the
    /// instance has then handled it. A cold start is done when the new process owns the bus name: later calls are
    /// queued behind its own command line, which opens the window. Returns false on timeout.
    /// </summary>
    public static bool WaitForInstance(Func<bool> exited, Func<bool> owned, bool ownedBefore, TimeSpan timeout, TimeSpan poll)
    {
        var until = DateTime.UtcNow + timeout;
        while (true)
        {
            if (exited() || (!ownedBefore && owned())) return true;
            if (DateTime.UtcNow >= until) return false;
            Thread.Sleep(poll);
        }
    }

    /// <summary>Whether <paramref name="name"/> has an owner on the session bus; null when gdbus cannot tell.</summary>
    private static bool? BusNameOwned(string name)
    {
        try
        {
            var psi = new ProcessStartInfo("gdbus") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var a in new[] { "call", "--session", "--dest", "org.freedesktop.DBus", "--object-path", "/org/freedesktop/DBus",
                         "--method", "org.freedesktop.DBus.NameHasOwner", name })
                psi.ArgumentList.Add(a);
            using var p = Process.Start(psi);
            if (p is null) return null;
            var output = p.StandardOutput.ReadToEnd();
            if (!p.WaitForExit(2000) || p.ExitCode != 0) return null;
            return output.Contains("true");
        }
        catch (Exception) { return null; }
    }

    public static bool OnPath(string exe) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':')
            .Any(dir => dir != "" && File.Exists(Path.Combine(dir, exe)));

    private static string Quote(string s) => "'" + s.Replace("'", "'\\''") + "'";
}
