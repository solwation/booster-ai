using BoosterAI;
using Xunit;

/// <summary>Codex's config.toml follows the YOLO alias (#182): only the two top-level keys change, and Standard restores them.</summary>
public class CodexConfigTests
{
    private const string CodexHelp = "--approve-for-me --ask-for-approval --sandbox --dangerously-bypass-approvals-and-sandbox";
    private static CliStatus Codex => new("codex", "file", "/sample/codex") { Help = CodexHelp, Version = "0.170.0" };
    private static AgentModeChoice Choice(AgentMode mode) => AgentModes.Choices(Codex).Single(c => c.Mode == mode);
    private static CliStatus Managed(AgentMode mode) => Codex with
    { Kind = "alias", AliasDefinition = AgentModes.AliasLine("codex", Choice(mode)), ManagedAlias = AgentModes.AliasLine("codex", Choice(mode)) };

    // A realistic file: Codex's own Auto settings, a model, trusted projects and a key with a matching suffix.
    private const string Original = "# my Codex settings\n"
        + "model = \"gpt-5-codex\"\n"
        + "sandbox_mode = \"workspace-write\" # set by Codex\n"
        + "approval_policy = \"on-request\"\n"
        + "approvals_reviewer = \"auto_review\"\n"
        + "my_sandbox_mode = \"keep\"\n"
        + "notes = \"\"\"\n"
        + "sandbox_mode = \"inside a string\"\n"
        + "\"\"\"\n"
        + "\n"
        + "[projects.\"/home/me/webshop\"]\n"
        + "trust_level = \"trusted\"\n"
        + "sandbox_mode = \"read-only\"\n"
        + "\n"
        + "[sandbox_workspace_write]\n"
        + "network_access = true\n";

    [Fact]
    public void YoloSetsOnlyTheTopLevelKeysInPlace()
    {
        Assert.False(CodexConfig.IsYolo(Original));
        var yolo = CodexConfig.ApplyYolo(Original);
        Assert.True(CodexConfig.IsYolo(yolo));
        Assert.Equal(Original
            .Replace("sandbox_mode = \"workspace-write\" # set by Codex", "sandbox_mode = \"danger-full-access\" # set by Codex")
            .Replace("approval_policy = \"on-request\"", "approval_policy = \"never\""), yolo);
        // Tables, the multi-line string and the suffix key are untouched.
        Assert.Contains("trust_level = \"trusted\"\nsandbox_mode = \"read-only\"\n", yolo);
        Assert.Contains("sandbox_mode = \"inside a string\"", yolo);
        Assert.Contains("my_sandbox_mode = \"keep\"", yolo);
        Assert.Equal("gpt-5-codex", CodexConfig.Get(yolo, "model"));
        Assert.Equal(yolo, CodexConfig.ApplyYolo(yolo));
    }

    [Fact]
    public void YoloInsertsMissingKeysBeforeTheFirstTableAndItsComments()
    {
        const string text = "model = \"o3\"\n\n# Trusted folders\n[projects.\"/home/me/x\"]\ntrust_level = \"trusted\"\n";
        var yolo = CodexConfig.ApplyYolo(text);
        Assert.Equal("model = \"o3\"\n\nsandbox_mode = \"danger-full-access\"\napproval_policy = \"never\"\n# Trusted folders\n[projects.\"/home/me/x\"]\ntrust_level = \"trusted\"\n", yolo);
        Assert.Equal(text, CodexConfig.Restore(yolo, text));
        // Without a table the keys go at the end, after a missing final newline is added.
        Assert.Equal("model = \"o3\"\nsandbox_mode = \"danger-full-access\"\napproval_policy = \"never\"\n", CodexConfig.ApplyYolo("model = \"o3\""));
    }

    [Fact]
    public void MissingOrEmptyFileGetsBothKeysAndRestoresToNothing()
    {
        var yolo = CodexConfig.ApplyYolo(null);
        Assert.Equal("sandbox_mode = \"danger-full-access\"\napproval_policy = \"never\"\n", yolo);
        Assert.Equal(yolo, CodexConfig.ApplyYolo(""));
        Assert.Equal("", CodexConfig.Restore(yolo, ""));
        Assert.Equal(new CodexConfigState(null, null, false), CodexConfig.State(null, false));
    }

    [Fact]
    public void CrLfFilesKeepTheirLineEndings()
    {
        var text = Original.Replace("\n", "\r\n");
        var yolo = CodexConfig.ApplyYolo(text);
        Assert.True(CodexConfig.IsYolo(yolo));
        Assert.DoesNotContain("\n", yolo.Replace("\r\n", ""));
        Assert.Equal(text, CodexConfig.Restore(yolo, text));
        var inserted = CodexConfig.ApplyYolo("model = \"o3\"\r\n[tui]\r\n");
        Assert.Equal("model = \"o3\"\r\nsandbox_mode = \"danger-full-access\"\r\napproval_policy = \"never\"\r\n[tui]\r\n", inserted);
    }

