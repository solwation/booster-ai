using System.Text.Json;
using Avalonia.Threading;
using BoosterAI;
using Xunit;

/// <summary>
/// #293: a used-up window while the account has credits (Codex keeps working on them) is shown as "using credits", in a calmer
/// style than a stop; the quota warning, save notice and model switch do not treat it as out of quota; and the status popup offers a
/// free Codex reset through the Accounts page's own question and redemption.
/// </summary>
[Collection(ConfigFileCollection.Name)]
public sealed class CreditsTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("bs-credits").FullName;
    private readonly string _language = Loc.Language;
    private static readonly DateTimeOffset Now = new(2026, 11, 28, 10, 0, 0, TimeSpan.Zero);
    private static readonly LimitCredits UnknownBalance = new(true, false, null);
    private static readonly LimitCredits Unlimited = new(false, true, null);
    private static readonly LimitCredits None = new(false, false, null);

    public CreditsTests()
    {
        ConfigStore.UseFile(Path.Combine(_root, "config.json"));
        Loc.Use("sv");
    }

    public void Dispose()
    {
        Loc.Use(_language);
        Directory.Delete(_root, recursive: true);
    }

    /// <summary>Codex's report from the issue: only a weekly window, at exactly 100 %, and the given credits.</summary>
    private static AccountLimits Week(double used, LimitCredits? credits) =>
        new(null, new LimitWindow(used, Now.AddDays(3), 10080), credits, Now.AddMinutes(-2)) { SessionAbsent = true };

    [Fact]
    public void TheParsedIssueReadingIsUsingCreditsOfUnknownAmount()
    {
        var limits = AccountLimitsProbe.Parse("codex", AccountLimitsProbe.Marker
            + "{\"at\":\"2026-11-28T09:58:00Z\",\"primary\":{\"used\":100.0,\"resets_at\":" + Now.AddDays(3).ToUnixTimeSeconds()
            + ",\"minutes\":10080},\"secondary\":null,\"credits\":{\"has_credits\":true,\"unlimited\":false,\"balance\":null}}");
        Assert.True(limits.AsOf(Now).UsingCredits);
        Assert.Equal("Krediter: okänt antal", LimitText.Credits(limits.Credits));
        Loc.Use("en");
        Assert.Equal("Credits: unknown amount", LimitText.Credits(limits.Credits));
    }

    [Fact]
    public void MeterStatusAndPopupRowSayUsingCreditsCalmly()
    {
        var limit = LimitIndicator.For(Week(100, UnknownBalance), Now)!;
        Assert.True(limit.UsingCredits);
        Assert.False(limit.Warn); // no warning cue, icon or diamond
        Assert.Equal("0 % kvar · använder krediter", limit.RemainingCaption);
        Assert.Equal("Krediter", limit.RemainingShort);
        Assert.Equal("100 % · krediter", limit.Caption);
        Assert.Equal("Använder krediter · Krediter: okänt antal", limit.CreditsText);
        Assert.Contains("Använder krediter · Krediter: okänt antal", limit.Tip);
        Assert.DoesNotContain("gränsen nådd", limit.Name);
        Assert.Contains("använder krediter", limit.Name, StringComparison.OrdinalIgnoreCase);
        var row = limit.Rows[1];
        Assert.True(row.Credits);
        Assert.False(row.Warn);
        Assert.Equal("0 % kvar", row.RemainingText);
        Assert.EndsWith(", använder krediter", row.Name);
        var meter = LimitMeter.From(row);
        Assert.True(meter.Credits);
        Assert.False(meter.Warn);
        Assert.Equal(100, meter.Percent);
        var choice = new AgentChoice("codex", "", "", false, null, limit);
        Assert.True(choice.LimitCredits);
        Assert.True(choice.WeekMeterCredits);
        Assert.False(choice.WeekMeterWarn);
        Assert.False(choice.LimitWarn);

        Loc.Use("en");
        Assert.Equal("0% left · using credits", limit.RemainingCaption);
        Assert.Equal("Credits", limit.RemainingShort);
        var en = LimitIndicator.For(Week(100, UnknownBalance), Now)!;
        Assert.Equal("Using credits · Credits: unknown amount", en.CreditsText);
    }

    [Fact]
    public void UnlimitedAndKnownBalancesAreSaidAsReported()
    {
        var unlimited = LimitIndicator.For(Week(100, Unlimited), Now)!;
        Assert.True(unlimited.UsingCredits);
        Assert.False(unlimited.Warn);
        Assert.Equal("Använder krediter · Krediter: obegränsade", unlimited.CreditsText);
        var balance = LimitIndicator.For(Week(104, new LimitCredits(true, false, "120")), Now)!;
        Assert.Equal("Använder krediter · Krediter: 120", balance.CreditsText);
    }

    [Fact]
    public void WithoutCreditsAUsedUpWindowStaysAStop()
    {
        foreach (var credits in new[] { None, null })
        {
            var limit = LimitIndicator.For(Week(100, credits), Now)!;
            Assert.False(limit.UsingCredits);
            Assert.True(limit.Warn);
            Assert.Equal("0 % kvar", limit.RemainingCaption);
            Assert.Equal("0 %", limit.RemainingShort);
            Assert.Equal("100 %", limit.Caption);
            Assert.Equal("", limit.CreditsText);
            Assert.Contains("gränsen nådd", limit.Name);
            Assert.True(limit.Rows[1].Warn);
            Assert.False(limit.Rows[1].Credits);
        }
        // Credits but a window not used up: nothing is on credits yet; the warning range warns as before.
        var near = LimitIndicator.For(Week(95, UnknownBalance), Now)!;
        Assert.False(near.UsingCredits);
        Assert.True(near.Warn);
        Assert.Equal("5 %", near.RemainingShort);
    }

    [Fact]
    public void TheQuotaWarningNeverAlarmsOnCredits()
    {
        foreach (var credits in new[] { UnknownBalance, Unlimited })
        {
            var warnings = new QuotaWarnings();
            var changes = warnings.Evaluate("codex", "codex\0", null, Week(100, credits), Now, 5);
            Assert.Empty(changes.Warnings);
            Assert.Empty(changes.Recoveries);
        }
        var without = new QuotaWarnings().Update("codex", "codex\0", null, Week(100, None), Now, 5);
        Assert.Equal(0, Assert.Single(without).RemainingPercent);

        // Before the window is used up the warning still comes, and says that credits follow.
        var before = Assert.Single(new QuotaWarnings().Update("codex", "codex\0", null, Week(97, UnknownBalance), Now, 5));
        Assert.True(before.Credits);
        Assert.EndsWith(". Sedan används kontots krediter.", before.Text(Now));
        Loc.Use("en");
        Assert.EndsWith(". Then the account's credits are used.", before.Text(Now));
    }

    private (MainViewModel Vm, TabSession Session, List<(string, string)> Notified) RunningCodex(bool saveNotice = true, bool warn = true)
    {
        var notified = new List<(string, string)>();
        var vm = new MainViewModel(new AppConfig
        {
            QuotaWarn = warn, QuotaSaveNotice = saveNotice, AutoModelSwitch = true,
            Tabs = [new() { Name = "Gamma", Llm = "codex", Model = "gpt-6.1-terra" }],
        })
        {
            LimitsClock = () => Now,
            SaveNoticeClock = () => Now,
            QuotaNotify = (title, body) => notified.Add((title, body)),
            SwitchAccounts = () =>
            [
                new("codex", "", "", null, true, true, Week(100, UnknownBalance)),
                new("claude", "", "", null, true, true, new AccountLimits(new LimitWindow(10, Now.AddHours(3), 300), new LimitWindow(10, Now.AddDays(3), 10080), null, Now)),
            ],
            StopTabForeground = (_, _) => { },
        };
        vm.ShowNotification = (title, body, _) => notified.Add((title, body));
        var session = new TabSession(Path.Combine(_root, "sessions-" + Guid.NewGuid().ToString("N")), DateTime.Now);
        var tab = vm.Tabs[0];
        tab.LaunchedAs = tab.ToConfig(forLaunch: true);
        tab.Session = session;
        tab.Track = session.NewTrack(statusChannel: true);
        tab.ShellPid = 4_100_000;
        tab.SetState(TabState.Running, null, activity: Activity.Idle);
        return (vm, session, notified);
    }

    private static string? Notice(TabSession session, TabViewModel row)
    {
        var path = Path.Combine(session.Directory, TabChannel.NoticeFile(row.TrackId!));
        if (!File.Exists(path)) return null;
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.GetProperty("text").GetString();
    }

    [Fact]
    public void AUsedUpWindowOnCreditsGetsNoWarningSaveNoticeOrSwitch()
    {
        var (vm, session, notified) = RunningCodex();
        var tab = vm.Tabs[0];
        vm.CheckQuota("codex", "", null, Week(100, UnknownBalance));
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(vm.Quota.Items);
        Assert.Empty(notified);
        Assert.Null(Notice(session, tab));
        Assert.Empty(vm.SwitchOffers);
        Assert.Equal("codex", tab.Llm); // no automatic switch either
        Assert.Null(ModelSwitch.Exhausted(Week(100, UnknownBalance), Now));
        Assert.Null(ModelSwitch.Exhausted(Week(100, Unlimited), Now));
        Assert.NotNull(ModelSwitch.Exhausted(Week(100, None), Now));
    }

    [Fact]
    public void AUsedUpWindowWithoutCreditsStillWarnsNotifiesAndSwitches()
    {
        var (vm, session, _) = RunningCodex();
        var tab = vm.Tabs[0];
        vm.CheckQuota("codex", "", null, Week(100, None));
        Dispatcher.UIThread.RunJobs();
        Assert.Single(vm.Quota.Items);
        Assert.Contains("Work stops when it runs out.", Notice(session, tab));
        Assert.Equal("claude", tab.Llm); // the experimental automatic switch went to the equivalent model elsewhere
    }

    [Fact]
    public void TheSaveNoticeBeforeAStopOnCreditsSaysWorkContinues()
    {
        var (vm, session, _) = RunningCodex(warn: false);
        var tab = vm.Tabs[0];
        vm.CheckQuota("codex", "", null, Week(99.4, UnknownBalance));
        Dispatcher.UIThread.RunJobs();
        var text = Notice(session, tab);
        Assert.Contains("When it runs out, work continues on the account's credits.", text);
        Assert.DoesNotContain("Work stops", text);
        Assert.Empty(vm.SwitchOffers);
        Assert.Equal("codex", tab.Llm);
    }

    private sealed class Redemptions
    {
        public ResetCredits? Count = new(1, false);
        public readonly List<string?> Consumed = [];
    }

    private static (MainViewModel Vm, Redemptions Fakes) PopupSetup(AccountLimits limits)
    {
        var fakes = new Redemptions();
        var vm = new MainViewModel(new AppConfig { Tabs = [new() { Name = "Gamma", Directory = "/srv/gamma", Llm = "codex" }, new() { Name = "Delta", Directory = "/srv/delta", Llm = "claude" }] })
        {
            LimitsClock = () => Now,
            AccountLimitsEnabled = true,
            CodexResetsEnabled = true,
            QuotaNotify = (_, _) => { },
        };
        vm.ShowNotification = (_, _, _) => { };
        vm.ReadAccountLimits = (_, cli, _) => Task.FromResult(cli == "codex" ? limits : AccountLimits.Unknown);
        vm.ReadCodexResets = (_, _) => Task.FromResult(fakes.Count);
        vm.ConsumeCodexReset = (_, dir, _) => { lock (fakes) fakes.Consumed.Add(dir); return Task.FromResult(ResetOutcome.Reset); };
        foreach (var t in vm.Tabs) t.SetState(TabState.Running, null, activity: Activity.Busy);
        return (vm, fakes);
    }

    [Fact]
    public async Task ThePopupOffersAResetOnCreditsThroughTheAccountsQuestion()
    {
        var (vm, fakes) = PopupSetup(Week(100, UnknownBalance));
        await vm.AccountsEditor("codex")!.RefreshLimitsAsync();
        Dispatcher.UIThread.RunJobs();
        var (gamma, delta) = (vm.Tabs[0], vm.Tabs[1]);

        Assert.True(gamma.PeekUsingCredits);
        Assert.Equal("Använder krediter · Krediter: okänt antal", gamma.PeekCreditsText);
        Assert.Equal("Krediter", gamma.StatusIndicatorText);
        Assert.True(gamma.StatusIndicatorCredits);
        Assert.False(gamma.StatusIndicatorWarn);
        Assert.NotEqual(StatusDotKind.Warn, gamma.StatusDot);
        Assert.Contains("0 % kvar · använder krediter", gamma.StatusDisplay);
        Assert.Equal("1 reset kvar", gamma.PeekResetsText);
        Assert.Equal(Loc.Get("peek.reset.credits"), gamma.PeekResetHint);
        Assert.True(gamma.CanPeekUseReset);
        Assert.False(delta.CanPeekUseReset);
        Assert.False(delta.PeekUsingCredits);

        // No: nothing is redeemed. Yes: exactly one, through the Accounts page's flow with the same question.
        (string Name, int Count)? asked = null;
        Assert.Null(await gamma.UseResetAsync((name, n) => { asked = (name, n); return Task.FromResult(false); }));
        Assert.Equal((vm.DefaultAccountName("codex"), 1), asked);
        Assert.Empty(fakes.Consumed);
        fakes.Count = new ResetCredits(0, false);
        Assert.Equal(ResetOutcome.Reset, await gamma.UseResetAsync((_, _) => Task.FromResult(true)));
        Assert.Equal([null], fakes.Consumed);
        Dispatcher.UIThread.RunJobs();

        // With no reset left the button is gone and nothing can be redeemed.
        Assert.Equal("0 resets kvar", gamma.PeekResetsText);
        Assert.False(gamma.CanPeekUseReset);
        Assert.Null(await gamma.UseResetAsync((_, _) => Task.FromResult(true)));
        Assert.Single(fakes.Consumed);
    }

    [Fact]
    public async Task WithoutCreditsThePopupKeepsTodaysHintAndNoButton()
    {
        var (vm, _) = PopupSetup(Week(100, None));
        await vm.AccountsEditor("codex")!.RefreshLimitsAsync();
        Dispatcher.UIThread.RunJobs();
        var gamma = vm.Tabs[0];
        Assert.False(gamma.PeekUsingCredits);
        Assert.False(gamma.HasPeekCredits);
        Assert.Equal("0 %", gamma.StatusIndicatorText);
        Assert.True(gamma.StatusIndicatorWarn);
        Assert.False(gamma.StatusIndicatorCredits);
        Assert.Equal(Loc.Get("peek.reset.hint"), gamma.PeekResetHint);
        Assert.False(gamma.CanPeekUseReset);
    }
}
