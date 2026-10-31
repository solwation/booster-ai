using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace BoosterAI;

/// <summary>Whether WSL can run the tabs (Windows only).</summary>
public enum WslState { Ok, Missing, DistroMissing, Unknown }

/// <summary>One CLI as the tab's login shell sees it: what <c>type -t</c> says and the file <c>type -P</c> finds.</summary>
public sealed record CliStatus(string Cli, string Kind, string? Path)
{
    /// <summary>An executable is on PATH; an alias or function alone does not make a CLI installed.</summary>
    public bool Installed => Path is not null;
    /// <summary>The user's shell already defines the name as an alias or function: BoosterAI never adds one then.</summary>
    public bool HasAlias => Kind is "alias" or "function";
}

/// <summary>What the setup check found: WSL (Windows), the CLIs and npm in the tab's login shell, and BoosterAI's alias block.</summary>
public sealed record SetupStatus(WslState Wsl, IReadOnlyList<string> Distros, IReadOnlyList<CliStatus> Clis, bool NpmFound, bool HasAliasBlock)
{
    public static readonly SetupStatus Empty = new(WslState.Unknown, [], [], false, false);
    /// <summary>The shell check ran (false when WSL is missing or the check failed or timed out).</summary>
    public bool Checked => Clis.Count > 0;
    public CliStatus? Cli(string name) => Clis.FirstOrDefault(c => c.Cli == name);
}

/// <summary>
/// Finds out whether WSL and the AI CLIs are installed where the tabs run, and the commands that install them (#74):
/// each CLI's official installer, run in a visible tab, and an optional full-auto alias in a marked ~/.bashrc block.
/// </summary>
public static partial class Prerequisites
{
    public static readonly IReadOnlyList<string> Clis = ["claude", "codex", "agy"];

    /// <summary>Lines of the detection output start with this; everything else (login banners, .bashrc output) is ignored.</summary>
    private const string Marker = "bsdetect";

    public const string BlockStart = "# >>> BoosterAI full-auto aliases >>>";
    public const string BlockEnd = "# <<< BoosterAI full-auto aliases <<<";

    /// <summary>The bash a check runs in an interactive login shell (<c>bash -lic</c>), so aliases, functions and PATH match a tab's.</summary>
    public const string DetectScript =
        "for c in claude codex agy npm; do t=$(type -t \"$c\" 2>/dev/null | head -n1); p=$(type -P \"$c\" 2>/dev/null); "
        + "printf '" + Marker + " %s %s %s\\n' \"$c\" \"${t:-none}\" \"${p:--}\"; done; "
        + "grep -qF '" + BlockStart + "' ~/.bashrc 2>/dev/null && echo '" + Marker + "-block yes'; true";

