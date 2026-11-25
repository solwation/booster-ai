using System.Text.Json;
using Avalonia.Threading;
using BoosterAI;
using Xunit;

namespace BoosterAI.Tests;

/// <summary>#211: the low-quota warning fires once per window and account, re-arms on reset or rise, and is a setting.</summary>
[Collection(ConfigFileCollection.Name)]
public class QuotaWarningTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 10, 0, 0, TimeSpan.Zero);

    private static AccountLimits Limits(double? session, double? week, DateTimeOffset? sessionReset = null, DateTimeOffset? weekReset = null) =>
        new(session is { } s ? new LimitWindow(s, sessionReset ?? Now.AddHours(2), 300) : null,
            week is { } w ? new LimitWindow(w, weekReset ?? Now.AddDays(3), 10080) : null, null, Now);

    private static IReadOnlyList<QuotaWarning> Read(QuotaWarnings q, AccountLimits l, DateTimeOffset? now = null, int threshold = 5, string account = "a") =>
        q.Update("claude", account, null, l, now ?? Now, threshold);

    [Fact]
    public void CrossingBelowTheThresholdWarnsOncePerWindow()
    {
        var q = new QuotaWarnings();
        Assert.Empty(Read(q, Limits(90, 50)));
        var w = Assert.Single(Read(q, Limits(97, 50)));
        Assert.Equal(3, w.RemainingPercent);
        Assert.Equal("5 h", w.Window);
        Assert.Equal(Now.AddHours(2), w.ResetsAt);
        Assert.Empty(Read(q, Limits(98, 50))); // still below: no repeat
        Assert.Empty(Read(q, Limits(99, 50)));
    }

    [Fact]
    public void ExactlyTheThresholdIsNotBelow()
    {
        var q = new QuotaWarnings();
        Assert.Empty(Read(q, Limits(95, null))); // 5 % left, threshold 5
        Assert.Single(Read(q, Limits(96, null)));
    }

    [Fact]
    public void RisingAboveTheThresholdRearms()
    {
        var q = new QuotaWarnings();
        Assert.Single(Read(q, Limits(97, null)));
        Assert.Empty(Read(q, Limits(40, null))); // e.g. quota bought or window shortened
        Assert.Single(Read(q, Limits(98, null)));
    }

    [Fact]
    public void AResetWindowRearmsEvenWithoutAReadingAbove()
    {
        var q = new QuotaWarnings();
        Assert.Single(Read(q, Limits(97, null)));
        var later = Now.AddHours(3); // the window expired; the new one is already low
        var fresh = Read(q, Limits(96, null, later.AddHours(5)), later);
        Assert.Equal(later.AddHours(5), Assert.Single(fresh).ResetsAt);
        Assert.Empty(Read(q, Limits(96, null, later.AddHours(5)), later));
    }

    [Fact]
    public void UnknownOrExpiredWindowsNeitherWarnNorRearm()
    {
        var q = new QuotaWarnings();
        Assert.Single(Read(q, Limits(97, null)));
        Assert.Empty(Read(q, AccountLimits.Unknown)); // a failed read says nothing
        Assert.Empty(Read(q, Limits(97, null))); // and the same window does not warn again
        Assert.Empty(Read(q, Limits(97, null), Now.AddHours(3))); // already reset: dropped, no stale warning
    }

    [Fact]
    public void WindowsAndAccountsAreIndependent()
    {
        var q = new QuotaWarnings();
        var both = Read(q, Limits(97, 98));
        Assert.Equal(["5 h", "Vecka"], both.Select(w => w.Window));
        Assert.Empty(Read(q, Limits(97, 98)));
        Assert.Equal(2, Read(q, Limits(97, 98), account: "b").Count); // another account has its own state
        Assert.Empty(Read(q, Limits(97, 40))); // week rises, session stays quiet
        Assert.Single(Read(q, Limits(97, 99)));
    }

    [Fact]
    public void ThresholdIsClampedAndUsesTheRoundedShownValue()
    {
        var q = new QuotaWarnings();
        Assert.Empty(Read(q, Limits(95.4, null), threshold: 5)); // shows "5 % kvar"
        Assert.Single(Read(q, Limits(95.6, null), threshold: 5)); // shows "4 % kvar"
        Assert.Equal(1, QuotaWarnings.Normalize(0));
        Assert.Equal(50, QuotaWarnings.Normalize(99));
    }

    [Theory]
    [InlineData("sv", "claude (Privat), 5 h: 3 % kvar, återställs om 1 h 40 min (11:40)", "codex, Vecka: 3 % kvar, återställs ")]
    [InlineData("en", "claude (Privat), 5 h: 3% left, resets in 1h 40m (11:40)", "codex, Week: 3% left, resets ")]
    public void TextNamesAgentAccountWindowRemainingAndReset(string language, string expected, string noAccountStart)
    {
        var previous = Loc.Language;
        Loc.Use(language);
        try
        {
            var clock = HumanTime.Clock(Now.AddMinutes(100)); // the machine's zone decides the clock, not the test
            var text = new QuotaWarning("claude", "Privat", "5 h", 3, Now.AddMinutes(100)).Text(Now);
            Assert.Equal(expected, text.Replace(clock, "11:40"));
            Assert.StartsWith(noAccountStart, new QuotaWarning("codex", null, Loc.Get("account.limit.week"), 3, Now.AddDays(3)).Text(Now));
        }
        finally { Loc.Use(previous); }
    }

    [Fact]
    public void NoticesReplaceTheSameWindowAndStayFew()
    {
        var n = new QuotaNotices();
        n.Show(new QuotaWarning("claude", null, "5 h", 3, Now.AddHours(1)), Now);
        n.Show(new QuotaWarning("claude", null, "5 h", 2, Now.AddHours(1)), Now);
        Assert.Single(n.Items);
        Assert.Contains("2 % kvar", n.Items[0].Text);
        for (var i = 0; i < 6; i++) n.Show(new QuotaWarning("agent" + i, null, "5 h", 1, Now.AddHours(1)), Now);
        Assert.Equal(QuotaNotices.Max, n.Items.Count);
        n.Close(n.Items[0]);
        Assert.Equal(QuotaNotices.Max - 1, n.Items.Count);
    }

    [Fact]
    public void SettingIsOnByDefaultWithFivePercentAndRoundTrips()
    {
        var cfg = new AppConfig();
        Assert.True(cfg.QuotaWarn);
        Assert.Equal(5, cfg.QuotaWarnPercent);
        var old = JsonSerializer.Deserialize("{\"AutoUpdateClis\": false}", ConfigJsonContext.Default.AppConfig)!;
        Assert.True(old.QuotaWarn);
        Assert.Equal(5, old.QuotaWarnPercent);
        var json = JsonSerializer.Serialize(new AppConfig { QuotaWarn = false, QuotaWarnPercent = 12 }, ConfigJsonContext.Default.AppConfig);
        var back = JsonSerializer.Deserialize(json, ConfigJsonContext.Default.AppConfig)!;
        Assert.Equal((false, 12), (back.QuotaWarn, back.QuotaWarnPercent));
    }

    [Fact]
    public void SettingsBoxClampsAndKeepsAValueWhenEmptied()
    {
        var cfg = new AppConfig();
        var vm = new MainViewModel(cfg);
        vm.QuotaWarnPercent = 0;
        Assert.Equal(QuotaWarnings.MinPercent, cfg.QuotaWarnPercent);
        vm.QuotaWarnPercent = 20;
        vm.QuotaWarnPercent = null;
        Assert.Equal(20, cfg.QuotaWarnPercent);
        vm.QuotaWarnPercent = 500;
        Assert.Equal(QuotaWarnings.MaxPercent, cfg.QuotaWarnPercent);
    }

    [Fact]
    public void ConfigMergeTakesAnotherInstancesChange()
    {
        var baseline = new AppConfig();
        var mine = ConfigStore.Copy(baseline);
        var disk = ConfigStore.Copy(baseline);
        disk.QuotaWarn = false;
        disk.QuotaWarnPercent = 9;
        Assert.True(ConfigMerge.TakeOthers(mine, baseline, disk));
        Assert.Equal((false, 9), (mine.QuotaWarn, mine.QuotaWarnPercent));
    }

    [Fact]
    public async Task ReadingBelowTheThresholdShowsOneNoticeAndTheSettingSilencesIt()
    {
        var cfg = new AppConfig { Accounts = [new() { Id = "c-work", Agent = "claude", Name = "Jobb", Directory = "~/.claude-work" }] };
        var vm = new MainViewModel(cfg);
        vm.LimitsClock = () => Now;
        vm.AccountLimitsEnabled = true;
        var used = 97.0;
        vm.ReadAccountLimits = (_, _, dir) => Task.FromResult(dir is null ? Limits(used, null) : AccountLimits.Unknown);
        var accounts = vm.AccountsEditor("claude")!;
        await accounts.RefreshLimitsAsync();
        Dispatcher.UIThread.RunJobs();
        var notice = Assert.Single(vm.Quota.Items);
        Assert.Contains("(Standard)", notice.Text);
        Assert.Contains("3 % kvar", notice.Text);
        await accounts.RefreshLimitsAsync();
        Dispatcher.UIThread.RunJobs();
        Assert.Single(vm.Quota.Items); // same window: no second warning
        vm.Quota.Close(notice);
        await accounts.RefreshLimitsAsync();
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(vm.Quota.Items); // closed stays closed until the window resets or rises

        vm.QuotaWarn = false;
        used = 40; await accounts.RefreshLimitsAsync();
        used = 99; await accounts.RefreshLimitsAsync();
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(vm.Quota.Items);
    }
}
