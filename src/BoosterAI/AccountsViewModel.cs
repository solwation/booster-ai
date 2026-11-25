using System.Collections.ObjectModel;
using Avalonia.Threading;

namespace BoosterAI;

/// <summary>One account on an agent's Accounts page in Settings (#186); the first row is the implicit default account.</summary>
public sealed class AccountRow : Observable
{
    private readonly AgentAccountsViewModel _owner;
    internal AgentAccount? Account { get; }
    private string _name;

    internal AccountRow(AgentAccountsViewModel owner, AgentAccount? account)
    {
        _owner = owner;
        Account = account;
        _name = account?.Name ?? owner.DefaultName;
    }

    /// <summary>Key of the account's login status: its directory, empty for the default account.</summary>
    internal string Key => Account?.Directory ?? "";

    public bool IsDefault => Account is null;
    public bool CanEdit => !IsDefault;
    public string Directory => Account?.Directory ?? Accounts.DefaultDirectory(_owner.Cli);
    /// <summary>The account's name; a rename keeps the id and directory, so the tabs that use it keep it.</summary>
    public string Name
    {
        get => _name;
        set
        {
            if (!Set(ref _name, value ?? "")) return;
            var trimmed = _name.Trim();
            if (trimmed.Length == 0) return;
            // The default account's name is stored apart from the accounts list (#192); naming it "Standard" gives the name back.
            if (Account is null) { _owner.RenameDefault(trimmed); return; }
            if (trimmed == Account.Name) return;
            Account.Name = trimmed;
            _owner.Changed();
        }
    }
    public string UsageText => Loc.FormatCount("account.usage", _owner.UsedBy(Account));
    public string LoginName => Loc.Format("account.login.name", _name);
    public string RemoveName => Loc.Format("account.remove.name", _name);
    /// <summary>The last known login status (#192); Unknown until a check has answered.</summary>
    public AccountLoginStatus Status => _owner.StatusOf(Key);
    public string StatusText => _owner.IsChecking(Key) && !_owner.HasStatus(Key) ? Loc.Get("account.status.checking") : Status.Text;
    /// <summary>Who is logged in, shown beside the name; empty when unknown or logged out.</summary>
    public string Identity => Status.Identity;
    public bool HasIdentity => Identity.Length > 0;
    public bool IsLoggedIn => Status.State == LoginState.LoggedIn;
    public bool IsLoggedOut => Status.State == LoginState.LoggedOut;
    /// <summary>"Logga in" until the account is logged in, then "Logga in igen".</summary>
    public string LoginText => Loc.Get(IsLoggedIn ? "account.login.again" : "account.login");
    public void Login() => _owner.Login(this);
    public void Remove() => _owner.Remove(this);
    /// <summary>The account's last read plan limits (#199), with windows that have reset since dropped; Unknown until a read answered.</summary>
    public AccountLimits Limits => _owner.LimitsOf(Key).AsOf(_owner.Now);
    /// <summary>"5 h": the short window's length when the agent gave one, otherwise the usual five hours.</summary>
    public string SessionLabel => Loc.Format("account.limit.hours", Loc.Number((Limits.Session?.Minutes ?? 300) / 60.0, "0.#"));
    public string WeekLabel => Loc.Get("account.limit.week");
    public bool HasSession => Limits.Session is not null;
    public bool HasWeek => Limits.Week is not null;
    /// <summary>The bar's value; a use above 100 % (Codex allows it) fills the bar.</summary>
    public double SessionPercent => Math.Clamp(Limits.Session?.UsedPercent ?? 0, 0, 100);
    public double WeekPercent => Math.Clamp(Limits.Week?.UsedPercent ?? 0, 0, 100);
    public string SessionText => LimitText.Used(Limits.Session, _owner.Now, Limits.SessionReset);
    public string WeekText => LimitText.Used(Limits.Week, _owner.Now, Limits.WeekReset);
    public LimitRow? FableLimit => Limits.Fable is { } fable ? LimitRow.For(Loc.Get("account.limit.fable"), fable, _owner.Now) : null;
    public bool HasFableLimit => FableLimit is not null;
    public string FableText => FableLimit is { } fable
        ? string.Join(" · ", new[] { fable.UsedText, fable.RemainingText, fable.ResetText }.Where(text => text.Length > 0)) : "";
    public string CreditsText => LimitText.Credits(Limits.Credits);
    public bool HasCredits => CreditsText.Length > 0;
    /// <summary>"avläst för 8 min sedan": when the agent reported the values; "Ingen avläsning än" before any.</summary>
    public string LimitsAge => Limits.At is { } at ? LimitText.Age(at, _owner.Now) : Loc.Get("account.limit.never");
    public string LimitsTip => Loc.Get("account.limit.tip." + _owner.Cli);

