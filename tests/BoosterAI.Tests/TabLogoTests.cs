using System.Diagnostics;
using BoosterAI;
using Xunit;

/// <summary>BoosterAI's logo at the top of a tab (#242): the art, where the tab script shows it, and how bs_logo adapts.</summary>
public class TabLogoTests
{
    private const string Logo = "(bs_logo 2>/dev/null || true) && ";

    private static TabTrack Track(bool logo = true) => new("/s/tab-lib.sh", "/s", "tab1", Logo: logo);

    [Fact]
    public void ArtIsRectangularAndFitsItsSmallestTerminal()
    {
        var full = TabLogo.Full();
        var word = TabLogo.Wordmark();
        Assert.Equal(16, full.Length);
        Assert.Equal(8, word.Length);
        Assert.All(full, r => Assert.Equal(full[0].Length, r.Length));
        Assert.All(word, r => Assert.Equal(word[0].Length, r.Length));
        Assert.All(TabLogo.Mark, r => Assert.Equal(16, r.Length));
        Assert.Equal(16 + TabLogo.Gap + word[0].Length, full[0].Length);
        // Two columns of margin, and nothing reaches the last column (a full line would wrap in some terminals).
        Assert.True(full[0].Length + 2 < TabLogo.FullColumns);
        Assert.True(word[0].Length + 2 < TabLogo.WordColumns);
        Assert.True(TabLogo.FullLines > full.Length / 2 + 2);
        Assert.True(TabLogo.WordLines > word.Length / 2 + 2);
        // The mark keeps the icon's white bolt; the wordmark is only gradient pixels.
        Assert.Contains(TabLogo.Mark, r => r.Contains('@'));
        Assert.DoesNotContain(word, r => r.Contains('@'));
    }

    [Fact]
    public void CellsPairPixelRows()
    {
        Assert.Equal(["1054", "5486"], TabLogo.Cells(["..##", "#.@#", "##@@", "@#@."]));
        Assert.Equal(8, TabLogo.Cells(TabLogo.Full()).Length);
        Assert.Equal(4, TabLogo.Cells(TabLogo.Wordmark()).Length);
    }

    [Fact]
    public void TabLibDefinesTheLogo()
    {
        Assert.Contains("\nbs_logo() (", TabSession.Lib);
        Assert.Contains("\nbs_logo_full=(" + string.Join(' ', TabLogo.Cells(TabLogo.Full())) + ")\n", TabSession.Lib);
        Assert.DoesNotContain("\r", TabLogo.Script);
    }

    [Theory]
    [InlineData("claude")]
    [InlineData("codex")]
    [InlineData("agy")]
    [InlineData("aider")]
    [InlineData("openhands")]
    [InlineData("interpreter")]
    public void InteractiveStartsContinuationsAndRestartsShowTheLogoOnce(string llm)
    {
        var tab = new TabConfig { Name = "Demo", Directory = "~/demo", Llm = llm, Orkester = true };
        foreach (var (script, restart) in new[]
        {
            (Launcher.TabScript(tab, null, Track()), false),
            (Launcher.TabScript(tab, null, Track(), resume: true), false),
            (Launcher.TabScript(tab, Launcher.InstallOrkesterPrompt, Track()), false),
            (Launcher.RestartScript(tab, Track(), resume: false), true),
            (Launcher.RestartScript(tab, Track(), resume: true), true),
        })
        {
            // After the terminal reset, before the update and the CLI; the resume fallback's fresh start does not repeat it.
            // A restart first loads the session's logo script into a shell from an older build (#243).
            var load = restart ? "{ type bs_logo >/dev/null 2>&1 || . '/s/" + TabLogo.ScriptFile + "' 2>/dev/null || true\n} && " : "";
            Assert.Contains("(bs_reset 2>/dev/null || true) && " + load + Logo, script);
            Assert.Equal(1, Count(script, Logo));
            Assert.DoesNotContain(';', script);
        }
    }

    [Fact]
    public void ShellTabsShowTheLogo()
    {
        var tab = new TabConfig { Name = "Shell", Directory = "~", Llm = Catalog.ShellOnly };
        Assert.Contains(Logo + "(bs_cli", Launcher.TabScript(tab, null, Track()));
        Assert.Contains(Logo, Launcher.RestartScript(tab, Track(), resume: false));
    }

