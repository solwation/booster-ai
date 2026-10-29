using System.Diagnostics;
using BoosterAI;
using Xunit;

/// <summary>Updating claude, codex and agy before a tab starts them (#60): the tab chain and tab-lib.sh's bs_update.</summary>
public class CliUpdateTests
{
    private static readonly TabTrack Track = new("/s/tab-lib.sh", "/s", "tab1", UpdateClis: true);

    [Theory]
    [InlineData("claude")]
    [InlineData("codex")]
    [InlineData("agy")]
    public void UpdateRunsBeforeTheCliAndCannotFailTheChain(string llm)
    {
        var tab = new TabConfig { Llm = llm, Directory = "~/proj" };
        foreach (var script in new[] { Launcher.TabScript(tab, null, Track), Launcher.RestartScript(tab, Track, resume: false) })
        {
            Assert.Contains($"~/'proj' && (bs_update {llm} 2>/dev/null || true) && (bs_cli 2>/dev/null || true) && ", script.Replace(@"'\''", "'"));
            Assert.DoesNotContain(';', script);
        }
    }

    [Fact]
    public void NoUpdateWhenOffForShellsOrWithoutLibrary()
    {
        var codex = new TabConfig { Llm = "codex" };
        Assert.DoesNotContain("bs_update", Launcher.TabScript(codex, null, Track with { UpdateClis = false }));
        Assert.DoesNotContain("bs_update", Launcher.RestartScript(codex, Track with { UpdateClis = false }, resume: true));
        Assert.DoesNotContain("bs_update", Launcher.TabScript(new TabConfig { Llm = Catalog.ShellOnly }, null, Track));
        Assert.DoesNotContain("bs_update", Launcher.RestartScript(new TabConfig { Llm = Catalog.ShellOnly }, Track, resume: false));
        // The fallback for a missing library (and an untracked start) has no bs_update to call.
        Assert.DoesNotContain("bs_update", Launcher.TabScript(codex));
        Assert.DoesNotContain("bs_update", Launcher.TabScript(codex, null, Track).Split("bs_command=")[1]);
    }

    [Fact]
    public void SettingIsOnByDefaultAndSaved()
    {
        Assert.True(new AppConfig().AutoUpdateClis);
        var json = System.Text.Json.JsonSerializer.Serialize(new AppConfig { AutoUpdateClis = false }, ConfigJsonContext.Default.AppConfig);
        Assert.Contains("\"AutoUpdateClis\": false", json);
        Assert.False(System.Text.Json.JsonSerializer.Deserialize(json, ConfigJsonContext.Default.AppConfig)!.AutoUpdateClis);
        Assert.True(System.Text.Json.JsonSerializer.Deserialize("{}", ConfigJsonContext.Default.AppConfig)!.AutoUpdateClis);
    }

