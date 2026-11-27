using System.Globalization;
using Avalonia.Threading;

namespace BoosterAI;

/// <summary>
/// The save notice of #275: when a plan window of an account has about 1 % left, every running agent tab on that account gets
/// a short, neutral note suggesting that it saves its state; the agent decides what to do and nothing is interrupted.
/// <para>
/// Delivery path: the tab status channel's MCP server (#248). BoosterAI leaves the note in tabN.notice, and the server adds it
/// to the result of the agent's next boosterai_status or boosterai_ask call, which agents make at every new step (#273). That
/// reaches a running claude or codex between two steps without typing into its terminal. Not chosen: typing into the terminal
/// (bs_pty, as auto-clear does), which is safe only into an idle claude and would start a new turn there, and a Claude Code
/// hook (additionalContext), which would serve claude only and needs a second mechanism beside the channel. agy, aider and the
/// other agents have no MCP server or instruction from BoosterAI, nothing that would read a note mid-session, so they are skipped
/// and the skip is recorded; so are tabs started with the channel off and Run once tabs (no MCP server there).
/// </para>
/// </summary>
public static class QuotaSaveNotices
{
    public const int DefaultPercent = 1;

    /// <summary>The session folder's record of every save notice sent or skipped, one line each.</summary>
    public const string LogFile = "quota-notices.log";

    public enum Route { Channel, NoSafePath, ChannelOff }

    /// <summary>How a note reaches the running CLI of a tab launched as <paramref name="launched"/> on <paramref name="track"/>.</summary>
    public static Route RouteFor(TabConfig launched, TabTrack track) =>
        !TabChannelAgent.Registers(launched.Llm) || StartCommands.RunsOnce(launched) ? Route.NoSafePath
        : !track.StatusChannel ? Route.ChannelOff
        : Route.Channel;

    /// <summary>
    /// The note to the agent, in English like the other prompts: short, neutral, a suggestion. <paramref name="week"/>: the weekly
    /// window, else the 5-hour one.
    /// </summary>
    public static string Message(string llm, bool week, double remainingPercent, DateTimeOffset resetsAt, DateTimeOffset now)
    {
        var left = remainingPercent.ToString("0.#", CultureInfo.InvariantCulture);
        var window = week ? "weekly" : "5-hour";
        return $"The {llm} account of this tab has about {left}% of its {window} quota left (it resets in {Span(resetsAt - now)}). "
            + "Work stops when it runs out. You may want to save your state now: commit and push what is complete and write down "
            + "the next steps. Whether and how is up to you.";
    }

    /// <summary>"2 h 5 min", "3 days 4 h", "less than a minute": a clock-free time, since the agent's time zone is unknown.</summary>
    public static string Span(TimeSpan span)
    {
        if (span < TimeSpan.FromMinutes(1)) return "less than a minute";
        if (span.TotalDays >= 1)
        {
            var days = (int)span.TotalDays;
            return (days == 1 ? "1 day" : $"{days} days") + (span.Hours > 0 ? $" {span.Hours} h" : "");
        }
        var hours = (int)span.TotalHours;
        return hours > 0 ? $"{hours} h" + (span.Minutes > 0 ? $" {span.Minutes} min" : "") : $"{span.Minutes} min";
    }
}

/// <summary>A save notice on a tab (#275): when it was sent or skipped (epoch-ms), the notice id, and why it was skipped.</summary>
public sealed record SaveNoticeMark(long At, string? NoticeId, string? Skipped);

public sealed partial class TabViewModel
{
    private SaveNoticeMark? _saveNotice;

    /// <summary>The save notice of this tab's running CLI; a new track (restart, new start) clears it.</summary>
    internal SaveNoticeMark? SaveNotice
    {
        get => _saveNotice;
        set
        {
            if (_saveNotice == value) return;
            _saveNotice = value;
            RaiseStatusTexts();
        }
    }

    /// <summary>The status tooltip's line about the save notice: sent, read by the agent (its notice-read event), or skipped and why.</summary>
    internal string SaveNoticeLine => _saveNotice is not { } n || _state is not TabState.Running ? ""
        : n.Skipped is { } why ? Loc.Format("quota.save.skipped", DayTimeOf(n.At), why)
        : Channel.NoticeRead == n.NoticeId && Channel.NoticeReadAt is { } read ? Loc.Format("quota.save.read", DayTimeOf(n.At), DayTimeOf(read))
        : Loc.Format("quota.save.sent", DayTimeOf(n.At));

    private void RaiseStatusTexts()
    {
        Raise(nameof(StatusTip));
        Raise(nameof(StatusActionName));
    }

    private static string DayTimeOf(long epochMs) => HumanTime.Day(DateTimeOffset.FromUnixTimeMilliseconds(epochMs), DateTimeOffset.UtcNow);
}

public sealed partial class MainViewModel
{
    private readonly QuotaWarnings _saveNoticeWindows = new(atThreshold: true);

    /// <summary>The clock of the save notices (epoch-ms on the rows); tests replace it.</summary>
    internal Func<DateTimeOffset> SaveNoticeClock { get; set; } = () => DateTimeOffset.UtcNow;

