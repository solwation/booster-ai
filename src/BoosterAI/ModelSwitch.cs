using System.Globalization;
using Avalonia.Threading;

namespace BoosterAI;

/// <summary>The size class of a model (#279): what "an equivalent model" means across agents.</summary>
public enum ModelTier { Top, Mid, Small }

/// <summary>Whether a tab's agent is idle enough to be switched (#279): only Idle switches, Unknown never automatically.</summary>
public enum SwitchActivity { Idle, Working, Unknown }

/// <summary>One account an equivalent model could run on (#279), as BoosterAI last knew it.</summary>
/// <param name="AccountId">The tab's <see cref="TabConfig.Account"/> value: "" for the default account.</param>
/// <param name="Key">The quota key: the account's directory, "" for the default account.</param>
/// <param name="Name">The account's name when the agent has several, else null.</param>
/// <param name="LoggedIn">Not known to be logged out (see <see cref="MainViewModel.SwitchAccounts"/>).</param>
public sealed record SwitchAccount(string Cli, string AccountId, string Key, string? Name, bool Installed, bool LoggedIn, AccountLimits Limits);

/// <summary>A switch BoosterAI offers or makes (#279): from the exhausted account to an equivalent model on another one.</summary>
public sealed record SwitchPlan(string FromCli, string FromKey, string? FromName, DateTimeOffset ResetsAt, bool Week,
    SwitchAccount Target, string Model, double RemainingPercent)
{
    /// <summary>"claude (Work)" when the agent has several accounts, else "claude".</summary>
    public static string Who(string cli, string? name) =>
        name is { Length: > 0 } ? Loc.Format("quota.warn.who", Loc.Display("llm", cli), name) : Loc.Display("llm", cli);
    public string FromText => Who(FromCli, FromName);
    /// <summary>"codex · gpt-6.2-sol" or "claude (Work) · claude-opus-5-5".</summary>
    public string TargetText => Who(Target.Cli, Target.Name) + " · " + Model;
}

/// <summary>A switch offered to the user for one tab (#279); answered once.</summary>
public sealed record SwitchOffer(TabViewModel Tab, SwitchPlan Plan, DateTimeOffset Now)
{
    /// <summary>"Kvoten för claude är slut till i morgon 09:00. Byta Atlas till codex · gpt-6.2-sol, som har 80 % kvar?"</summary>
    public string Question => Loc.Format("switch.question", Plan.FromText, HumanTime.Day(Plan.ResetsAt, Now), Tab.Name.Trim(),
        Plan.TargetText, LimitPercent.Text(Plan.RemainingPercent));
}

/// <summary>What accepting a switch did: switched now, waits for idle, or needs a confirmation because the activity is unknown.</summary>
public enum SwitchResult { Switched, Waiting, ConfirmUnknown, Gone }

/// <summary>
/// Switching a running tab to an equivalent model with quota left when its account's quota is used up (#279).
/// <para>
/// When: a reading of the tab's account shows a window (5 h or week) 100 % used, no resets left (Codex's earned resets, #274; the
/// other agents have none) and the reset more than <see cref="MinWait"/> away. It looks at the account, not at whether the tab has
/// stopped: a Codex tab's stop on its quota is not visible in its log (#277).
/// </para>
/// <para>
/// Equivalence table, by the model id's family as the catalog (<see cref="Catalog.ModelsFor"/>, the built-in list plus what
/// <see cref="ModelDiscovery"/> found, #265) names them:
/// <list type="table">
/// <item><term>Top</term><description>claude-opus-*; codex *-sol, *-astra; agy gemini-*-neon-*, gemini-*-hydrogen-*, gemini-pro-*, claude-opus-*; the
/// Standard model of claude, codex and agy (their defaults are their largest models)</description></item>
/// <item><term>Mid</term><description>claude-sonnet-*; codex *-terra; agy gemini-*-carbon-*, gemini-*-argon-*, claude-sonnet-*, gpt-oss-*</description></item>
/// <item><term>Small</term><description>claude-haiku-*; codex *-luna; agy gemini-*-flash-*</description></item>
/// </list>
/// claude-fable-* (its own Fable window) and every other id have no tier: no switch is offered from or to them. The target model is
/// the first id of the same tier in the target agent's catalog order (the CLI's own priority first), e.g. claude-opus-5-5,
/// gpt-6.2-sol, gemini-4-neon-high for Top. Only claude, codex and agy can be targets: BoosterAI reads quota for those alone.
/// </para>
/// <para>
/// A target is installed, not known to be logged out, and has quota left in every window of its latest reading; the most left wins.
/// A tab is offered (or switched) at most once per quota window, and never back to an account it left while that account's window has
/// not reset, so two exhausted accounts never ping-pong. The switch only happens while the tab is idle (#279): a tab that works waits,
/// and an unknown activity is never switched automatically. No save prompt is sent first: #275 asked the agent at about 1 % left.
/// </para>
/// </summary>
public static class ModelSwitch
{
    /// <summary>A reset closer than this is worth waiting for: no switch is offered.</summary>
    public static readonly TimeSpan MinWait = TimeSpan.FromHours(2);

