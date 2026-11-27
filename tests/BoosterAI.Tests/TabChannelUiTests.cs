using System.Text.Json;
using BoosterAI;
using Xunit;

/// <summary>The tab status channel in the main window (#249): status line, question badge, answers once, stale questions, notifications.</summary>
[Collection(ConfigFileCollection.Name)]
public sealed class TabChannelUiTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("bs-channel-ui").FullName;
    private readonly List<(string Title, string Body, bool Question)> _notified = [];

    public TabChannelUiTests() => ConfigStore.UseFile(Path.Combine(_root, "config.json"));

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static long Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private static string Status(long at, string state, string text) =>
        JsonSerializer.Serialize(new { v = 1, at, kind = "status", state, text }) + "\n";

    private static string Question(long at, string id, string text, string[] options, bool free, long? expiresAt = null) =>
        expiresAt is { } exp
            ? JsonSerializer.Serialize(new { v = 1, at, kind = "question", id, text, options, free, expires_at = exp }) + "\n"
            : JsonSerializer.Serialize(new { v = 1, at, kind = "question", id, text, options, free }) + "\n";

    /// <summary>A project "Atlas" with a running subtab "Atlas-api" whose channel lives in a real session folder.</summary>
    private (MainViewModel Vm, TabViewModel Project, TabViewModel Sub, TabSession Session) Setup(bool notifications = true, bool channel = true)
    {
        var vm = new MainViewModel(new AppConfig
        {
            Notifications = notifications, TabStatusChannel = channel,
            Tabs = [new() { Name = "Atlas", Llm = "claude", SubTabs = [new() { Name = "Atlas-api", Llm = "codex" }] }],
        });
        vm.ShowNotification = (title, body, question) => _notified.Add((title, body, question));
        vm.ChannelNotifyAfter = 0;
        var project = vm.Tabs[0];
        var sub = vm.Tabs[1];
        var session = new TabSession(Path.Combine(_root, "sessions"), DateTime.Now);
        sub.LaunchedAs = sub.ToConfig(forLaunch: true);
        sub.Session = session;
        sub.Track = session.NewTrack(statusChannel: true);
        sub.SetState(TabState.Running, null, activity: Activity.Idle);
        return (vm, project, sub, session);
    }

    private static void Write(TabSession session, TabViewModel row, string events) =>
        File.AppendAllText(Path.Combine(session.Directory, TabChannel.EventsFile(row.TrackId!)), events);

    private static void Poll(MainViewModel vm, TabSession session, TabViewModel row) => vm.UpdateChannel(row, session.ReadChannel(row.TrackId!));

    private static string AnswerFile(TabSession session, TabViewModel row, string id) =>
        Path.Combine(session.Directory, TabChannel.AnswersDirectory(row.TrackId!), id);

    [Fact]
    public void StatusAndQuestionAreShownOnTheTabAndItsProject()
    {
        var (vm, project, sub, session) = Setup();
        Assert.False(sub.HasAgentStatus);
        Assert.False(project.HasQuestionBadge);
        Write(session, sub, Status(Now - 2000, "blocked", "Need the branch\nfor the fix") + Question(Now - 1000, "q1", "Which branch?", ["main", "dev"], true));
        Poll(vm, session, sub);

        Assert.True(sub.HasAgentStatus);
        Assert.True(sub.AgentBlocked);
        Assert.Equal("Blocked: Need the branch for the fix", sub.AgentStatusText);
        Assert.Contains("Need the branch\nfor the fix", sub.AgentStatusTip);
        Assert.Contains("Atlas-api", sub.AgentStatusName);
        Assert.False(project.HasAgentStatus);

        Assert.True(sub.HasQuestionBadge);
        Assert.True(project.HasQuestionBadge);
        Assert.False(project.QuestionBadgeStale);
        Assert.Equal("?", project.QuestionBadgeText);
        Assert.Equal("Atlas-api: Which branch?", project.QuestionBadgeTip);
        Assert.Equal("Atlas: 1 question from the agent. Open the question", project.QuestionBadgeName);
        var q = Assert.Single(project.GroupQuestions);
        Assert.Same(q, Assert.Single(sub.GroupQuestions));
        Assert.Equal("Which branch?", q.Text);
        Assert.Equal(["main", "dev"], q.Options.Select(o => o.Text));
        Assert.Equal("Answer dev", q.Options[1].Name);
        Assert.True(q.Free);
        Assert.True(q.CanAnswer);
        Assert.False(q.IsStale);
        Assert.Same(sub, vm.QuestionTarget());

        // The next poll keeps the same view model, so a typed answer survives it.
        q.FreeText = "draft";
        Poll(vm, session, sub);
        Assert.Same(q, Assert.Single(sub.GroupQuestions));
        Assert.Equal("draft", q.FreeText);
    }

    [Fact]
    public void AnAnswerIsWrittenOnceAndTheBadgeDisappears()
    {
        var (vm, project, sub, session) = Setup();
        Write(session, sub, Question(Now - 1000, "q1", "Which branch?", ["main", "dev"], false));
        Poll(vm, session, sub);
        var q = Assert.Single(sub.GroupQuestions);
        var option = q.Options[1];
        Assert.False(q.Free);

        Assert.Equal(AnswerResult.Answered, option.Choose());
        var file = AnswerFile(session, sub, "q1");
        var written = File.ReadAllText(file);
        Assert.Equal("dev", TabChannel.ParseAnswer(written)!.Text);
        Assert.False(sub.HasQuestionBadge);
        Assert.False(project.HasQuestionBadge);
        Assert.Empty(project.GroupQuestions);
        Assert.Contains("Atlas-api", vm.Status);

        // A second click, Enter or another answer does not write again.
        Assert.Null(option.Choose());
        Assert.Null(q.Options[0].Choose());
        Assert.False(q.CanAnswer);
        Assert.Equal(AnswerResult.AlreadyAnswered, session.Answer(sub.TrackId!, "q1", "main"));
        Assert.Equal(written, File.ReadAllText(file));
        Poll(vm, session, sub);
        Assert.False(sub.HasQuestionBadge);
    }

    [Fact]
    public void FreeTextIsSentOnceAndRejectedWhenEmpty()
    {
        var (vm, _, sub, session) = Setup();
        Write(session, sub, Question(Now - 1000, "name", "What should the release be called?", [], true));
        Poll(vm, session, sub);
        var q = Assert.Single(sub.GroupQuestions);
        Assert.False(q.HasOptions);
        Assert.False(q.CanSend);
        Assert.Null(q.SendFreeText());
        q.FreeText = "  Aurora  ";
        Assert.True(q.CanSend);
        Assert.Equal(AnswerResult.Answered, q.SendFreeText());
        Assert.Equal("Aurora", TabChannel.ReadAnswer(AnswerFile(session, sub, "name"))!.Text);
        Assert.Null(q.SendFreeText());
        Assert.False(sub.HasQuestionBadge);
    }

    [Fact]
    public void AnAnswerSentElsewhereFirstIsReportedAndClearsTheQuestion()
    {
        var (vm, _, sub, session) = Setup();
        Write(session, sub, Question(Now - 1000, "q1", "Go?", ["yes", "no"], false));
        Poll(vm, session, sub);
        var q = Assert.Single(sub.GroupQuestions);
        // Another BoosterAI window answered between this window's poll and the click.
        Assert.Equal(AnswerResult.Answered, session.Answer(sub.TrackId!, "q1", "no"));
        Assert.Equal(AnswerResult.AlreadyAnswered, q.Options[0].Choose());
        Assert.Equal("The question has already been answered.", q.Message);
        Assert.Equal("no", TabChannel.ReadAnswer(AnswerFile(session, sub, "q1"))!.Text);
        Assert.False(sub.HasQuestionBadge);
    }

    [Fact]
    public void QuestionsOfAClosedTabOrPastTheirExpiryAreStaleAndCannotBeAnswered()
    {
        var (vm, project, sub, session) = Setup();
        Write(session, sub, Question(Now - 60_000, "old", "Still there?", ["yes"], false, expiresAt: Now - 1000)
            + Question(Now - 1000, "q2", "Delete the cache?", ["yes", "no"], false, expiresAt: Now + 600_000));
        Poll(vm, session, sub);
        var expired = sub.GroupQuestions.Single(q => q.Question.Id == "old");
        var open = sub.GroupQuestions.Single(q => q.Question.Id == "q2");
        Assert.True(expired.IsStale);
        Assert.Equal("Stale: the agent is no longer waiting for an answer.", expired.StaleText);
        Assert.False(expired.CanAnswer);
        Assert.Null(expired.Options[0].Choose());
        Assert.False(File.Exists(AnswerFile(session, sub, "old")));
        Assert.False(open.IsStale);
        Assert.False(project.QuestionBadgeStale);
        Assert.Equal("?2", project.QuestionBadgeText);
        Assert.Equal("1 question waiting for an answer", project.QuestionBadgeTip);

        sub.SetState(TabState.Closed, null);
        Assert.True(open.IsStale);
        Assert.StartsWith("Stale: the tab is closed", open.StaleText);
        Assert.False(open.CanAnswer);
        Assert.Null(open.Options[0].Choose());
        Assert.False(File.Exists(AnswerFile(session, sub, "q2")));
        Assert.True(sub.QuestionBadgeStale);
        Assert.True(project.QuestionBadgeStale);
        Assert.False(sub.HasAgentStatus);
        Assert.Null(vm.QuestionTarget());

        // Removed stale questions stay away at the next polls.
        expired.Dismiss();
        open.Dismiss();
        Poll(vm, session, sub);
        Assert.False(sub.HasQuestionBadge);
        Assert.False(project.HasQuestionBadge);
    }

    [Fact]
    public void ARestartedTabStartsWithoutTheOldQuestions()
    {
        var (vm, _, sub, session) = Setup();
        Write(session, sub, Question(Now - 1000, "q1", "Go?", ["yes"], false));
        Poll(vm, session, sub);
        Assert.True(sub.HasQuestionBadge);
        sub.Track = session.NewTrack(statusChannel: true);
        Assert.False(sub.HasQuestionBadge);
    }

    [Fact]
    public void NothingIsShownWhileTheSettingIsOff()
    {
        var (vm, project, sub, session) = Setup();
        Write(session, sub, Status(Now - 2000, "done", "Finished") + Question(Now - 1000, "q1", "Go?", ["yes"], false));
        Poll(vm, session, sub);
        Assert.True(sub.HasAgentStatus);
        Assert.True(project.HasQuestionBadge);
        vm.TabStatusChannel = false;
        Assert.False(sub.HasAgentStatus);
        Assert.False(sub.HasQuestionBadge);
        Assert.False(project.HasQuestionBadge);
        Assert.Empty(project.GroupQuestions);
        Assert.Null(vm.QuestionTarget());
        vm.TabStatusChannel = true;
        Assert.True(project.HasQuestionBadge);

        var (offVm, offProject, offSub, offSession) = Setup(channel: false);
        _notified.Clear();
        Write(offSession, offSub, Status(Now - 2000, "done", "Finished") + Question(Now - 1000, "q1", "Go?", ["yes"], false));
        Poll(offVm, offSession, offSub);
        Assert.False(offSub.HasAgentStatus);
        Assert.False(offProject.HasQuestionBadge);
        Assert.Empty(_notified);
    }

    [Fact]
    public void NewQuestionsAndDoneOrBlockedNotifyOnceAndOpenTheQuestion()
    {
        var (vm, _, sub, session) = Setup();
        vm.SetMainWindowFocused(true);
        Write(session, sub, Status(Now - 3000, "working", "Building"));
        Poll(vm, session, sub);
        Assert.Empty(_notified);
        vm.SetMainWindowFocused(false);

        Write(session, sub, Question(Now - 2000, "q1", "Which branch?", ["main"], true));
        Poll(vm, session, sub);
        Poll(vm, session, sub);
        Assert.Equal([("Atlas-api has a question", "Which branch?", true)], _notified);

        Write(session, sub, Status(Now - 1000, "blocked", "Waiting for the answer"));
        Poll(vm, session, sub);
        Poll(vm, session, sub);
        Assert.Equal(("Atlas-api is blocked", "Waiting for the answer", true), _notified[^1]);
        Assert.Equal(2, _notified.Count);

        Assert.Equal(AnswerResult.Answered, sub.GroupQuestions[0].Options[0].Choose());
        Write(session, sub, Status(Now, "done", ""));
        Poll(vm, session, sub);
        Assert.Equal(("Atlas-api is done", "Done", false), _notified[^1]);
        Assert.Equal(3, _notified.Count);
    }

    [Fact]
    public void NotificationsFollowTheSettingAndSkipEventsFromBeforeStart()
    {
        var (vm, _, sub, session) = Setup(notifications: false);
        Write(session, sub, Question(Now - 1000, "q1", "Go?", ["yes"], false));
        Poll(vm, session, sub);
        Assert.Empty(_notified);
        Assert.True(sub.HasQuestionBadge);
        Assert.Same(sub, vm.QuestionTarget());

        var (later, _, laterSub, laterSession) = Setup();
        later.ChannelNotifyAfter = Now;
        Write(laterSession, laterSub, Status(Now - 5000, "done", "Old") + Question(Now - 5000, "q1", "Old?", ["yes"], false));
        Poll(later, laterSession, laterSub);
        Assert.Empty(_notified);
        Assert.True(laterSub.HasQuestionBadge);
    }

    [Fact]
    public void ChangedStatusTextNotifiesOnceWhileUnfocusedAndNotOnRepoll()
    {
        var (vm, _, sub, session) = Setup();
        Write(session, sub, Status(Now - 1000, "working", "Step 1"));
        Poll(vm, session, sub);
        Poll(vm, session, sub);
        Assert.Single(_notified);
        Assert.Equal(("Atlas-api has a new status", "Step 1", false), _notified[0]);
    }

    [Fact]
    public void StatusTextFromBeforeStartNeverNotifies()
    {
        var (vm, _, sub, session) = Setup();
        vm.ChannelNotifyAfter = Now;
        Write(session, sub, Status(Now - 5000, "working", "Old"));
        Poll(vm, session, sub);
        Assert.Empty(_notified);
    }

    [Fact]
    public void StatusTextChangedWhileFocusedNeverNotifiesAfterwards()
    {
        var (vm, _, sub, session) = Setup();
        var clock = DateTimeOffset.UtcNow;
        vm.Clock = () => clock;
        vm.SetMainWindowFocused(true);
        Write(session, sub, Status(Now - 1000, "working", "Seen"));
        Poll(vm, session, sub);
        vm.SetMainWindowFocused(false);
        clock = clock.AddMinutes(5);
        Poll(vm, session, sub);
        Assert.Empty(_notified);
    }

    [Fact]
    public void StatusTextIsThrottledToOneNotificationAMinuteWithTheLatestText()
    {
        var (vm, _, sub, session) = Setup();
        var clock = DateTimeOffset.UtcNow;
        vm.Clock = () => clock;
        Write(session, sub, Status(Now - 3000, "working", "A"));
        Poll(vm, session, sub);
        clock = clock.AddSeconds(20);
        Write(session, sub, Status(Now - 2000, "working", "B"));
        Poll(vm, session, sub);
        clock = clock.AddSeconds(20);
        Write(session, sub, Status(Now - 1000, "working", "C"));
        Poll(vm, session, sub);
        Assert.Equal(["A"], _notified.Select(n => n.Body));
        clock = clock.AddSeconds(25);
        Poll(vm, session, sub);
        Assert.Equal(["A", "C"], _notified.Select(n => n.Body));
        Poll(vm, session, sub);
        Assert.Equal(2, _notified.Count);
    }

    [Fact]
    public void DoneIsImmediateAndNotDoubledByTheStatusTextNotification()
    {
        var (vm, _, sub, session) = Setup();
        var clock = DateTimeOffset.UtcNow;
        vm.Clock = () => clock;
        Write(session, sub, Status(Now - 3000, "working", "A"));
        Poll(vm, session, sub);
        clock = clock.AddSeconds(5);
        Write(session, sub, Status(Now - 1000, "done", "All done"));
        Poll(vm, session, sub);
        Poll(vm, session, sub);
        clock = clock.AddMinutes(5);
        Poll(vm, session, sub);
        Assert.Equal(["A", "All done"], _notified.Select(n => n.Body));
        Assert.Equal("Atlas-api is done", _notified[1].Title);
    }

    [Fact]
    public void ExpiresAtIsOptionalAndMustFollowTheQuestion()
    {
        var parsed = TabChannel.Parse(
            Question(1000, "a", "A?", ["x"], false, expiresAt: 5000)
            + Question(1000, "b", "B?", ["x"], false, expiresAt: 500)
            + Question(1000, "c", "C?", ["x"], false), null);
        Assert.Equal([5000L, null, null], parsed.Open.Select(q => q.ExpiresAt));
    }

    [Fact]
    public void AQuestionNotificationClickOpensTheQuestion()
    {
        Assert.Contains($"launch=\"{Notifier.QuestionUri}\"", Notifier.ToastScript("T", "B", openQuestion: true));
        Assert.Contains($"launch=\"{Notifier.ShowUri}\"", Notifier.ToastScript("T", "B"));
    }
}