    internal void RefreshLimits()
    {
        foreach (var name in new[] { nameof(Limits), nameof(SessionLabel), nameof(HasSession), nameof(HasWeek), nameof(SessionPercent), nameof(WeekPercent),
            nameof(SessionText), nameof(WeekText), nameof(FableLimit), nameof(HasFableLimit), nameof(FableText), nameof(CreditsText), nameof(HasCredits), nameof(LimitsAge) }) Raise(name);
    }

    internal void RefreshStatus()
    {
        foreach (var name in new[] { nameof(Status), nameof(StatusText), nameof(Identity), nameof(HasIdentity), nameof(IsLoggedIn), nameof(IsLoggedOut), nameof(LoginText) }) Raise(name);
    }

    internal void Refresh()
    {
        Raise(nameof(UsageText));
        Raise(nameof(LoginName));
        Raise(nameof(RemoveName));
    }
}

/// <summary>
/// An agent's accounts in Settings (#186): add, rename, remove and log in. Only agents with a configuration directory
/// variable (<see cref="Accounts.Supports"/>) get one; the others only have their default account.
/// </summary>
public sealed class AgentAccountsViewModel : Observable
{
    private readonly MainViewModel _owner;
    private string _newName = "";
    public string Cli { get; }

    internal AgentAccountsViewModel(MainViewModel owner, string cli)
    {
        _owner = owner;
        Cli = cli;
        Reload();
    }

    public ObservableCollection<AccountRow> Rows { get; } = [];
    public string EnvVar => Accounts.EnvVar(Cli) ?? "";
    internal string DefaultName => _owner.DefaultAccountName(Cli);
    internal void RenameDefault(string name) { if (_owner.SetDefaultAccountName(Cli, name)) _owner.AccountsEdited(Cli); }

    private readonly Dictionary<string, AccountLoginStatus> _status = [];
    private readonly HashSet<string> _checking = [];
    // The app updates these on the UI thread; a check's continuation may run on another thread (tests), so access is locked.
    private readonly object _statusLock = new();
    internal AccountLoginStatus StatusOf(string key) { lock (_statusLock) return _status.GetValueOrDefault(key) ?? AccountLoginStatus.Unknown; }
    internal bool HasStatus(string key) { lock (_statusLock) return _status.ContainsKey(key); }
    internal bool IsChecking(string key) { lock (_statusLock) return _checking.Contains(key); }

    /// <summary>
    /// Checks every account's login in the background (#192): shown with the Accounts page and again when the window is activated.
    /// An account that is already being checked is skipped, so a trigger during a check never starts a second one. The returned
    /// task completes when the started checks have answered; the UI never waits for it.
    /// </summary>
    internal Task RefreshStatusAsync()
    {
        var limits = RefreshLimitsAsync();
        if (!_owner.AccountStatusEnabled) return limits;
        var started = new List<Task> { limits };
        foreach (var row in Rows.ToList())
        {
            bool start;
            lock (_statusLock) start = _checking.Add(row.Key);
            if (start) started.Add(CheckAsync(row.Account, row.Key));
        }
        if (started.Count > 0) foreach (var row in Rows) row.RefreshStatus();
        return Task.WhenAll(started);
    }

    private readonly Dictionary<string, AccountLimits> _limits = [];
    private readonly HashSet<string> _reading = [];
    internal AccountLimits LimitsOf(string key)
    {
        lock (_statusLock)
        {
            var limits = _limits.GetValueOrDefault(key) ?? AccountLimits.Unknown;
            return limits.Fable is not null && !_owner.ShowClaudeFableQuota ? limits with { Fable = null } : limits;
        }
    }
    internal bool HasLimits(string key) { lock (_statusLock) return _limits.ContainsKey(key); }
    internal DateTimeOffset Now => _owner.LimitsClock();