    /// <summary>The agents a switch can go to: those whose quota BoosterAI reads.</summary>
    public static readonly IReadOnlyList<string> Agents = ["claude", "codex", "agy"];

    /// <summary>The new agent's first prompt, in English like the other prompts. No ';' (see Launcher.TabScript).</summary>
    public const string ContinuationPrompt = "Continue where the previous agent left off. Read the repository state, recent commits and any notes "
        + "or issue comments it left before changing anything.";

    /// <summary>The tier of <paramref name="model"/> on <paramref name="cli"/>; null when it has no equivalent (see the table above).</summary>
    public static ModelTier? Tier(string cli, string model)
    {
        if (!Agents.Contains(cli)) return null;
        var m = model.Trim().ToLowerInvariant();
        if (m is "" or Catalog.StandardModel) return ModelTier.Top;
        if (m.StartsWith("claude-fable", StringComparison.Ordinal)) return null;
        if (m.StartsWith("claude-opus", StringComparison.Ordinal)) return ModelTier.Top;
        if (m.StartsWith("claude-sonnet", StringComparison.Ordinal)) return ModelTier.Mid;
        if (m.StartsWith("claude-haiku", StringComparison.Ordinal)) return ModelTier.Small;
        if (m.StartsWith("gpt-oss", StringComparison.Ordinal)) return ModelTier.Mid;
        if (m.StartsWith("gpt-", StringComparison.Ordinal))
            return m.EndsWith("-sol", StringComparison.Ordinal) || m.EndsWith("-astra", StringComparison.Ordinal) ? ModelTier.Top
                : m.EndsWith("-terra", StringComparison.Ordinal) ? ModelTier.Mid
                : m.EndsWith("-luna", StringComparison.Ordinal) ? ModelTier.Small : null;
        if (m.StartsWith("gemini-", StringComparison.Ordinal))
            return m.Contains("-flash", StringComparison.Ordinal) ? ModelTier.Small
                : m.Contains("-neon", StringComparison.Ordinal) || m.Contains("-hydrogen", StringComparison.Ordinal) || m.StartsWith("gemini-pro", StringComparison.Ordinal) ? ModelTier.Top
                : m.Contains("-carbon", StringComparison.Ordinal) || m.Contains("-argon", StringComparison.Ordinal) ? ModelTier.Mid : null;
        return null;
    }

    /// <summary>The first model of <paramref name="tier"/> in <paramref name="cli"/>'s catalog; null when it offers none.</summary>
    public static string? ModelFor(string cli, ModelTier tier) =>
        Catalog.ModelsFor(cli).FirstOrDefault(m => m != Catalog.StandardModel && Tier(cli, m) == tier);