    [Fact]
    public void NewTrackCarriesTheSetting()
    {
        var root = Directory.CreateTempSubdirectory("booster-update-track").FullName;
        try
        {
            var session = new TabSession(root, DateTime.Now);
            Assert.True(session.NewTrack(updateClis: true)!.UpdateClis);
            Assert.False(session.NewTrack()!.UpdateClis);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void DryRunListsTheCommandPerCli() =>
        Assert.Equal(["claude", "codex", "agy"], Launcher.CliUpdateCommands.Select(c => c.Llm));

    /// <summary>Fake codex (installed under a fake npm root), npm, claude and agy, a HOME for the stamps, and the lib.</summary>
    private sealed class Fakes : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("booster-cli-update").FullName;
        public string Bin => Path.Combine(Root, "bin");
        public string Home => Path.Combine(Root, "home");
        public string Calls => Path.Combine(Root, "calls");
        public string StateDir => Path.Combine(Home, ".local/share/BoosterAI/cli-update");

        public Fakes()
        {
            Directory.CreateDirectory(Bin);
            Directory.CreateDirectory(Home);
            var codexDir = Path.Combine(Root, "npmroot/@openai/codex/bin");
            Directory.CreateDirectory(codexDir);
            Set("codex", "0.165.0");
            Set("codex-latest", "0.165.0");
            Set("claude", "2.1.312");
            Set("agy", "1.4.10");
            Write(Path.Combine(codexDir, "codex.js"), $"""
                [ "$1" = --version ] && {'{'} echo "codex-cli $(cat {Root}/codex)"; exit 0; {'}'}
                echo "codex $*" >> {Calls}
                """);
            File.CreateSymbolicLink(Path.Combine(Bin, "codex"), Path.Combine(codexDir, "codex.js"));
            Write(Path.Combine(Bin, "npm"), $$"""
                echo "npm $*" >> {{Calls}}
                case "$1 $2" in
                  "root -g") echo {{Root}}/npmroot ;;
                  "view @openai/codex") [ -f {{Root}}/offline ] && { echo "npm error network request failed" >&2; exit 1; }; cat {{Root}}/codex-latest ;;
                  "install -g") sleep "${FAKE_INSTALL_SECONDS:-0}"; echo "${3##*@}" > {{Root}}/codex ;;
                esac
                """);
            foreach (var cli in new[] { "claude", "agy" })
                Write(Path.Combine(Bin, cli), $$"""
                    [ "$1" = --version ] && { echo "$(cat {{Root}}/{{cli}}) ({{cli}})"; exit 0; }
                    echo "{{cli}} $*" >> {{Calls}}
                    [ -f {{Root}}/{{cli}}-sleep ] && sleep 5
                    [ -f {{Root}}/{{cli}}-fail ] && { echo "Error: permission denied" >&2; exit 1; }
                    [ ! -f {{Root}}/{{cli}}-latest ] || cp {{Root}}/{{cli}}-latest {{Root}}/{{cli}}
                    """);
            File.WriteAllText(Path.Combine(Root, "tab-lib.sh"), TabSession.Lib.Replace("\r\n", "\n"));
        }

        public void Set(string name, string value) => File.WriteAllText(Path.Combine(Root, name), value + "\n");

        private static void Write(string path, string body)
        {
            File.WriteAllText(path, "#!/bin/bash\n" + body.Replace("\r\n", "\n") + "\n");
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        public string[] ReadCalls() => File.Exists(Calls) ? File.ReadAllLines(Calls) : [];

        /// <summary>Sources the lib like a tab, runs <paramref name="body"/>; stdout and the exit code.</summary>
        public (string Output, int Code) Run(string body, string? path = null, int seconds = 20)
        {
            var psi = new ProcessStartInfo("bash") { RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Root };
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add($". '{Root}/tab-lib.sh' '{Root}' tab1 && {body}");
            psi.Environment["HOME"] = Home;
            psi.Environment["PATH"] = path ?? Bin + ":/usr/bin:/bin";
            using var p = Process.Start(psi)!;
            var output = p.StandardOutput.ReadToEndAsync();
            Assert.True(p.WaitForExit(seconds * 1000), "bash did not finish");
            return (output.Result, p.ExitCode);
        }

        public void Dispose() => Directory.Delete(Root, true);
    }

    private static bool CanRunBash => OperatingSystem.IsLinux() && File.Exists("/usr/bin/flock") && File.Exists("/usr/bin/timeout");

    [Theory]
    [InlineData("0.166.0", "npm root -g|npm view @openai/codex version|npm install -g @openai/codex@0.166.0", "updated to 0.166.0")]
    [InlineData("0.165.0", "npm root -g|npm view @openai/codex version", "up to date")]
    [InlineData("0.164.9", "npm root -g|npm view @openai/codex version", "up to date")]
    public void CodexFromNpmInstallsOnlyANewerVersion(string latest, string calls, string result)
    {
        if (!CanRunBash) return;
        using var f = new Fakes();
        f.Set("codex-latest", latest);
        var (output, code) = f.Run("bs_update codex && echo started");
        Assert.Equal(0, code);
        Assert.Equal(calls.Split('|'), f.ReadCalls());
        Assert.Equal($"BoosterAI: checking for a newer codex 0.165.0... {result}\nstarted\n", output);
    }

    [Fact]
    public void CodexOutsideNpmUsesItsOwnUpdater()
    {
        if (!CanRunBash) return;
        using var f = new Fakes();
        File.Delete(Path.Combine(f.Bin, "npm"));
        Assert.Equal(0, f.Run("bs_update codex").Code);
        Assert.Equal(["codex update"], f.ReadCalls());
    }

    [Theory]
    [InlineData("claude", "2.1.313")]
    [InlineData("agy", "1.4.11")]
    public void ClaudeAndAgyUseTheirOwnUpdater(string cli, string newer)
    {
        if (!CanRunBash) return;
        using var f = new Fakes();
        f.Set(cli + "-latest", newer);
        var (output, _) = f.Run($"bs_update {cli}");
        Assert.Equal([cli + " update"], f.ReadCalls());
        Assert.EndsWith($"updated to {newer}\n", output);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(5 * 3600, false)]
    [InlineData(7 * 3600, true)]
    public void ChecksAtMostOncePerInterval(int ageSeconds, bool checks)
    {
        if (!CanRunBash) return;
        using var f = new Fakes();
        Directory.CreateDirectory(f.StateDir);
        var stamp = Path.Combine(f.StateDir, "claude.checked");
        File.WriteAllText(stamp, "");
        File.SetLastWriteTimeUtc(stamp, DateTime.UtcNow.AddSeconds(-ageSeconds));
        var (output, _) = f.Run("bs_update claude && echo started");
        Assert.Equal(checks, f.ReadCalls().Contains("claude update"));
        Assert.Equal(checks, output.Contains("checking"));
        if (checks) Assert.True(File.GetLastWriteTimeUtc(stamp) > DateTime.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public void ASecondStartWithinTheIntervalChecksNothing()
    {
        if (!CanRunBash) return;
        using var f = new Fakes();
        f.Run("bs_update codex");
        File.Delete(f.Calls);
        Assert.Equal("started\n", f.Run("bs_update codex && echo started").Output);
        Assert.Empty(f.ReadCalls());
    }

    [Theory]
    [InlineData("offline", "codex", "skipped: npm view @openai/codex failed (npm error network request failed)")]
    [InlineData("claude-fail", "claude", "skipped: claude update failed (Error: permission denied)")]
    [InlineData("agy-sleep", "agy", "skipped: agy update failed (timed out)")]
    public void FailuresPrintOneLineAndNeverStopTheStart(string failure, string cli, string reason)
    {
        if (!CanRunBash) return;
        using var f = new Fakes();
        f.Set(failure, "");
        var (output, code) = f.Run($"bs_up_limit=1 && bs_update {cli} && echo started");
        Assert.Equal(0, code);
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.StartsWith("BoosterAI: checking for a newer " + cli, lines[0]);
        Assert.EndsWith(reason, lines[0]);
        Assert.Equal("started", lines[1]);
        // A failure is not retried on every start either.
        Assert.True(File.Exists(Path.Combine(f.StateDir, cli + ".checked")));
    }

    [Fact]
    public void MissingToolsOrStateFolderOnlySkip()
    {
        if (!CanRunBash) return;
        using var f = new Fakes();
        // A PATH with the fakes and coreutils but without flock.
        var tools = Path.Combine(f.Root, "tools");
        Directory.CreateDirectory(tools);
        foreach (var t in new[] { "bash", "cat", "cut", "date", "stat", "grep", "head", "mkdir", "touch", "printf", "rm", "timeout", "tr", "tail", "sort", "readlink" })
            if (File.Exists("/usr/bin/" + t)) File.CreateSymbolicLink(Path.Combine(tools, t), "/usr/bin/" + t);
        Assert.Equal("BoosterAI: claude update skipped: flock or timeout is missing\nstarted\n",
            f.Run("bs_update claude && echo started", f.Bin + ":" + tools).Output);
        // A state folder that cannot be created.
        File.WriteAllText(Path.Combine(f.Home, ".local"), "a file, not a folder");
        var (output, code) = f.Run("bs_update claude && echo started");
        Assert.Equal(0, code);
        Assert.StartsWith("BoosterAI: claude update skipped: cannot write ", output);
        Assert.EndsWith("started\n", output);
        Assert.Empty(f.ReadCalls());
    }

    [Fact]
    public void MissingCliOrFunctionIsLeftToTheStart()
    {
        if (!CanRunBash) return;
        using var f = new Fakes();
        File.Delete(Path.Combine(f.Bin, "agy"));
        Assert.Equal("started\n", f.Run("agy() { :; } && bs_update agy && echo started").Output);
        Assert.Equal("started\n", f.Run("bs_update skal && echo started").Output);
        Assert.False(Directory.Exists(f.StateDir));
    }

    [Fact]
    public void ConcurrentStartsInstallOnce()
    {
        if (!CanRunBash) return;
        using var f = new Fakes();
        f.Set("codex-latest", "0.166.0");
        var runs = Enumerable.Range(0, 3)
            .Select(_ => Task.Run(() => f.Run("FAKE_INSTALL_SECONDS=2 && export FAKE_INSTALL_SECONDS && bs_update codex && codex --version")))
            .ToArray();
        Task.WaitAll(runs);
        Assert.Single(f.ReadCalls(), c => c.StartsWith("npm install"));
        // The tabs that waited start the updated CLI, not one being replaced.
        Assert.All(runs, r => Assert.EndsWith("codex-cli 0.166.0\n", r.Result.Output));
        Assert.Equal(2, runs.Count(r => r.Result.Output.Contains("waiting for another tab's codex update")));
    }
}
