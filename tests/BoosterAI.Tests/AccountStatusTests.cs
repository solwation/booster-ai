using BoosterAI;
using Xunit;

/// <summary>Login status per account and the default account's own name (#192).</summary>
[Collection(ConfigFileCollection.Name)]
public sealed class AccountStatusTests
{
    private const string ClaudeIn = """{"loggedIn": true, "authMethod": "claude.ai", "email": "namn@example.com", "orgName": "Example AB", "subscriptionType": "max", "loginExpiresAt": null}""";

    [Fact]
    public void ClaudeLoggedInShowsEmailAndOrganisation()
    {
        var s = AccountStatusProbe.Parse("claude", true, 0, ClaudeIn);
        Assert.Equal(LoginState.LoggedIn, s.State);
        Assert.Equal("namn@example.com · Example AB", s.Who);
        Assert.Equal("namn@example.com · Example AB", s.Identity);
    }

    [Fact]
    public void ClaudeJsonAfterShellNoiseAndWithoutEmailStillParses()
    {
        Assert.Equal("namn@example.com", AccountStatusProbe.Parse("claude", true, 0, "welcome\n" + ClaudeIn.Replace(", \"orgName\": \"Example AB\"", "")).Who);
        var apiKey = AccountStatusProbe.Parse("claude", true, 0, """{"loggedIn": true, "authMethod": "api_key"}""");
        Assert.Equal(LoginState.LoggedIn, apiKey.State);
        Assert.Null(apiKey.Who);
        Assert.Equal("api_key", apiKey.Identity);
    }

    [Theory]
    [InlineData(1, """{"loggedIn": false, "authMethod": "none"}""", LoginState.LoggedOut)]
    [InlineData(0, "not json at all", LoginState.Unknown)]
    [InlineData(0, "{ broken", LoginState.Unknown)]
    [InlineData(0, "", LoginState.Unknown)]
    [InlineData(0, """{"other": 1}""", LoginState.Unknown)]
    [InlineData(124, "", LoginState.Unknown)]
    [InlineData(127, "bash: claude: command not found", LoginState.Unknown)]
    [InlineData(3, "", LoginState.LoggedOut)]
    public void ClaudeOtherOutputs(int exit, string output, LoginState expected) =>
        Assert.Equal(expected, AccountStatusProbe.Parse("claude", true, exit, output).State);

    [Fact]
    public void CodexLoggedInShowsTheLoginMethod()
    {
        var s = AccountStatusProbe.Parse("codex", true, 0, "WARNING: proceeding\nLogged in using ChatGPT\n");
        Assert.Equal(LoginState.LoggedIn, s.State);
        Assert.Equal("ChatGPT", s.Method);
        Assert.Equal("ChatGPT", s.Identity);
    }

    [Theory]
    [InlineData(1, "Not logged in", LoginState.LoggedOut)]
    [InlineData(1, "Error loading configuration: CODEX_HOME points to x", LoginState.Unknown)]
    [InlineData(2, "", LoginState.Unknown)]
    [InlineData(124, "", LoginState.Unknown)]
    [InlineData(1, "Logged in using ChatGPT", LoginState.Unknown)]
    [InlineData(3, "", LoginState.LoggedOut)]
    public void CodexOtherOutputs(int exit, string output, LoginState expected) =>
        Assert.Equal(expected, AccountStatusProbe.Parse("codex", true, exit, output).State);

    [Fact]
    public void ACheckThatDidNotStartOrTimedOutIsUnknown()
    {
        Assert.Equal(LoginState.Unknown, AccountStatusProbe.Parse("claude", false, -1, null).State);
        Assert.Equal(LoginState.Unknown, AccountStatusProbe.Parse("codex", true, -1, null).State);
        Assert.Equal(LoginState.Unknown, AccountStatusProbe.Parse("claude", true, -1, null).State);
    }

    [Fact]
    public void TheCommandSetsTheVariableOnlyForANonDefaultAccount()
    {
        var work = AccountStatusProbe.Script("claude", "~/.claude-work");
        Assert.StartsWith("export CLAUDE_CONFIG_DIR=~/'.claude-work'; ", work);
        Assert.Contains("claude auth status --json", work);
        var codex = AccountStatusProbe.Script("codex", "/srv/it's");
        Assert.StartsWith(@"export CODEX_HOME='/srv/it'\''s'; ", codex);
        Assert.Contains("codex login status", codex);

        foreach (var llm in new[] { "claude", "codex" })
        {
            var def = AccountStatusProbe.Script(llm, null);
            Assert.DoesNotContain("export", def);
            Assert.DoesNotContain("CLAUDE_CONFIG_DIR", def);
            Assert.DoesNotContain("CODEX_HOME", def);
            Assert.StartsWith("timeout ", def);
        }
        Assert.Throws<ArgumentException>(() => AccountStatusProbe.Script("agy", null));
        var psi = AccountStatusProbe.Command("Ubuntu", "claude", "~/.claude-work");
        Assert.Contains("-lic", psi.ArgumentList);
        Assert.Equal(OperatingSystem.IsWindows() ? "wsl.exe" : "bash", psi.FileName);
    }

