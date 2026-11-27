using System.Collections.ObjectModel;

namespace BoosterAI;

/// <summary>One low-quota warning (#211): which agent/account/window is nearly used up, how much is left and when it resets.</summary>
public sealed record QuotaWarning(string Agent, string? Account, string Window, double RemainingPercent, DateTimeOffset ResetsAt)
{
    /// <summary>Identity of the notice: a newer warning for the same agent, account and window replaces the older one.</summary>
    public string Key => Agent + "\0" + Account + "\0" + Window;

    /// <summary>"claude (Privat), 5 h: 4 % kvar, återställs om 1 h 10 min (15:30)" (#208 times).</summary>
    public string Text(DateTimeOffset now)
    {
        var who = Account is { Length: > 0 } ? Loc.Format("quota.warn.who", Agent, Account) : Agent;
        return Loc.Format("quota.warn.text", who, Window, Loc.Format("account.limit.remaining", LimitPercent.Text(RemainingPercent)),
            LimitText.Resets(ResetsAt, now));
    }
}

/// <summary>A quota that is back (#239): a window that warned (or was used up) has reset or risen to the threshold or above.</summary>
public sealed record QuotaRecovery(string Agent, string? Account, string Window, double RemainingPercent)
{
    /// <summary>"Nu har din claude (Privat) fått fler tokens: 5 h, 80 % kvar".</summary>
    public string Text()
    {
        var who = Account is { Length: > 0 } ? Loc.Format("quota.warn.who", Agent, Account) : Agent;
        return Loc.Format("quota.back.text", who, Window, Loc.Format("account.limit.remaining", LimitPercent.Text(RemainingPercent)));
    }
}

/// <summary>What one reading changed: windows that newly warn and windows that are back.</summary>
public sealed record QuotaChanges(IReadOnlyList<QuotaWarning> Warnings, IReadOnlyList<QuotaRecovery> Recoveries);

/// <summary>
/// Decides when a plan window crosses below the warning threshold (#211), pure and testable. A window warns once per agent, account and
/// window instance: the same reset time never warns again while it stays below the threshold. It re-arms when the quota is at or above
/// the threshold again or when the window has reset (a different reset time). An unknown or expired window changes nothing.
/// A window that has warned and then shows a reading at or above the threshold has recovered (#239), reported once; a clock-only reset
/// (an expired window) never counts until a reading confirms it.
/// </summary>
public sealed class QuotaWarnings(bool atThreshold = false)
{
    /// <summary>
    /// <paramref name="atThreshold"/>: a share left equal to the threshold already counts as below it (the save notice of #275 at
    /// "about 1 %", where readings in whole percent would otherwise jump from 1 % straight to 0 %).
    /// </summary>
    private readonly bool _atThreshold = atThreshold;

    public const int MinPercent = 1;
    public const int MaxPercent = 50;
    public const int DefaultPercent = 5;

    /// <summary>Reset times of one window that differ by less than this are the same window (readings round differently).</summary>
    private static readonly TimeSpan SameWindow = TimeSpan.FromMinutes(10);

    private readonly Dictionary<string, DateTimeOffset> _warned = [];
    private readonly object _lock = new();

    public static int Normalize(int percent) => Math.Clamp(percent, MinPercent, MaxPercent);

    /// <summary>
    /// A reading for one agent/account: the warnings that are new now, 5 h window first. <paramref name="account"/> is the account's name when
    /// the agent has several, else null. Remaining share is the displayed one (<see cref="LimitPercent"/>, one decimal below 3 %), so the text never contradicts the threshold.
    /// </summary>
    public IReadOnlyList<QuotaWarning> Update(string agent, string accountKey, string? account, AccountLimits limits, DateTimeOffset now, int thresholdPercent) =>
        Evaluate(agent, accountKey, account, limits, now, thresholdPercent).Warnings;

    /// <summary>Like <see cref="Update"/>, and also the windows that have recovered since they warned.</summary>
    public QuotaChanges Evaluate(string agent, string accountKey, string? account, AccountLimits limits, DateTimeOffset now, int thresholdPercent)
    {
        limits = limits.AsOf(now);
        var threshold = Normalize(thresholdPercent);
        var result = new List<QuotaWarning>();
        var back = new List<QuotaRecovery>();
        var session = Loc.Format("account.limit.hours", Loc.Number((limits.Session?.Minutes ?? 300) / 60.0, "0.#"));
        Check(agent, accountKey, account, session, limits.Session, threshold, result, back);
        Check(agent, accountKey, account, Loc.Get("account.limit.week"), limits.Week, threshold, result, back);
        return new QuotaChanges(result, back);
    }

    private void Check(string agent, string accountKey, string? account, string label, LimitWindow? window, int threshold, List<QuotaWarning> result, List<QuotaRecovery> back)
    {
        if (window?.ResetsAt is not { } resetsAt) return;
        var key = agent + "\0" + accountKey + "\0" + (label == Loc.Get("account.limit.week") ? "week" : "session");
        var remaining = LimitPercent.Remaining(window.UsedPercent);
        lock (_lock)
        {
            if (_atThreshold ? remaining > threshold : remaining >= threshold)
            {
                if (_warned.Remove(key)) back.Add(new QuotaRecovery(agent, account, label, remaining));
                return;
            }
            if (_warned.TryGetValue(key, out var seen) && (resetsAt - seen).Duration() < SameWindow) return;
            _warned[key] = resetsAt;
        }
        result.Add(new QuotaWarning(agent, account, label, remaining, resetsAt));
    }
}

/// <summary>A quota warning shown in the main window's non-modal bar until it is closed (#211).</summary>
public sealed class QuotaNotice(QuotaWarning warning, string text) : Observable
{
    public QuotaWarning Warning { get; } = warning;
    public string Text { get; } = text;
}

/// <summary>The open quota notices, newest last; one notice per agent/account/window and a few at most.</summary>
public sealed class QuotaNotices
{
    public const int Max = 4;
    public ObservableCollection<QuotaNotice> Items { get; } = [];

    public void Show(QuotaWarning warning, DateTimeOffset now)
    {
        var old = Items.FirstOrDefault(n => n.Warning.Key == warning.Key);
        if (old is not null) Items.Remove(old);
        Items.Add(new QuotaNotice(warning, warning.Text(now)));
        while (Items.Count > Max) Items.RemoveAt(0);
    }

    public void Close(QuotaNotice notice) => Items.Remove(notice);
}