    /// <summary>
    /// A limits reading of an account that tabs use (#275): when a window has just reached the save-notice share or less, once per
    /// account and window, sends the note to every running tab and subtab on it. Independent of the low-quota warning. May run
    /// off the UI thread; the tabs are read and changed on it.
    /// </summary>
    private void CheckSaveNotice(string cli, string key, string? accountName, AccountLimits limits)
    {
        if (!_cfg.QuotaSaveNotice) return;
        var now = LimitsClock();
        var warnings = _saveNoticeWindows.Update(Loc.Display("llm", cli), cli + "\0" + key, accountName, limits, now, _cfg.QuotaSaveNoticePercent);
        if (warnings.Count == 0) return;
        Dispatcher.UIThread.Post(() => { foreach (var w in warnings) SendSaveNotice(cli, key, w, now); });
    }

    /// <summary>Sends one window's note to the running tabs of <paramref name="cli"/> on account <paramref name="key"/>; returns the rows that got it.</summary>
    internal IReadOnlyList<TabViewModel> SendSaveNotice(string cli, string key, QuotaWarning warning, DateTimeOffset now)
    {
        var week = warning.Window == Loc.Get("account.limit.week");
        var text = QuotaSaveNotices.Message(cli, week, warning.RemainingPercent, warning.ResetsAt, now);
        var at = SaveNoticeClock().ToUnixTimeMilliseconds();
        var noticeId = "quota-" + (week ? "week" : "session") + "-" + at.ToString(CultureInfo.InvariantCulture);
        var sent = new List<TabViewModel>();
        var skipped = new List<TabViewModel>();
        foreach (var tab in Tabs.ToList())
        {
            if (tab is not { IsRunning: true, LaunchedAs: { } launched, Session: { } session, Track: { } track }) continue;
            if (launched.Llm != cli || (launched.AccountDirectory ?? "") != key) continue;
            string? why = QuotaSaveNotices.RouteFor(launched, track) switch
            {
                QuotaSaveNotices.Route.NoSafePath => Loc.Format("quota.save.reason.agent", Loc.Display("llm", launched.Llm)),
                QuotaSaveNotices.Route.ChannelOff => Loc.Get("quota.save.reason.channel"),
                _ => null,
            };
            bool delivered;
            try { delivered = why is null && session.SendNotice(track.Id, noticeId, text); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { delivered = false; }
            if (why is null && !delivered) why = Loc.Get("quota.save.reason.failed");
            tab.SaveNotice = new SaveNoticeMark(at, why is null ? noticeId : null, why);
            (why is null ? sent : skipped).Add(tab);
            LogSaveNotice(session, track.Id, tab.Name, warning, why);
        }
        if (sent.Count == 0 && skipped.Count == 0) return sent;
        var who = warning.Account is { Length: > 0 } ? Loc.Format("quota.warn.who", warning.Agent, warning.Account) : warning.Agent;
        var head = who + ", " + warning.Window;
        var left = Loc.Format("account.limit.remaining", LimitPercent.Text(warning.RemainingPercent));
        static string Names(IEnumerable<TabViewModel> rows) => string.Join(", ", rows.Select(r => r.Name.Trim()));
        var status = sent.Count > 0 ? Loc.Format("quota.save.status", head, left, Names(sent)) : Loc.Format("quota.save.status.none", head, left);
        if (skipped.Count > 0) status += " " + Loc.Format("quota.save.status.skipped", Names(skipped));
        ShowStatus(status);
        // Like a new status text (#270): a system notification only while the main window is unfocused; once per window anyway.
        if (sent.Count > 0 && !MainWindowFocused && _cfg.Notifications) ShowNotification(Loc.Get("notify.quota.save"), status, false);
        return sent;
    }

    /// <summary>
    /// For --screenshot --fake-save-notice (after ShowSampleChannel): codex's 5 h window at 0.8 % left sends the note to the running
    /// codex rows through <see cref="SendSaveNotice"/>, and the first one's agent records it as read. No notification is shown.
    /// </summary>
    internal void ShowSampleSaveNotice()
    {
        var now = DateTimeOffset.UtcNow;
        SaveNoticeClock = () => now.AddMinutes(-3);
        var sent = SendSaveNotice("codex", "", new QuotaWarning("codex", null, Loc.Format("account.limit.hours", "5"), 0.8, now.AddMinutes(70)), now);
        if (sent.FirstOrDefault() is { Session: { } session, TrackId: { } id, SaveNotice.NoticeId: { } noticeId } first)
        {
            File.AppendAllText(Path.Combine(session.Directory, TabChannel.EventsFile(id)),
                $"{{\"v\":1,\"at\":{now.AddMinutes(-2).ToUnixTimeMilliseconds()},\"kind\":\"notice-read\",\"id\":\"{noticeId}\"}}\n");
            UpdateChannel(first, session.ReadChannel(id));
        }
    }

    /// <summary>Records a sent or skipped note in the session folder (<see cref="QuotaSaveNotices.LogFile"/>); a failed write is ignored.</summary>
    private static void LogSaveNotice(TabSession session, string id, string name, QuotaWarning warning, string? skipped)
    {
        var line = string.Create(CultureInfo.InvariantCulture,
            $"{DateTimeOffset.UtcNow:yyyy-MM-ddTHH:mm:ssZ} {id} {(skipped is null ? "sent" : "skipped")} agent={warning.Agent} window={warning.Window} left={warning.RemainingPercent} tab={name.Trim()}{(skipped is null ? "" : " reason=" + skipped)}\n");
        try { File.AppendAllText(Path.Combine(session.Directory, QuotaSaveNotices.LogFile), line); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