    /// <summary>
    /// Reads every account's plan limits in the background (#199), with the login checks: when the Accounts page shows and the
    /// window is activated. An account already being read is skipped.
    /// </summary>
    internal Task RefreshLimitsAsync()
    {
        if (!_owner.AccountLimitsEnabled) return Task.CompletedTask;
        var started = new List<Task>();
        foreach (var row in Rows.ToList())
        {
            bool start;
            lock (_statusLock) start = _reading.Add(row.Key);
            if (start) started.Add(ReadLimitsAsync(row.Account, row.Key));
        }
        return Task.WhenAll(started);
    }

    private async Task ReadLimitsAsync(AgentAccount? account, string key)
    {
        AccountLimits result;
        try { result = await _owner.ReadAccountLimitsAsync(Cli, account?.Directory); }
        catch (Exception e) when (e is not OutOfMemoryException) { result = AccountLimits.Unknown; }
        var keep = key == "" || Rows.Any(r => r.Key == key);
        lock (_statusLock)
        {
            _reading.Remove(key);
            if (keep) _limits[key] = result;
        }
        foreach (var row in Rows) row.RefreshLimits();
        _owner.AccountLimitsChanged(Cli);
        if (keep) _owner.CheckQuota(Cli, key, Rows.Count > 1 ? account?.Name ?? DefaultName : null, result);
    }

    /// <summary>
    /// Screenshots (--fake-limits): invented limits in row order (both windows, Unknown, only the week, near the limit, maxed
    /// yesterday, reset since); never reads a file.
    /// </summary>
    internal void ShowSampleLimits(string cli)
    {
        var now = Now;
        var hour = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, now.Offset);
        AccountLimits[] cycle =
        [
            new(new LimitWindow(37, hour.AddHours(3).AddMinutes(20), 300), new LimitWindow(62, hour.AddDays(3).AddHours(2), 10080),
                cli == "codex" ? new LimitCredits(true, false, "120") : null, hour.AddMinutes(-15))
                { Fable = cli == "claude" ? new LimitWindow(82.5, hour.AddDays(3).AddHours(2)) : null },
            AccountLimits.Unknown,
            new(null, new LimitWindow(8, hour.AddDays(5), 10080), cli == "codex" ? new LimitCredits(false, false, null) : null, hour.AddDays(-1).AddHours(2)),
            new(new LimitWindow(91, hour.AddHours(1).AddMinutes(40), 300), new LimitWindow(64, hour.AddDays(2), 10080), null, hour.AddMinutes(-4)),
            // #214: an account no tab uses, maxed at its last reading yesterday, and one whose windows have reset since.
            new(new LimitWindow(100, hour.AddHours(-20), 300), new LimitWindow(100, hour.AddDays(1).AddHours(3), 10080), null, hour.AddDays(-1).AddHours(-2)),
            new(new LimitWindow(97, hour.AddHours(-2), 300), new LimitWindow(70, hour.AddHours(-1), 10080), null, hour.AddDays(-2)),
        ];
        lock (_statusLock) for (var i = 0; i < Rows.Count; i++) _limits[Rows[i].Key] = cycle[i % cycle.Length];
        foreach (var row in Rows) row.RefreshLimits();
        _owner.AccountLimitsChanged(Cli);
    }

    /// <summary>Screenshots: invented statuses, in row order (logged in, logged out, unknown, then again); never runs an agent.</summary>
    internal void ShowSampleStatus(string cli)
    {
        var sample = cli == "codex" ? new AccountLoginStatus(LoginState.LoggedIn, null, "ChatGPT") : new AccountLoginStatus(LoginState.LoggedIn, "namn@example.com · Example AB");
        AccountLoginStatus[] cycle = [sample, AccountLoginStatus.LoggedOut, AccountLoginStatus.Unknown];
        lock (_statusLock) for (var i = 0; i < Rows.Count; i++) _status[Rows[i].Key] = cycle[i % cycle.Length];
        foreach (var row in Rows) row.RefreshStatus();
    }

    private async Task CheckAsync(AgentAccount? account, string key)
    {
        AccountLoginStatus result;
        try { result = await _owner.CheckAccountLoginAsync(Cli, account?.Directory); }
        catch (Exception e) when (e is not OutOfMemoryException) { result = AccountLoginStatus.Unknown; }
        // An account removed meanwhile keeps no status.
        var keep = key == "" || Rows.Any(r => r.Key == key);
        lock (_statusLock)
        {
            _checking.Remove(key);
            if (keep) _status[key] = result;
        }
        foreach (var row in Rows) row.RefreshStatus();
        _owner.AccountStatusChanged(Cli);
    }
    public string Explanation => Loc.Format("account.explanation", Loc.Display("llm", Cli), EnvVar, Accounts.DefaultDirectory(Cli));
    /// <summary>The overview card's line: how many accounts the agent has, the default one included.</summary>
    public string Summary => Loc.FormatCount("account.count", Rows.Count);

    public string NewName
    {
        get => _newName;
        set { if (Set(ref _newName, value ?? "")) Raise(nameof(CanAdd)); }
    }
    public bool CanAdd => _newName.Trim().Length > 0;

    internal void Reload()
    {
        Rows.Clear();
        Rows.Add(new AccountRow(this, null));
        foreach (var account in _owner.AccountsFor(Cli)) Rows.Add(new AccountRow(this, account));
        Raise(nameof(Summary));
    }

    internal int UsedBy(AgentAccount? account) => _owner.TabsUsingAccount(Cli, account?.Id ?? "");

    /// <summary>Adds an account named <see cref="NewName"/> with its own directory; true when added.</summary>
    public bool Add()
    {
        if (!CanAdd) return false;
        var account = _owner.AddAccount(Cli, _newName);
        Rows.Add(new AccountRow(this, account));
        NewName = "";
        Raise(nameof(Summary));
        return true;
    }

    /// <summary>Removes the account from BoosterAI; its directory and login stay on disk. Tabs that used it use the default account.</summary>
    public void Remove(AccountRow row)
    {
        if (row.Account is not { } account) return;
        _owner.RemoveAccount(account);
        Rows.Remove(row);
        lock (_statusLock) { _status.Remove(row.Key); _limits.Remove(row.Key); }
        Raise(nameof(Summary));
        RefreshUsage();
    }

    public void Login(AccountRow row)
    {
        if (row.Account is { } account) _owner.LoginAccount(account);
        else _owner.LoginDefaultAccount(Cli, row.Name);
    }

    internal void Changed() => _owner.AccountsEdited(Cli);

    internal void RefreshUsage()
    {
        foreach (var row in Rows) row.Refresh();
    }
}

