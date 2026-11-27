using System.Globalization;

namespace BoosterAI;

/// <summary>
/// The tab status channel in the main window (#249): the agent's latest status under the row, a question badge on the tab
/// and its project, and the questions with their answers. Read at every status poll (TabSession.ReadChannel); nothing is
/// shown while the TabStatusChannel setting is off.
/// </summary>
public sealed partial class TabViewModel
{
    private TabChannelState _channel = TabChannelState.Empty;
    private List<QuestionViewModel> _questions = [];
    /// <summary>Stale questions the user put away (question id and event time); they stay away until the tab restarts.</summary>
    private readonly HashSet<(string Id, long At)> _dismissed = [];

    internal TabChannelState Channel => _channel;
    internal MainViewModel Owner => _owner;

    /// <summary>Takes a new reading of the channel; question view models (and their typed answers) are kept while the question stays open.</summary>
    internal void SetChannel(TabChannelState state)
    {
        var statusChanged = _channel.Status != state.Status;
        _channel = state;
        var open = new List<QuestionViewModel>();
        foreach (var q in state.Open)
        {
            if (_dismissed.Contains((q.Id, q.At))) continue;
            // Each reading parses new records (and option lists): match by id and event time, the question's identity.
            open.Add(_questions.FirstOrDefault(v => v.Question.Id == q.Id && v.Question.At == q.At) ?? new QuestionViewModel(this, q));
        }
        if (state.Open.Count == 0 && state.Status is null) _dismissed.Clear();
        var changed = !open.SequenceEqual(_questions);
        _questions = open;
        if (statusChanged) RaiseAgentStatus();
        if (changed) RaiseQuestions();
        UpdateStatusSeen();
    }

    /// <summary>How long a seen status stays before the block hides (#271).</summary>
    internal static readonly TimeSpan StatusSeenHideAfter = TimeSpan.FromSeconds(60);
    private (AgentState State, string Text)? _seenKey;
    private DateTimeOffset? _seenSince;
    private bool _statusExpired;

    /// <summary>
    /// Counts a status as seen from when the main window has focus; 60 s later it hides, unless it is blocked or the tab has an
    /// open question. A new text or state shows it again and restarts; the same text re-read does not. Runs at every poll and on focus change.
    /// </summary>
    internal void UpdateStatusSeen()
    {
        var before = ShowAgentStatus;
        var key = ShownAgentStatus is { } s ? (s.State, s.Text) : ((AgentState, string)?)null;
        if (key != _seenKey) { _seenKey = key; _seenSince = null; _statusExpired = false; }
        var now = _owner.Clock();
        if (key is not null && _seenSince is null && _owner.MainWindowFocused) _seenSince = now;
        if (_seenSince is { } since && now - since >= StatusSeenHideAfter) _statusExpired = true;
        if (before != ShowAgentStatus) Raise(nameof(ShowAgentStatus));
    }

    /// <summary>The status block is shown: there is a status and it is not seen-and-expired; blocked status and open questions always show.</summary>
    public bool ShowAgentStatus => HasAgentStatus && (!_statusExpired || AgentBlocked || _questions.Count > 0);

    /// <summary>The tab's state, name or the setting changed: status visibility, staleness and texts follow.</summary>
    internal void RaiseChannel()
    {
        UpdateStatusSeen();
        RaiseAgentStatus();
        foreach (var q in _questions) q.RaiseStale();
        RaiseQuestions();
    }

    internal void DismissQuestion(QuestionViewModel question)
    {
        if (!_questions.Contains(question)) return;
        _dismissed.Add((question.Question.Id, question.Question.At));
        _questions = _questions.Where(q => q != question).ToList();
        RaiseQuestions();
    }

