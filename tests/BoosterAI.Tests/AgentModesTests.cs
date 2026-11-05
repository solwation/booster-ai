using System.Diagnostics;
using System.Text;
using BoosterAI;
using Xunit;

public class AgentModesTests
{
    private const string CodexHelp = "--approve-for-me --ask-for-approval --sandbox --dangerously-bypass-approvals-and-sandbox";
    private static CliStatus Codex => new("codex", "file", "/sample/codex") { Help = CodexHelp, Version = "0.170.0" };
    private static AgentModeChoice Choice(CliStatus cli, AgentMode mode) => AgentModes.Choices(cli).Single(c => c.Mode == mode);
    private static CliStatus Managed(CliStatus cli, AgentModeChoice mode) => cli with
    { Kind = "alias", AliasDefinition = AgentModes.AliasLine(cli.Cli, mode), ManagedAlias = AgentModes.AliasLine(cli.Cli, mode) };

    [Theory]
    [InlineData("codex-cli 0.170.0", "0.170.0")]
    [InlineData("2.1.0 (Claude Code)", "2.1.0")]
    [InlineData("aider 0.86.1\n", "0.86.1")]
    [InlineData("Open Interpreter 0.4.2 The Beginning", "0.4.2")]
    [InlineData("\u001b[32mv1.4.2-beta.1\u001b[0m", "1.4.2-beta.1")]
    [InlineData("permission denied", null)]
    [InlineData("gpt-4.1", null)]
    [InlineData("model-4.1.2", null)]
    [InlineData("Error: requires 1.2.3", null)]
    [InlineData("", null)]
    public void VersionsAreProgramTokensNotModelNames(string output, string? expected) =>
        Assert.Equal(expected, Prerequisites.ParseVersion(output));