public sealed partial class MainViewModel
{
    private readonly Dictionary<string, AgentAccountsViewModel> _accountEditors = [];

    /// <summary>The Accounts page of an agent in Settings; null for an agent without separate accounts.</summary>
    internal AgentAccountsViewModel? AccountsEditor(string cli)
    {
        if (!Accounts.Supports(cli)) return null;
        if (!_accountEditors.TryGetValue(cli, out var editor)) _accountEditors[cli] = editor = new AgentAccountsViewModel(this, cli);
        return editor;
    }

    /// <summary>Off until the real app starts its background checks; tests and screenshots never run an agent.</summary>
    internal bool AccountStatusEnabled { get; set; }
    internal Func<string, string, string?, Task<AccountLoginStatus>> CheckAccountLogin { get; set; } =
        (distro, llm, directory) => Task.Run(() => AccountStatusProbe.Check(distro, llm, directory));
    internal Task<AccountLoginStatus> CheckAccountLoginAsync(string llm, string? directory) =>
        directory is not null && !Accounts.IsValidDirectory(directory) ? Task.FromResult(AccountLoginStatus.Unknown) : CheckAccountLogin(_cfg.WslDistro, llm, directory);

    /// <summary>Off until the real app starts its background checks (#199); tests and screenshots never read an account's files.</summary>
    internal bool AccountLimitsEnabled { get; set; }
    internal Func<DateTimeOffset> LimitsClock { get; set; } = () => DateTimeOffset.Now;
    internal Func<string, string, string?, Task<AccountLimits>> ReadAccountLimits { get; set; } =
        (distro, llm, directory) => Task.Run(() => AccountLimitsProbe.Read(distro, llm, directory));
    private readonly ClaudeFableUsage _claudeFableUsage = new();
    internal bool ShowClaudeFableQuota => _cfg.ReadClaudeFableQuota || _sampleLimits;