    [Theory]
    [InlineData("claude")]
    [InlineData("codex")]
    [InlineData("agy")]
    [InlineData("aider")]
    [InlineData("openhands")]
    public void RunOnceAndTheSwitchOffShowNoLogo(string llm)
    {
        var once = new TabConfig { Name = "Once", Directory = "~/demo", Llm = llm, Orkester = true, NonInteractive = true };
        Assert.DoesNotContain("bs_logo", Launcher.TabScript(once, null, Track()));
        Assert.DoesNotContain("bs_logo", Launcher.TabScript(once, null, Track(), resume: true)); // Run once never continues.
        Assert.DoesNotContain("bs_logo", Launcher.RestartScript(once, Track(), resume: false));
        // Run once with a given prompt (the orchestra install) is interactive and shows it.
        Assert.Contains("bs_logo", Launcher.TabScript(once, Launcher.InstallOrkesterPrompt, Track()));
        var tab = new TabConfig { Name = "Demo", Directory = "~/demo", Llm = llm, Orkester = true };
        Assert.DoesNotContain("bs_logo", Launcher.TabScript(tab, null, Track(logo: false)));
        Assert.DoesNotContain("bs_logo", Launcher.RestartScript(tab, Track(logo: false), resume: true));
        Assert.DoesNotContain("bs_logo", Launcher.TabScript(tab)); // Untracked: no tab-lib.sh.
    }

    [Fact]
    public void AgyLaunchPathsShowTheLogo()
    {
        var tab = new TabConfig { Name = "Agy", Directory = "~", Llm = "agy", Orkester = true, NonInteractive = true };
        Assert.All(Launcher.AgyLaunchPaths(tab, Track()), p => Assert.Contains(Logo, p.Script));
    }

    [Fact]
    public void SettingIsOnByDefaultAndSaved()
    {
        Assert.True(new AppConfig().ShowTabLogo);
        var json = System.Text.Json.JsonSerializer.Serialize(new AppConfig { ShowTabLogo = false }, ConfigJsonContext.Default.AppConfig);
        Assert.False(System.Text.Json.JsonSerializer.Deserialize(json, ConfigJsonContext.Default.AppConfig)!.ShowTabLogo);
        Assert.True(System.Text.Json.JsonSerializer.Deserialize("{}", ConfigJsonContext.Default.AppConfig)!.ShowTabLogo);
        Assert.True(new TabTrack("/s/tab-lib.sh", "/s", "tab1").Logo);
    }

    private static bool CanRunBash => OperatingSystem.IsLinux() && File.Exists("/bin/bash");
    private static bool CanRunScript => CanRunBash && File.Exists("/usr/bin/script") && File.Exists("/usr/bin/stty");

    [Theory]
    [InlineData(200, 50, "full")]
    [InlineData(TabLogo.FullColumns, TabLogo.FullLines, "full")]
    [InlineData(TabLogo.FullColumns - 1, 50, "word")]
    [InlineData(200, TabLogo.FullLines - 1, "word")]
    [InlineData(TabLogo.WordColumns, TabLogo.WordLines, "word")]
    [InlineData(TabLogo.WordColumns - 1, 50, "line")]
    [InlineData(200, TabLogo.WordLines - 1, "line")]
    [InlineData(TabLogo.LineColumns, TabLogo.LineLines, "line")]
    [InlineData(TabLogo.LineColumns - 1, 50, "none")]
    [InlineData(200, TabLogo.LineLines - 1, "none")]
    public async Task SizeFollowsTheTerminal(int columns, int lines, string expected)
    {
        if (!CanRunBash) return;
        Assert.Equal(expected, (await Bash($"bs_logo_size {columns} {lines}")).Trim());
    }

    [Theory]
    [InlineData("COLORTERM=truecolor", "24")]
    [InlineData("COLORTERM=24bit TERM=xterm", "24")]
    [InlineData("WT_SESSION=x TERM=xterm-256color", "24")]
    [InlineData("TERM=xterm-direct", "24")]
    [InlineData("TERM=xterm-256color", "8")]
    [InlineData("TERM=vt100", "0")]
    [InlineData("NO_COLOR=1 COLORTERM=truecolor", "0")]
    [InlineData("NO_COLOR= TERM=xterm-256color", "8")] // An empty NO_COLOR does not count (no-color.org).
    public async Task ColourDepthFollowsTheTerminal(string env, string expected)
    {
        if (!CanRunBash) return;
        Assert.Equal(expected, (await Bash("bs_logo_depth", env)).Trim());
    }

    [Fact]
    public async Task NothingWithoutATerminal()
    {
        if (!CanRunBash) return;
        Assert.Equal("done", await Bash("bs_logo && printf done", "COLORTERM=truecolor"));
    }