    /// <summary>The agent's latest status while its CLI runs and the setting is on; null otherwise.</summary>
    private AgentStatus? ShownAgentStatus => _owner.TabStatusChannel && _state is TabState.Running && _channel.Status is { } s ? s : null;
    public bool HasAgentStatus => ShownAgentStatus is not null;
    private string AgentStateLabel => ShownAgentStatus is { } s ? Loc.Get("agent.state." + s.State.ToString().ToLowerInvariant()) : "";
    private static string OneLine(string text) => string.Join(' ', text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    /// <summary>"Klar: Finished the release notes" in one line; the row cuts it with "…" and the tooltip has it all.</summary>
    public string AgentStatusText => ShownAgentStatus is { } s
        ? s.Text.Trim().Length == 0 ? AgentStateLabel : Loc.Format("agent.status.line", AgentStateLabel, OneLine(s.Text))
        : "";
    public string AgentStatusTip => ShownAgentStatus is { } s
        ? Loc.Format("agent.status.tip", AgentStateLabel, DayTime(s.At)) + (s.Text.Trim().Length > 0 ? "\n" + s.Text.Trim() : "")
        : "";
    public string AgentStatusName => ShownAgentStatus is null ? "" : Loc.Format("agent.status.name", Name, AgentStatusText);
    public bool AgentDone => ShownAgentStatus?.State == AgentState.Done;
    public bool AgentBlocked => ShownAgentStatus?.State == AgentState.Blocked;
    public bool AgentWorking => ShownAgentStatus?.State == AgentState.Working;
    public bool AgentIdle => ShownAgentStatus?.State == AgentState.Idle;

    private void RaiseAgentStatus()
    {
        foreach (var n in new[] { nameof(HasAgentStatus), nameof(ShowAgentStatus), nameof(AgentStatusText), nameof(AgentStatusTip), nameof(AgentStatusName),
            nameof(AgentDone), nameof(AgentBlocked), nameof(AgentWorking), nameof(AgentIdle) }) Raise(n);
    }

    /// <summary>
    /// The open questions the badge stands for: the tab's own, and on a project also its subtabs' ("on the tab and its project").
    /// Empty while the setting is off.
    /// </summary>
    public IReadOnlyList<QuestionViewModel> GroupQuestions => !_owner.TabStatusChannel ? []
        : IsProject ? _questions.Concat(_owner.Tabs.Where(t => t.Parent == this).SelectMany(t => t._questions)).ToList()
        : _questions.ToList();
    internal IReadOnlyList<QuestionViewModel> OwnQuestions => _owner.TabStatusChannel ? _questions : [];
    public bool HasQuestionBadge => GroupQuestions.Count > 0;
    /// <summary>Every question behind the badge is stale (closed tab or expired): the badge is muted instead of accented.</summary>
    public bool QuestionBadgeStale => GroupQuestions is { Count: > 0 } q && q.All(x => x.IsStale);
    public string QuestionBadgeText => GroupQuestions.Count is var n && n > 1 ? "?" + n.ToString(CultureInfo.InvariantCulture) : "?";
    public string QuestionBadgeTip => GroupQuestions switch
    {
        [] => "",
        [var only] => (only.Tab != this ? only.Tab.Name.Trim() + ": " : "") + (only.IsStale ? Loc.Format("question.badge.stale", Short(only.Text)) : Short(only.Text)),
        var all => Loc.FormatCount("question.badge.tip", all.Count(q => !q.IsStale)),
    };
    public string QuestionBadgeName
    {
        get
        {
            var n = GroupQuestions.Count;
            return Loc.Format(n == 1 ? "question.badge.name.one" : "question.badge.name.other", n, Name.Trim());
        }
    }

    private static string Short(string text) => OneLine(text) is var line && line.Length > 160 ? line[..159] + "…" : line;

    private void RaiseQuestions()
    {
        foreach (var n in new[] { nameof(GroupQuestions), nameof(HasQuestionBadge), nameof(QuestionBadgeStale), nameof(QuestionBadgeText),
            nameof(QuestionBadgeTip), nameof(QuestionBadgeName) }) Raise(n);
        Parent?.RaiseQuestions();
    }

    /// <summary>Why the tab's questions can no longer be answered; null while its CLI runs (or is starting).</summary>
    internal bool CanAnswerQuestions => _state is TabState.Running or TabState.Starting && Session is not null && Track is not null;
}

/// <summary>One open question of a tab (#249): the options as buttons, free text when allowed, and why it is stale.</summary>
public sealed class QuestionViewModel : Observable
{
    private string _freeText = "";
    private string _message = "";
    private bool _sending, _answered;

    internal QuestionViewModel(TabViewModel tab, TabQuestion question)
    {
        Tab = tab;
        Question = question;
        Options = question.Options.Select(o => new QuestionOption(this, o)).ToList();
    }

