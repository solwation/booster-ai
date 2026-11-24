using System.Diagnostics;
using BoosterAI;
using Xunit;

/// <summary>Several accounts per agent and the account choice per tab (#186).</summary>
[Collection(ConfigFileCollection.Name)]
public sealed class AccountTests : IDisposable
{
    private static readonly TabTrack Track = new("/s/tab-lib.sh", "/s", "tab1");
    private static readonly AgentAccount Work = new() { Id = "claude-work", Agent = "claude", Name = "Work", Directory = "~/.claude-work" };
    private static readonly AgentAccount CodexWork = new() { Id = "codex-work", Agent = "codex", Name = "Work", Directory = "~/.codex-work" };
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bs-accounts-" + Guid.NewGuid().ToString("N"));

    public AccountTests()
    {
        Directory.CreateDirectory(_root);
        ConfigStore.UseFile(Path.Combine(_root, "config.json"));
    }

    public void Dispose()
    {
        TestShell.StopLeftovers(_root);
        Directory.Delete(_root, recursive: true);
    }

    private static TabConfig Tab(string llm, string account, string? dir, string directory = "~/proj") =>
        new() { Name = "Atlas", Llm = llm, Directory = directory, Account = account, AccountDirectory = dir };

    [Theory]
    [InlineData("claude", "CLAUDE_CONFIG_DIR", "~/.claude-work", "~/'.claude-work'")]
    [InlineData("codex", "CODEX_HOME", "~/.codex-work", "~/'.codex-work'")]
    [InlineData("claude", "CLAUDE_CONFIG_DIR", "/srv/it's here", @"'/srv/it'\''s here'")]
    [InlineData("codex", "CODEX_HOME", "~/my \"codex\" home", @"~/'my '$'\x22''codex'$'\x22'' home'")]
    public void LinuxAndWslTabsExportTheChosenAccountBeforeTheCli(string llm, string variable, string dir, string quoted)
    {
        var tab = Tab(llm, llm + "-work", dir);
        var export = $"export {variable}={quoted} && bs_acct={variable} && mkdir -p {quoted} && cd ~/'proj' && ";
        foreach (var track in new TabTrack?[] { null, Track })
        {
            // Linux: the script the terminal runs with bash -lic.
            var linux = Launcher.TabScript(tab, null, track);
            Assert.Contains(export, linux);
            Assert.DoesNotContain(';', linux);
            Assert.DoesNotContain('"', linux);
            // Windows: the same through wsl.exe, after the directory is mapped (here it already is a Linux path).
            var wsl = Launcher.WindowsTerminalArgs([tab], "Ubuntu", tracks: [track])[^5];
            Assert.Contains(export, wsl);
            Assert.DoesNotContain('"', wsl);
        }
        // Both branches of a tracked start (with and without tab-lib.sh) export it.
        var tracked = Launcher.TabScript(tab, null, Track);
        Assert.Equal(2, tracked.Split(export).Length - 1);
        foreach (var call in Launcher.LinuxCommands([tab], "gnome-terminal", null, [Track], 0))
            Assert.Contains(Launcher.Quote(tracked), string.Join(' ', call.Args));
    }

    [Fact]
    public void WindowsDirectoriesAreMappedAndTheAccountIsKept()
    {
        var tab = Tab("claude", "claude-work", "~/.claude-work", @"C:\Work\atlas");
        var wsl = Launcher.WindowsTerminalArgs([tab], "Ubuntu", tracks: [Track])[^5];
        Assert.Contains("export CLAUDE_CONFIG_DIR=~/'.claude-work' && bs_acct=CLAUDE_CONFIG_DIR && mkdir -p ~/'.claude-work' && cd '/mnt/c/Work/atlas' && ", wsl);
    }