    [Fact]
    public void StatusTextsAndButtonFollowTheState()
    {
        var previous = Loc.Language;
        Loc.Use("sv");
        try
        {
            Assert.Equal("Inloggad som namn@example.com · Example AB", AccountStatusProbe.Parse("claude", true, 0, ClaudeIn).Text);
            Assert.Equal("Inloggad med ChatGPT", new AccountLoginStatus(LoginState.LoggedIn, null, "ChatGPT").Text);
            Assert.Equal("Inte inloggad", AccountLoginStatus.LoggedOut.Text);
            Assert.Equal("Okänd status", AccountLoginStatus.Unknown.Text);

            var vm = new MainViewModel(new AppConfig { Accounts = [new() { Id = "claude-work", Agent = "claude", Name = "Work", Directory = "~/.claude-work" }] });
            var accounts = vm.AccountsEditor("claude")!;
            var work = accounts.Rows[1];
            Assert.Equal("Logga in", work.LoginText);
            accounts.ShowSampleStatus("claude");
            Assert.Equal("Logga in igen", accounts.Rows[0].LoginText);
            Assert.Equal("Logga in", work.LoginText);
            Assert.Equal("namn@example.com · Example AB", accounts.Rows[0].Identity);
            Assert.False(work.HasIdentity);
        }
        finally { Loc.Use(previous); }
    }

    [Fact]
    public async Task StatusIsCheckedPerAccountWithoutDoubleChecksAndNeverUnlessEnabled()
    {
        var vm = new MainViewModel(new AppConfig { Accounts = [new() { Id = "claude-work", Agent = "claude", Name = "Work", Directory = "~/.claude-work" }] });
        var calls = new List<string?>();
        var gate = new TaskCompletionSource();
        vm.CheckAccountLogin = async (_, llm, dir) =>
        {
            lock (calls) calls.Add(dir);
            await gate.Task;
            return dir is null ? AccountStatusProbe.Parse(llm, true, 0, ClaudeIn) : AccountLoginStatus.LoggedOut;
        };
        var accounts = vm.AccountsEditor("claude")!;

        await accounts.RefreshStatusAsync();
        Assert.Empty(calls); // screenshots and tests never run the real commands

        vm.AccountStatusEnabled = true;
        var first = accounts.RefreshStatusAsync();
        var second = accounts.RefreshStatusAsync(); // while the first is running: nothing new starts
        Assert.Equal(2, calls.Count);
        Assert.Equal([null, "~/.claude-work"], calls);
        gate.SetResult();
        await Task.WhenAll(first, second);
        Assert.Equal(LoginState.LoggedIn, accounts.Rows[0].Status.State);
        Assert.Equal(LoginState.LoggedOut, accounts.Rows[1].Status.State);

        await accounts.RefreshStatusAsync(); // a later trigger checks again
        Assert.Equal(4, calls.Count);
    }

    [Fact]
    public async Task AFailingCheckGivesUnknownAndAnInvalidDirectoryIsNotRun()
    {
        var vm = new MainViewModel(new AppConfig { Accounts = [new() { Id = "claude-bad", Agent = "claude", Name = "Bad", Directory = "relative; rm" }] })
        {
            AccountStatusEnabled = true,
        };
        var calls = new List<string?>();
        vm.CheckAccountLogin = (_, _, dir) => { calls.Add(dir); throw new InvalidOperationException("boom"); };
        var accounts = vm.AccountsEditor("claude")!;
        await accounts.RefreshStatusAsync();
        Assert.Equal([(string?)null], calls);
        Assert.All(accounts.Rows, r => Assert.Equal(LoginState.Unknown, r.Status.State));
    }

    [Fact]
    public void TheDefaultAccountNameRoundTripsAndMerges()
    {
        var cfg = new AppConfig { DefaultAccountNames = new() { ["claude"] = "Privat" } };
        var back = ConfigStore.Parse(System.Text.Json.JsonSerializer.Serialize(cfg, ConfigJsonContext.Default.AppConfig))!;
        Assert.Equal("Privat", back.DefaultAccountNames["claude"]);
        Assert.Empty(ConfigStore.Parse("{}")!.DefaultAccountNames); // an older config has none

        var mine = new AppConfig();
        Assert.True(ConfigMerge.TakeOthers(mine, new AppConfig(), cfg));
        Assert.Equal("Privat", mine.DefaultAccountNames["claude"]);
    }

    [Fact]
    public void ARenamedDefaultReplacesStandardEverywhereAndAnUnnamedOneStaysStandard()
    {
        var previous = Loc.Language;
        Loc.Use("sv");
        try
        {
            var cfg = new AppConfig { Tabs = [new() { Name = "Atlas", Llm = "claude" }] };
            var vm = new MainViewModel(cfg);
            var accounts = vm.AccountsEditor("claude")!;
            var atlas = vm.Tabs.Single();
            Assert.Equal("Standard", accounts.Rows[0].Name);
            // Only the default account: the Agent box shows no account label (#194).
            Assert.False(atlas.AgentChoices[0].ShowAccount);
            Assert.Equal("", atlas.AccountLabel);

            accounts.Rows[0].Name = "Privat";
            Assert.Equal("Privat", cfg.DefaultAccountNames["claude"]);
            Assert.False(atlas.AgentChoices[0].ShowAccount);
            Assert.Equal("Privat", atlas.AccountLabel);
            Assert.Contains("(Privat)", atlas.PeekHeader);
            Assert.Equal("Privat", Accounts.DisplayName(cfg, "claude", ""));
            Assert.Equal("Standard", Accounts.DefaultName(cfg, "codex"));
            // Still the default account: no directory, no variable, not removable.
            Assert.Null(accounts.Rows[0].Account);
            Assert.False(accounts.Rows[0].CanEdit);
            Assert.Null(Accounts.DirectoryFor(cfg, atlas.ToConfig(true)));

            accounts.Rows[0].Name = "Standard"; // naming it Standard gives the name back
            Assert.False(cfg.DefaultAccountNames.ContainsKey("claude"));
            Assert.Equal("", atlas.AccountLabel);
        }
        finally { Loc.Use(previous); }
    }
}
