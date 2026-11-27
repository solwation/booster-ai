using System.Diagnostics;
using BoosterAI;
using Xunit;

/// <summary>Claude's away summaries ("recaps") are off in every claude tab, whatever the model (#241).</summary>
public sealed class RecapTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("bs-recap-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    public static TheoryData<string> ClaudeModels() => [.. Catalog.ModelsFor("claude"), "my-custom-model"];

    [Theory]
    [MemberData(nameof(ClaudeModels))]
    public void OptOutCoversStartContinueRestartOneShotAndSaveForEveryModel(string model)
    {
        var tab = new TabConfig { Llm = "claude", Model = model, Orkester = true };
        var track = new TabTrack("/s/lib.sh", "/s", "tab1");
        var commands = new List<string> { Launcher.TabScript(tab), Launcher.TabScript(tab, track: track),
            Launcher.TabScript(tab, track: track, resume: true), Launcher.RestartScript(tab, track, resume: false),
            Launcher.RestartScript(tab, track, resume: true), Launcher.SaveAndCloseScript(tab, "token") };
        tab.NonInteractive = true;
        commands.Add(Launcher.TabScript(tab, track: track));
        Assert.Contains("bs_stream claude", commands[^1]);
        tab.NonInteractive = false;
        tab.CompactTokens = 200_000;
        commands.Add(Launcher.TabScript(tab, track: track));
        foreach (var command in commands)
        {
            Assert.Contains(Launcher.RecapOptOut + " ", command);
            Assert.DoesNotContain(';', command);
        }
    }

    [Fact]
    public void CodexGetsItsOwnOptOutOnStartContinueRestartAndRunOnce()
    {
        var tab = new TabConfig { Llm = "codex", Model = "gpt-6-sol", Orkester = true, CompactTokens = 200_000 };
        var track = new TabTrack("/s/lib.sh", "/s", "tab1");
        var commands = new List<string> { Launcher.TabScript(tab), Launcher.TabScript(tab, track: track),
            Launcher.TabScript(tab, track: track, resume: true), Launcher.RestartScript(tab, track, resume: true) };
        tab.NonInteractive = true;
        commands.Add(Launcher.TabScript(tab, track: track));
        Assert.Contains("codex exec ", commands[^1]);
        Assert.Contains("codex resume --last ", commands[2]);
        foreach (var command in commands)
        {
            Assert.Contains(" " + Launcher.CodexRecapOptOut + " ", command);
            Assert.DoesNotContain("CLAUDE_CODE_ENABLE_AWAY_SUMMARY", command);
        }
    }

    [Theory]
    [InlineData("agy")]
    [InlineData("aider")]
    [InlineData("openhands")]
    [InlineData("interpreter")]
    public void OtherAgentsGetNoRecapOptOut(string llm)
    {
        var script = Launcher.TabScript(new TabConfig { Llm = llm });
        Assert.DoesNotContain("CLAUDE_CODE_ENABLE_AWAY_SUMMARY", script);
        Assert.DoesNotContain("auto_recap", script);
    }

    [Fact]
    public async Task OptOutReachesTheCliThroughAShellAliasAndDoesNotLeakToTheShell()
    {
        if (OperatingSystem.IsWindows()) return; // The generated bash command runs in Linux/WSL.
        var psi = new ProcessStartInfo("bash")
        {
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, WorkingDirectory = _root,
        };
        TestShell.Configure(psi, _root);
        psi.Environment.Remove("CLAUDE_CODE_ENABLE_AWAY_SUMMARY");
        var command = Launcher.TabScript(new TabConfig { Directory = _root, Llm = "claude", Model = "claude-opus-5-5" });
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("shopt -s expand_aliases\nfake_claude() { printf 'cli=%s\\n' \"${CLAUDE_CODE_ENABLE_AWAY_SUMMARY-unset}\"; }\nalias claude=fake_claude\n(\n"
            + command + "\n)\nprintf 'shell=%s\\n' \"${CLAUDE_CODE_ENABLE_AWAY_SUMMARY-unset}\"\n");
        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.StandardInput.Close();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            Assert.True(process.ExitCode == 0, await stderr);
            Assert.Equal("cli=0\nshell=unset\n", await stdout);
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }
    }
}