    [Fact]
    public void TheDefaultAccountStartsExactlyAsBefore()
    {
        foreach (var llm in Catalog.Llms)
        {
            var before = new TabConfig { Name = "Atlas", Llm = llm, Directory = "~/proj", Orkester = true };
            var chosen = Tab(llm, "", null);
            chosen.Orkester = true;
            foreach (var track in new TabTrack?[] { null, Track })
            {
                Assert.Equal(Launcher.TabScript(before, null, track), Launcher.TabScript(chosen, null, track));
                Assert.DoesNotContain("bs_acct", Launcher.TabScript(chosen, null, track));
                Assert.DoesNotContain("CLAUDE_CONFIG_DIR", Launcher.TabScript(chosen, null, track));
                Assert.DoesNotContain("CODEX_HOME=", Launcher.TabScript(chosen, null, track));
            }
            Assert.Equal(Launcher.WindowsTerminalArgs([before], "Ubuntu", tracks: [Track]), Launcher.WindowsTerminalArgs([chosen], "Ubuntu", tracks: [Track]));
        }
    }

    [Fact]
    public void AgentsWithoutAConfigDirectoryVariableOnlyHaveTheDefaultAccount()
    {
        foreach (var llm in Catalog.Llms)
            Assert.Equal(llm is "claude" or "codex", Accounts.Supports(llm));
        // A stray id on such an agent never exports anything and never blocks the launch.
        var agy = Tab("agy", "claude-work", null);
        Assert.False(Accounts.IsMissing(agy));
        Assert.Equal("", Launcher.Validate([agy]));
        Assert.DoesNotContain("export", Launcher.TabScript(agy, null, Track));
        var cfg = new AppConfig { Accounts = [Work] };
        Assert.Empty(Accounts.For(cfg, "agy"));
        Assert.Null(Accounts.DirectoryFor(cfg, agy));
    }

    [Fact]
    public void AMissingOrForeignAccountIsRefusedInsteadOfUsingTheDefaultLogin()
    {
        var cfg = new AppConfig { Accounts = [Work, CodexWork] };
        var gone = new TabConfig { Name = "Atlas", Llm = "claude", Account = "claude-gone" };
        var foreign = new TabConfig { Name = "Atlas", Llm = "claude", Account = "codex-work" };
        var fine = new TabConfig { Name = "Atlas", Llm = "claude", Account = "claude-work" };
        Accounts.Resolve(cfg, [gone, foreign, fine]);
        Assert.Contains("Atlas", Launcher.Validate([gone]));
        Assert.NotEqual("", Launcher.Validate([foreign]));
        Assert.Equal("", Launcher.Validate([fine]));
        Assert.Equal("~/.claude-work", fine.AccountDirectory);
        Assert.NotEqual("", Launcher.Validate([Tab("claude", "x", "relative/dir")]));
        Assert.NotEqual("", Launcher.Validate([Tab("claude", "x", "~/a;b")]));
    }

    [Fact]
    public void AnInPlaceRestartSwitchesAccountsInTheTabsShell()
    {
        var work = Launcher.RestartScript(Tab("claude", "claude-work", "~/.claude-work"), Track, resume: false);
        Assert.Contains(Accounts.ResetCommand + " && export CLAUDE_CONFIG_DIR=~/'.claude-work' && bs_acct=CLAUDE_CONFIG_DIR && mkdir -p ~/'.claude-work' && (bs_cli", work);
        // Back to the default account: only the variable BoosterAI exported is taken back.
        var standard = Launcher.RestartScript(Tab("claude", "", null), Track, resume: false);
        Assert.Contains(Accounts.ResetCommand + " && (bs_cli", standard);
        Assert.DoesNotContain("export", standard);
        Assert.EndsWith(Accounts.ResetCommand, Launcher.RestartScript(Tab(Catalog.ShellOnly, "", null), Track, resume: false));
        // Conversations live in the account's directory: another account never resumes.
        Assert.False(Launcher.CanResume(Tab("claude", "", null), Tab("claude", "claude-work", "~/.claude-work")));
        Assert.True(Launcher.CanResume(Tab("claude", "claude-work", "~/.claude-work"), Tab("claude", "claude-work", "~/.claude-work")));
    }