    [Theory]
    [InlineData(100, "COLORTERM=truecolor", "\e[38;2;")]
    [InlineData(70, "TERM=xterm-256color", "\e[38;5;")]
    public async Task SplashIsCentredFadesOutAndRestoresTheScreen(int columns, string env, string colour)
    {
        if (!CanRunScript) return;
        const int lines = 30;
        var (output, ms) = await InTerminal(columns, lines, env);
        var art = TabLogo.Cells(columns >= TabLogo.FullColumns ? TabLogo.Full() : TabLogo.Wordmark());
        // On the alternate screen with the cursor hidden; left again, cleared and the cursor shown last (#244).
        Assert.StartsWith("\e[?1049h\e[?25l\e[2J", output);
        Assert.EndsWith("\e[0m\e[2J\e[?1049l\e[?25hok", output);
        Assert.Contains(colour, output);
        // Every row in the middle of the tab, horizontally and vertically.
        var top = (lines - art.Length) / 2 + 1;
        var left = (columns - art[0].Length) / 2 + 1;
        Assert.Contains($"\e[{top};{left}H", output);
        Assert.Contains($"\e[{top + art.Length - 1};{left}H", output);
        Assert.DoesNotContain("\n", output.Replace("ok", ""));
        // The fade ends near the dark background, unless the 1.5 s deadline cut it short on a loaded machine.
        if (ms < 1450) Assert.Contains(colour.Contains(";2;") ? "\e[38;2;12;12;12m" : "\e[38;5;16m", output);
        // Bounded, not a speed test: the splash stops 1.5 s after bs_logo starts, but a slow CI runner draws each frame
        // slowly; a splash that ignored its deadlines would run for several seconds.
        Assert.InRange(ms, 900, 4000);
        Assert.Contains("█", output);
    }

    [Theory]
    [InlineData("NO_COLOR=1 COLORTERM=truecolor")]
    [InlineData("TERM=vt100")]
    public async Task PlainTerminalsGetItStillAndCentred(string env)
    {
        if (!CanRunScript) return;
        var (output, _) = await InTerminal(100, 30, env);
        Assert.DoesNotContain("\e[38", output);
        Assert.DoesNotContain("\e[?25l", output);
        var rows = output.Split('\n').Where(l => l.Contains('▀') || l.Contains('▄') || l.Contains('█')).ToArray();
        Assert.Equal(8, rows.Length);
        // Centred: the widest row starts after half the spare columns (tput's clear may come first).
        var left = (100 - TabLogo.Cells(TabLogo.Full())[0].Length) / 2;
        Assert.Equal(left, rows.Select(r => r[(r.LastIndexOf('\e') + 1)..]).Select(r => r.TrimStart('[', 'H', 'J', '2', '3')).Min(r => r.Length - r.TrimStart(' ').Length));
    }

    [Theory]
    [InlineData(40, "COLORTERM=truecolor", true)]
    [InlineData(40, "NO_COLOR=1", true)]
    [InlineData(TabLogo.LineColumns - 1, "COLORTERM=truecolor", false)]
    public async Task NarrowTerminalsGetOneLineOrNothing(int columns, string env, bool line)
    {
        if (!CanRunScript) return;
        var (output, _) = await InTerminal(columns, 30, env);
        Assert.DoesNotContain("█", output);
        if (!line) { Assert.Equal("ok", output.Trim()); return; }
        if (env.StartsWith("NO_COLOR")) Assert.Contains(new string(' ', (columns - 9) / 2) + "BoosterAI", output);
        else Assert.Contains($"\e[15;{(columns - 9) / 2 + 1}H\e[1;38;2;", output);
    }

    private static int Count(string s, string part) => (s.Length - s.Replace(part, "").Length) / part.Length;