    /// <summary>The exhausted window that warrants a switch: 100 % used and resetting more than <see cref="MinWait"/> from now; the later one.</summary>
    public static (DateTimeOffset ResetsAt, bool Week)? Exhausted(AccountLimits limits, DateTimeOffset now)
    {
        limits = limits.AsOf(now);
        (DateTimeOffset, bool)? found = null;
        foreach (var (window, week) in new[] { (limits.Session, false), (limits.Week, true) })
            if (window is { UsedPercent: >= 100, ResetsAt: { } at } && at - now > MinWait && (found is null || at > found.Value.Item1))
                found = (at, week);
        return found;
    }

    /// <summary>The share left on an account's latest reading: the smallest of its known windows; null when none is known.</summary>
    public static double? Remaining(AccountLimits limits, DateTimeOffset now)
    {
        limits = limits.AsOf(now);
        var known = new[] { limits.Session, limits.Week }.OfType<LimitWindow>().ToList();
        return known.Count == 0 ? null : known.Min(w => LimitPercent.Remaining(w.UsedPercent));
    }

    /// <summary>
    /// The switch for a tab running <paramref name="model"/> on <paramref name="cli"/>/<paramref name="key"/> whose account is
    /// exhausted until <paramref name="resetsAt"/>: the candidate with the most quota left that offers a model of the same tier,
    /// excluding the account itself and <paramref name="avoid"/> (accounts the tab left, cli + "\0" + key). Null: no equivalent.
    /// </summary>
    public static SwitchPlan? Plan(string cli, string key, string? name, string model, DateTimeOffset resetsAt, bool week,
        IEnumerable<SwitchAccount> candidates, DateTimeOffset now, ISet<string>? avoid = null)
    {
        if (Tier(cli, model) is not { } tier) return null;
        SwitchPlan? best = null;
        foreach (var c in candidates)
        {
            if (c.Cli == cli && c.Key == key || avoid?.Contains(c.Cli + "\0" + c.Key) == true) continue;
            if (!c.Installed || !c.LoggedIn || !Agents.Contains(c.Cli)) continue;
            if (Remaining(c.Limits, now) is not { } left || left <= 0) continue;
            if (ModelFor(c.Cli, tier) is not { } target) continue;
            if (best is null || left > best.RemainingPercent) best = new SwitchPlan(cli, key, name, resetsAt, week, c, target, left);
        }
        return best;
    }

    /// <summary>
    /// Idle when the CLI's activity is idle, the agent's own status says blocked (it waits for an answer, even inside a tool call), or
    /// it says idle or done while the CLI is not working; Working when the CLI works or the status says working; otherwise Unknown
    /// (no hooks, no output timing and no status, as for some Codex tabs). A done or idle status is the latest since the CLI started
    /// and never ages, so it cannot outweigh a CLI that works on a new turn (#284).
    /// </summary>
    public static SwitchActivity ActivityOf(Activity activity, AgentStatus? status) =>
        activity == Activity.Idle || status?.State == AgentState.Blocked
            || activity != Activity.Working && status?.State is AgentState.Idle or AgentState.Done ? SwitchActivity.Idle
        : activity is Activity.Working or Activity.Busy || status?.State == AgentState.Working ? SwitchActivity.Working
        : SwitchActivity.Unknown;
}

public sealed partial class TabViewModel
{
    private SwitchPlan? _pendingSwitch;

    /// <summary>A switch waiting for the tab to become idle (#279), with the choices it replaced; null when none waits.</summary>
    internal SwitchPlan? PendingSwitch
    {
        get => _pendingSwitch;
        set { if (_pendingSwitch != value) { _pendingSwitch = value; Raise(nameof(StatusText)); RaiseStatusTexts(); } }
    }

    /// <summary>The choices before a switch (agent, account, model), so cancelling the wait gives them back.</summary>
    internal (string Llm, string Account, string Model)? SwitchedFrom { get; set; }

    /// <summary>The first prompt of the next in-place restart (#279): the continuation prompt after a switch; null for none.</summary>
    internal string? SwitchPrompt { get; set; }

    /// <summary>The switch was automatic: its restart posts a notification.</summary>
    internal SwitchPlan? AutoSwitch { get; set; }