    [Fact]
    public void TheExportedVariableReachesTheAgentAndARestartTakesItBack()
    {
        if (!OperatingSystem.IsLinux()) return;
        var tab = Tab("codex", "codex-work", "~/.codex-work");
        var script = Accounts.ExportCommand(tab) + "printenv CODEX_HOME && test -d ~/.codex-work && "
            + Accounts.ResetCommand + " && { printenv CODEX_HOME || echo unset\n}";
        var start = new ProcessStartInfo("bash") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add(script);
        var home = TestShell.Configure(start, _root);
        using var p = Process.Start(start)!;
        try
        {
            var output = p.StandardOutput.ReadToEnd();
            Assert.True(p.WaitForExit(10000));
            Assert.Equal($"{home}/.codex-work\nunset\n", output);
        }
        finally
        {
            if (!p.HasExited) p.Kill(entireProcessTree: true);
            p.WaitForExit();
        }
    }

    [Fact]
    public void DefaultAccountLoginUsesTheAgentsOwnDirectory()
    {
        Assert.Equal("unset CLAUDE_CONFIG_DIR && cd ~ && printf '%s\\n' 'BoosterAI: Privat (~/.claude)' && claude auth login || true && exec bash",
            Accounts.DefaultLoginScript("claude", "Privat"));
        Assert.StartsWith("unset CODEX_HOME && ", Accounts.DefaultLoginScript("codex", "Standard"));
        Assert.Contains(" && codex login || true && exec bash", Accounts.DefaultLoginScript("codex", "Standard"));
        Assert.DoesNotContain(';', Accounts.DefaultLoginScript("claude", "a;b \"c\""));
        Assert.DoesNotContain('"', Accounts.DefaultLoginScript("claude", "a;b \"c\""));
        Assert.Throws<ArgumentException>(() => Accounts.DefaultLoginScript("aider", "x"));
    }

    [Fact]
    public void LoginRunsTheAgentsLoginWithTheAccountsVariable()
    {
        Assert.Equal("export CLAUDE_CONFIG_DIR=~/'.claude-work' && mkdir -p ~/'.claude-work' && cd ~ && printf '%s\\n' 'BoosterAI: Work (CLAUDE_CONFIG_DIR=~/.claude-work)' && claude auth login || true && exec bash",
            Accounts.LoginScript(Work));
        Assert.Contains("export CODEX_HOME=~/'.codex-work' && ", Accounts.LoginScript(CodexWork));
        Assert.Contains(" && codex login || true && exec bash", Accounts.LoginScript(CodexWork));
        Assert.DoesNotContain(';', Accounts.LoginScript(new AgentAccount { Agent = "claude", Name = "a;b \"c\"", Directory = "~/.x" }));
        Assert.DoesNotContain('"', Accounts.LoginScript(new AgentAccount { Agent = "claude", Name = "a;b \"c\"", Directory = "~/.x" }));
    }

    [Fact]
    public void NewAccountsGetTheirOwnStableDirectory()
    {
        var first = Accounts.Create([], "claude", "  Jobb Åsa ");
        Assert.Equal(new AgentAccount { Id = "claude-jobb-asa", Agent = "claude", Name = "Jobb Åsa", Directory = "~/.claude-jobb-asa" }, first);
        var second = Accounts.Create([first], "claude", "jobb åsa");
        Assert.Equal("~/.claude-jobb-asa-2", second.Directory);
        Assert.Equal("~/.codex-account", Accounts.Create([], "codex", "!!!").Directory);
        Assert.Throws<ArgumentException>(() => Accounts.Create([], "agy", "Work"));
        Assert.True(Accounts.IsValidDirectory(first.Directory));
    }