    [Fact]
    public void DetectionMetadataDistinguishesManagedAndCustomAliases()
    {
        static string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s));
        var alias = "alias codex='codex --approve-for-me'";
        var output = "bsdetect codex alias /sample/bin/codex\n"
            + "bsdetect-version codex " + B64("codex-cli 0.170.0") + "\n"
            + "bsdetect-help codex " + B64(CodexHelp) + "\n"
            + "bsdetect-alias codex " + B64(alias) + "\n"
            + "bsdetect-managed " + B64(Prerequisites.BlockStart + "\n" + alias + "\n" + Prerequisites.BlockEnd) + "\n";
        var cli = Assert.Single(Prerequisites.ParseDetect(output).Clis);
        Assert.Equal("0.170.0", cli.Version);
        Assert.Equal(AgentMode.AutoApprove, AgentModes.Current(cli));
        Assert.Equal(AgentMode.Custom, AgentModes.Current(cli with { ManagedAlias = null }));
        Assert.Equal(AgentMode.Custom, AgentModes.Current(cli with { Kind = "function" }));
        Assert.Null(Assert.Single(Prerequisites.ParseDetect("bsdetect codex file /sample/codex\nbsdetect-version codex invalid!\n").Clis).Version);
    }

    [Fact]
    public void ModesFollowDetectedCapabilitiesAndKeepApprovalSeparateFromSandbox()
    {
        Assert.Equal([AgentMode.Standard], AgentModes.Choices(new("codex", "file", "/x")).Select(c => c.Mode));
        Assert.Equal("--approve-for-me", Choice(Codex, AgentMode.AutoApprove).Arguments);
        Assert.Equal("--dangerously-bypass-approvals-and-sandbox", Choice(Codex, AgentMode.Yolo).Arguments);
        var modern = new CliStatus("interpreter", "file", "/x") { Help = "--oss " + CodexHelp };
        Assert.Equal("--ask-for-approval never --sandbox workspace-write", Choice(modern, AgentMode.AutoApprove).Arguments);
        var legacy = modern with { Help = "--local --auto_run" };
        Assert.Equal([AgentMode.Standard, AgentMode.Yolo], AgentModes.Choices(legacy).Select(c => c.Mode));
        Assert.Equal("--auto_run", Choice(legacy, AgentMode.Yolo).Arguments);
        var aider = new CliStatus("aider", "file", "/x") { Help = "--yes-always" };
        Assert.DoesNotContain(AgentModes.Choices(aider), c => c.Mode == AgentMode.AutoApprove);
        var openhands = new CliStatus("openhands", "file", "/x") { Help = "--llm-approve --always-approve" };
        Assert.Equal("--llm-approve", Choice(openhands, AgentMode.AutoApprove).Arguments);
        Assert.Equal("--always-approve", Choice(openhands, AgentMode.Yolo).Arguments);
    }

    [Fact]
    public void OwnedModesRoundTripAndRepeatWithoutTouchingUnrelatedText()
    {
        const string original = "# personal settings\nexport EDITOR=nano\n";
        var auto = Choice(Codex, AgentMode.AutoApprove);
        var yolo = Choice(Codex, AgentMode.Yolo);
        var text = AgentModes.Change(original, Codex, auto);
        Assert.StartsWith(original, text);
        Assert.Equal(text, AgentModes.Change(text, Managed(Codex, auto), auto));
        text = AgentModes.Change(text, Managed(Codex, auto), yolo);
        Assert.DoesNotContain("--approve-for-me", text);
        Assert.Contains("--dangerously-bypass-approvals-and-sandbox", text);
        text = AgentModes.Change(text, Managed(Codex, yolo), Choice(Codex, AgentMode.Standard));
        Assert.StartsWith(original, text);
        Assert.DoesNotContain("alias codex", text);
        Assert.Equal(text, AgentModes.Change(text, Codex, Choice(Codex, AgentMode.Standard)));
    }

    [Fact]
    public void ExistingBlockKeepsCrLfAndOtherAliasesWhenRestoringStandard()
    {
        var auto = Choice(Codex, AgentMode.AutoApprove);
        var preserved = "alias helper='printf hello'\r\n";
        var text = "# before\r\n" + Prerequisites.BlockStart + "\r\n" + preserved
            + AgentModes.AliasLine("codex", auto) + "\r\n" + Prerequisites.BlockEnd + "\r\n# after\r\n";
        var result = AgentModes.Change(text, Managed(Codex, auto), Choice(Codex, AgentMode.Standard));
        Assert.Equal(text.Replace(AgentModes.AliasLine("codex", auto) + "\r\n", ""), result);
    }

    [Theory]
    [InlineData("alias codex='codex --my-options'\n")]
    [InlineData("alias helper='echo hi' codex='codex --my-options'\n")]
    [InlineData("codex() { command codex --my-options \"$@\"; }\n")]
    [InlineData("function codex { command codex; }\n")]
    public void ExternalDefinitionsCannotBeOverwritten(string own)
    {
        Assert.Throws<InvalidOperationException>(() => AgentModes.Change(own, Codex, Choice(Codex, AgentMode.Yolo)));
        var custom = Codex with { Kind = "alias", AliasDefinition = "alias codex='codex --mine'" };
        Assert.Throws<InvalidOperationException>(() => AgentModes.Change("", custom, Choice(Codex, AgentMode.Standard)));
    }

    [Fact]
    public void MalformedAndModifiedBlocksArePreserved()
    {
        foreach (var text in new[]
        {
            Prerequisites.BlockStart + "\n",
            Prerequisites.BlockEnd + "\n",
            Prerequisites.BlockStart + "\nalias codex='codex --mine'\n" + Prerequisites.BlockEnd,
            Prerequisites.BlockStart + "\n" + Prerequisites.BlockEnd + "\n" + Prerequisites.BlockStart + "\n" + Prerequisites.BlockEnd,
            "echo '" + Prerequisites.BlockStart + "'\nalias codex='codex --approve-for-me'\necho '" + Prerequisites.BlockEnd + "'\n",
        })
            Assert.Throws<InvalidOperationException>(() => AgentModes.Change(text, Codex, Choice(Codex, AgentMode.AutoApprove)));
    }

    [Fact]
    public void UnknownVersionAndCustomModeRemainVisibleAndYoloRequiresConfirmation()
    {
        var unknown = new AgentInfo(new CliStatus("aider", "file", "/x"));
        Assert.Equal(Loc.Get("agents.version.unknown"), unknown.VersionText);
        var custom = new AgentInfo(Codex with { Kind = "function" });
        Assert.Equal(AgentMode.Custom, custom.CurrentMode);
        Assert.True(custom.CanSelectMode);
        Assert.False(custom.CanApply);
        var agent = new AgentInfo(Codex) { SelectedMode = Choice(Codex, AgentMode.Yolo) };
        Assert.True(agent.CanApply);
        var vm = new MainViewModel(new AppConfig());
        // The rejection happens before any user's file path is resolved or written.
        Assert.False(vm.ApplyAgentMode(agent, yoloConfirmed: false));
    }

    [Fact]
    public void PermissionPreviewNeverChangesTheDetectedAlias()
    {
        var status = Managed(Codex, Choice(Codex, AgentMode.AutoApprove));
        var agent = new AgentInfo(status);
        agent.SelectedMode = agent.ModeChoices.Single(c => c.Mode == AgentMode.Yolo);
        Assert.True(agent.CanApply);
        Assert.True(agent.IsYolo);
        Assert.Equal(status.AliasDefinition, agent.CurrentAliasCommand);
        Assert.Equal(AgentMode.AutoApprove, agent.CurrentMode);
        Assert.Contains("--dangerously-bypass", agent.ModePreview);
        agent.SelectedMode = agent.ModeChoices.Single(c => c.Mode == AgentMode.Standard);
        Assert.Equal("codex", agent.ModePreview);
        Assert.Equal(status.AliasDefinition, agent.CurrentAliasCommand);
    }

    [Theory]
    [InlineData("alias", "alias codex='codex --my-options'")]
    [InlineData("function", "codex () { command codex --my-options \"$@\"; }")]
    public void CustomDefinitionsCanPreviewPresetsButNeverWriteThem(string kind, string definition)
    {
        var agent = new AgentInfo(Codex with { Kind = kind,
            AliasDefinition = kind == "alias" ? definition : null,
            FunctionDefinition = kind == "function" ? definition : null });
        Assert.True(agent.HasExternalDefinition);
        Assert.Equal(definition, agent.CurrentAliasCommand);
        foreach (var choice in agent.ModeChoices)
        {
            agent.SelectedMode = choice;
            Assert.False(agent.CanApply);
            Assert.Equal(definition, agent.CurrentAliasCommand);
            Assert.Equal(choice.Explanation, agent.ModeExplanation);
            Assert.False(new MainViewModel(new AppConfig()).ApplyAgentMode(agent, yoloConfirmed: true));
        }
        var missing = new AgentInfo(Codex with { Kind = "function" });
        Assert.True(missing.MissingDefinition);
        Assert.Contains("declare -f codex", missing.AliasInspectCommand);
    }

    [Fact]
    public void DetectionReadsFunctionDefinitionWithoutExecutingIt()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var f = new Shell();
        f.Write("codex", "if [ \"$1\" = --version ]; then echo 'codex-cli 0.170.0'; else echo --approve-for-me; fi");
        var result = f.Run("codex() { touch function-must-not-run; command codex --mine \"$@\"; }\n" + Prerequisites.DetectScript);
        Assert.Equal(0, result.Code);
        var cli = Prerequisites.ParseDetect(result.Output).Clis.Single(c => c.Cli == "codex");
        Assert.Equal("function", cli.Kind);
        Assert.Contains("command codex --mine", cli.FunctionDefinition);
        Assert.Contains("function-must-not-run", new AgentInfo(cli).CurrentAliasCommand);
        Assert.False(File.Exists(Path.Combine(f.Root, "function-must-not-run")));
    }

    [Fact]
    public void RestartRefreshesOnlyOwnedAliasAndStandardRemovesIt()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var f = new Shell();
        var auto = Choice(Codex, AgentMode.AutoApprove);
        var yolo = Choice(Codex, AgentMode.Yolo);
        var initial = AgentModes.Change("", Codex, auto);
        var changed = AgentModes.Change(initial, Managed(Codex, auto), yolo);
        var standard = AgentModes.Change(changed, Managed(Codex, yolo), Choice(Codex, AgentMode.Standard));
        File.WriteAllText(Path.Combine(f.Root, ".bashrc"), initial);
        var result = f.Run("shopt -s expand_aliases\n. ~/.bashrc\n. ./lib.sh . tab1\n"
            + "printf '%s' " + Launcher.Quote(changed) + " > ~/.bashrc\nbs_run 'codex'\n"
            + "printf '%s' " + Launcher.Quote(standard) + " > ~/.bashrc\nbs_run 'codex'\n");
        Assert.Equal(0, result.Code);
        Assert.Equal(["--dangerously-bypass-approvals-and-sandbox", ""], File.ReadAllLines(Path.Combine(f.Root, "args")));
    }

    [Fact]
    public void RestartPreservesCustomAliasAddedInTheRunningShellOrOutsideBlock()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var f = new Shell();
        var initial = AgentModes.Change("", Codex, Choice(Codex, AgentMode.AutoApprove));
        File.WriteAllText(Path.Combine(f.Root, ".bashrc"), initial);
        var result = f.Run("shopt -s expand_aliases\n. ~/.bashrc\n. ./lib.sh . tab1\n"
            + "alias codex='codex --custom'\nbs_run 'codex'\n");
        Assert.Equal(0, result.Code);
        Assert.Equal(["--custom"], File.ReadAllLines(Path.Combine(f.Root, "args")));
        Assert.Equal(initial, File.ReadAllText(Path.Combine(f.Root, ".bashrc")));
    }

    [Fact]
    public void RefreshNeverExecutesBashrcContentsAndPreservesOutsideOverrides()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var f = new Shell();
        var auto = Choice(Codex, AgentMode.AutoApprove);
        var owned = AgentModes.Change("", Codex, auto);
        var outside = owned + "\nalias codex='codex --outside'\ntouch should-not-exist\n";
        File.WriteAllText(Path.Combine(f.Root, ".bashrc"), owned);
        var result = f.Run("shopt -s expand_aliases\n. ~/.bashrc\n. ./lib.sh . tab1\n"
            + "printf '%s' " + Launcher.Quote(outside) + " > ~/.bashrc\nbs_run 'codex'\n");
        Assert.Equal(0, result.Code);
        Assert.Equal(["--approve-for-me"], File.ReadAllLines(Path.Combine(f.Root, "args")));
        Assert.False(File.Exists(Path.Combine(f.Root, "should-not-exist")));
    }

    [Fact]
    public void InterpreterCompoundCommandKeepsThePermissionAliasInItsNestedShell()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var f = new Shell();
        f.Write("interpreter", "if [ \"$1\" = --help ]; then echo --oss; else printf '%s\\n' \"$*\" >> " + Launcher.Quote(Path.Combine(f.Root, "args")) + "; fi");
        var cli = new CliStatus("interpreter", "file", "/sample/interpreter") { Help = "--oss " + CodexHelp };
        var owned = AgentModes.Change("", cli, Choice(cli, AgentMode.Yolo));
        File.WriteAllText(Path.Combine(f.Root, ".bashrc"), owned);
        var command = InterpreterCli.Command(new TabConfig { Llm = "interpreter", Model = "local" }, null);
        var result = f.Run("shopt -s expand_aliases\n. ~/.bashrc\n. ./lib.sh . tab1\nbs_run " + Launcher.Quote(command));
        Assert.Equal(0, result.Code);
        Assert.Equal(["--dangerously-bypass-approvals-and-sandbox --oss"], File.ReadAllLines(Path.Combine(f.Root, "args")));
    }

    [Fact]
    public void OpenHandsEnvironmentModelKeepsThePermissionAlias()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var f = new Shell();
        f.Write("openhands", "printf '%s\\n' \"$LLM_MODEL $*\" >> " + Launcher.Quote(Path.Combine(f.Root, "args")));
        var cli = new CliStatus("openhands", "file", "/sample/openhands") { Help = "--always-approve" };
        File.WriteAllText(Path.Combine(f.Root, ".bashrc"), AgentModes.Change("", cli, Choice(cli, AgentMode.Yolo)));
        var result = f.Run("shopt -s expand_aliases\n. ~/.bashrc\n. ./lib.sh . tab1\nbs_run "
            + Launcher.Quote("LLM_MODEL='local/model' openhands --override-with-envs"));
        Assert.Equal(0, result.Code);
        Assert.Equal(["local/model --always-approve --override-with-envs"], File.ReadAllLines(Path.Combine(f.Root, "args")));
    }

    [Fact]
    public void DetectionUsesExecutableVersionAndTimesOutBadHelpWithoutLosingInstalledStatus()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var f = new Shell();
        f.Write("codex", "if [ \"$1\" = --version ]; then echo 'codex-cli 0.170.0'; else sleep 30; fi");
        var result = f.Run("shopt -s expand_aliases\nalias codex='codex --mine'\n" + Prerequisites.DetectScript);
        Assert.Equal(0, result.Code);
        var cli = Prerequisites.ParseDetect(result.Output).Clis.Single(c => c.Cli == "codex");
        Assert.True(cli.Installed);
        Assert.Equal("0.170.0", cli.Version);
        Assert.Empty(cli.Help);
        Assert.Equal(AgentMode.Custom, AgentModes.Current(cli));
    }

    [Fact]
    public void FailedVersionDoesNotTurnInstalledIntoMissingOrInventAVersion()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var f = new Shell();
        f.Write("codex", "if [ \"$1\" = --version ]; then echo 'Error: requires 1.2.3'; exit 1; else echo --approve-for-me; fi");
        var result = f.Run(Prerequisites.DetectScript);
        var cli = Prerequisites.ParseDetect(result.Output).Clis.Single(c => c.Cli == "codex");
        Assert.True(cli.Installed);
        Assert.Null(cli.Version);
        Assert.Contains(AgentModes.Choices(cli), c => c.Mode == AgentMode.AutoApprove);
    }

    private sealed class Shell : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("booster-agent-mode-").FullName;
        private string Bin => Path.Combine(Root, "bin");
        public Shell()
        {
            Directory.CreateDirectory(Bin);
            File.WriteAllText(Path.Combine(Root, "lib.sh"), TabSession.Lib);
            Write("codex", "printf '%s\\n' \"$*\" >> " + Launcher.Quote(Path.Combine(Root, "args")));
        }
        public void Write(string name, string body)
        {
            var path = Path.Combine(Bin, name);
            File.WriteAllText(path, "#!/bin/bash\n" + body + "\n");
            if (OperatingSystem.IsLinux()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        public (int Code, string Output, string Error) Run(string command)
        {
            var psi = new ProcessStartInfo("/bin/bash")
            {
                ArgumentList = { "--noprofile", "--norc", "-c", command }, WorkingDirectory = Root,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            };
            psi.Environment.Clear();
            psi.Environment["HOME"] = Root;
            psi.Environment["PATH"] = Bin + ":/usr/bin:/bin";
            psi.Environment["SHELL"] = "/bin/bash";
            using var process = Process.Start(psi)!;
            try
            {
                process.StandardInput.Close();
                var output = process.StandardOutput.ReadToEndAsync();
                var error = process.StandardError.ReadToEndAsync();
                Assert.True(process.WaitForExit(20000));
                return (process.ExitCode, output.GetAwaiter().GetResult(), error.GetAwaiter().GetResult());
            }
            finally
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                process.WaitForExit();
            }
        }
        public void Dispose() => Directory.Delete(Root, true);
    }
}