    internal void ClaudeFableQuotaChanged()
    {
        if (_accountEditors.TryGetValue("claude", out var editor))
            foreach (var row in editor.Rows) row.RefreshLimits();
        AccountLimitsChanged("claude");
        if (_cfg.ReadClaudeFableQuota && AccountLimitsEnabled)
            _ = AccountsEditor("claude")!.RefreshLimitsAsync();
    }

    internal async Task<AccountLimits> ReadAccountLimitsAsync(string llm, string? directory)
    {
        if (directory is not null && !Accounts.IsValidDirectory(directory)) return AccountLimits.Unknown;
        var distro = _cfg.WslDistro;
        var shared = ReadAccountLimits(distro, llm, directory);
        if (llm != "claude" || !_cfg.ReadClaudeFableQuota) return await shared;
        var fable = _claudeFableUsage.ReadAsync(distro, directory, LimitsClock());
        var limits = await shared;
        return limits with { Fable = await fable };
    }

    private readonly Dictionary<string, AccountLimits> _agentLimits = [];
    private readonly HashSet<string> _readingAgents = [];
    private readonly object _agentLimitsLock = new();

    private AccountLimits AgentLimitsOf(string cli)
    {
        lock (_agentLimitsLock) return _agentLimits.GetValueOrDefault(cli) ?? AccountLimits.Unknown;
    }

    private bool _sampleLimits;

    private async Task RefreshSingleAgentLimitsAsync(string cli)
    {
        if (!AccountLimitsEnabled || !AccountLimitsProbe.Supports(cli)) return;
        lock (_agentLimitsLock) { if (!_readingAgents.Add(cli)) return; }
        AccountLimits result;
        try { result = await ReadAccountLimitsAsync(cli, null); }
        catch (Exception e) when (e is not OutOfMemoryException) { result = AccountLimits.Unknown; }
        lock (_agentLimitsLock)
        {
            _readingAgents.Remove(cli);
            _agentLimits[cli] = result;
        }
        AccountLimitsChanged(cli);
        CheckQuota(cli, "", null, result);
    }

    private readonly QuotaWarnings _quotaWarnings = new();

    /// <summary>
    /// A limits reading answered (#211): shows a non-modal notice for each plan window that has just crossed below the configured
    /// share left, once per window and account (see <see cref="QuotaWarnings"/>). May run off the UI thread.
    /// </summary>
    internal void CheckQuota(string cli, string key, string? accountName, AccountLimits limits)
    {
        if (!_cfg.QuotaWarn) return;
        var now = LimitsClock();
        var fresh = _quotaWarnings.Update(Loc.Display("llm", cli), cli + "\0" + key, accountName, limits, now, _cfg.QuotaWarnPercent);
        if (fresh.Count == 0) return;
        Dispatcher.UIThread.Post(() => { foreach (var w in fresh) Quota.Show(w, now); });
    }

    /// <summary>Screenshots (--fake-quota): two invented readings that just fell below the threshold, through the real check.</summary>
    internal void ShowSampleQuota()
    {
        var now = LimitsClock();
        var hour = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, now.Offset);
        CheckQuota("claude", "/work/a", "Privat", new(new LimitWindow(96, hour.AddHours(1).AddMinutes(40), 300), new LimitWindow(41, hour.AddDays(3), 10080), null, hour));
        CheckQuota("codex", "", null, new(new LimitWindow(30, hour.AddHours(2), 300), new LimitWindow(97, hour.AddDays(2).AddHours(3), 10080), null, hour));
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// The plan-limit indicator of an account or single-account agent in the Agent boxes (#200, #206), from the same reading
    /// the Accounts page shows (one per account/agent, never per row); null for agents without limits and while unknown.
    /// </summary>
    internal LimitIndicator? LimitIndicatorOf(string cli, string key, bool includeFable = false) =>
        Accounts.Supports(cli) && AccountsEditor(cli) is { } editor ? LimitIndicator.For(editor.LimitsOf(key), LimitsClock(), includeFable)
        : AccountLimitsProbe.Supports(cli) ? LimitIndicator.For(AgentLimitsOf(cli), LimitsClock())
        : null;