    /// <summary>The current activity as a switch sees it.</summary>
    internal SwitchActivity SwitchActivity => ModelSwitch.ActivityOf(Activity, Channel.Status);

    /// <summary>"Byter till codex · gpt-6.2-sol när fliken är ledig" while a switch waits.</summary>
    internal string? PendingSwitchText => _pendingSwitch is { } p && PendingRestart ? Loc.Format("switch.waiting", p.TargetText) : null;
}

public sealed partial class MainViewModel
{
    /// <summary>Per tab, the quota windows a switch was offered or made for (#279): account + window → reset time.</summary>
    private readonly Dictionary<TabViewModel, Dictionary<string, DateTimeOffset>> _switchWindows = [];
    /// <summary>Per tab, the accounts it was switched away from and until when they are exhausted: never switched back before that.</summary>
    private readonly Dictionary<TabViewModel, Dictionary<string, DateTimeOffset>> _switchedAway = [];
    private readonly List<SwitchOffer> _switchOffers = [];

    /// <summary>The switch questions not yet answered, oldest first; the main window shows them while it has focus.</summary>
    internal IReadOnlyList<SwitchOffer> SwitchOffers => _switchOffers;

    /// <summary>Raised on the UI thread when a new switch question waits.</summary>
    internal event Action? SwitchOffered;

    /// <summary>The experimental automatic switch (#279), off by default; turning it on asks first (<see cref="RequestAutoModelSwitchAsync"/>).</summary>
    public bool AutoModelSwitch { get => _cfg.AutoModelSwitch; private set { _cfg.AutoModelSwitch = value; Raise(); } }

    /// <summary>
    /// The setting's checkbox: turning it on asks <paramref name="confirm"/> ("Detta kan förbruka dina tokens snabbare än planerat.
    /// Experimentell funktion.") and stays off on Avbryt; turning it off needs no question. Returns the new value.
    /// </summary>
    internal async Task<bool> RequestAutoModelSwitchAsync(bool on, Func<Task<bool>> confirm)
    {
        if (on && !_cfg.AutoModelSwitch && !await confirm()) { Raise(nameof(AutoModelSwitch)); return false; }
        AutoModelSwitch = on;
        return on;
    }

    /// <summary>
    /// Every account a switch could go to (#279): claude, codex and agy, installed per the last setup check, with each account's latest
    /// limits. An account a login check found logged out is left out; one whose login is unknown counts when it has a reading, since
    /// the agent itself reported it. Tests replace it.
    /// </summary>
    internal Func<IReadOnlyList<SwitchAccount>>? SwitchAccounts { get; set; }

    private IReadOnlyList<SwitchAccount> KnownSwitchAccounts()
    {
        var list = new List<SwitchAccount>();
        var status = Setup.Status;
        foreach (var cli in ModelSwitch.Agents)
        {
            var installed = status.Checked && status.Cli(cli)?.Installed == true;
            var accounts = Accounts.Supports(cli)
                ? new[] { ("", "", (string?)DefaultAccountName(cli)) }.Concat(AccountsFor(cli).Select(a => (a.Id, a.Directory, (string?)a.Name))).ToList()
                : [("", "", null)];
            foreach (var (id, key, name) in accounts)
            {
                var limits = Accounts.Supports(cli) ? AccountsEditor(cli)!.LimitsOf(key) : AgentLimitsOf(cli);
                var login = KnownLoginStatus(cli, key)?.State;
                var loggedIn = login == LoginState.LoggedIn || login != LoginState.LoggedOut && limits.At is not null;
                list.Add(new SwitchAccount(cli, id, key, accounts.Count > 1 ? name : null, installed, loggedIn, limits));
            }
        }
        return list;
    }