    [Theory]
    [InlineData("sandbox_mode = 'danger-full-access'\napproval_policy = 'never'\n", true)]
    [InlineData("\"sandbox_mode\" = \"danger-full-access\"\n  approval_policy=\"never\"  # ok\n", true)]
    [InlineData("sandbox_mode = \"danger-full-access\"\n", false)]
    [InlineData("[tui]\nsandbox_mode = \"danger-full-access\"\napproval_policy = \"never\"\n", false)]
    [InlineData("# sandbox_mode = \"danger-full-access\"\n# approval_policy = \"never\"\n", false)]
    [InlineData("sandbox_mode = \"workspace-write\"\napproval_policy = \"never\"\n", false)]
    public void YoloNeedsBothTopLevelValuesInEitherQuoteStyle(string text, bool expected) =>
        Assert.Equal(expected, CodexConfig.IsYolo(text));

    [Fact]
    public void RestoreTakesTheBackupsLinesAndRemovesKeysItDidNotHave()
    {
        var yolo = CodexConfig.ApplyYolo(Original);
        // The user changed another key while YOLO was active: that change survives the restore.
        var edited = yolo.Replace("model = \"gpt-5-codex\"", "model = \"o4\"");
        Assert.Equal(Original.Replace("model = \"gpt-5-codex\"", "model = \"o4\""), CodexConfig.Restore(edited, Original));
        // A backup without the keys removes them from the top level only.
        const string backup = "model = \"o3\"\n[tui]\nsandbox_mode = \"x\"\n";
        Assert.Equal(backup, CodexConfig.Restore(CodexConfig.ApplyYolo(backup), backup));
        // Restoring into a file that lost the keys puts the backup's lines back.
        Assert.Equal("model = \"o3\"\nsandbox_mode = \"read-only\"\napproval_policy = \"untrusted\"\n",
            CodexConfig.Restore("model = \"o3\"\n", "sandbox_mode = \"read-only\"\napproval_policy = \"untrusted\"\n"));
    }

    [Fact]
    public void ApplyingYoloThenStandardRoundTripsConfigAndBackup()
    {
        using var home = new TempHome();
        File.WriteAllText(home.Config, Original);
        var vm = home.ViewModel();
        Assert.True(vm.ApplyAgentMode(new AgentInfo(Codex) { SelectedMode = Choice(AgentMode.Yolo) }, yoloConfirmed: true));
        Assert.Contains("--dangerously-bypass-approvals-and-sandbox", File.ReadAllText(home.Bashrc));
        var yolo = File.ReadAllText(home.Config);
        // What `bash -c 'codex exec …'` (no alias) reads: no sandbox, no approval prompts.
        Assert.Equal("danger-full-access", CodexConfig.Get(yolo, CodexConfig.SandboxKey));
        Assert.Equal("never", CodexConfig.Get(yolo, CodexConfig.ApprovalKey));
        Assert.Equal(Original, File.ReadAllText(home.Backup));
        Assert.Equal(new CodexConfigState("danger-full-access", "never", true), vm.ReadCodexConfig());

        // Applying YOLO again (the config repair) never replaces the original backup.
        var yoloAgent = new AgentInfo(Managed(AgentMode.Yolo));
        yoloAgent.SetCodexConfig(new CodexConfigState("workspace-write", "on-request", true));
        Assert.True(yoloAgent.CanApply);
        File.WriteAllText(home.Config, Original.Replace("on-request", "untrusted"));
        Assert.True(vm.ApplyAgentMode(yoloAgent, yoloConfirmed: true));
        Assert.Equal(Original, File.ReadAllText(home.Backup));
        Assert.True(CodexConfig.IsYolo(File.ReadAllText(home.Config)));

        var standard = new AgentInfo(Managed(AgentMode.Yolo)) { SelectedMode = Choice(AgentMode.Standard) };
        Assert.True(vm.ApplyAgentMode(standard, yoloConfirmed: false));
        Assert.Equal(Original, File.ReadAllText(home.Config));
        Assert.False(File.Exists(home.Backup));
        Assert.DoesNotContain("alias codex", File.ReadAllText(home.Bashrc));
        Assert.Equal(new CodexConfigState("workspace-write", "on-request", false), vm.ReadCodexConfig());
    }

    [Fact]
    public void YoloWithoutConfigCreatesItAndAutoRemovesItAgain()
    {
        using var home = new TempHome();
        var vm = home.ViewModel();
        Assert.True(vm.ApplyAgentMode(new AgentInfo(Codex) { SelectedMode = Choice(AgentMode.Yolo) }, yoloConfirmed: true));
        Assert.True(CodexConfig.IsYolo(File.ReadAllText(home.Config)));
        Assert.Equal("", File.ReadAllText(home.Backup));
        var auto = new AgentInfo(Managed(AgentMode.Yolo)) { SelectedMode = Choice(AgentMode.AutoApprove) };
        Assert.True(vm.ApplyAgentMode(auto, yoloConfirmed: false));
        Assert.False(File.Exists(home.Config));
        Assert.False(File.Exists(home.Backup));
    }

