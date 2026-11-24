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
        if (!_owner.AccountStatusEnabled) return Task.CompletedTask;
        var started = new List<Task>();
        foreach (var row in Rows.ToList())
        {
            bool start;
            lock (_statusLock) start = _checking.Add(row.Key);
            if (start) started.Add(CheckAsync(row.Account, row.Key));
        }
        if (started.Count > 0) foreach (var row in Rows) row.RefreshStatus();
        return Task.WhenAll(started);
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
        lock (_statusLock) _status.Remove(row.Key);
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