    /// <summary>
    /// A limits reading of an account tabs use (#279): when a window is used up with no resets and a long wait, offers each running
    /// tab on it an equivalent model elsewhere, or switches it at once with the experimental setting. May run off the UI thread.
    /// </summary>
    private void CheckModelSwitch(string cli, string key, string? accountName, AccountLimits limits)
    {
        var now = LimitsClock();
        if (ModelSwitch.Exhausted(limits, now) is not { } exhausted) return;
        // Codex's earned resets (#274): an account that still has one can be reset instead. The other agents have none.
        if (cli == "codex" && AccountsEditor(cli)?.ResetsOf(key) is { Available: > 0 }) return;
        Dispatcher.UIThread.Post(() => OfferModelSwitch(cli, key, accountName, exhausted.ResetsAt, exhausted.Week, now));
    }

    /// <summary>The offers or automatic switches for the running tabs on an exhausted account; returns the tabs handled.</summary>
    internal IReadOnlyList<TabViewModel> OfferModelSwitch(string cli, string key, string? accountName, DateTimeOffset resetsAt, bool week, DateTimeOffset now)
    {
        var handled = new List<TabViewModel>();
        IReadOnlyList<SwitchAccount>? candidates = null;
        foreach (var tab in Tabs.ToList())
        {
            if (tab is not { IsRunning: true, LaunchedAs: { } launched } || tab.IsChanged || tab.PendingRestart || tab.IsClosing || IsClosingAll) continue;
            if (launched.Llm != cli || (launched.AccountDirectory ?? "") != key || StartCommands.RunsOnce(launched)) continue;
            var windows = _switchWindows.TryGetValue(tab, out var w) ? w : _switchWindows[tab] = [];
            var windowKey = cli + "\0" + key + "\0" + (week ? "week" : "session");
            if (windows.TryGetValue(windowKey, out var seen) && (resetsAt - seen).Duration() < TimeSpan.FromMinutes(10)) continue;
            var avoid = _switchedAway.TryGetValue(tab, out var away)
                ? away.Where(p => p.Value > now).Select(p => p.Key).ToHashSet() : [];
            candidates ??= (SwitchAccounts ?? KnownSwitchAccounts)();
            if (ModelSwitch.Plan(cli, key, accountName, launched.Model, resetsAt, week, candidates, now, avoid) is not { } plan) continue;
            windows[windowKey] = resetsAt;
            handled.Add(tab);
            if (_cfg.AutoModelSwitch && tab.SwitchActivity != SwitchActivity.Unknown) { StartSwitch(tab, plan, automatic: true); continue; }
            // An unknown activity is never switched automatically: the user is asked instead.
            _switchOffers.Add(new SwitchOffer(tab, plan, now));
            if (!MainWindowFocused && _cfg.Notifications) ShowNotification(Loc.Get("notify.switch"), _switchOffers[^1].Question, false);
            SwitchOffered?.Invoke();
        }
        return handled;
    }

    /// <summary>Avbryt on a switch question: nothing changes, and the tab is not asked again for this quota window.</summary>
    internal void DeclineSwitch(SwitchOffer offer) => _switchOffers.Remove(offer);

    /// <summary>
    /// Byt on a switch question: an idle tab switches now, a working one waits for idle (its row says so, and ↻ cancels the wait), and
    /// one whose activity is unknown needs <paramref name="unknownConfirmed"/> ("Aktiviteten är okänd, byt ändå?").
    /// </summary>
    internal SwitchResult AcceptSwitch(SwitchOffer offer, bool unknownConfirmed = false)
    {
        var tab = offer.Tab;
        if (!Tabs.Contains(tab) || !tab.IsRunning || tab.IsClosing || IsClosingAll) { _switchOffers.Remove(offer); return SwitchResult.Gone; }
        if (tab.SwitchActivity == SwitchActivity.Unknown && !unknownConfirmed) return SwitchResult.ConfirmUnknown;
        _switchOffers.Remove(offer);
        return StartSwitch(tab, offer.Plan, automatic: false, force: unknownConfirmed);
    }