    [Fact]
    public void OldConfigsLoadWithTheDefaultAccountAndLaunchDirectoriesAreNotSaved()
    {
        var old = ConfigStore.Parse("""{ "Tabs": [ { "Name": "Atlas", "Llm": "claude" } ] }""")!;
        Assert.Empty(old.Accounts);
        Assert.Equal("", old.Tabs[0].Account);

        var vm = new MainViewModel(new AppConfig { Accounts = [Work], Tabs = [new() { Name = "Atlas", Llm = "claude", Account = "claude-work" }] });
        Assert.Equal("~/.claude-work", vm.Tabs[0].ToConfig(forLaunch: true).AccountDirectory);
        vm.Save();
        var saved = File.ReadAllText(ConfigStore.PathOnDisk);
        Assert.DoesNotContain("AccountDirectory", saved);
        var loaded = ConfigStore.Load();
        Assert.Equal([Work], loaded.Accounts);
        Assert.Equal("claude-work", loaded.Tabs[0].Account);
    }

    [Fact]
    public void TheRowChoosesAndShowsItsAccount()
    {
        var vm = new MainViewModel(new AppConfig
        {
            Accounts = [Work],
            Tabs = [new() { Name = "Atlas", Llm = "claude" }, new() { Name = "Borg", Llm = "agy" }, new() { Name = "Ceres", Llm = "codex" }],
        });
        var (atlas, borg, ceres) = (vm.Tabs[0], vm.Tabs[1], vm.Tabs[2]);
        Assert.True(atlas.AccountVisible);
        Assert.False(borg.AccountVisible);
        Assert.False(ceres.AccountVisible);
        Assert.Equal(["", "claude-work"], atlas.AccountChoices.Select(c => c.Id));
        Assert.False(atlas.HasAccount);

        atlas.SelectedAccount = atlas.AccountChoices[1];
        Assert.Equal("claude-work", atlas.Account);
        Assert.Equal("Work", atlas.AccountLabel);
        Assert.Contains("(Work)", atlas.PeekHeader);
        // A subtab starts with its project's account and keeps its own choice.
        var child = vm.AddSubTab(atlas);
        Assert.Equal("claude-work", child.Account);
        child.Account = "";
        Assert.Equal("claude-work", atlas.Account);

        // A running tab whose account changed shows it and restarts fresh.
        atlas.LaunchedAs = atlas.ToConfig(forLaunch: true);
        atlas.SetState(TabState.Running, null, activity: BoosterAI.Activity.Idle);
        Assert.False(atlas.IsChanged);
        atlas.Account = "";
        Assert.True(atlas.IsChanged);
        Assert.False(atlas.RestartResumes);

        // Another agent cannot keep a claude account.
        atlas.Account = "claude-work";
        atlas.Llm = "codex";
        Assert.Equal("", atlas.Account);
    }

    [Fact]
    public void SettingsAddRenameRemoveAndLogIn()
    {
        var vm = new MainViewModel(new AppConfig { Tabs = [new() { Name = "Atlas", Llm = "claude" }] });
        var claude = vm.Setup.Agents.Single(a => a.Cli == "claude");
        Assert.True(claude.SupportsAccounts);
        Assert.False(vm.Setup.Agents.Single(a => a.Cli == "agy").SupportsAccounts);
        var accounts = claude.Accounts!;
        var standard = Assert.Single(accounts.Rows);
        Assert.True(standard.IsDefault);
        Assert.False(standard.CanEdit);
        Assert.Equal("~/.claude", standard.Directory);

        Assert.False(accounts.Add());
        accounts.NewName = "Work";
        Assert.True(accounts.Add());
        var work = accounts.Rows[1];
        Assert.Equal("~/.claude-work", work.Directory);
        Assert.True(vm.Tabs[0].AccountVisible);
        vm.Tabs[0].Account = "claude-work";
        Assert.Equal(Loc.FormatCount("account.usage", 1), work.UsageText);

        work.Name = "Job";
        Assert.Equal("Job", ConfigStore.Load().Accounts.Single().Name);
        Assert.Equal("claude-work", ConfigStore.Load().Accounts.Single().Id);
        Assert.Equal("Job", vm.Tabs[0].AccountLabel);

        var name = Loc.Format("account.login.tab", "Job");
        work.Login();
        var login = Assert.Single(TestTerminal.For(name)).Tabs.Single();
        Assert.Contains("export CLAUDE_CONFIG_DIR=~/'.claude-work'", login.Script);

        work.Remove();
        Assert.Single(accounts.Rows);
        Assert.Empty(ConfigStore.Load().Accounts);
        Assert.Equal("", vm.Tabs[0].Account);
        Assert.False(vm.Tabs[0].AccountVisible);
    }

