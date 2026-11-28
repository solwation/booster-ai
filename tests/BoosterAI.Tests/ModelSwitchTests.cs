using Avalonia.Threading;
using BoosterAI;
using Xunit;

/// <summary>#279: switching a tab to an equivalent model with quota left when its account's quota is used up, only while it is idle.</summary>
[Collection(ConfigFileCollection.Name)]
public sealed class ModelSwitchTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("bs-model-switch").FullName;
    private readonly string _language = Loc.Language;
    private readonly List<(string Title, string Body)> _notified = [];
    private readonly List<int> _stopped = [];
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset WeekReset = Now.AddDays(2);

    public ModelSwitchTests()
    {
        ConfigStore.UseFile(Path.Combine(_root, "config.json"));
        Loc.Use("en");
    }

    public void Dispose()
    {
        Loc.Use(_language);
        Directory.Delete(_root, recursive: true);
    }

    private static AccountLimits Used(double session, double week, DateTimeOffset? weekReset = null) =>
        new(new LimitWindow(session, Now.AddHours(3), 300), new LimitWindow(week, weekReset ?? WeekReset, 10080), null, Now.AddMinutes(-2));

    /// <summary>claude default (Privat), claude Work, codex default, agy (not installed) and their latest readings.</summary>
    private sealed class Candidates
    {
        public AccountLimits ClaudeDefault = Used(30, 100);
        public AccountLimits ClaudeWork = Used(50, 50);
        public AccountLimits Codex = Used(20, 10);
        public bool CodexLoggedIn = true;

        public IReadOnlyList<SwitchAccount> List() =>
        [
            new("claude", "", "", "Privat", true, true, ClaudeDefault),
            new("claude", "claude-work", "~/.claude-work", "Work", true, true, ClaudeWork),
            new("codex", "", "", null, true, CodexLoggedIn, Codex),
            new("agy", "", "", null, false, true, Used(0, 0)),
        ];
    }

    /// <summary>Atlas (claude Opus), Beta (codex), Once (claude, Run once) and Fable (claude Fable), all running on the default accounts.</summary>
    private (MainViewModel Vm, TabSession Session, Candidates Candidates) Setup(Activity activity = Activity.Idle, bool auto = false)
    {
        var cfg = new AppConfig
        {
            QuotaWarn = false, QuotaSaveNotice = false, AutoModelSwitch = auto,
            Accounts = [new() { Id = "claude-work", Agent = "claude", Name = "Work", Directory = "~/.claude-work" }],
            DefaultAccountNames = new() { ["claude"] = "Privat" },
            Tabs =
            [
                new() { Name = "Atlas", Llm = "claude", Model = "claude-opus-5-5", Directory = "~/atlas" },
                new() { Name = "Beta", Llm = "codex", Model = "gpt-6.1-terra", Directory = "~/beta" },
                new() { Name = "Once", Llm = "claude", Model = "claude-opus-5-5", Command = "/report", NonInteractive = true, Directory = "~/once" },
                new() { Name = "Fable", Llm = "claude", Model = "claude-fable-5-2", Directory = "~/fable" },
            ],
        };
        var candidates = new Candidates();
        var vm = new MainViewModel(cfg)
        {
            LimitsClock = () => Now,
            QuotaNotify = (_, _) => { },
            SwitchAccounts = candidates.List,
            StopTabForeground = (pid, _) => { lock (_stopped) _stopped.Add(pid); },
        };
        vm.ShowNotification = (title, body, _) => _notified.Add((title, body));
        var session = new TabSession(Path.Combine(_root, "sessions-" + Guid.NewGuid().ToString("N")), DateTime.Now);
        var pid = 4_000_100;
        foreach (var tab in vm.Tabs)
        {
            tab.LaunchedAs = tab.ToConfig(forLaunch: true);
            tab.Session = session;
            tab.Track = session.NewTrack();
            tab.ShellPid = pid++;
            tab.SetState(TabState.Running, null, activity: activity);
        }
        return (vm, session, candidates);
    }

    private static TabViewModel Row(MainViewModel vm, string name) => vm.Tabs.Single(t => t.Name == name);

    private static string? Next(TabSession session, TabViewModel row)
    {
        var path = Path.Combine(session.Directory, row.TrackId! + ".next");
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    private static void Read(MainViewModel vm, string cli, string key, AccountLimits limits)
    {
        // The account's name as the Accounts page passes it when the agent has several.
        vm.CheckQuota(cli, key, cli != "claude" ? null : key == "" ? "Privat" : "Work", limits);
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public void EquivalenceTableFollowsTheCatalog()
    {
        Assert.Equal(ModelTier.Top, ModelSwitch.Tier("claude", "claude-opus-5-5"));
        Assert.Equal(ModelTier.Top, ModelSwitch.Tier("codex", Catalog.StandardModel));
        Assert.Equal(ModelTier.Mid, ModelSwitch.Tier("claude", "claude-sonnet-5-5"));
        Assert.Equal(ModelTier.Small, ModelSwitch.Tier("agy", "gemini-3.8-flash-low"));
        Assert.Null(ModelSwitch.Tier("claude", "claude-fable-5-2"));
        Assert.Null(ModelSwitch.Tier("aider", "gpt-4o"));
        Assert.Equal("gpt-6.2-sol", ModelSwitch.ModelFor("codex", ModelTier.Top));
        Assert.Equal("gpt-6.1-terra", ModelSwitch.ModelFor("codex", ModelTier.Mid));
        Assert.Equal("gpt-6-luna", ModelSwitch.ModelFor("codex", ModelTier.Small));
        Assert.Equal("claude-sonnet-5-5", ModelSwitch.ModelFor("claude", ModelTier.Mid));
        Assert.Equal("gemini-4-neon-high", ModelSwitch.ModelFor("agy", ModelTier.Top));
        Assert.Equal("gemini-4-carbon-medium", ModelSwitch.ModelFor("agy", ModelTier.Mid));
        Assert.Equal("gemini-3.8-flash-high", ModelSwitch.ModelFor("agy", ModelTier.Small));
        // Every catalog model of the three agents except Fable has a tier, so a discovered list is covered too.
        foreach (var cli in ModelSwitch.Agents)
            Assert.All(Catalog.ModelsFor(cli).Where(m => !m.StartsWith("claude-fable", StringComparison.Ordinal)), m => Assert.NotNull(ModelSwitch.Tier(cli, m)));
    }

    [Fact]
    public void OfferOnlyAtAFullWindowWithALongWait()
    {
        Assert.Equal((WeekReset, true), ModelSwitch.Exhausted(Used(30, 100), Now));
        Assert.Null(ModelSwitch.Exhausted(Used(30, 99), Now));
        // A reset two hours away or closer is worth waiting for.
        Assert.Null(ModelSwitch.Exhausted(Used(30, 100, Now.AddHours(2)), Now));
        Assert.NotNull(ModelSwitch.Exhausted(Used(30, 100, Now.AddHours(2).AddMinutes(1)), Now));
        Assert.Equal((Now.AddHours(3), false), ModelSwitch.Exhausted(new(new LimitWindow(100, Now.AddHours(3), 300), null, null, Now), Now));
    }

    [Fact]
    public void PlanPicksTheEquivalentWithMostQuotaLeft()
    {
        var c = new Candidates().List();
        var plan = ModelSwitch.Plan("claude", "", "Privat", "claude-opus-5-5", WeekReset, true, c, Now)!;
        Assert.Equal(("codex", "gpt-6.2-sol", 80.0), (plan.Target.Cli, plan.Model, plan.RemainingPercent));
        // Logged out, not installed, exhausted and the account itself are never targets; Work is what is left.
        var others = c.Select(a => a.Cli == "codex" ? a with { LoggedIn = false } : a).ToList();
        plan = ModelSwitch.Plan("claude", "", "Privat", "claude-sonnet-5-5", WeekReset, true, others, Now)!;
        Assert.Equal(("claude", "claude-work", "claude-sonnet-5-5"), (plan.Target.Cli, plan.Target.AccountId, plan.Model));
        Assert.Null(ModelSwitch.Plan("claude", "", "Privat", "claude-opus-5-5", WeekReset, true, others, Now, new HashSet<string> { "claude\0~/.claude-work" }));
        Assert.Null(ModelSwitch.Plan("claude", "", "Privat", "claude-fable-5-2", WeekReset, true, c, Now));
    }

    [Fact]
    public void AgentStatusCountsAsIdleAndUnknownStaysUnknown()
    {
        Assert.Equal(SwitchActivity.Idle, ModelSwitch.ActivityOf(Activity.Idle, null));
        Assert.Equal(SwitchActivity.Idle, ModelSwitch.ActivityOf(Activity.Unknown, new AgentStatus(AgentState.Done, "klart", 1)));
        Assert.Equal(SwitchActivity.Idle, ModelSwitch.ActivityOf(Activity.Busy, new AgentStatus(AgentState.Blocked, "väntar", 1)));
        Assert.Equal(SwitchActivity.Working, ModelSwitch.ActivityOf(Activity.Working, null));
        Assert.Equal(SwitchActivity.Working, ModelSwitch.ActivityOf(Activity.Unknown, new AgentStatus(AgentState.Working, "", 1)));
        Assert.Equal(SwitchActivity.Unknown, ModelSwitch.ActivityOf(Activity.Unknown, null));
        // A done or idle status from an earlier turn never outweighs a CLI that works now (#284); a blocked agent waits for an answer.
        Assert.Equal(SwitchActivity.Working, ModelSwitch.ActivityOf(Activity.Working, new AgentStatus(AgentState.Done, "klart", 1)));
        Assert.Equal(SwitchActivity.Working, ModelSwitch.ActivityOf(Activity.Working, new AgentStatus(AgentState.Idle, "", 1)));
        Assert.Equal(SwitchActivity.Idle, ModelSwitch.ActivityOf(Activity.Working, new AgentStatus(AgentState.Blocked, "väntar", 1)));
    }

    [Fact]
    public void AnExhaustedAccountOffersAnEquivalentModelToItsRunningTabs()
    {
        var (vm, session, _) = Setup();
        Read(vm, "claude", "", Used(30, 99));
        Assert.Empty(vm.SwitchOffers);

        Read(vm, "claude", "", Used(30, 100));
        var offer = Assert.Single(vm.SwitchOffers);
        Assert.Same(Row(vm, "Atlas"), offer.Tab); // Run once and Fable are not offered; Beta is on another account
        Assert.Equal("The quota of claude (Privat) is used up until " + HumanTime.Day(WeekReset, Now)
            + ". Switch Atlas to codex · gpt-6.2-sol, which has 80% left?", offer.Question);
        // The window is unfocused: a notification says the same.
        Assert.Equal(("Quota used up", offer.Question), Assert.Single(_notified));
        // Nothing changes before the answer.
        Assert.Equal(("claude", "claude-opus-5-5"), (Row(vm, "Atlas").Llm, Row(vm, "Atlas").Model));
        Assert.Null(Next(session, Row(vm, "Atlas")));

        // The same window never asks again.
        Read(vm, "claude", "", Used(30, 100));
        Assert.Single(vm.SwitchOffers);
    }

    [Fact]
    public void NoEquivalentOrResetsLeftMeansNoOffer()
    {
        var (vm, _, candidates) = Setup();
        candidates.Codex = Used(100, 100);
        candidates.ClaudeWork = Used(40, 100);
        Read(vm, "claude", "", Used(30, 100));
        Assert.Empty(vm.SwitchOffers);

        // A Codex account with a reset left is reset instead (#274).
        var (codexVm, _, _) = Setup();
        codexVm.CodexResetsEnabled = codexVm.AccountLimitsEnabled = true;
        codexVm.ReadAccountLimits = (_, _, _) => Task.FromResult(AccountLimits.Unknown);
        codexVm.ReadCodexResets = (_, _) => Task.FromResult<ResetCredits?>(new ResetCredits(1, true));
        codexVm.AccountsEditor("codex")!.RefreshResetsAsync(force: true).GetAwaiter().GetResult();
        Read(codexVm, "codex", "", Used(100, 100));
        Assert.Empty(codexVm.SwitchOffers);
        codexVm.ReadCodexResets = (_, _) => Task.FromResult<ResetCredits?>(new ResetCredits(0, true));
        codexVm.AccountsEditor("codex")!.RefreshResetsAsync(force: true).GetAwaiter().GetResult();
        Read(codexVm, "codex", "", Used(100, 100));
        Assert.Equal("Beta", Assert.Single(codexVm.SwitchOffers).Tab.Name);
    }

    [Fact]
    public void CancelChangesNothingAndDoesNotAskAgainThisWindow()
    {
        var (vm, session, _) = Setup();
        Read(vm, "claude", "", Used(30, 100));
        vm.DeclineSwitch(vm.SwitchOffers[0]);
        Assert.Empty(vm.SwitchOffers);
        Assert.Equal(("claude", "claude-opus-5-5", false), (Row(vm, "Atlas").Llm, Row(vm, "Atlas").Model, Row(vm, "Atlas").IsChanged));
        Assert.Null(Next(session, Row(vm, "Atlas")));
        Read(vm, "claude", "", Used(30, 100));
        Assert.Empty(vm.SwitchOffers);
        // A new window (another reset time) may ask again.
        Read(vm, "claude", "", Used(30, 100, WeekReset.AddDays(7)));
        Assert.Single(vm.SwitchOffers);
    }

    [Fact]
    public void SwitchOnAnIdleTabRestartsItWithTheContinuationPrompt()
    {
        var (vm, session, _) = Setup();
        Read(vm, "claude", "", Used(30, 100));
        var atlas = Row(vm, "Atlas");
        var pid = atlas.ShellPid!.Value;
        Assert.Equal(SwitchResult.Switched, vm.AcceptSwitch(vm.SwitchOffers[0]));
        Assert.Equal(("codex", "", "gpt-6.2-sol"), (atlas.Llm, atlas.Account, atlas.Model));
        Assert.Equal(("codex", "gpt-6.2-sol"), (atlas.LaunchedAs!.Llm, atlas.LaunchedAs.Model));
        var next = Next(session, atlas)!;
        Assert.Contains("codex -c tui.auto_recap=false --model", next);
        Assert.Contains("Continue where the previous agent left off. Read the repository state, recent commits and any notes or issue comments it left before changing anything.", next);
        Assert.DoesNotContain("resume --last", next);
        // RestartChanged stops the old CLI on the thread pool (Task.Run), so under a full, parallel run it may not have run yet.
        Assert.True(SpinWait.SpinUntil(() => { lock (_stopped) return _stopped.Count > 0; }, TimeSpan.FromSeconds(10)));
        lock (_stopped) Assert.Equal([pid], _stopped);
        Assert.Equal("Atlas restarts with codex / gpt-6.2-sol and continues where the previous agent left off.", vm.Status);
        Assert.Null(atlas.SwitchPrompt);
        Assert.False(atlas.IsChanged);
        // A manual switch posts no notification of its own.
        Assert.DoesNotContain(_notified, n => n.Title == "Model switched");
    }

    [Fact]
    public void SwitchOnAWorkingTabWaitsUntilItIsIdleAndTheWaitCanBeCancelled()
    {
        var (vm, session, _) = Setup(Activity.Working);
        Read(vm, "claude", "", Used(30, 100));
        var atlas = Row(vm, "Atlas");
        Assert.Equal(SwitchResult.Waiting, vm.AcceptSwitch(vm.SwitchOffers[0]));
        Assert.True(atlas.PendingRestart);
        Assert.Equal("Switching to codex · gpt-6.2-sol when the tab is idle", atlas.StatusText);
        // Nothing is stopped, queued or closed while the agent works.
        vm.RestartWhenIdle(atlas, TabState.Running, Activity.Working);
        vm.RestartWhenIdle(atlas, TabState.Running, Activity.Unknown);
        Assert.Null(Next(session, atlas));
        Assert.Empty(_stopped);
        Assert.False(atlas.IsClosing);

        // ↻ cancels the wait and gives the earlier choices back.
        vm.CancelSwitch(atlas);
        Assert.False(atlas.PendingRestart);
        Assert.Null(atlas.PendingSwitch);
        Assert.Equal(("claude", "", "claude-opus-5-5", false), (atlas.Llm, atlas.Account, atlas.Model, atlas.IsChanged));
        vm.RestartWhenIdle(atlas, TabState.Running, Activity.Idle);
        Assert.Null(Next(session, atlas));
    }

    [Fact]
    public void AWaitingSwitchRestartsOnceTheAgentSaysItIsDone()
    {
        var (vm, session, _) = Setup(Activity.Working);
        Read(vm, "claude", "", Used(30, 100));
        var atlas = Row(vm, "Atlas");
        vm.AcceptSwitch(vm.SwitchOffers[0]);
        File.AppendAllText(Path.Combine(session.Directory, TabChannel.EventsFile(atlas.TrackId!)),
            $"{{\"v\":1,\"at\":{Now.ToUnixTimeMilliseconds()},\"kind\":\"status\",\"state\":\"done\",\"text\":\"Pushed.\"}}\n");
        vm.UpdateChannel(atlas, session.ReadChannel(atlas.TrackId!));
        // The CLI still works on a turn after the done status (#284): the switch keeps waiting.
        vm.RestartWhenIdle(atlas, TabState.Running, Activity.Working);
        Assert.Null(Next(session, atlas));
        Assert.True(atlas.PendingRestart);
        vm.RestartWhenIdle(atlas, TabState.Running, Activity.Busy);
        Assert.Contains("Continue where the previous agent left off.", Next(session, atlas));
        Assert.False(atlas.PendingRestart);
        Assert.Null(atlas.PendingSwitch);
    }

    [Fact]
    public void UnknownActivityAsksFirstAndIsNeverSwitchedAutomatically()
    {
        var (vm, session, _) = Setup(Activity.Unknown);
        Read(vm, "claude", "", Used(30, 100));
        var offer = vm.SwitchOffers[0];
        Assert.Equal(SwitchResult.ConfirmUnknown, vm.AcceptSwitch(offer));
        Assert.Equal("claude", Row(vm, "Atlas").Llm);
        Assert.Null(Next(session, Row(vm, "Atlas")));
        Assert.Equal("The activity is unknown. Switch anyway?", ChoiceQuestion.SwitchUnknown(offer).Heading);
        Assert.Equal(SwitchResult.Switched, vm.AcceptSwitch(offer, unknownConfirmed: true));
        Assert.Contains("Continue where the previous agent left off.", Next(session, Row(vm, "Atlas")));

        // With the automatic switch on, an unknown activity gets the question instead of a switch.
        var (auto, autoSession, _) = Setup(Activity.Unknown, auto: true);
        Read(auto, "claude", "", Used(30, 100));
        Assert.Single(auto.SwitchOffers);
        Assert.Equal("claude", Row(auto, "Atlas").Llm);
        Assert.Null(Next(autoSession, Row(auto, "Atlas")));
    }

    [Fact]
    public void AutomaticSwitchNeedsNoQuestionAndAlwaysNotifies()
    {
        var (vm, session, _) = Setup(auto: true);
        vm.MainWindowFocused = true;
        Read(vm, "claude", "", Used(30, 100));
        Assert.Empty(vm.SwitchOffers);
        var atlas = Row(vm, "Atlas");
        Assert.Equal(("codex", "gpt-6.2-sol"), (atlas.LaunchedAs!.Llm, atlas.LaunchedAs.Model));
        Assert.Contains("Continue where the previous agent left off.", Next(session, atlas));
        Assert.Equal(("Model switched", "Atlas switched to codex · gpt-6.2-sol: the quota of claude (Privat) is used up until "
            + HumanTime.Day(WeekReset, Now) + "."), Assert.Single(_notified));
    }

    [Fact]
    public void AutomaticSwitchOfAWorkingTabWaitsAndNotifiesWhenItSwitches()
    {
        var (vm, session, _) = Setup(Activity.Working, auto: true);
        Read(vm, "claude", "", Used(30, 100));
        var atlas = Row(vm, "Atlas");
        Assert.True(atlas.PendingRestart);
        Assert.Empty(_notified);
        Assert.Null(Next(session, atlas));
        vm.RestartWhenIdle(atlas, TabState.Running, Activity.Idle);
        Assert.NotNull(Next(session, atlas));
        Assert.Equal("Model switched", Assert.Single(_notified).Title);
    }

    [Fact]
    public async Task TheSettingIsOffByDefaultAndTurningItOnAsksFirst()
    {
        Assert.False(new AppConfig().AutoModelSwitch);
        Assert.False(ConfigStore.Parse("{}").AutoModelSwitch);
        var vm = new MainViewModel(new AppConfig());
        Assert.False(vm.AutoModelSwitch);
        var asked = 0;
        Assert.False(await vm.RequestAutoModelSwitchAsync(true, () => { asked++; return Task.FromResult(false); }));
        Assert.False(vm.AutoModelSwitch);
        Assert.True(await vm.RequestAutoModelSwitchAsync(true, () => { asked++; return Task.FromResult(true); }));
        Assert.True(vm.AutoModelSwitch);
        Assert.Equal(2, asked);
        // Turning it off asks nothing.
        Assert.False(await vm.RequestAutoModelSwitchAsync(false, () => throw new InvalidOperationException()));
        Assert.False(vm.AutoModelSwitch);
        var question = ChoiceQuestion.EnableAutoSwitch();
        Assert.Equal(("This may use up your tokens faster than planned. Experimental feature.", "Turn on"), (question.Body, question.Confirm));
        Loc.Use("sv");
        question = ChoiceQuestion.EnableAutoSwitch();
        Assert.Equal(("Detta kan förbruka dina tokens snabbare än planerat. Experimentell funktion.", "Slå på", "Avbryt"),
            (question.Body, question.Confirm, question.Cancel));
    }

    [Fact]
    public void OneSwitchPerWindowAndNeverBackToTheAccountItLeft()
    {
        var (vm, session, candidates) = Setup(auto: true);
        Read(vm, "claude", "", Used(30, 100));
        var atlas = Row(vm, "Atlas");
        Assert.Equal("codex", atlas.LaunchedAs!.Llm);
        File.Delete(Path.Combine(session.Directory, atlas.TrackId! + ".next"));

        // Codex runs out too. The default claude account now shows quota left (a stale or optimistic reading), but the tab left it
        // in this window, so it goes to Work instead.
        candidates.ClaudeDefault = Used(10, 10);
        candidates.ClaudeWork = Used(40, 40);
        candidates.Codex = Used(100, 100);
        Read(vm, "codex", "", Used(100, 100));
        Assert.Equal(("claude", "claude-work", "claude-opus-5-5"), (atlas.LaunchedAs.Llm, atlas.Account, atlas.LaunchedAs.Model));
        File.Delete(Path.Combine(session.Directory, atlas.TrackId! + ".next"));

        // Work runs out as well: the only accounts with quota are ones it left, so nothing happens, and a repeat reading changes nothing.
        candidates.ClaudeWork = Used(100, 100);
        Read(vm, "claude", "~/.claude-work", Used(100, 100));
        Read(vm, "claude", "~/.claude-work", Used(100, 100));
        Assert.Equal("claude-work", atlas.Account);
        Assert.Null(Next(session, atlas));
        Assert.Equal(2, _notified.Count(n => n.Title == "Model switched" && n.Body.StartsWith("Atlas ", StringComparison.Ordinal)));
    }
}
