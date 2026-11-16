using System.Diagnostics;
using BoosterAI;
using Xunit;

[Collection(ConfigFileCollection.Name)]
public sealed class AdvisorTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("bs-advisor-").FullName;

    public AdvisorTests() => ConfigStore.UseFile(Path.Combine(_root, "config.json"));
    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("standard")]
    [InlineData("off")]
    public void DefaultAndLegacyValuesStayOffThroughSaveAndSubtabInheritance(string? saved)
    {
        var tab = new TabConfig { Llm = "claude", Model = "sonnet" };
        Assert.Equal(AdvisorPolicy.Disabled, tab.Advisor);
        if (saved is not null) tab.Advisor = saved;
        var vm = new MainViewModel(new AppConfig { Tabs = [tab] });
        var project = vm.Tabs[0];
        var child = vm.AddSubTab(project);
        Assert.Equal(AdvisorPolicy.Disabled, project.Advisor);
        Assert.Equal(project.Advisor, child.Advisor);
        Assert.False(project.HasAdvisorWarning);
        Assert.Equal(AdvisorPolicy.Disabled, project.ToConfig(forLaunch: false).Advisor);
        Assert.Contains("CLAUDE_CODE_DISABLE_ADVISOR_TOOL=1", Launcher.TabScript(child.ToConfig(forLaunch: true)));
    }

    [Theory]
    [InlineData("sv", "Av")]
    [InlineData("en", "Off")]
    public void LocalizedOffMapsToAStableSavedIdAndExplicitModelsArePreserved(string language, string off)
    {
        var previous = Loc.Language;
        Loc.Use(language);
        try
        {
            var vm = new MainViewModel(new AppConfig { Tabs = [new() { Llm = "claude", Model = "sonnet", Advisor = "opus" }] });
            var project = vm.Tabs[0];
            var child = vm.AddSubTab(project);
            Assert.Equal("opus", project.AdvisorChoice);
            Assert.Equal(off, project.Advisors[0]);
            Assert.DoesNotContain(Catalog.StandardModel, project.Advisors);
            project.AdvisorChoice = off;
            Assert.Equal(AdvisorPolicy.Disabled, project.Advisor);
            Assert.Equal(off, child.AdvisorChoice);
            project.AdvisorChoice = "claude-fable-5-2";
            Assert.Equal("claude-fable-5-2", child.Advisor);
            var reloaded = new MainViewModel(new AppConfig { Tabs = [project.ToConfig(forLaunch: false)] });
            Assert.Equal("claude-fable-5-2", reloaded.Tabs[0].AdvisorChoice);
        }
        finally { Loc.Use(previous); }
    }

    [Fact]
    public void ARunningLegacyStandardTabNeedsARestartToBecomeExplicitlyOff()
    {
        var vm = new MainViewModel(new AppConfig { Tabs = [new() { Llm = "claude", Model = "sonnet", Advisor = "standard" }] });
        var tab = vm.Tabs[0];
        tab.LaunchedAs = new() { Llm = "claude", Model = "sonnet", Advisor = "standard" };
        tab.SetState(TabState.Running, null);
        Assert.Equal(AdvisorPolicy.Disabled, tab.Advisor);
        Assert.True(tab.IsChanged);
        tab.LaunchedAs = tab.ToConfig(forLaunch: true);
        Assert.False(tab.IsChanged);
    }

    [Theory]
    [InlineData("off", "sonnet", true)]
    [InlineData("standard", "sonnet", true)]
    [InlineData("opus", "opus", true)]
    [InlineData("opus", "standard", true)]
    [InlineData("haiku", "sonnet", true)]
    [InlineData("opus", "sonnet", false)]
    public void OptOutCoversTrackedStartContinueOneShotAndSaveWithoutChangingHooks(string advisor, string model, bool disabled)
    {
        var tab = new TabConfig { Llm = "claude", Advisor = advisor, Model = model, Orkester = true };
        var track = new TabTrack("/s/lib.sh", "/s", "tab1");
        var commands = new List<string> { Launcher.TabScript(tab), Launcher.TabScript(tab, track: track),
            Launcher.RestartScript(tab, track, resume: true), Launcher.SaveAndCloseScript(tab, "token") };
        Assert.Contains("--settings", commands[1]);
        Assert.Contains("--continue", commands[2]);
        tab.NonInteractive = true;
        commands.Add(Launcher.TabScript(tab, track: track));
        Assert.Contains("bs_stream claude", commands[^1]);
        foreach (var command in commands)
        {
            Assert.Equal(disabled, command.Contains("CLAUDE_CODE_DISABLE_ADVISOR_TOOL=1"));
            Assert.DoesNotContain(';', command);
            Assert.DoesNotContain('"', command);
        }
        Assert.DoesNotContain("CLAUDE_CODE_DISABLE_ADVISOR_TOOL", Launcher.TabScript(new TabConfig { Llm = "codex" }));
    }

    [Theory]
    [InlineData("off", "sonnet", "1")]
    [InlineData("standard", "sonnet", "1")]
    [InlineData("opus", "opus", "1")]
    [InlineData("opus", "sonnet", "unset")]
    public async Task OptOutReachesTheCliThroughAShellAliasAndDoesNotLeakToTheShell(string advisor, string model, string expected)
    {
        if (OperatingSystem.IsWindows()) return; // The generated bash command runs in Linux/WSL.
        var psi = new ProcessStartInfo("bash")
        {
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, WorkingDirectory = _root,
        };
        TestShell.Configure(psi, _root);
        psi.Environment.Remove("CLAUDE_CODE_DISABLE_ADVISOR_TOOL");
        var command = Launcher.TabScript(new TabConfig { Directory = _root, Llm = "claude", Model = model, Advisor = advisor });
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("shopt -s expand_aliases\nfake_claude() { printf 'cli=%s\\n' \"${CLAUDE_CODE_DISABLE_ADVISOR_TOOL-unset}\"; }\nalias claude=fake_claude\n(\n"
            + command + "\n)\nprintf 'shell=%s\\n' \"${CLAUDE_CODE_DISABLE_ADVISOR_TOOL-unset}\"\n");
        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.StandardInput.Close();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            Assert.True(process.ExitCode == 0, await stderr);
            Assert.Equal($"cli={expected}\nshell=unset\n", await stdout);
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }
    }
}
