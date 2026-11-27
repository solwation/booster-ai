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
        foreach (var script in new[]
        {
            Launcher.TabScript(tab, null, Track()),
            Launcher.TabScript(tab, null, Track(), resume: true),
            Launcher.TabScript(tab, Launcher.InstallOrkesterPrompt, Track()),
            Launcher.RestartScript(tab, Track(), resume: false),
            Launcher.RestartScript(tab, Track(), resume: true),
        })
        {
            // After the terminal reset, before the update and the CLI; the resume fallback's fresh start does not repeat it.
            Assert.Contains("(bs_reset 2>/dev/null || true) && " + Logo, script);
            Assert.Equal(1, Count(script, "bs_logo"));
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
    public async Task AnimatesInColourAndRestoresTheCursor(int columns, string env, string colour)
    {
        if (!CanRunScript) return;
        var (output, ms) = await InTerminal(columns, 30, env);
        Assert.Contains("\e[?25l", output);
        Assert.EndsWith("\e[0m\e[?25hok", output); // The cursor is shown again last.
        Assert.Contains(colour, output);
        Assert.Contains(columns >= TabLogo.FullColumns ? "\e[8A" : "\e[4A", output); // Frames redraw in place.
        Assert.InRange(ms, 0, 1500);
        Assert.Contains("█", output);
    }

    [Theory]
    [InlineData("NO_COLOR=1 COLORTERM=truecolor")]
    [InlineData("TERM=vt100")]
    public async Task PlainTerminalsGetItStill(string env)
    {
        if (!CanRunScript) return;
        var (output, _) = await InTerminal(100, 30, env);
        Assert.DoesNotContain('\e', output);
        Assert.Contains("█", output);
        Assert.Equal(8, output.Split('\n').Count(l => l.Contains('▀') || l.Contains('▄') || l.Contains('█')));
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
        Assert.DoesNotContain("\e[?25l", output);
        if (line) Assert.Contains(env.StartsWith("NO_COLOR") ? "BoosterAI" : "\e[1;38;2;", output);
        else Assert.Equal("ok", output.Trim());
    }

    private static int Count(string s, string part) => (s.Length - s.Replace(part, "").Length) / part.Length;

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