    /// <summary>Changes the tab's agent, account and model and restarts it now when idle (or <paramref name="force"/>), else when it is idle.</summary>
    private SwitchResult StartSwitch(TabViewModel tab, SwitchPlan plan, bool automatic, bool force = false)
    {
        tab.SwitchedFrom = (tab.Llm, tab.Account, tab.Model);
        tab.Llm = plan.Target.Cli;
        tab.Account = plan.Target.AccountId;
        tab.Model = plan.Model;
        tab.SwitchPrompt = ModelSwitch.ContinuationPrompt;
        tab.AutoSwitch = automatic ? plan : null;
        if (!_switchedAway.TryGetValue(tab, out var away)) _switchedAway[tab] = away = [];
        away[plan.FromCli + "\0" + plan.FromKey] = plan.ResetsAt;
        if (force || tab.SwitchActivity == SwitchActivity.Idle)
        {
            RestartChanged(tab);
            return SwitchResult.Switched;
        }
        tab.PendingRestart = true;
        tab.PendingSwitch = plan;
        Save();
        return SwitchResult.Waiting;
    }

    /// <summary>↻ on a tab whose switch waits: the wait ends and the earlier agent, account and model come back.</summary>
    internal void CancelSwitch(TabViewModel tab)
    {
        if (tab.PendingSwitch is null) return;
        tab.PendingRestart = false;
        tab.PendingSwitch = null;
        tab.SwitchPrompt = null;
        tab.AutoSwitch = null;
        if (tab.SwitchedFrom is { } from)
        {
            tab.Llm = from.Llm;
            tab.Account = from.Account;
            tab.Model = from.Model;
        }
        tab.SwitchedFrom = null;
        ShowStatus(Loc.Format("switch.cancelled", tab.Name.Trim()));
        Save();
    }

    /// <summary>A switch restarted its tab: says so, and an automatic one always posts a notification (also when BoosterAI has focus).</summary>
    private void SwitchRestarted(TabViewModel tab)
    {
        var auto = tab.AutoSwitch;
        tab.PendingSwitch = null;
        tab.SwitchPrompt = null;
        tab.AutoSwitch = null;
        tab.SwitchedFrom = null;
        if (auto is null) return;
        ShowNotification(Loc.Get("notify.switch.done"), Loc.Format("switch.auto", tab.Name.Trim(), auto.TargetText, auto.FromText,
            HumanTime.Day(auto.ResetsAt, LimitsClock())), false);
    }

    /// <summary>Screenshots (--window switch): a question for the first AI tab, codex's account exhausted until tomorrow.</summary>
    internal SwitchOffer SampleSwitchOffer()
    {
        var tab = Tabs.First(t => t.IsAi);
        var now = DateTimeOffset.Now;
        var reset = new DateTimeOffset(now.Year, now.Month, now.Day, 9, 0, 0, now.Offset).AddDays(1);
        var target = new SwitchAccount("codex", "", "", null, true, true, AccountLimits.Unknown);
        return new SwitchOffer(tab, new SwitchPlan("claude", "", "Privat", reset, true, target, "gpt-6.2-sol", 82), now);
    }
}

/// <summary>The texts of a two-button question window (#279): the switch question, its unknown-activity check and the setting's confirmation.</summary>
public sealed record ChoiceQuestion(string Title, string Heading, string Body, string Confirm, string Cancel)
{
    public bool HasBody => Body != "";

    public static ChoiceQuestion Switch(SwitchOffer offer) =>
        new(Loc.Get("switch.title"), offer.Question, Loc.Get("switch.body"), Loc.Get("switch.confirm"), Loc.Get("cancel"));

    public static ChoiceQuestion SwitchUnknown(SwitchOffer offer) =>
        new(Loc.Get("switch.title"), Loc.Get("switch.unknown"), Loc.Format("switch.unknown.body", offer.Tab.Name.Trim()), Loc.Get("switch.confirm"), Loc.Get("cancel"));

    public static ChoiceQuestion EnableAutoSwitch() =>
        new(Loc.Get("settings.autoSwitch.confirm.title"), Loc.Get("settings.autoSwitch.confirm.heading"), Loc.Get("settings.autoSwitch.confirm.body"),
            Loc.Get("settings.autoSwitch.confirm.on"), Loc.Get("cancel"));
}