    public TabViewModel Tab { get; }
    internal TabQuestion Question { get; }
    public string Text => Question.Text;
    public IReadOnlyList<QuestionOption> Options { get; }
    public bool HasOptions => Options.Count > 0;
    public bool Free => Question.Free;
    /// <summary>"Atlas · claude · i dag 14:20": who asks, and when.</summary>
    public string Header => $"{Tab.Name.Trim()} · {Loc.Display("llm", Tab.Llm)} · {HumanTime.Day(DateTimeOffset.FromUnixTimeMilliseconds(Question.At), DateTimeOffset.UtcNow)}";

    /// <summary>Why the question cannot be answered any more, or null: its tab closed (or its CLI ended), or the asker stopped waiting.</summary>
    internal string? StaleReason(DateTimeOffset now) =>
        !Tab.CanAnswerQuestions ? Loc.Get("question.stale.closed")
        : Question.ExpiresAt is { } expires && now.ToUnixTimeMilliseconds() >= expires ? Loc.Get("question.stale.expired")
        : null;
    public bool IsStale => StaleReason(DateTimeOffset.UtcNow) is not null;
    public string StaleText => StaleReason(DateTimeOffset.UtcNow) ?? "";

    public bool CanAnswer => !IsStale && !_sending && !_answered;
    public string FreeText
    {
        get => _freeText;
        set { if (Set(ref _freeText, value ?? "")) Raise(nameof(CanSend)); }
    }
    public bool CanSend => CanAnswer && Free && FreeText.Trim().Length > 0;
    public string Message { get => _message; private set { if (Set(ref _message, value)) Raise(nameof(HasMessage)); } }
    public bool HasMessage => Message.Length > 0;

    public string FreeTextName => Loc.Format("question.free.name", Tab.Name.Trim());
    public string SendName => Loc.Format("question.send.name", Tab.Name.Trim());
    public string DismissName => Loc.Format("question.dismiss.name", Tab.Name.Trim());
    public string PanelName => Loc.Format("question.name", Tab.Name.Trim(), Text);

    internal void RaiseStale()
    {
        foreach (var n in new[] { nameof(IsStale), nameof(StaleText), nameof(CanAnswer), nameof(CanSend), nameof(Header) }) Raise(n);
        foreach (var o in Options) o.RaiseCanAnswer();
    }

    /// <summary>
    /// Sends <paramref name="text"/> as the answer, once: a second click (or Enter) while or after it is written does nothing.
    /// Null when nothing was sent (stale, already sent, or empty).
    /// </summary>
    public AnswerResult? Answer(string text)
    {
        if (!CanAnswer || text.Trim().Length == 0) return null;
        _sending = true;
        RaiseStale();
        var result = Tab.Owner.AnswerQuestion(this, text);
        _sending = false;
        _answered = result is AnswerResult.Answered or AnswerResult.AlreadyAnswered or AnswerResult.UnknownQuestion;
        Message = result switch
        {
            AnswerResult.Answered => "",
            AnswerResult.AlreadyAnswered => Loc.Get("question.result.already"),
            AnswerResult.UnknownQuestion => Loc.Get("question.result.unknown"),
            AnswerResult.Invalid => Loc.Get("question.result.invalid"),
            _ => Loc.Get("question.result.failed"),
        };
        RaiseStale();
        return result;
    }

    public AnswerResult? SendFreeText() => CanSend ? Answer(FreeText) : null;

