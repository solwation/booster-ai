using System.Text.Json;
using Avalonia.Threading;
using BoosterAI;
using Xunit;

/// <summary>#275: at about 1 % left, every running agent tab on the account gets a save notice through the status channel, once per window.</summary>
[Collection(ConfigFileCollection.Name)]
public sealed class QuotaSaveNoticeTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("bs-save-notice").FullName;
    private readonly string _language = Loc.Language;
    private readonly List<(string Title, string Body)> _notified = [];
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 10, 0, 0, TimeSpan.Zero);

    public QuotaSaveNoticeTests()
    {
        ConfigStore.UseFile(Path.Combine(_root, "config.json"));
        Loc.Use("en");
    }

    public void Dispose()
    {
        Loc.Use(_language);
        Directory.Delete(_root, recursive: true);
    }

    private static AccountLimits Limits(double sessionUsed, DateTimeOffset? reset = null, double weekUsed = 40) =>
        new(new LimitWindow(sessionUsed, reset ?? Now.AddMinutes(70), 300), new LimitWindow(weekUsed, Now.AddDays(3), 10080), null, Now);

    /// <summary>
    /// Atlas (claude) with subtab Atlas-api (claude), Beta (claude, account Work), Gamma (codex), Delta (claude, started with
    /// the channel off), Eps (agy) and Idle (claude, never started). All but Idle run.
    /// </summary>
    private (MainViewModel Vm, TabSession Session) Setup(bool saveNotice = true, int percent = QuotaSaveNotices.DefaultPercent)
    {
        var vm = new MainViewModel(new AppConfig
        {
            QuotaWarn = false, QuotaSaveNotice = saveNotice, QuotaSaveNoticePercent = percent,
            Accounts = [new() { Id = "claude-work", Agent = "claude", Name = "Work", Directory = "~/.claude-work" }],
            Tabs =
            [
                new() { Name = "Atlas", Llm = "claude", SubTabs = [new() { Name = "Atlas-api", Llm = "claude" }] },
                new() { Name = "Beta", Llm = "claude", Account = "claude-work" },
                new() { Name = "Gamma", Llm = "codex" },
                new() { Name = "Delta", Llm = "claude" },
                new() { Name = "Eps", Llm = "agy" },
                new() { Name = "Idle", Llm = "claude" },
            ],
        })
        {
            LimitsClock = () => Now,
            SaveNoticeClock = () => Now,
            QuotaNotify = (_, _) => { },
        };
        vm.ShowNotification = (title, body, _) => _notified.Add((title, body));
        var session = new TabSession(Path.Combine(_root, "sessions"), DateTime.Now);
        foreach (var tab in vm.Tabs.Where(t => t.Name != "Idle"))
        {
            tab.LaunchedAs = tab.ToConfig(forLaunch: true);
            tab.Session = session;
            tab.Track = session.NewTrack(statusChannel: tab.Name != "Delta");
            tab.SetState(TabState.Running, null, activity: Activity.Working);
        }
        return (vm, session);
    }

    private static TabViewModel Row(MainViewModel vm, string name) => vm.Tabs.Single(t => t.Name == name);

    private static string NoticePath(TabSession session, TabViewModel row) => Path.Combine(session.Directory, TabChannel.NoticeFile(row.TrackId!));

    private static string? NoticeText(TabSession session, TabViewModel row)
    {
        var path = NoticePath(session, row);
        if (!File.Exists(path)) return null;
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.GetProperty("text").GetString();
    }

    private static void Read(MainViewModel vm, string cli, string key, AccountLimits limits)
    {
        vm.CheckQuota(cli, key, null, limits);
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public void CrossingTheThresholdSendsOneNoticePerRunningTabOnTheAccount()
    {
        var (vm, session) = Setup();
        Read(vm, "claude", "", Limits(97));
        Assert.Empty(Directory.GetFiles(session.Directory, "*.notice"));

        Read(vm, "claude", "", Limits(99.2));
        foreach (var name in new[] { "Atlas", "Atlas-api" })
        {
            var text = NoticeText(session, Row(vm, name));
            Assert.Equal("The claude account of this tab has about 0.8% of its 5-hour quota left (it resets in 1 h 10 min). "
                + "Work stops when it runs out. You may want to save your state now: commit and push what is complete and write down "
                + "the next steps. Whether and how is up to you.", text);
            Assert.StartsWith("Save notice sent to the agent ", Row(vm, name).StatusTip.Split('\n')[^1]);
        }
        // Another account, another agent and a tab that is not running get nothing.
        foreach (var name in new[] { "Beta", "Gamma", "Eps" }) Assert.Null(NoticeText(session, Row(vm, name)));
        Assert.Null(Row(vm, "Beta").SaveNotice);
        Assert.Null(Row(vm, "Idle").SaveNotice);
        // Started with the channel off: skipped, recorded on the row and in the log.
        Assert.Null(NoticeText(session, Row(vm, "Delta")));
        Assert.Contains("not sent", Row(vm, "Delta").StatusTip);
        Assert.Contains("the status channel was off", Row(vm, "Delta").StatusTip);
        var log = File.ReadAllLines(Path.Combine(session.Directory, QuotaSaveNotices.LogFile));
        Assert.Equal(3, log.Length);
        Assert.Equal(2, log.Count(l => l.Contains(" sent ")));
        Assert.Single(log, l => l.Contains(" skipped ") && l.Contains("tab=Delta"));
        Assert.Equal("claude, 5 h: 0.8% left. Asked Atlas, Atlas-api to save their state. Skipped Delta.", vm.Status);
        Assert.Equal(("Quota nearly used up", vm.Status), Assert.Single(_notified));
    }

    [Fact]
    public void OncePerWindowUntilItResets()
    {
        var (vm, session) = Setup();
        var atlas = Row(vm, "Atlas");
        Read(vm, "claude", "", Limits(99.2));
        File.Delete(NoticePath(session, atlas)); // the agent took it
        Read(vm, "claude", "", Limits(99.6));
        Read(vm, "claude", "", Limits(100));
        Assert.Null(NoticeText(session, atlas));
        Assert.Single(_notified);

        // The window reset (a new reset time): the next time it runs low, the notice comes again.
        Read(vm, "claude", "", Limits(10, Now.AddHours(5)));
        Read(vm, "claude", "", Limits(99.5, Now.AddHours(5)));
        Assert.NotNull(NoticeText(session, atlas));
        Assert.Equal(2, _notified.Count);
    }

    [Fact]
    public void TheWeeklyWindowHasItsOwnNoticeAndExactlyTheThresholdCounts()
    {
        var (vm, session) = Setup();
        Read(vm, "claude", "", Limits(30, weekUsed: 99));
        Assert.Contains("about 1% of its weekly quota", NoticeText(session, Row(vm, "Atlas")));
    }

    [Fact]
    public void AnotherAccountGetsItsOwnNoticeOnly()
    {
        var (vm, session) = Setup();
        Read(vm, "claude", "~/.claude-work", Limits(99.5));
        Assert.NotNull(NoticeText(session, Row(vm, "Beta")));
        foreach (var name in new[] { "Atlas", "Atlas-api", "Delta" }) Assert.Null(Row(vm, name).SaveNotice);
    }

    [Fact]
    public void AnAgentWithoutASafePathIsSkippedAndLogged()
    {
        var (vm, session) = Setup();
        Read(vm, "agy", "", Limits(99.5));
        var eps = Row(vm, "Eps");
        Assert.Null(NoticeText(session, eps));
        Assert.Contains("agy has no safe way to receive it", eps.StatusTip);
        Assert.Contains("reason=agy has no safe way", Assert.Single(File.ReadAllLines(Path.Combine(session.Directory, QuotaSaveNotices.LogFile))));
        Assert.Equal("agy, 5 h: 0.5% left. No running tab could receive a save notice. Skipped Eps.", vm.Status);
        Assert.Empty(_notified); // nothing was sent
    }

    [Fact]
    public void RunOnceTabsHaveNoServerAndAreSkipped()
    {
        var track = new TabTrack("/s/tab-lib.sh", "/s", "tab1", StatusChannel: true);
        var once = new TabConfig { Llm = "claude", Command = "/review", NonInteractive = true };
        Assert.Equal(QuotaSaveNotices.Route.NoSafePath, QuotaSaveNotices.RouteFor(once, track));
        Assert.Equal(QuotaSaveNotices.Route.Channel, QuotaSaveNotices.RouteFor(new TabConfig { Llm = "codex" }, track));
        Assert.Equal(QuotaSaveNotices.Route.ChannelOff, QuotaSaveNotices.RouteFor(new TabConfig { Llm = "codex" }, track with { StatusChannel = false }));
        foreach (var llm in new[] { "agy", "aider", "openhands", "interpreter", Catalog.ShellOnly })
            Assert.Equal(QuotaSaveNotices.Route.NoSafePath, QuotaSaveNotices.RouteFor(new TabConfig { Llm = llm }, track));
    }

    [Fact]
    public void TheSettingOffSendsNothing()
    {
        var (vm, session) = Setup(saveNotice: false);
        Read(vm, "claude", "", Limits(99.9));
        Assert.Empty(Directory.GetFiles(session.Directory, "*.notice"));
        Assert.False(File.Exists(Path.Combine(session.Directory, QuotaSaveNotices.LogFile)));
        Assert.Empty(_notified);
    }

    [Fact]
    public void AFocusedWindowShowsTheStatusButNoNotification()
    {
        var (vm, _) = Setup();
        vm.MainWindowFocused = true;
        Read(vm, "claude", "", Limits(99.5));
        Assert.StartsWith("claude, 5 h: 0.5% left. Asked Atlas", vm.Status);
        Assert.Empty(_notified);
    }

    [Fact]
    public void TheRowSaysWhenTheAgentReadTheNoticeAndARestartClearsIt()
    {
        var (vm, session) = Setup();
        var atlas = Row(vm, "Atlas");
        Read(vm, "claude", "", Limits(99.5));
        var id = atlas.SaveNotice!.NoticeId!;
        var at = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        File.AppendAllText(Path.Combine(session.Directory, TabChannel.EventsFile(atlas.TrackId!)),
            JsonSerializer.Serialize(new { v = 1, at, kind = "notice-read", id }) + "\n");
        vm.UpdateChannel(atlas, session.ReadChannel(atlas.TrackId!));
        Assert.Contains(", read by the agent ", atlas.StatusTip);
        Assert.True(atlas.HasPeekDetail);

        atlas.Track = session.NewTrack(statusChannel: true);
        Assert.Null(atlas.SaveNotice);
        Assert.DoesNotContain("Save notice", atlas.StatusTip);
    }

    [Fact]
    public void SpansAreClockFree()
    {
        Assert.Equal("less than a minute", QuotaSaveNotices.Span(TimeSpan.FromSeconds(20)));
        Assert.Equal("45 min", QuotaSaveNotices.Span(TimeSpan.FromMinutes(45)));
        Assert.Equal("2 h", QuotaSaveNotices.Span(TimeSpan.FromHours(2)));
        Assert.Equal("1 day 4 h", QuotaSaveNotices.Span(TimeSpan.FromHours(28)));
        Assert.Equal("3 days", QuotaSaveNotices.Span(TimeSpan.FromDays(3)));
    }

    [Fact]
    public void ParseKeepsTheLatestNoticeRead()
    {
        var text = "{\"v\":1,\"at\":5,\"kind\":\"notice-read\",\"id\":\"quota-a\"}\n{\"v\":1,\"at\":9,\"kind\":\"notice-read\",\"id\":\"quota-b\"}\n"
            + "{\"v\":1,\"at\":10,\"kind\":\"notice-read\",\"id\":\"../x\"}\n";
        var state = TabChannel.Parse(text, null);
        Assert.Equal(("quota-b", 9L), (state.NoticeRead, state.NoticeReadAt));
        Assert.Null(TabChannel.Parse(text, 10).NoticeRead);
    }
}