    /// <summary>
    /// The Agent list's "Okänd"/"Troligen återställd" with its reason (#214) for a limit-capable agent or account without a known level;
    /// null when a level is known, for agents without limits, and while limits are not read at all (tests, plain screenshots).
    /// </summary>
    internal LimitNote? LimitNoteOf(string cli, string key)
    {
        if (!AccountLimitsProbe.Supports(cli) || !AccountLimitsEnabled && !_sampleLimits) return null;
        var limits = Accounts.Supports(cli) && AccountsEditor(cli) is { } editor ? editor.LimitsOf(key) : AgentLimitsOf(cli);
        var now = LimitsClock();
        return LimitIndicator.For(limits, now) is null ? LimitIndicator.Note(limits, now, Loc.Get("account.limit.tip." + cli)) : null;
    }

    /// <summary>Screenshots (--fake-limits): invented limits for every limit-capable agent, also shown in the main window (#200, #206).</summary>
    internal void ShowSampleLimitsForAll()
    {
        _sampleLimits = true;
        foreach (var cli in Catalog.Llms.Where(Accounts.Supports)) AccountsEditor(cli)!.ShowSampleLimits(cli);
        var now = LimitsClock();
        var hour = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, now.Offset);
        lock (_agentLimitsLock)
            _agentLimits["agy"] = new(new LimitWindow(28, hour.AddHours(2).AddMinutes(45), 300),
                new LimitWindow(44, hour.AddDays(4).AddHours(1), 10080), null, hour.AddMinutes(-8));
        AccountLimitsChanged("agy");
    }

    private readonly Dictionary<string, string> _limitSignature = [];
    private readonly object _limitSignatureLock = new();

    /// <summary>A read answered: every tab's Agent box follows when what it shows changed (a reset window also changes it).</summary>
    internal void AccountLimitsChanged(string cli)
    {
        if (!AccountLimitsProbe.Supports(cli)) return;
        var keys = Accounts.Supports(cli) ? new[] { "" }.Concat(AccountsFor(cli).Select(a => a.Directory)) : new[] { "" };
        var signature = string.Join("|", keys.Select(k => LimitIndicatorOf(cli, k, includeFable: true) is { } i ? i.Caption + i.Warn + i.Tip + i.Stale
            : LimitNoteOf(cli, k) is { } n ? n.Caption + n.Tip : "-"));
        lock (_limitSignatureLock)
        {
            if (_limitSignature.TryGetValue(cli, out var old) && old == signature) return;
            _limitSignature[cli] = signature;
        }
        Dispatcher.UIThread.Post(() => { foreach (var tab in Tabs) tab.RaiseLlms(); });
    }

    private DateTimeOffset _limitsRefreshed = DateTimeOffset.MinValue;
    private DateTimeOffset _idleLimitsRefreshed = DateTimeOffset.MinValue;

    /// <summary>
    /// How often agents that no tab uses are read (#214). Their snapshot only changes when the agent runs elsewhere, so a slower pace
    /// keeps the extra shells few; the window's own reset is noticed on every refresh without a read.
    /// </summary>
    internal static readonly TimeSpan IdleLimitsInterval = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Reads the plan limits, one read per account/agent (#200, #205, #206): the agents the tab list uses on every refresh, and the
    /// other installed limit-capable agents with all their accounts every <see cref="IdleLimitsInterval"/> (#214), so the Agent list
    /// also knows accounts no tab uses. Before the first setup check every limit-capable agent counts as installed (a missing one
    /// reads as Unknown). The main window asks on activation and when monitored tab activity changes (at most every 5 seconds), and
    /// every 30 seconds while visible; <paramref name="force"/> skips the 5-second wait, not the idle interval.
    /// </summary>
    internal Task RefreshAccountLimitsAsync(bool force = false)
    {
        if (!AccountLimitsEnabled) return Task.CompletedTask;
        var now = LimitsClock();
        if (!force && now - _limitsRefreshed < TimeSpan.FromSeconds(5)) return Task.CompletedTask;
        _limitsRefreshed = now;
        var used = Tabs.Select(t => t.Llm).Distinct().Where(AccountLimitsProbe.Supports).ToList();
        var status = Setup.Status;
        var idle = Catalog.Llms.Where(c => AccountLimitsProbe.Supports(c) && !used.Contains(c) && (!status.Checked || status.Cli(c)?.Installed == true)).ToList();
        if (idle.Count > 0 && now - _idleLimitsRefreshed >= IdleLimitsInterval) _idleLimitsRefreshed = now;
        else idle.Clear();
        var reads = used.Concat(idle)
            .Select(c => Accounts.Supports(c) ? AccountsEditor(c)!.RefreshLimitsAsync() : RefreshSingleAgentLimitsAsync(c)).ToList();
        // Agents not read now still follow the clock: a window whose reset time passed shows "Troligen återställd".
        foreach (var cli in Catalog.Llms.Where(AccountLimitsProbe.Supports).Except(used).Except(idle)) AccountLimitsChanged(cli);
        return Task.WhenAll(reads);
    }

    /// <summary>An account's login status once a check in Settings has answered (#194); null while unknown to BoosterAI.</summary>
    internal AccountLoginStatus? KnownLoginStatus(string cli, string key) =>
        _accountEditors.TryGetValue(cli, out var editor) && editor.HasStatus(key) ? editor.StatusOf(key) : null;

    /// <summary>A check answered (possibly off the UI thread): the Agent boxes' account tooltips follow on the UI thread.</summary>
    internal void AccountStatusChanged(string cli) => Dispatcher.UIThread.Post(() =>
    {
        if (AccountsFor(cli).Count == 0) return; // no Agent box shows this agent's accounts
        foreach (var tab in Tabs) tab.RaiseLlms();
    });

    internal int TabsUsingAccount(string cli, string id) => Tabs.Count(t => t.Llm == cli && t.Account == id);

    internal AgentAccount AddAccount(string cli, string name)
    {
        var account = Accounts.Create(_cfg.Accounts, cli, name);
        _cfg.Accounts.Add(account);
        AccountsEdited(cli);
        ShowStatus(Loc.Format("account.added", account.Name, Loc.Display("llm", cli)));
        return account;
    }

    /// <summary>Tabs that used the account switch to the default account; running ones then show that they changed.</summary>
    internal void RemoveAccount(AgentAccount account)
    {
        _cfg.Accounts.Remove(account);
        var moved = Tabs.Where(t => t.Llm == account.Agent && t.Account == account.Id).ToList();
        foreach (var tab in moved) tab.Account = "";
        AccountsEdited(account.Agent);
        ShowStatus(moved.Count == 0 ? Loc.Format("account.removed", account.Name, account.Directory)
            : Loc.Format("account.removed.tabs", account.Name, account.Directory, Loc.FormatCount("account.tabs", moved.Count)));
    }

    /// <summary>Accounts changed in Settings: the rows' choices follow, the config is saved and Codex's metadata is read again.</summary>
    internal void AccountsEdited(string cli)
    {
        foreach (var tab in Tabs) tab.AccountsChanged();
        if (_accountEditors.TryGetValue(cli, out var editor)) editor.RefreshUsage();
        Save();
        if (cli == "codex" && _setupEnabled && Setup.Status.Cli("codex")?.Installed == true) _ = LoadCompactMetadataAsync(_cfg.WslDistro);
    }

    /// <summary>Opens a visible tab that runs the agent's login with the account's variable (#186), like the CLI installs.</summary>
    public void LoginAccount(AgentAccount account) =>
        OpenLoginTab(account.Agent, account.Name, Accounts.LoginScript(account),
            Accounts.IsValidDirectory(account.Directory) ? null : Loc.Format("err.accountDirectory", account.Name, account.Directory),
            () => LoginAccount(account));

    /// <summary>The same for the default account (#193): the agent's own directory, no variable.</summary>
    public void LoginDefaultAccount(string agent, string name) =>
        OpenLoginTab(agent, name, Accounts.DefaultLoginScript(agent, name), null, () => LoginDefaultAccount(agent, name));

    private void OpenLoginTab(string agent, string name, string script, string? invalid, Action retry)
    {
        var cfg = new AppConfig
        {
            WslDistro = _cfg.WslDistro, LinuxTerminal = _cfg.LinuxTerminal, ZoomPercent = _cfg.ZoomPercent,
            Tabs = [new TabConfig { Name = Loc.Format("account.login.tab", name).Replace(';', ','), Directory = "~", Llm = Catalog.ShellOnly, Script = script }],
        };
        var error = invalid ?? Launcher.Launch(cfg);
        ShowLaunchOutcome(error, cfg.Tabs, retry, Loc.Format("account.login.started", name, Accounts.LoginCommand(agent)));
    }
}