    [Fact]
    public void StandardWithoutBackupLeavesTheUsersConfigAlone()
    {
        using var home = new TempHome();
        const string own = "sandbox_mode = \"danger-full-access\"\napproval_policy = \"never\"\n";
        File.WriteAllText(home.Config, own);
        var auto = new AgentInfo(Codex) { SelectedMode = Choice(AgentMode.AutoApprove) };
        Assert.True(home.ViewModel().ApplyAgentMode(auto, yoloConfirmed: false));
        Assert.Equal(own, File.ReadAllText(home.Config));
        // Other agents never touch Codex's file.
        var claude = new CliStatus("claude", "file", "/sample/claude") { Help = "--permission-mode acceptEdits --dangerously-skip-permissions" };
        var claudeYolo = new AgentInfo(claude) { SelectedMode = AgentModes.Choices(claude).Single(c => c.Mode == AgentMode.Yolo) };
        File.Delete(home.Config);
        Assert.True(home.ViewModel().ApplyAgentMode(claudeYolo, yoloConfirmed: true));
        Assert.False(Directory.Exists(home.CodexHome) && Directory.EnumerateFiles(home.CodexHome).Any());
    }

    [Fact]
    public void ConfigWriteFailureIsReportedAsAnError()
    {
        using var home = new TempHome();
        // A directory where config.toml should be: reading it as a file fails.
        Directory.CreateDirectory(home.Config);
        var vm = home.ViewModel();
        Assert.False(vm.ApplyAgentMode(new AgentInfo(Codex) { SelectedMode = Choice(AgentMode.Yolo) }, yoloConfirmed: true));
        Assert.True(vm.Error.IsVisible);
        Assert.Contains("config.toml", vm.Error.Text);
    }

    [Fact]
    public void WarningShowsWhenAliasAndConfigDisagree()
    {
        var yolo = new AgentInfo(Managed(AgentMode.Yolo));
        Assert.False(yolo.HasConfigWarning);
        Assert.False(yolo.CanApply);
        yolo.SetCodexConfig(new CodexConfigState("workspace-write", "on-request", false));
        Assert.True(yolo.ConfigNeedsYolo);
        Assert.True(yolo.HasConfigWarning);
        Assert.Contains("sandbox_mode = \"workspace-write\"", yolo.ConfigWarningText);
        // Re-applying the current YOLO mode is allowed only to repair the file.
        Assert.True(yolo.CanApply);
        yolo.SetCodexConfig(new CodexConfigState(null, "never", false));
        Assert.Contains(Loc.Format("agents.codexConfig.unset", "sandbox_mode"), yolo.ConfigWarningText);
        yolo.SetCodexConfig(new CodexConfigState("danger-full-access", "never", true));
        Assert.False(yolo.HasConfigWarning);
        Assert.False(yolo.CanApply);

        var standard = new AgentInfo(Codex);
        standard.SetCodexConfig(new CodexConfigState("danger-full-access", "never", false));
        Assert.True(standard.ConfigUnexpectedYolo);
        Assert.True(standard.HasConfigWarning);
        // Without BoosterAI's backup there is nothing to restore, so Apply stays disabled.
        Assert.False(standard.CanApply);
        standard.SetCodexConfig(new CodexConfigState("danger-full-access", "never", true));
        Assert.True(standard.CanApply);

        // Other agents and unread files never warn.
        var claude = new AgentInfo(new CliStatus("claude", "file", "/x"));
        claude.SetCodexConfig(new CodexConfigState("danger-full-access", "never", false));
        Assert.False(claude.HasConfigWarning);
        var unknown = new AgentInfo(Managed(AgentMode.Yolo));
        unknown.SetCodexConfig(null);
        Assert.False(unknown.HasConfigWarning);
    }

    [Fact]
    public void SetupCheckCarriesTheConfigStateToTheCodexPage()
    {
        var vm = new MainViewModel(new AppConfig());
        vm.ShowSampleAgents(codexConfigMismatch: true);
        var codex = vm.Setup.Agents.Single(a => a.Cli == "codex");
        Assert.Equal(AgentMode.Yolo, codex.CurrentMode);
        Assert.True(codex.HasConfigWarning);
        Assert.False(vm.Setup.Agents.Single(a => a.Cli == "claude").HasConfigWarning);
        vm.ShowSampleAgents();
        Assert.False(vm.Setup.Agents.Single(a => a.Cli == "codex").HasConfigWarning);
    }

    private sealed class TempHome : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("booster-codex-config-").FullName;
        public string Bashrc => Path.Combine(Root, ".bashrc");
        public string CodexHome => Path.Combine(Root, ".codex");
        public string Config => Path.Combine(CodexHome, CodexConfig.FileName);
        public string Backup => Path.Combine(CodexHome, CodexConfig.BackupName);
        public TempHome() => Directory.CreateDirectory(CodexHome);
        public MainViewModel ViewModel() => new(new AppConfig()) { PermissionConfigPath = Bashrc, CodexHomePath = CodexHome };
        public void Dispose() => Directory.Delete(Root, true);
    }
}
