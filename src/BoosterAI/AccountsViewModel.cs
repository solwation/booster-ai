using System.Collections.ObjectModel;

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
        _name = account?.Name ?? Loc.Get("account.default");
    }

    public bool IsDefault => Account is null;
    public bool CanEdit => !IsDefault;
    public string Directory => Account?.Directory ?? Accounts.DefaultDirectory(_owner.Cli);
    /// <summary>The account's name; a rename keeps the id and directory, so the tabs that use it keep it.</summary>
    public string Name
    {
        get => _name;
        set
        {
            if (Account is null || !Set(ref _name, value ?? "")) return;
            var trimmed = _name.Trim();
            if (trimmed.Length == 0 || trimmed == Account.Name) return;
            Account.Name = trimmed;
            _owner.Changed();
        }
    }
    public string UsageText => Loc.FormatCount("account.usage", _owner.UsedBy(Account));
    public string LoginName => Loc.Format("account.login.name", _name);
    public string RemoveName => Loc.Format("account.remove.name", _name);
    public void Login() => _owner.Login(this);
    public void Remove() => _owner.Remove(this);
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
        Raise(nameof(Summary));
        RefreshUsage();
    }

    public void Login(AccountRow row)
    {
        if (row.Account is { } account) _owner.LoginAccount(account);
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
    public void LoginAccount(AgentAccount account)
    {
        var cfg = new AppConfig
        {
            WslDistro = _cfg.WslDistro, LinuxTerminal = _cfg.LinuxTerminal, ZoomPercent = _cfg.ZoomPercent,
            Tabs = [new TabConfig { Name = Loc.Format("account.login.tab", account.Name), Directory = "~", Llm = Catalog.ShellOnly, Script = Accounts.LoginScript(account) }],
        };
        var error = Accounts.IsValidDirectory(account.Directory) ? Launcher.Launch(cfg) : Loc.Format("err.accountDirectory", account.Name, account.Directory);
        ShowLaunchOutcome(error, cfg.Tabs, () => LoginAccount(account), Loc.Format("account.login.started", account.Name, Accounts.LoginCommand(account.Agent)));
    }
}