    /// <summary>Reads the detection output; lines without the marker are noise from the user's shell startup.</summary>
    public static (IReadOnlyList<CliStatus> Clis, bool NpmFound, bool HasBlock) ParseDetect(string output)
    {
        var clis = new List<CliStatus>();
        var npm = false;
        var block = false;
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line == Marker + "-block yes") { block = true; continue; }
            if (!line.StartsWith(Marker + " ")) continue;
            var parts = line.Split(' ', 4);
            if (parts.Length < 4) continue;
            var (name, kind, path) = (parts[1], parts[2], parts[3].Trim());
            var file = path is "" or "-" ? null : path;
            if (name == "npm") npm = file is not null;
            else if (Clis.Contains(name) && clis.All(c => c.Cli != name)) clis.Add(new CliStatus(name, kind, file));
        }
        return (clis, npm, block);
    }

    /// <summary>The distribution names from <c>wsl.exe -l -q</c> (UTF-16 or UTF-8, with stray NULs, CRs and blank lines).</summary>
    public static IReadOnlyList<string> ParseDistros(string output) =>
        output.Replace("\0", "").Split('\n').Select(l => l.Trim().TrimStart('﻿')).Where(l => l != "").ToList();

    /// <summary>
    /// The WSL state: no wsl.exe, a failing list or no distribution means WSL must be installed; another distribution
    /// than the configured one means the setting is wrong, which an install would not fix.
    /// </summary>
    public static WslState Wsl(bool exeFound, int exitCode, IReadOnlyList<string> distros, string configured)
    {
        if (!exeFound || distros.Count == 0) return WslState.Missing;
        if (distros.Any(d => string.Equals(d, configured.Trim(), StringComparison.OrdinalIgnoreCase))) return WslState.Ok;
        return exitCode == 0 ? WslState.DistroMissing : WslState.Missing;
    }

    /// <summary>
    /// The CLIs to offer an install for: missing ones that a project row uses. When no AI CLI is installed at all
    /// (a new user), all three, so there is always a way to get started.
    /// </summary>
    public static IReadOnlyList<string> MissingToOffer(SetupStatus s, IEnumerable<string> rowLlms)
    {
        if (!s.Checked) return [];
        var missing = Clis.Where(c => s.Cli(c) is not { Installed: true }).ToList();
        if (missing.Count == Clis.Count) return missing;
        var used = rowLlms.ToHashSet();
        return missing.Where(used.Contains).ToList();
    }

    /// <summary>Installed CLIs without an alias or function of their name: those can get the full-auto alias.</summary>
    public static IReadOnlyList<string> AliasesToOffer(SetupStatus s, IEnumerable<string> dismissed)
    {
        var skip = dismissed.ToHashSet();
        return s.Clis.Where(c => c.Installed && !c.HasAlias && !skip.Contains(c.Cli)).Select(c => c.Cli).ToList();
    }

    /// <summary>The full-auto alias for a CLI: it then runs commands and edits files without asking.</summary>
    public static string AliasLine(string cli) => cli switch
    {
        "claude" => "alias claude='claude --dangerously-skip-permissions'",
        "codex" => "alias codex='codex --dangerously-bypass-approvals-and-sandbox'",
        "agy" => "alias agy='agy --dangerously-skip-permissions'",
        _ => throw new ArgumentException("No alias for " + cli, nameof(cli)),
    };

    [GeneratedRegex(@"^\s*(?:alias\s+(?:-\S+\s+)*([A-Za-z0-9_.-]+)=|(?:function\s+)?([A-Za-z0-9_.-]+)\s*\(\s*\)|function\s+([A-Za-z0-9_.-]+))", RegexOptions.Multiline)]
    private static partial Regex Definition();

    /// <summary>The names .bashrc text defines as an alias or function (anywhere, also inside BoosterAI's block).</summary>
    public static IReadOnlySet<string> Defined(string? bashrc) =>
        Definition().Matches(bashrc ?? "").Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value).ToHashSet();

    /// <summary>
    /// ~/.bashrc with a full-auto alias for each CLI added to BoosterAI's marked block (created at the end when missing).
    /// A CLI the file already defines as an alias or function is skipped, so nothing is overwritten or added twice;
    /// the rest of the file is kept byte for byte.
    /// </summary>
    public static string AddAliases(string? bashrc, IEnumerable<string> clis)
    {
        var text = bashrc ?? "";
        var defined = Defined(text);
        var lines = clis.Distinct().Where(c => Clis.Contains(c) && !defined.Contains(c)).Select(AliasLine).ToList();
        if (lines.Count == 0) return text;
        var end = text.IndexOf(BlockEnd, StringComparison.Ordinal);
        if (text.Contains(BlockStart, StringComparison.Ordinal) && end >= 0)
            return text[..end] + string.Concat(lines.Select(l => l + "\n")) + text[end..];
        var sep = text == "" || text.EndsWith('\n') ? "" : "\n";
        return text + sep + (text == "" ? "" : "\n") + BlockStart + "\n"
            + "# Added by BoosterAI: these CLIs run commands and edit files without asking. Delete this block to undo.\n"
            + string.Concat(lines.Select(l => l + "\n")) + BlockEnd + "\n";
    }

    /// <summary>
    /// The bash a visible install tab runs for a CLI (same on Windows, inside WSL, and Linux): the official installer,
    /// a line on the next step (log in) or on the failure, then a login shell, which has the new PATH.
    /// No ';' (Windows Terminal splits on it).
    /// </summary>
    public static string InstallScript(string cli)
    {
        var (intro, body, failHint) = cli switch
        {
            // https://code.claude.com/docs/en/setup (native install: macOS, Linux, WSL)
            "claude" => ("installing Claude Code with the official script", "set -o pipefail && curl -fsSL https://claude.ai/install.sh | bash", ""),
            // The npm package from the Codex README; npm comes with Node.js.
            "codex" => ("installing Codex with npm install -g @openai/codex",
                "(command -v npm >/dev/null || ! echo 'BoosterAI: npm was not found. Codex is installed with npm, which comes with Node.js. "
                + "Install Node.js first, for example with nvm (https://github.com/nvm-sh/nvm, then nvm install --lts), and press Install again.') "
                + "&& npm install -g @openai/codex",
                " If npm reported EACCES, its global folder needs root: use a Node.js from nvm instead of sudo."),
            // https://antigravity.google/docs/getting-started?tab=cli (macOS and Linux; installs ~/.local/bin/agy)
            "agy" => ("installing the Antigravity CLI with the official script", "set -o pipefail && curl -fsSL https://antigravity.google/cli/install.sh | bash", ""),
            _ => throw new ArgumentException("No installer for " + cli, nameof(cli)),
        };
        return $"cd ~ && echo 'BoosterAI: {intro}' && echo && {body}"
            + $" && echo && echo 'BoosterAI: done. Start a new BoosterAI tab, or run {LoginCommand(cli)} here to log in.'"
            + $" || (echo && echo 'BoosterAI: the install did not finish, see the output above.{failHint} BoosterAI keeps working.')"
            + " && exec bash -l";
    }

    /// <summary>How each CLI logs in after the install.</summary>
    public static string LoginCommand(string cli) => cli == "codex" ? "codex login" : cli;

    /// <summary>The visible, elevated command that installs WSL on Windows (after the user confirmed; a restart usually follows).</summary>
    public static readonly (string Exe, string[] Args) WslInstall = ("powershell.exe", ["-NoExit", "-Command", "wsl.exe --install"]);

    /// <summary>Runs the whole check off the UI thread. Never shows a window; gives up after <paramref name="timeout"/>.</summary>
    public static SetupStatus Detect(string distro, TimeSpan? timeout = null)
    {
        var limit = timeout ?? TimeSpan.FromSeconds(30);
        var wsl = WslState.Ok;
        IReadOnlyList<string> distros = [];
        ProcessStartInfo shell;
        if (OperatingSystem.IsWindows())
        {
            var list = Hidden("wsl.exe", ["-l", "-q"]);
            list.Environment["WSL_UTF8"] = "1";
            var (found, code, output) = Run(list, TimeSpan.FromSeconds(15));
            distros = ParseDistros(output ?? "");
            wsl = Wsl(found, code, distros, distro);
            if (wsl != WslState.Ok) return new SetupStatus(wsl, distros, [], false, false);
            shell = Hidden("wsl.exe", ["-d", distro, "--exec", "bash", "-lic", DetectScript]);
        }
        else shell = Hidden("bash", ["-lic", DetectScript]);
        var (_, _, text) = Run(shell, limit);
        var (clis, npm, block) = ParseDetect(text ?? "");
        return new SetupStatus(wsl, distros, clis, npm, block);
    }

    internal static ProcessStartInfo Hidden(string exe, IEnumerable<string> args)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        return psi;
    }

    /// <summary>
    /// Starts the process with no input, collects its output and always leaves nothing behind: a shell that is still
    /// running at the limit (a .bashrc waiting for something) is killed with its whole tree.
    /// Returns (started, exit code or -1, output or null).
    /// </summary>
    internal static (bool Started, int ExitCode, string? Output) Run(ProcessStartInfo psi, TimeSpan limit)
    {
        Process? p = null;
        try
        {
            try { p = Process.Start(psi); }
            catch (System.ComponentModel.Win32Exception) { return (false, -1, null); }
            if (p is null) return (false, -1, null);
            p.StandardInput.Close();
            var output = p.StandardOutput.ReadToEndAsync();
            _ = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(limit)) return (true, -1, null);
            p.WaitForExit();
            return (true, p.ExitCode, output.Result);
        }
        catch (Exception e) when (e is IOException or InvalidOperationException) { return (true, -1, null); }
        finally
        {
            try { if (p is { HasExited: false }) p.Kill(entireProcessTree: true); }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            p?.Dispose();
        }
    }
}