    [Theory]
    [InlineData("claude")]
    [InlineData("agy")]
    [InlineData(Catalog.ShellOnly)]
    public async Task RestartInAShellFromAnOlderBuildLoadsTheSessionsLogo(string llm)
    {
        if (!CanRunScript) return;
        var root = Directory.CreateTempSubdirectory("booster-logo").FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, TabLogo.ScriptFile), TabLogo.Script);
            var track = new TabTrack(root + "/tab-lib.sh", root, "tab1");
            var restart = Launcher.RestartScript(new TabConfig { Llm = llm, Directory = root }, track, resume: false);
            Assert.DoesNotContain(';', restart);
            // The tab's shell has no bs_logo, as after sourcing an older tab-lib.sh; the restart is eval'd like bs_next does.
            File.WriteAllText(Path.Combine(root, "next"), restart);
            var older = await InPty(root, "unset -f bs_logo && eval \"$(cat next)\"")
                .ConfigureAwait(false);
            Assert.Contains("█", older);
            Assert.Contains("\e[?25h", older);
            // A shell that has bs_logo keeps its own and loads nothing.
            File.WriteAllText(Path.Combine(root, TabLogo.ScriptFile), "bs_logo() { echo reloaded\n}\n");
            var current = await InPty(root, "bs_logo() { echo current\n}\neval \"$(cat next)\"");
            Assert.Contains("current", current);
            Assert.DoesNotContain("reloaded", current);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void RestartLoadsNothingWithTheLogoOffOrInRunOnce()
    {
        var track = new TabTrack("/s/tab-lib.sh", "/s", "tab1");
        Assert.DoesNotContain(TabLogo.ScriptFile, Launcher.RestartScript(new TabConfig { Llm = "claude" }, track with { Logo = false }, resume: false));
        Assert.DoesNotContain(TabLogo.ScriptFile, Launcher.RestartScript(new TabConfig { Llm = "claude", Orkester = true, NonInteractive = true }, track, resume: false));
        Assert.Contains("/s/" + TabLogo.ScriptFile, Launcher.RestartScript(new TabConfig { Llm = "claude" }, track, resume: true));
    }

    [Fact]
    public void EverySessionWritesTheLogoScript()
    {
        var root = Directory.CreateTempSubdirectory("booster-logo").FullName;
        try
        {
            var session = new TabSession(root, DateTime.Now);
            Assert.Equal(TabLogo.Script.Replace("\r\n", "\n"), File.ReadAllText(Path.Combine(session.Directory, TabLogo.ScriptFile)));
        }
        finally { Directory.Delete(root, true); }
    }

    /// <summary>Runs a bash <paramref name="body"/> in <paramref name="root"/> on an 120 × 30 pseudo-terminal; returns its output.</summary>
    private static async Task<string> InPty(string root, string body)
    {
        File.WriteAllText(Path.Combine(root, "body.sh"), "stty cols 120 rows 30\n" + body + "\nprintf ok\n");
        var psi = new ProcessStartInfo("script") { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = root };
        psi.ArgumentList.Add("-qec");
        psi.ArgumentList.Add("bash --noprofile --norc body.sh");
        psi.ArgumentList.Add("/dev/null");
        Environment(psi, root, "COLORTERM=truecolor TERM=xterm-256color");
        return (await Run(psi, root)).Replace("\r\n", "\n");
    }

    /// <summary>Runs <paramref name="command"/> after sourcing the logo script, with stdout a pipe.</summary>
    private static async Task<string> Bash(string command, string env = "")
    {
        var root = Directory.CreateTempSubdirectory("booster-logo").FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "logo.sh"), TabLogo.Script);
            var psi = new ProcessStartInfo("bash") { RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = root };
            psi.ArgumentList.Add("--noprofile");
            psi.ArgumentList.Add("--norc");
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(". ./logo.sh && " + command);
            Environment(psi, root, env);
            return await Run(psi, root);
        }
        finally { Directory.Delete(root, true); }
    }

    /// <summary>Runs bs_logo on a pseudo-terminal of <paramref name="columns"/> × <paramref name="lines"/>; returns its output and duration.</summary>
    private static async Task<(string Output, long Ms)> InTerminal(int columns, int lines, string env)
    {
        var root = Directory.CreateTempSubdirectory("booster-logo").FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "logo.sh"), TabLogo.Script);
            var psi = new ProcessStartInfo("script") { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = root };
            psi.ArgumentList.Add("-qec");
            psi.ArgumentList.Add($"bash --noprofile --norc -c '. ./logo.sh && stty cols {columns} rows {lines} && t=$(date +%s%3N) && bs_logo && echo $(($(date +%s%3N) - t)) > ms && printf ok'");
            psi.ArgumentList.Add("/dev/null");
            Environment(psi, root, env);
            var output = await Run(psi, root);
            return (output.Replace("\r\n", "\n"), long.Parse(File.ReadAllText(Path.Combine(root, "ms"))));
        }
        finally { Directory.Delete(root, true); }
    }

    private static void Environment(ProcessStartInfo psi, string root, string env)
    {
        TestShell.Configure(psi, root);
        psi.Environment["LANG"] = "C.UTF-8";
        foreach (var pair in env.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var i = pair.IndexOf('=');
            psi.Environment[pair[..i]] = pair[(i + 1)..];
        }
    }

    private static async Task<string> Run(ProcessStartInfo psi, string root)
    {
        using var process = Process.Start(psi)!;
        try
        {
            var output = process.StandardOutput.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await process.WaitForExitAsync(timeout.Token);
            return await output;
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            TestShell.StopLeftovers(root);
        }
    }
}