    public void Dismiss() => Tab.DismissQuestion(this);
}

/// <summary>An option button of a question; its accessible name says it answers.</summary>
public sealed class QuestionOption(QuestionViewModel owner, string text) : Observable
{
    public QuestionViewModel Owner { get; } = owner;
    public string Text { get; } = text;
    public string Name => Loc.Format("question.option.name", Text);
    public bool CanAnswer => Owner.CanAnswer;
    internal void RaiseCanAnswer() => Raise(nameof(CanAnswer));
    public AnswerResult? Choose() => Owner.Answer(Text);
}

public sealed partial class MainViewModel
{
    /// <summary>Events from before this BoosterAI started do not notify: a recovered tab's old question shows its badge only.</summary>
    internal long ChannelNotifyAfter { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    /// <summary>How a channel notification is shown (title, body, opens the question); tests and screenshots replace it.</summary>
    internal Action<string, string, bool> ShowNotification { get; set; } = Notifier.Show;
    private readonly HashSet<string> _channelNotified = [];
    /// <summary>True while the BoosterAI main window has focus: a changed status then is seen and never notifies (#270).</summary>
    internal bool MainWindowFocused { get; set; }
    /// <summary>The clock of the status-text throttle; tests replace it.</summary>
    internal Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.UtcNow;
    internal static readonly TimeSpan StatusNotifyInterval = TimeSpan.FromMinutes(1);
    private sealed class StatusNotifyState { public string? Seen; public string? Pending; public DateTimeOffset LastNotified = DateTimeOffset.MinValue; }
    private readonly Dictionary<string, StatusNotifyState> _statusNotify = [];

    /// <summary>The window got or lost focus; text that changed while it was unfocused but is still throttled is dropped on focus, as the user sees it now.</summary>
    internal void SetMainWindowFocused(bool focused)
    {
        MainWindowFocused = focused;
        if (focused) foreach (var st in _statusNotify.Values) st.Pending = null;
        foreach (var t in Tabs) t.UpdateStatusSeen();
    }

    /// <summary>
    /// A new status text while the window is unfocused notifies at most once a minute per tab with the latest text; a change
    /// seen while focused, a text from before this start and a repeated text never notify (#270).
    /// </summary>
    private void NotifyStatusText(TabViewModel row, string key, AgentStatus? status)
    {
        if (!_statusNotify.TryGetValue(key, out var st)) _statusNotify[key] = st = new();
        var now = Clock();
        if (status is { Text: var raw } && status.At >= ChannelNotifyAfter && raw.Trim() is { Length: > 0 } text && text != st.Seen)
        {
            st.Seen = text;
            st.Pending = MainWindowFocused || status.State is AgentState.Done or AgentState.Blocked ? null : text;
        }
        if (st.Pending is not { } pending || MainWindowFocused || now - st.LastNotified < StatusNotifyInterval) return;
        st.Pending = null;
        st.LastNotified = now;
        if (_cfg.Notifications) ShowNotification(Loc.Format("notify.agent.status", row.Name.Trim()), pending, false);
    }
    /// <summary>The tab whose question was notified last; a click on a notification opens its questions.</summary>
    private TabViewModel? _latestQuestionTab;

    internal void RaiseChannels()
    {
        foreach (var t in Tabs) t.RaiseChannel();
    }

    /// <summary>A poll's reading of a tab's channel: shows it, and notifies a new question or a new done/blocked status once.</summary>
    internal void UpdateChannel(TabViewModel row, TabChannelState state)
    {
        row.SetChannel(state);
        if (!_cfg.TabStatusChannel || row.TrackId is not { } id) return;
        var key = (row.Session?.Directory ?? "") + "/" + id;
        NotifyStatusText(row, key, state.Status);
        foreach (var q in state.Open)
        {
            if (q.At < ChannelNotifyAfter || !_channelNotified.Add($"{key}/q/{q.Id}/{q.At}")) continue;
            _latestQuestionTab = row;
            if (_cfg.Notifications) ShowNotification(Loc.Format("notify.question", row.Name.Trim()), q.Text, true);
        }
        if (state.Status is { State: AgentState.Done or AgentState.Blocked } s && s.At >= ChannelNotifyAfter && _channelNotified.Add($"{key}/s/{s.At}"))
        {
            var hasQuestion = row.OwnQuestions.Any(q => !q.IsStale);
            if (hasQuestion) _latestQuestionTab = row;
            if (_cfg.Notifications)
                ShowNotification(Loc.Format(s.State == AgentState.Done ? "notify.agent.done" : "notify.agent.blocked", row.Name.Trim()),
                    s.Text.Trim().Length > 0 ? s.Text.Trim() : Loc.Get("agent.state." + s.State.ToString().ToLowerInvariant()), hasQuestion);
        }
    }

    /// <summary>The row whose questions a notification click opens: the latest notified one while it has an answerable question, else the first.</summary>
    internal TabViewModel? QuestionTarget() =>
        _latestQuestionTab is { } latest && Tabs.Contains(latest) && latest.OwnQuestions.Any(q => !q.IsStale) ? latest
        : Tabs.FirstOrDefault(t => t.OwnQuestions.Any(q => !q.IsStale));

    /// <summary>Writes the answer through the channel (TabSession.Answer: once, atomically) and reads the tab's channel again.</summary>
    internal AnswerResult AnswerQuestion(QuestionViewModel question, string text)
    {
        var tab = question.Tab;
        if (tab.Session is not { } session || tab.TrackId is not { } id) return AnswerResult.Failed;
        AnswerResult result;
        try { result = session.Answer(id, question.Question.Id, text); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { result = AnswerResult.Failed; }
        if (result is AnswerResult.Answered or AnswerResult.AlreadyAnswered or AnswerResult.UnknownQuestion)
            tab.SetChannel(session.ReadChannel(id));
        if (result == AnswerResult.Answered) ShowStatus(Loc.Format("question.result.sent", tab.Name.Trim(), OneLine(text)));
        return result;
    }

    private static string OneLine(string text)
    {
        var line = string.Join(' ', text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return line.Length > 80 ? line[..79] + "…" : line;
    }

    /// <summary>
    /// For --screenshot --fake-channel status|question|answered|stale (#249): a real session in a temporary folder with invented
    /// events in the v1 format, read and answered through the real channel code; returns the folder (the caller removes it)
    /// and the row whose question panel to show. Nothing starts; no notification is shown.
    /// </summary>
    internal (string Root, TabViewModel? QuestionRow) ShowSampleChannel(string stage)
    {
        ShowNotification = (_, _, _) => { };
        var root = Directory.CreateTempSubdirectory("boosterai-shot-channel").FullName;
        var session = new TabSession(root, DateTime.Now);
        var now = DateTimeOffset.UtcNow;
        long Ago(int minutes) => now.AddMinutes(-minutes).ToUnixTimeMilliseconds();
        var sv = Loc.Language == "sv";
        string Status(long at, string state, string text) =>
            $"{{\"v\":1,\"at\":{at},\"kind\":\"status\",\"state\":\"{state}\",\"text\":{System.Text.Json.JsonSerializer.Serialize(text)}}}\n";
        string Question(long at, string id, string text, string[] options, bool free) =>
            $"{{\"v\":1,\"at\":{at},\"kind\":\"question\",\"id\":\"{id}\",\"text\":{System.Text.Json.JsonSerializer.Serialize(text)},\"options\":{System.Text.Json.JsonSerializer.Serialize(options)},\"free\":{(free ? "true" : "false")}}}\n";
        (TabState State, Activity Activity, string Events)[] samples =
        [
            (TabState.Running, Activity.Idle, Status(Ago(6), "done", sv ? "Klar med versionsanteckningarna, inget kvar att göra" : "Finished the release notes, nothing left to do")),
            (TabState.Running, Activity.Idle, Status(Ago(3), "blocked", sv ? "Behöver veta vilken gren rättelsen ska till" : "Need to know which branch the fix goes into")
                + (stage == "status" ? "" : Question(Ago(3), "q1", sv ? "Vilken gren ska rättelsen slås ihop med?" : "Which branch should the fix be merged into?", ["main", "release/2.4"], true))),
            (TabState.Running, Activity.Working, Status(Ago(1), "working", sv ? "Kör integrationstesterna (steg 3 av 5), sedan hela testsviten mot testdatabasen och till sist checklistan inför versionen, som tar en stund"
                : "Running the integration tests (step 3 of 5), then the end-to-end suite against the test database and finally the release checklist, which takes a while")),
            (TabState.Closed, Activity.Unknown, stage == "status" ? "" : Question(Ago(40), "q1", sv ? "Ska den gamla byggcachen tas bort?" : "Delete the old build cache?", [sv ? "Ja" : "Yes", sv ? "Nej" : "No"], false)),
        ];
        TabViewModel? questionRow = null, staleRow = null;
        foreach (var (tab, sample) in Tabs.Where(t => t.Enabled && t.IsAi).Zip(samples))
        {
            tab.LaunchedAs = tab.ToConfig(forLaunch: true);
            tab.Session = session;
            tab.Track = session.NewTrack(statusChannel: true);
            tab.SetState(sample.State, null, activity: sample.Activity);
            File.WriteAllText(Path.Combine(session.Directory, TabChannel.EventsFile(tab.TrackId!)), sample.Events);
            UpdateChannel(tab, session.ReadChannel(tab.TrackId!));
            if (questionRow is null && tab.OwnQuestions.Any(q => !q.IsStale)) questionRow = tab;
            if (staleRow is null && tab.OwnQuestions.Any(q => q.IsStale)) staleRow = tab;
        }
        if (stage == "answered" && questionRow?.OwnQuestions.FirstOrDefault() is { } question)
        {
            question.Options[^1].Choose();
            questionRow = null;
        }
        return (root, stage switch { "question" => questionRow, "stale" => staleRow, _ => null });
    }
}