    [Fact]
    public void CodexMetadataComesFromTheTabsAccount()
    {
        Assert.Contains("python3 - ~/'.codex-work' <<'PY'", AutoCompactModels.ProbeFor("~/.codex-work"));
        Assert.Equal(AutoCompactModels.Probe, AutoCompactModels.ProbeFor(null));
        var vm = new MainViewModel(new AppConfig { AutoCompactMode = AutoCompactPolicy.Auto, Accounts = [CodexWork] });
        vm.ApplyCompactMetadata(new Dictionary<string, int> { ["gpt-x"] = 100_000 },
            new Dictionary<string, IReadOnlyDictionary<string, int>> { ["~/.codex-work"] = new Dictionary<string, int> { ["gpt-x"] = 400_000 } });
        var standard = vm.CompactTokens("codex", "gpt-x");
        var work = vm.CompactTokens("codex", "gpt-x", accountDirectory: "~/.codex-work");
        Assert.NotNull(standard);
        Assert.NotNull(work);
        Assert.True(work > standard);
    }

    [Fact]
    public async Task CodexMetadataIsReadForEveryCodexAccount()
    {
        var vm = new MainViewModel(new AppConfig { Accounts = [CodexWork, Work] });
        var read = new List<string>();
        await vm.LoadCompactMetadataAsync(new AppConfig().WslDistro, _ => Task.FromResult<IReadOnlyDictionary<string, int>>(new Dictionary<string, int>()),
            (_, home) => { read.Add(home); return Task.FromResult<IReadOnlyDictionary<string, int>>(new Dictionary<string, int>()); });
        Assert.Equal(["~/.codex-work"], read);
    }

    [Fact]
    public void CodexYoloFollowsEveryAccountsConfig()
    {
        var home = Path.Combine(_root, "home");
        var bashrc = Path.Combine(home, ".bashrc");
        Directory.CreateDirectory(Path.Combine(home, ".codex"));
        var vm = new MainViewModel(new AppConfig { Accounts = [CodexWork] })
        {
            PermissionConfigPath = bashrc,
            CodexHomePath = Path.Combine(home, ".codex"),
            AccountHomePath = d => Path.Combine(home, d[2..]),
        };
        var codex = new CliStatus("codex", "file", "/sample/bin/codex") { Version = "0.170.0", Help = "--dangerously-bypass-approvals-and-sandbox --ask-for-approval --sandbox" };
        var agent = new AgentInfo(codex);
        agent.SelectedMode = agent.ModeChoices.Single(c => c.Mode == AgentMode.Yolo);
        Assert.True(vm.ApplyAgentMode(agent, yoloConfirmed: true));
        var work = Path.Combine(home, ".codex-work", CodexConfig.FileName);
        Assert.True(CodexConfig.IsYolo(File.ReadAllText(work)));
        Assert.True(CodexConfig.IsYolo(File.ReadAllText(Path.Combine(home, ".codex", CodexConfig.FileName))));
        Assert.Equal(2, vm.ReadCodexConfigs().Count);

        // One account drifted back to the sandbox: the warning shows it although the default account is fine.
        File.WriteAllText(work, "sandbox_mode = \"workspace-write\"\n");
        var states = vm.ReadCodexConfigs();
        Assert.False(CodexConfig.Pick(states, aliasYolo: true)!.IsYolo);
        Assert.True(CodexConfig.Pick(states, aliasYolo: false)!.IsYolo);
    }
}
