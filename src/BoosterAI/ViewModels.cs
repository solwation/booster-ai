using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia.Media;
using Avalonia.Threading;

namespace BoosterAI;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }

    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed partial class TabViewModel : Observable
{
    private readonly MainViewModel _owner;
    public string Id { get; }
    public TabViewModel? Parent { get; internal set; }
    public bool IsSubTab => Parent is not null;
    public bool IsProject => Parent is null;
    private bool _expanded;
    /// <summary>Kept in the config for compatibility only (#196): subtabs are always shown, so this no longer hides anything.</summary>
    public bool Expanded { get => _expanded; set => Set(ref _expanded, value); }
    public bool CanRemoveSubTab => IsSubTab && !HasOpenTerminal && !IsAlive;
    /// <summary>The subtab's × and its menu item explain why they are inactive while its terminal tab is open (#196).</summary>
    public string RemoveSubTabTip => Loc.Get(CanRemoveSubTab ? "subtab.remove.tip" : "subtab.remove.blocked");
    public string RemoveSubTabName => Loc.Format("subtab.remove.name", Name);
    /// <summary>A disabled menu item cannot take focus, so its header carries the reason for screen readers.</summary>
    public string RemoveSubTabMenu => Loc.Get(CanRemoveSubTab ? "subtab.remove" : "subtab.remove.menu.blocked");
    /// <summary>The accessible name of the row's ⋯ (more options) button.</summary>
    public string MoreName => Loc.Format("row.more.name", Name);
    public string SubTabOfName => Parent is null ? "" : Loc.Format("subtab.of", Parent.Name);
    public bool CanRemoveProject => _owner.CanRemoveProject(this);
    public string ProjectRemovalHint => Loc.Get("project.remove.blocked");
    internal void GroupChanged() => Raise(nameof(CanRemoveProject));

    /// <summary>
    /// The group card (#196): a project with subtabs and its subtabs are drawn as one card, one segment per row. A project
    /// row with subtabs is the card's top, the last subtab its bottom; a project without subtabs is no group.
    /// </summary>
    public bool InGroup => IsSubTab || _owner.Tabs.Any(t => t.Parent == this);
    public bool GroupTop => IsProject && InGroup;
    public bool GroupBottom
    {
        get
        {
            if (Parent is not { } parent) return false;
            var index = _owner.Tabs.IndexOf(this);
            return index >= 0 && (index + 1 == _owner.Tabs.Count || _owner.Tabs[index + 1].Parent != parent);
        }
    }
    /// <summary>The group's colour: the project's, also on its subtab rows.</summary>
    public IBrush GroupColorBrush => (Parent ?? this).ColorBrush;

    private void RaiseRemoveSubTab()
    {
        Raise(nameof(CanRemoveSubTab));
        Raise(nameof(RemoveSubTabTip));
        Raise(nameof(RemoveSubTabMenu));
    }

    private string _name, _directory, _llm, _model;
    private int _colorIndex;
    private string _advisor = AdvisorPolicy.Disabled;
    private string _account = "";
    private bool _enabled, _orkester, _nonInteractive;
    private bool? _hasOrkester;

    /// <param name="index">The colour index when the config has none (ColorIndex -1 and no palette colour).</param>
    public TabViewModel(TabConfig c, MainViewModel owner, int index)
    {
        _owner = owner;
        Id = c.Id;
        _expanded = c.Expanded;
        _name = c.Name; _directory = c.Directory; _enabled = c.Enabled;
        _colorIndex = c.ColorIndex >= 0 ? c.ColorIndex : LegacyIndex(c.Color, index);
        _llm = Catalog.Llms.Contains(c.Llm) ? c.Llm : Catalog.DefaultLlm;
        _model = string.IsNullOrWhiteSpace(c.Model) ? Catalog.StandardModel : c.Model;
        _advisor = AdvisorPolicy.Normalize(c.Advisor);
        _advisorPinned = _advisor != AdvisorPolicy.Disabled;
        _orkester = c.Orkester;
        _nonInteractive = c.NonInteractive;
        _account = c.Account ?? "";
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(Name) or nameof(Llm) or nameof(StatusText) or nameof(Account)) Raise(nameof(PeekHeader));
            if (e.PropertyName is nameof(StatusText)) { Raise(nameof(StatusDisplay)); RaiseStatusIndicator(); }
            if (e.PropertyName is nameof(StatusText) or nameof(StatusTip)) { Raise(nameof(PeekDetail)); Raise(nameof(HasPeekDetail)); }
        };
    }

    public string Name
    {
        get => _name;
        set
        {
            if (!Set(ref _name, value)) return;
            Raise(nameof(StatusActionName));
            Raise(nameof(OpenTip));
            Raise(nameof(ContinueName));
            Raise(nameof(StartName));
            Raise(nameof(ShowTerminalHelp));
            Raise(nameof(ReorderName));
            Raise(nameof(MoreName));
            Raise(nameof(RemoveSubTabName));
            RaiseChannel();
            foreach (var child in _owner.Tabs.Where(t => t.Parent == this)) child.Raise(nameof(SubTabOfName));
            _owner.RaiseLaunchTip();
        }
    }

    /// <summary>Tooltip and accessible name of the row's Start checkbox: what ticking it means, or why a running row is left alone (#151).</summary>
    public string OpenTip => Loc.Format(IsAlive ? "tab.open.alive" : "tab.open.tip", Name);

    /// <summary>A ticked row that is not running is the only kind Start opens; this is the one the accent fill marks.</summary>
    public bool WillStart => Enabled && !IsAlive;
    public bool Orkester
    {
        get => _orkester;
        set { if (Set(ref _orkester, value)) { Raise(nameof(CanNonInteractive)); Raise(nameof(OnceChecked)); Raise(nameof(OrchestraMode)); RaiseRestart(); } }
    }

    public bool NonInteractive { get => _nonInteractive; set { if (Set(ref _nonInteractive, value)) { Raise(nameof(OnceChecked)); Raise(nameof(OrchestraMode)); } } }

    /// <summary>The row's orchestra choices (#152), in <see cref="OrchestraMode"/> order: Off · Orchestra · Orchestra, once.</summary>
    public static IReadOnlyList<string> OrchestraModes => [Loc.Get("orkester.mode.off"), Loc.Get("orkester.mode.on"), Loc.Get("orkester.mode.once")];

    /// <summary>
    /// One control for the two saved settings (#152): 0 = Orkester off, 1 = on, 2 = on with NonInteractive.
    /// Off keeps a stored NonInteractive (ignored while off, as before); choosing 1 or 2 sets it explicitly.
    /// </summary>
    public int OrchestraMode
    {
        get => !_orkester ? 0 : _nonInteractive ? 2 : 1;
        set
        {
            if (value is < 0 or > 2 || value == OrchestraMode) return;
            if (value > 0) NonInteractive = value == 2;
            Orkester = value > 0;
        }
    }

    /// <summary>What the toggle shows: a stored choice looks off while it cannot apply (orchestra off).</summary>
    public bool OnceChecked { get => _nonInteractive && _orkester; set => NonInteractive = value; }

    /// <summary>One-shot mode applies to the orchestra command only, so it needs the orchestra on.</summary>
    public bool CanNonInteractive => _orkester && CanOrkester;
    /// <summary>Per-project <c>--advisor</c> for claude (#170); a subtab uses its project's.</summary>
    public string Advisor
    {
        get => Parent?.Advisor ?? _advisor;
        set
        {
            if (IsSubTab) return;
            var v = AdvisorPolicy.Normalize(value);
            if (Set(ref _advisor, v)) { Raise(nameof(AdvisorChoice)); RaiseAdvisorWarning(); foreach (var t in _owner.Tabs.Where(t => t.Parent == this)) t.RaiseAdvisor(); ChoiceChanged(); }
        }
    }
    /// <summary>The localized editor text is separate from the persisted advisor ID.</summary>
    public string AdvisorChoice
    {
        get => Advisor == AdvisorPolicy.Disabled ? Loc.Get("advisor.off") : Advisor;
        set => Advisor = value == Loc.Get("advisor.off") ? AdvisorPolicy.Disabled : value;
    }
    internal void RaiseAdvisor() { Raise(nameof(Advisor)); Raise(nameof(AdvisorChoice)); ChoiceChanged(); }
    private void AdvisorInputsChanged() => (Parent ?? this).RaiseAdvisorWarning();
    /// <summary>The Advisor choice is shown for a claude project only.</summary>
    public bool CanAdvisor => _llm == "claude";
    // A chosen advisor keeps its row while the main window's Advisor switch is off, so an active choice is never hidden (#180).
    // Fixed at load and whenever the switch turns off, so clearing the field while editing does not hide the row mid-edit.
    private bool _advisorPinned;
    internal void RaiseAdvisorVisible() => Raise(nameof(AdvisorVisible));
    internal void PinAdvisor() { _advisorPinned = IsProject && Advisor != AdvisorPolicy.Disabled; Raise(nameof(AdvisorVisible)); }
    /// <summary>The Advisor row under a claude project: when the main window's switch is on, or this project has an advisor chosen.</summary>
    public bool AdvisorVisible => IsProject && CanAdvisor && (_owner.ShowAdvisor || _advisorPinned);
    public IReadOnlyList<string> Advisors => AdvisorPolicy.Choices.Select(a => a == AdvisorPolicy.Disabled ? Loc.Get("advisor.off") : a).ToArray();
    /// <summary>Why the chosen advisor is not passed (#172), for this project or any of its subtabs; null when it is fine.</summary>
    public string? AdvisorWarning
    {
        get
        {
            if (IsSubTab) return null;
            foreach (var t in new[] { this }.Concat(_owner.Tabs.Where(t => t.Parent == this)))
            {
                var key = AdvisorPolicy.Evaluate(t.Llm, t.Model, Advisor) switch
                {
                    AdvisorPolicy.Verdict.NotAdvisorModel => "advisor.warn.model",
                    AdvisorPolicy.Verdict.ExpensiveWorker => "advisor.warn.expensive",
                    AdvisorPolicy.Verdict.UnknownWorker => "advisor.warn.unknown",
                    _ => null,
                };
                if (key is not null) return Loc.Get(key);
            }
            return null;
        }
    }
    public bool HasAdvisorWarning => AdvisorWarning is not null;
    internal void RaiseAdvisorWarning() { Raise(nameof(AdvisorWarning)); Raise(nameof(HasAdvisorWarning)); }
    public string Model
    {
        get => _model;
        set
        {
            var usedFableQuota = UsesFableQuota;
            if (!Set(ref _model, value?.Trim() ?? "")) return;
            AdvisorInputsChanged();
            if (usedFableQuota != UsesFableQuota) AccountQuotaChoiceChanged();
            ChoiceChanged();
        }
    }

    internal bool UsesFableQuota => _llm == "claude" && ClaudeFableUsage.IsFableModel(_model);
    internal string? QuotaAccountKey => _account == "" ? "" : Accounts.Find(_owner.AccountsFor(_llm), _llm, _account)?.Directory;

    private void AccountQuotaChoiceChanged() => Dispatcher.UIThread.Post(() =>
    {
        // Apply the final agent, model and account after the ComboBox has finished committing its selection.
        RaiseLlms();
        _owner.RefreshFableQuota(this);
    });

    /// <summary>
    /// The tab's account (#186): an <see cref="AgentAccount.Id"/> of this agent, or "" for its default account. Saved per tab, so
    /// a project and each of its subtabs keep their own choice. An id that is no longer configured stays saved (the launch then
    /// refuses the tab) instead of quietly switching to another login.
    /// </summary>
    public string Account
    {
        get => _account;
        set
        {
            if (!Set(ref _account, value ?? "")) return;
            Raise(nameof(SelectedAgentChoice));
            Raise(nameof(AccountLabel));
            Raise(nameof(HasAccount));
            // Only a missing account's own entry depends on the choice; refresh the list after the selection commits.
            AccountQuotaChoiceChanged();
            ChoiceChanged();
        }
    }

    /// <summary>
    /// The Agent box (#194): one entry per agent and account, grouped per agent in <see cref="Llms"/> order. An agent with
    /// accounts besides its default one lists the default account first (with its name, #192), then its accounts in Settings
    /// order, all with the account shown; any other agent has one entry without it. A saved account that no longer exists
    /// keeps an entry of its own, so the choice stays visible instead of quietly switching login.
    /// </summary>
    public IReadOnlyList<AgentChoice> AgentChoices
    {
        get
        {
            var list = new List<AgentChoice>();
            foreach (var llm in Llms)
            {
                var accounts = _owner.AccountsFor(llm);
                var includeFable = llm == "claude" && UsesFableQuota;
                var missing = llm == _llm && _account != "" && accounts.All(a => a.Id != _account);
                if (accounts.Count == 0 && !missing)
                {
                    list.Add(new(llm, "", "", false, null, _owner.LimitIndicatorOf(llm, "", includeFable), _owner.LimitNoteOf(llm, "")));
                    continue;
                }
                list.Add(new(llm, "", _owner.DefaultAccountName(llm), true, AccountTip(llm, _owner.DefaultAccountName(llm), Accounts.DefaultDirectory(llm), ""), _owner.LimitIndicatorOf(llm, "", includeFable), _owner.LimitNoteOf(llm, "")));
                list.AddRange(accounts.Select(a => new AgentChoice(llm, a.Id, a.Name, true, AccountTip(llm, a.Name, a.Directory, a.Directory),
                    _owner.LimitIndicatorOf(llm, a.Directory, includeFable), _owner.LimitNoteOf(llm, a.Directory))));
                if (missing) list.Add(new(llm, _account, Loc.Format("account.missing", _account), true, Loc.Format("account.missing", _account)));
            }
            return list;
        }
    }

    /// <summary>The account's name, directory and, when a check in Settings has answered, its login status.</summary>
    private string AccountTip(string llm, string name, string directory, string statusKey)
    {
        var tip = Loc.Format("agent.account.tip", name, directory);
        return _owner.KnownLoginStatus(llm, statusKey) is { } status ? tip + "\n" + status.Text : tip;
    }

    /// <summary>The entry for the tab's agent and account; choosing one sets <see cref="Llm"/> and then <see cref="Account"/>.</summary>
    public AgentChoice? SelectedAgentChoice
    {
        get
        {
            var choices = AgentChoices;
            return choices.FirstOrDefault(c => c.Llm == _llm && c.AccountId == _account) ?? choices.FirstOrDefault(c => c.Llm == _llm);
        }
        set
        {
            if (value is null) return;
            // An agent change resets the account to the default one (see Llm); the entry then names the account to use.
            Llm = value.Llm;
            Account = value.AccountId;
            Raise(nameof(SelectedAgentChoice));
        }
    }

    /// <summary>The chosen account's name; empty for the default account, so only a non-default login is called out.</summary>
    public string AccountLabel => _account == "" ? (_owner.HasDefaultAccountName(_llm) ? _owner.DefaultAccountName(_llm) : "")
        : Accounts.Find(_owner.AccountsFor(_llm), _llm, _account)?.Name ?? _account;
    public bool HasAccount => AccountLabel != "";
    internal void AccountsChanged()
    {
        foreach (var n in new[] { nameof(SelectedAgentChoice), nameof(AccountLabel), nameof(HasAccount), nameof(PeekHeader) }) Raise(n);
        // Replacing the Agent box's items while it commits a selection re-enters it; refresh them afterwards (see Llm).
        AccountQuotaChoiceChanged();
        ChoiceChanged();
    }

    public string Directory
    {
        get => Parent?.Directory ?? _directory;
        set
        {
            if (IsSubTab || !Set(ref _directory, value)) return;
            _owner.CheckOrkester(this);
            foreach (var child in _owner.Tabs.Where(t => t.Parent == this))
            {
                child.Raise(nameof(Directory));
                _owner.CheckOrkester(child);
            }
        }
    }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (!Set(ref _enabled, value)) return;
            Raise(nameof(RowOpacity));
            Raise(nameof(WillStart));
            _owner.TabsChanged();
        }
    }

    /// <summary>Unticked tabs stay editable but recede.</summary>
    public double RowOpacity => _enabled ? 1 : 0.45;

    /// <summary>Whether the directory has the shared orchestra; null until checked.</summary>
    public bool? HasOrkester
    {
        get => _hasOrkester;
        set
        {
            if (!Set(ref _hasOrkester, value)) return;
            Raise(nameof(CanOrkester));
            Raise(nameof(CanInstallOrkester));
            Raise(nameof(CanNonInteractive));
            RaiseRestart();
            _owner.OrchestraColumnChanged();
        }
    }

    public bool CanOrkester => _hasOrkester == true && IsAi;

    /// <summary>What the "Installera" link will do, for the confirmation dialog.</summary>
    public string InstallSummary => Loc.Format("confirm.summary", Llm,
        Model == Catalog.StandardModel ? Loc.Get("confirm.summary.standardModel") : Loc.Format("confirm.summary.model", Model));
    public string InstallTitle => Loc.Format("confirm.heading", Name);
    public string InstallButton => Loc.Format("confirm.button", Loc.Display("llm", Llm));
    /// <summary>The install link shows on the project row only: subtabs share its folder, so one install covers them (#197).</summary>
    public bool CanInstallOrkester => _hasOrkester == false && IsAi && IsProject;

    public string Color => _owner.Theme.ColorAt(_colorIndex);
    internal int ColorIndex => _colorIndex;
    public IBrush ColorBrush => new SolidColorBrush(Avalonia.Media.Color.Parse(Color));

    /// <summary>Called when the theme changes.</summary>
    public void RaiseColor()
    {
        Raise(nameof(Color));
        Raise(nameof(ColorBrush));
        Raise(nameof(GroupColorBrush));
        if (IsProject) foreach (var child in _owner.Tabs.Where(t => t.Parent == this)) child.Raise(nameof(GroupColorBrush));
    }

    private static int LegacyIndex(string color, int fallback)
    {
        var i = Catalog.Themes[0].Colors.ToList().FindIndex(p => string.Equals(p, color, StringComparison.OrdinalIgnoreCase));
        return i >= 0 ? i : fallback;
    }

    /// <summary>Example shown in an empty directory field.</summary>
    public string DirectoryHint => Loc.Get(OperatingSystem.IsWindows() ? "dir.hint" : "dir.hint.unix");
    public IReadOnlyList<string> Llms
    {
        get
        {
            var status = _owner.Setup.Status;
            if (!status.Checked) return Catalog.Llms;
            var list = status.Clis.Where(c => c.Installed).Select(c => c.Cli).ToList();
            if (!list.Contains(_llm) && _llm != Catalog.ShellOnly)
                list.Add(_llm);
            list.Add(Catalog.ShellOnly);
            return list;
        }
    }
    public void RaiseLlms()
    {
        Raise(nameof(Llms));
        Raise(nameof(AgentChoices));
        Raise(nameof(SelectedAgentChoice));
        foreach (var n in new[] { nameof(HasPeekLimit), nameof(PeekLimitWarn), nameof(PeekLimitRows), nameof(PeekLimitAge), nameof(HasPeekLimitAge), nameof(PeekLimitName), nameof(StatusDisplay), nameof(PeekLimitMissing), nameof(HasPeekLimitMissing) }) Raise(n);
        RaiseStatusIndicator();
    }
    public IReadOnlyList<string> Models => Catalog.ModelsFor(_llm);
    public bool IsAi => _llm != Catalog.ShellOnly;

    public string Llm
    {
        get => _llm;
        set
        {
            if (value is null || !Set(ref _llm, value)) return;
            // Replacing ItemsSource during ComboBox selection re-enters its binding and restores the old item.
            // Refresh after the selection commits, including removal of a previously selected missing CLI.
            Dispatcher.UIThread.Post(RaiseLlms);
            Raise(nameof(Models));
            Raise(nameof(IsAi));
            Raise(nameof(CanAdvisor));
            Raise(nameof(AdvisorVisible));
            _owner.AdvisorSwitchChanged();
            AdvisorInputsChanged();
            Raise(nameof(CanOrkester));
            Raise(nameof(CanNonInteractive));
            Raise(nameof(CanInstallOrkester));
            _owner.OrchestraColumnChanged();
            _owner.SetupRowsChanged();
            // Another CLI's model id rarely fits; fall back to its default. An account belongs to one agent (#186).
            if (!Catalog.ModelsFor(value).Contains(Model)) Model = Catalog.StandardModel;
            if (_account != "" && _owner.AccountsFor(value).All(a => a.Id != _account)) Account = "";
            AccountsChanged();
            ChoiceChanged();
        }
    }

    public void NextColor()
    {
        _colorIndex = (_colorIndex + 1) % _owner.Theme.Colors.Count;
        RaiseColor();
    }

    /// <summary>
    /// The saved choice keeps Orkester even while the directory lacks the orchestra;
    /// a launch only sends /orkester where it exists.
    /// </summary>
    // Monitoring: set once the tab has been started from this window.
    private TabState? _state;
    private int? _rc;
    private Activity _activity;
    private long? _watchingSince;
    private long? _clearedAt;
    private bool _pending;
    private IReadOnlyList<string> _denied = [];
    internal TabConfig? LaunchedAs { get; set; }
    /// <summary>The session the tab's log is in; an earlier BoosterAI run's for a tab found running at start.</summary>
    internal TabSession? Session { get; set; }
    private TabTrack? _track;
    internal TabTrack? Track
    {
        get => _track;
        set
        {
            if (!Set(ref _track, value)) return;
            // A new track is a new events file (#249): the old tab's status and questions are gone with it.
            SetChannel(TabChannelState.Empty);
            RaiseRemoveSubTab();
            (Parent ?? this).GroupChanged();
        }
    }
    internal string? TrackId => Track?.Id;
    internal DateTime LaunchedAt { get; set; }
    /// <summary>The tab's shell, from its log; what an in-place restart stops the foreground of.</summary>
    internal int? ShellPid { get; set; }
    internal void RaiseRestartTip() => RaiseRestart();

    /// <summary>Screenshots only: back to never opened (no status, no launch).</summary>
    internal void ResetSampleState()
    {
        LaunchedAs = null;
        _state = null;
        foreach (var n in new[] { nameof(HasStatus), nameof(IsRunning), nameof(IsAlive), nameof(OpenTip), nameof(WillStart), nameof(StatusText), nameof(StatusBrush) }) Raise(n);
        RaiseRestart();
    }

    public bool HasStatus => _state is not null;
    internal bool HasOpenTerminal => Track is not null && _state is not null and not (TabState.Closed or TabState.Lost);
    public bool IsRunning => _state is TabState.Running;
    /// <summary>The tab has a terminal tab to switch to (#213): its shell lives, even when the CLI in it has ended.</summary>
    public bool CanShowTerminal => TerminalFocus.Supported && CanShowTerminalState;
    internal bool CanShowTerminalState => _state is TabState.Starting or TabState.Running or TabState.CliExited or TabState.CliMissing;
    /// <summary>Whether this platform offers the row menu's Show in terminal at all (#253); live rows enable it.</summary>
    public static bool ShowTerminalOffered => TerminalFocus.Supported;
    /// <summary>The grip's help text while the row shows its terminal tab on a click or Enter (#253), for screen readers.</summary>
    public string? ShowTerminalHelp => CanShowTerminal ? Loc.Format("tab.show.help", Name) : null;
    /// <summary>The wt.exe command that switches to this tab's terminal tab, or null when it cannot be built.</summary>
    internal TerminalFocus.Command? ShowTerminalCommand => _owner.FocusCommand(this);
    /// <summary>Started or running: Start leaves it alone rather than opening a duplicate.</summary>
    public bool IsAlive => _state is TabState.Running or TabState.Starting;

    /// <summary>
    /// The CLI is working on a turn; a one-shot run is busy until it exits, except while it watches (a turn
    /// ended, the CLI lives on and has written nothing since).
    /// </summary>
    public Activity Activity => LaunchedAs is { NonInteractive: true, Orkester: true } && IsRunning && _activity != Activity.Watching
        ? Activity.Busy : _activity;

    /// <summary>The LLM or model differs from what the running tab was started with.</summary>
    public bool IsChanged => IsRunning && LaunchedAs is { } l && l.Llm != Catalog.ShellOnly
        && (l.Llm != Llm || l.Model != (string.IsNullOrWhiteSpace(Model) ? Catalog.StandardModel : Model)
            // A pre-upgrade Standard launch may still inherit an advisor until it is restarted.
            || (Llm == "claude" && l.Advisor != Advisor)
            || l.AccountDirectory != AccountDirectory
            || l.CompactTokens != _owner.CompactTokens(Llm, Model, Orkester && CanOrkester, AccountDirectory));
    /// <summary>The chosen account's directory as a launch passes it; null for the default account.</summary>
    private string? AccountDirectory => _owner.AccountDirectory(_llm, _account);
    private static string NormalAdvisor(string? a) => AdvisorPolicy.Normalize(a);

    /// <summary>"Starta om när den är ledig" is waiting for the CLI's next idle.</summary>
    public bool PendingRestart
    {
        get => _pending;
        internal set { if (Set(ref _pending, value)) RaiseRestart(); }
    }

    /// <summary>Whether the in-place restart keeps the conversation (model change on claude/codex).</summary>
    public bool RestartResumes => LaunchedAs is { } l && Launcher.CanResume(l, ToConfig(forLaunch: true))
        && !_owner.SharesConversationDirectory(this, l.Llm);

    /// <summary>A one-shot tab is "run again" rather than "open again"; a changed one restarts with the new choices.</summary>
    public string RestartTip =>
        PendingRestart ? Loc.Get("restart.pending.tip")
        : IsChanged ? Loc.Format(RestartResumes ? "restart.changed.resume.tip"
            : _owner.SharesConversationDirectory(this, Llm) ? "subtab.restart.fresh.tip" : "restart.changed.tip", Llm, ModelLabel)
        : CanReloadOrchestra ? Loc.Get("restart.orkester.tip")
        : Loc.Get(LaunchedAs is { NonInteractive: true, Orkester: true } ? "restart.once.tip"
            : LaunchedAs is { Llm: Catalog.ShellOnly } ? "restart.shell.tip" : "restart.tip");

    private string ModelLabel => string.IsNullOrWhiteSpace(Model) || Model == Catalog.StandardModel ? Loc.Get("restart.standardModel") : Model;

    public bool CanRestart => IsChanged || _state is TabState.Closed or TabState.CliExited or TabState.CliMissing
        or TabState.NotStarted or TabState.Lost or TabState.Denied;

    private TabLog? _inputLog;
    private DateTime? _lastInputOutput;
    internal long? OrchestraRequestedFor { get; set; }
    public bool CanReloadOrchestra => !IsChanged && Orkester && !_owner.IsClosingAll && !IsClosing
        && OrchestraReload.Due(LaunchedAs, _state ?? TabState.NotStarted, Activity, PendingRestart, false,
            _inputLog, _lastInputOutput, OrchestraRequestedFor, DateTime.UtcNow) is not null;
    public bool ShowReload => CanRestart || CanReloadOrchestra;
    public string ReloadName => Loc.Get(CanReloadOrchestra ? "restart.orkester.name" : "restart.name");

    internal void RefreshInputState(TabLog? log, DateTime? lastOutput)
    {
        _inputLog = log;
        _lastInputOutput = lastOutput;
        RaiseRestart();
    }

    /// <summary>A separate action: reopen an ended interactive session with the latest conversation in its directory.</summary>
    public bool CanContinueClosed => CanRestart && !IsAlive && LaunchedAs is { } launched && Launcher.CanContinue(launched)
        && !_owner.SharesConversationDirectory(this, launched.Llm);
    public string ContinueTip => Loc.Get("restart.continue.tip");
    /// <summary>
    /// The row's Start icon (#198): a tab this run has never opened, such as a subtab created while the others run, starts on its own,
    /// ticked or not. Once opened it has ↻ and Continue instead, so the two never show together.
    /// </summary>
    public bool CanStart => LaunchedAs is null && !IsAlive;
    /// <summary>The status column holds the status, or the Start icon for a tab that has none yet.</summary>
    public bool ShowStatusColumn => HasStatus || CanStart;
    /// <summary>The accessible name of the row's Start icon (#198): which tab it starts.</summary>
    public string StartName => Loc.Format("tab.start.name", Name);
    /// <summary>
    /// Tooltip and accessible name of the folder icon, the same in the main window and Settings → Projects (#156):
    /// it always opens the folder, in Explorer on Windows and the file manager elsewhere.
    /// </summary>
    public static string OpenFolderName => Loc.Get(OperatingSystem.IsWindows() ? "folder.open.name" : "folder.open.name.files");
    /// <summary>The accessible name of the row's "Fortsätt" link (#156): which conversation it continues.</summary>
    public string ContinueName => Loc.Format("restart.continue.name", Name);
    /// <summary>The row menu's Move up / Move down (#156): only where there is a neighbour to swap with.</summary>
    public bool CanMoveUp => _owner.CanMoveStep(this, -1);
    public bool CanMoveDown => _owner.CanMoveStep(this, 1);
    internal void OrderChanged()
    {
        Raise(nameof(CanMoveUp));
        Raise(nameof(CanMoveDown));
        Raise(nameof(InGroup));
        Raise(nameof(GroupTop));
        Raise(nameof(GroupBottom));
    }
    /// <summary>The accessible name of the row's grip, which moves the row with Alt+Up/Down (#156).</summary>
    public string ReorderName => Loc.Format("tab.reorder.name", Name);

    private void ChoiceChanged()
    {
        if (!IsChanged) PendingRestart = false;
        RaiseRestart();
        Raise(nameof(StatusText));
        _owner.RefreshConversationChoices();
    }

    private void RaiseRestart()
    {
        foreach (var n in new[] { nameof(IsChanged), nameof(CanRestart), nameof(RestartTip), nameof(PendingRestart), nameof(StatusActionName), nameof(CanContinueClosed), nameof(CanReloadOrchestra), nameof(ShowReload), nameof(ReloadName), nameof(CanStart), nameof(ShowStatusColumn) }) Raise(n);
        RaiseStatusIndicator();
        _owner.RestartableChanged();
    }
    /// <summary>The programs of the commands agy denied (state Denied).</summary>
    internal IReadOnlyList<string> DeniedPrograms => _denied;
    private string? _closeProgress;
    /// <summary>Close all is saving and closing this tab.</summary>
    internal bool IsClosing => _closeProgress is not null;
    /// <summary>The idle period (epoch-ms of its "idle") auto-clear already asked this tab to clear.</summary>
    internal long? AutoClearRequestedFor { get; set; }
    /// <summary>The tab's CPU activity across polls (#215, TabCpu).</summary>
    internal CpuActivity Cpu { get; } = new();
    internal void SetCloseProgress(string? text)
    {
        _closeProgress = text;
        RaiseRestart();
        Raise(nameof(StatusText));
        Raise(nameof(StatusTip));
    }
    /// <summary>The status popup's header: "Atlas · claude · Running" (tab, agent, status).</summary>
    public string PeekHeader => $"{Name} · {Loc.Display("llm", Llm)}{(HasAccount ? " (" + AccountLabel + ")" : "")} · {StatusText}";
    /// <summary>The longer explanation under the header, when the tooltip says more than the status itself.</summary>
    public string PeekDetail => StatusTip;
    public bool HasPeekDetail => StatusTip != StatusText;

    /// <summary>The tab's account's plan limits for the status popup (#200); null for other agents and while unknown.</summary>
    private LimitIndicator? PeekLimit => _owner.LimitIndicatorOf(_llm, QuotaAccountKey ?? "\0", includeFable: UsesFableQuota);
    public bool HasPeekLimit => PeekLimit is not null;
    public bool PeekLimitWarn => PeekLimit?.Warn == true;
    /// <summary>The popup's limit table (#210): one row per window with remaining/used share and reset time.</summary>
    public IReadOnlyList<LimitRow> PeekLimitRows => PeekLimit?.Rows ?? [];
    public string PeekLimitAge => PeekLimit?.Age ?? "";
    public bool HasPeekLimitAge => PeekLimitAge.Length > 0;
    public string PeekLimitName => PeekLimit?.Name ?? "";
    /// <summary>
    /// Why the popup shows no plan limits for a live agy tab (#212), "" otherwise: a tab started without BoosterAI's agy settings
    /// (by a BoosterAI before #206) never saves the quota until it is restarted; one with them saves it shortly after AGY starts.
    /// </summary>
    public string PeekLimitMissing => _llm != "agy" || !_owner.AccountLimitsEnabled || PeekLimit is not null || !ShowStatusRemaining ? ""
        : Loc.Get(Session is { } session && Track is { } track && session.HasAgySettings(track.Id) ? "account.limit.missing.agy.pending" : "account.limit.missing.agy.restart");
    public bool HasPeekLimitMissing => PeekLimitMissing.Length > 0;
    /// <summary>The status indicator's accessible name: the full status (tip), with the remaining share when the indicator shows it (#209).</summary>
    public string StatusActionName => Loc.Format("peek.action", Name, WithRemaining(StatusTip));

    /// <summary>
    /// The full status with the remaining quota (#207): on normal live/starting/idle tabs with a known limit
    /// "Körs · 38 % kvar" / "Running · 38% left"; other rows keep their plain status. The main list shows only the
    /// compact <see cref="StatusIndicatorText"/> (#209); this text is what it stands for.
    /// </summary>
    public string StatusDisplay => WithRemaining(StatusText);

    private string WithRemaining(string status) => status is { Length: > 0 } && ShowStatusRemaining && PeekLimit is { } limit
        ? Loc.Format("tabstate.withRemaining", status, limit.RemainingCaption)
        : status;

    /// <summary>
    /// The compact status column's text beside the status dot (#209): the remaining share of the tightest known window on
    /// live, idle, watching and starting tabs ("38 %" / "38%"), "!" on an error row so colour is not the only cue, "…" while
    /// the tab closes or waits for idle to restart, and nothing otherwise. The full status is in the popup and the accessible name.
    /// </summary>
    public string StatusIndicatorText =>
        _closeProgress is not null || (_state is TabState.Running && PendingRestart) ? "…"
        : IsStatusError ? "!"
        : ShowStatusRemaining && PeekLimit is { } limit ? AnimatedRemainingShort(limit)
        : "";

    private string LimitAnimKey(string part) => MainViewModel.LimitAnimKey(_llm, QuotaAccountKey ?? "\0", UsesFableQuota && _llm == "claude", part);

    /// <summary>The compact remaining share; while a change counts (#234) the current whole number on its way to the new one.</summary>
    private string AnimatedRemainingShort(LimitIndicator limit)
    {
        var key = LimitAnimKey("t");
        var now = _owner.LimitsClock();
        if (!_owner.LimitAnim.IsCounting(key, now)) return limit.RemainingShort;
        return Loc.Format("account.limit.chip", LimitPercent.Text(Math.Round(_owner.LimitAnim.Value(key, limit.RemainingPercent, now))));
    }

    /// <summary>"(-5%)" beside the compact remaining share after a changed reading (#234); fades out, then is empty.</summary>
    public string StatusDiffText => ShowStatusRemaining && PeekLimit is { } limit ? _owner.LimitAnim.Diff(LimitAnimKey("t"), limit.RemainingPercent, _owner.LimitsClock()).Text : "";
    public double StatusDiffOpacity => ShowStatusRemaining && PeekLimit is { } limit ? _owner.LimitAnim.Diff(LimitAnimKey("t"), limit.RemainingPercent, _owner.LimitsClock()).Opacity : 0;
    public bool HasStatusDiff => StatusDiffText.Length > 0;

    /// <summary>The closed Agent box's two meters (#202, #219), counting from the old to the new level after a changed reading (#234).</summary>
    public double SessionMeterValue => MeterValue(0, "s");
    public double WeekMeterValue => MeterValue(1, "w");

    private double MeterValue(int row, string part)
    {
        if (PeekLimit is not { } l || l.Rows.Count <= row || !l.Rows[row].Known) return part == "s" ? SelectedAgentChoice?.SessionMeterPercent ?? 0 : SelectedAgentChoice?.WeekMeterPercent ?? 0;
        return _owner.LimitAnim.Value(LimitAnimKey(part), LimitMeter.From(l.Rows[row]).Percent, _owner.LimitsClock());
    }

    /// <summary>One frame of a limit change animation (#234).</summary>
    internal void RaiseLimitAnimation()
    {
        foreach (var n in new[] { nameof(StatusIndicatorText), nameof(StatusDiffText), nameof(StatusDiffOpacity), nameof(HasStatusDiff), nameof(SessionMeterValue), nameof(WeekMeterValue) }) Raise(n);
    }
    public bool HasStatusIndicatorText => StatusIndicatorText.Length > 0;
    /// <summary>A failed row: the CLI exited with an error, is missing, did not start, was denied or the tab was lost (#209).</summary>
    public bool IsStatusError => _closeProgress is null && (IsExitError || _state is TabState.CliMissing or TabState.NotStarted or TabState.Lost or TabState.Denied);
    /// <summary>The remaining share is shown in the warning colour from <see cref="LimitIndicator.WarnAt"/> percent used.</summary>
    public bool StatusIndicatorWarn => !IsStatusError && StatusIndicatorText.Length > 0 && StatusIndicatorText != "…" && PeekLimit?.Warn == true;

    private void RaiseStatusIndicator()
    {
        foreach (var n in new[] { nameof(StatusIndicatorText), nameof(HasStatusIndicatorText), nameof(IsStatusError), nameof(StatusIndicatorWarn), nameof(StatusActionName),
            nameof(StatusDiffText), nameof(StatusDiffOpacity), nameof(HasStatusDiff), nameof(SessionMeterValue), nameof(WeekMeterValue), nameof(StatusDot), nameof(StatusDotFilled), nameof(StatusDotRing), nameof(StatusDotDiamond), nameof(StatusPulse), nameof(StatusHalo) }) Raise(n);
    }

    /// <summary>
    /// The status dot's shape (#222), so the compact column tells the states apart without colour alone: an error, a closed tab
    /// or a plan limit in the warning range is a diamond; a live tab that works (a turn or CPU activity, #215) is a filled dot
    /// with a pulsing halo; a tab that waits for input is a ring; anything else (starting, watching, unknown) a plain dot.
    /// </summary>
    public StatusDotKind StatusDot =>
        _state is null ? StatusDotKind.Plain
        : _closeProgress is not null || (_state is TabState.Running && PendingRestart) ? StatusDotKind.Plain
        : IsStatusError || _state is TabState.Closed || StatusIndicatorWarn ? StatusDotKind.Warn
        : _state is TabState.Running && Activity is Activity.Busy or Activity.Working ? StatusDotKind.Working
        : (_state is TabState.Running && Activity == Activity.Idle) || (_state is TabState.CliExited && _rc == 0) ? StatusDotKind.Idle
        : StatusDotKind.Plain;
    public bool StatusDotFilled => StatusDot is StatusDotKind.Plain or StatusDotKind.Working;
    public bool StatusDotRing => StatusDot == StatusDotKind.Idle;
    public bool StatusDotDiamond => StatusDot == StatusDotKind.Warn;
    /// <summary>The working halo pulses, or stays still when the system turned animations off (<see cref="Motion"/>).</summary>
    public bool StatusPulse => StatusDot == StatusDotKind.Working && PulseAnimates;
    public bool StatusHalo => StatusDot == StatusDotKind.Working && !PulseAnimates;
    /// <summary>A still screenshot shows the halo it would otherwise catch at an arbitrary point of the pulse.</summary>
    private static bool PulseAnimates => Motion.Animate && !Screenshot.Active;

    private bool ShowStatusRemaining => _closeProgress is null && _state switch
    {
        TabState.Starting => true,
        TabState.Running => !PendingRestart && !IsChanged && !(_activity == Activity.Idle && _clearedAt is not null),
        TabState.CliExited => _rc == 0,
        _ => false,
    };

    public string StatusText => _closeProgress ?? (_state switch
    {
        null => "",
        TabState.Running when PendingRestart => Loc.Get("tabstate.waitingIdle"),
        TabState.Running when IsChanged => Loc.Get("tabstate.changed"),
        TabState.Running when Activity == Activity.Busy => Loc.Get("tabstate.busy"),
        TabState.Running when Activity == Activity.Working => Loc.Get("tabstate.working"),
        TabState.Running when Activity == Activity.Idle && _clearedAt is { } cleared => Loc.Format("tabstate.idleCleared", LocalTime(cleared)),
        TabState.Running when Activity == Activity.Idle => Loc.Get("tabstate.idle"),
        TabState.Running when Activity == Activity.Watching => Loc.Format("tabstate.watching", HumanTime.Minutes(TabStatus.WatchingMinutes(_watchingSince, DateTime.UtcNow))),
        TabState.Running => Loc.Get("tabstate.unknownActivity"),
        TabState.CliExited when _rc == 0 => Loc.Get("tabstate.idle"),
        TabState.CliExited => Loc.Format("tabstate.CliExited", _rc ?? -1),
        TabState.Denied => Loc.Format("tabstate.Denied", _denied.Count > 0 ? string.Join(", ", _denied) : "?"),
        var s => Loc.Get("tabstate." + s),
    });
    /// <summary>The status tooltip: what Watching means and when the last turn ended; otherwise the status itself.</summary>
    public string StatusTip => _state is TabState.Running && Activity == Activity.Watching && _watchingSince is { } since
        ? Loc.Format("tabstate.watching.tip", DayTime(since))
        : _state is TabState.Running && Activity == Activity.Idle && _clearedAt is { } cleared && _closeProgress is null
        ? Loc.Format("tabstate.idleCleared.tip", DayTime(cleared))
        : _state is TabState.Running && Activity == Activity.Working && !PendingRestart && !IsChanged && _closeProgress is null
        ? Loc.Format("tabstate.working.tip", Cpu.Busy?.Busiest ?? "?", Math.Round((Cpu.Busy?.BusiestShare ?? 0) * 100))
        : StatusText;

    private static string LocalTime(long epochMs) =>
        HumanTime.Clock(DateTimeOffset.FromUnixTimeMilliseconds(epochMs));
    /// <summary>A tooltip's time with its day (#208): "i dag 14:20", "i går 23:40", "fredag 10:00".</summary>
    private static string DayTime(long epochMs) => HumanTime.Day(DateTimeOffset.FromUnixTimeMilliseconds(epochMs), DateTimeOffset.UtcNow);
    /// <summary>The CLI ended with a non-zero exit code: the popup offers ↻ as its primary action.</summary>
    public bool IsExitError => _state is TabState.CliExited && _rc is not 0;

    /// <summary>The theme token (App.axaml) of the status dot's colour.</summary>
    public string StatusBrushKey => _state switch
    {
        TabState.Running when Activity == Activity.Watching => "InfoDotBrush",
        TabState.Running => "SuccessDotBrush",
        TabState.Starting => "NeutralStatusBrush",
        TabState.CliExited when _rc == 0 => "NeutralStatusBrush",
        TabState.Closed => "WarningDotBrush",
        _ => "DangerDotBrush",
    };

    public IBrush StatusBrush => ThemeBrushes.Get(StatusBrushKey);

    /// <summary>The theme changed: the status dot looks its colour up again.</summary>
    internal void RaiseThemeBrushes() => Raise(nameof(StatusBrush));

    /// <summary>Updates the state; true when it changed.</summary>
    /// <param name="clearedAt">When auto-clear typed /clear in the current idle period (AutoClear.ClearedAt); shown in the status.</param>
    internal bool SetState(TabState state, int? rc, IReadOnlyList<string>? denied = null, Activity activity = Activity.Unknown,
        long? watchingSince = null, long? clearedAt = null)
    {
        denied ??= [];
        if (_state == state && _rc == rc && _denied.SequenceEqual(denied) && _activity == activity && _watchingSince == watchingSince && _clearedAt == clearedAt)
        {
            // The elapsed minutes of Watching, and Working's busiest process, move on without a new event.
            if (activity is Activity.Watching or Activity.Working) { Raise(nameof(StatusText)); Raise(nameof(StatusTip)); Raise(nameof(StatusActionName)); }
            return false;
        }
        var stateChanged = _state != state || _rc != rc || !_denied.SequenceEqual(denied);
        _state = state;
        _rc = rc;
        _denied = denied;
        _activity = activity;
        _watchingSince = watchingSince;
        _clearedAt = clearedAt;
        if (state != TabState.Running) PendingRestart = false;
        foreach (var n in new[] { nameof(HasStatus), nameof(IsRunning), nameof(CanShowTerminal), nameof(ShowTerminalHelp), nameof(IsAlive), nameof(OpenTip), nameof(WillStart), nameof(Activity), nameof(StatusText), nameof(StatusTip), nameof(StatusBrush),
            nameof(IsExitError), nameof(PeekLimitMissing), nameof(HasPeekLimitMissing) }) Raise(n);
        RaiseRemoveSubTab();
        RaiseRestart();
        (Parent ?? this).GroupChanged();
        // A closed tab's questions turn stale and its agent status is hidden (#249).
        if (stateChanged) RaiseChannel();
        if (stateChanged) _owner.RecoveredTabChanged(this);
        return stateChanged;
    }

    public TabConfig ToConfig(bool forLaunch) => new()
    {
        Id = Id, Expanded = Expanded, Name = Name.Trim(), Color = Color, ColorIndex = _colorIndex, Directory = Directory.Trim(), Enabled = Enabled,
        Llm = Llm, Model = string.IsNullOrWhiteSpace(Model) ? Catalog.StandardModel : Model, Advisor = NormalAdvisor(Advisor),
        Orkester = forLaunch ? Orkester && CanOrkester : Orkester,
        NonInteractive = NonInteractive,
        Account = Accounts.Supports(Llm) ? _account : "",
        AccountDirectory = forLaunch ? AccountDirectory : null,
        CompactTokens = forLaunch ? _owner.CompactTokens(Llm, Model, Orkester && CanOrkester, AccountDirectory) : null,
    };
}

public sealed partial class MainViewModel : Observable
{
    /// <summary>Codex reads its model capacities from the tab's account (#186): <paramref name="accountDirectory"/>, null for the default.</summary>
    internal int? CompactTokens(string cli, string model, bool orchestra = false, string? accountDirectory = null) =>
        AutoCompactPolicy.Resolve(_cfg, cli, model, orchestra, accountDirectory is null ? _compactCodexModels
            : _compactCodexAccountModels.GetValueOrDefault(accountDirectory) ?? new Dictionary<string, int>());

    /// <summary>The agent's extra accounts from Settings (#186).</summary>
    internal string DefaultAccountName(string llm) => Accounts.DefaultName(_cfg, llm);
    internal bool HasDefaultAccountName(string llm) => Accounts.HasOwnDefaultName(_cfg, llm);
    internal bool SetDefaultAccountName(string llm, string name) => Accounts.SetDefaultName(_cfg, llm, name);
    internal IReadOnlyList<AgentAccount> AccountsFor(string llm) => Accounts.For(_cfg, llm);
    internal string? AccountDirectory(string llm, string? id) => Accounts.Find(_cfg.Accounts, llm, id)?.Directory;
    private readonly AppConfig _cfg;
    /// <summary>The config as this instance last loaded or saved it; see ConfigMerge.</summary>
    private AppConfig _saved;
    private string _status = "";
    private string? _wslHome;

    public MainViewModel(AppConfig cfg)
    {
        _cfg = cfg;
        _saved = ConfigStore.Copy(cfg);
        Tabs = [];
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var project in cfg.Tabs)
        {
            AddConfigured(project, null);
            var parent = Tabs[^1];
            foreach (var child in project.SubTabs) AddConfigured(child, parent);
        }
        void AddConfigured(TabConfig tab, TabViewModel? parent)
        {
            if (string.IsNullOrWhiteSpace(tab.Id) || !ids.Add(tab.Id))
            {
                tab.Id = Guid.NewGuid().ToString("N");
                ids.Add(tab.Id);
            }
            // A tab without a stored colour gets the next one (a subtab its project's), as a newly added project does.
            Tabs.Add(new TabViewModel(tab, this, parent?.ColorIndex ?? NextProjectColor()) { Parent = parent });
        }
        _projects = Tabs.Where(t => t.IsProject).ToArray();
        _visibleTabs = Tabs.ToArray();
        // An existing chosen advisor starts with the switch on, so the switch and the visible rows agree (#180).
        if (_projects.Any(p => p.CanAdvisor && p.Advisor != AdvisorPolicy.Disabled)) cfg.ShowAdvisor = true;
        AdvisorSwitchChanged();
        OrchestraColumnChanged();
        Tabs.CollectionChanged += (_, _) => TabsChanged();
        Update = new UpdateViewModel(cfg, Save);
        Denial = new DenialViewModel();
        Optimization = new OptimizationViewModel(cfg);
        Converters.IsCliMissing = cli => Setup.Status.Checked && Setup.Status.Cli(cli)?.Installed == false;
        Setup.OnStatusChanged = () =>
        {
            foreach (var t in Tabs) t.RaiseLlms();
        };
        Setup.ConfigureCompact(CompactEditor);
        Setup.ConfigureAccounts(AccountsEditor);
        WatchBanners();
        InitTask = InitAsync();
    }

    public ObservableCollection<TabViewModel> Tabs { get; }

    /// <summary>The theme changed: brushes looked up from theme tokens are fetched again.</summary>
    internal void RaiseThemeBrushes()
    {
        foreach (var tab in Tabs) tab.RaiseThemeBrushes();
    }
    private IReadOnlyList<TabViewModel> _projects = [];
    public IReadOnlyList<TabViewModel> Projects => _projects;
    private IReadOnlyList<TabViewModel> _visibleTabs = [];
    public IReadOnlyList<TabViewModel> VisibleTabs => _visibleTabs;
    public bool IsWindows => OperatingSystem.IsWindows();
    public bool IsLinux => !OperatingSystem.IsWindows();
    public IReadOnlyList<string> LinuxTerminals => Launcher.LinuxTerminals;
    /// <summary>The window title and the bottom-left label name the running build, so any report says which version it was.</summary>
    public string AppTitle => "BoosterAI " + UpdateLogic.Current;
    public string VersionText => "v" + UpdateLogic.Current;
    /// <summary>The version link's accessible name (#226): "Visa ändringar i BoosterAI v0.48.0".</summary>
    public string VersionName => Loc.Format("version.name", VersionText);
    /// <summary>The status line at the bottom: neutral acknowledgements only; errors go to the banner (#153).</summary>
    public string Status { get => _status; private set => Set(ref _status, value); }
    /// <summary>The newest outcome is an error, shown in the error banner.</summary>
    public bool StatusIsError => Error.IsVisible;
    /// <summary>The danger banner at the top of the main window.</summary>
    public ErrorBannerViewModel Error { get; } = new();
    /// <summary>Low-quota warnings (#211), a quiet bar per agent/account/window in the banner area; never modal.</summary>
    public QuotaNotices Quota { get; } = new();

    // Every status publication invalidates pending recovery notices, even when its text is unchanged.
    internal long StatusRevision { get; private set; }
    private HashSet<TabViewModel>? _recoveredStatusRows;

    /// <summary>A neutral acknowledgement in the status line; it also retires an older error banner.</summary>
    public void ShowStatus(string text)
    {
        StatusRevision++;
        _recoveredStatusRows = null;
        Status = text;
        Error.Hide();
    }

    /// <summary>
    /// An error in the banner, with the actions its kind offers (ErrorBannerViewModel.ActionsFor): pass
    /// <paramref name="retry"/> for kinds with Retry and <paramref name="tab"/> for kinds with Details.
    /// </summary>
    public void ShowError(ErrorKind kind, string text, Action? retry = null, TabViewModel? tab = null)
    {
        StatusRevision++;
        _recoveredStatusRows = null;
        Status = "";
        Error.Show(kind, text, retry, tab);
    }

    /// <summary>A launch's outcome: "" clears the status, otherwise an invalid tab or a terminal that did not open.</summary>
    private void ShowLaunchOutcome(string error, IEnumerable<TabConfig> tabs, Action retry, string success = "")
    {
        if (error == "") { ShowStatus(success); return; }
        var enabled = TabGroups.Flatten(tabs.ToList()).Where(t => t.Enabled).ToList();
        if (enabled.Count == 0 || Launcher.Validate(enabled) != "") ShowError(ErrorKind.InvalidTab, error);
        else ShowError(ErrorKind.Launch, error, retry);
    }

    /// <summary>The window's own Start (with its after-launch policy); "Försök igen" after a failed start uses it.</summary>
    internal Action? LaunchFromWindow { get; set; }

    /// <summary>A startup notice only: a newer status or the first recovered tab's state change retires it.</summary>
    internal void ShowRecoveredStatus(IEnumerable<TabViewModel> rows, long expectedRevision)
    {
        if (StatusRevision != expectedRevision) return;
        var running = rows.Where(row => row.IsAlive).ToHashSet();
        if (running.Count == 0) return;
        ShowStatus(Loc.FormatCount("status.foundRunning", running.Count));
        _recoveredStatusRows = running;
    }

    internal void RecoveredTabChanged(TabViewModel row)
    {
        if (_recoveredStatusRows?.Contains(row) == true) DismissRecoveredStatus();
    }

    private void DismissRecoveredStatus()
    {
        if (_recoveredStatusRows is not null) ShowStatus("");
    }

    public string WslDistro
    {
        get => _cfg.WslDistro;
        set { if (_cfg.WslDistro == value.Trim()) return; _cfg.WslDistro = value.Trim(); InvalidateCompactMetadata(); Raise(); InitTask = InitAsync(); }
    }

    /// <summary>Folder "Find projects" searches; empty = the default (Documents, else home).</summary>
    public string ProjectSearchRoot
    {
        get => _cfg.ProjectSearchRoot;
        set { value = (value ?? "").Trim(); if (_cfg.ProjectSearchRoot == value) return; _cfg.ProjectSearchRoot = value; Raise(); Raise(nameof(CanResetProjectSearchRoot)); }
    }
    public bool CanResetProjectSearchRoot => _cfg.ProjectSearchRoot != "";
    public string ProjectSearchRootDefault => ProjectScanner.DefaultRoot();
    public void ResetProjectSearchRoot() => ProjectSearchRoot = "";

    public string LinuxTerminal { get => _cfg.LinuxTerminal; set { _cfg.LinuxTerminal = value; Raise(); } }

    public IReadOnlyList<ColorTheme> Themes => Catalog.Themes;
    public ColorTheme Theme => Catalog.Theme(_cfg.ColorTheme);

    public ColorTheme SelectedTheme
    {
        get => Theme;
        set
        {
            if (value is null || value.Name == _cfg.ColorTheme) return;
            _cfg.ColorTheme = value.Name;
            Raise();
            foreach (var t in Tabs) t.RaiseColor();
        }
    }

    public bool HasTabs => Tabs.Count > 0;
    public int EnabledCount => Tabs.Count(t => t.Enabled);
    /// <summary>The chosen tabs Start opens: those not already running.</summary>
    public int ToStartCount => Tabs.Count(t => t.Enabled && !t.IsAlive);
    public bool CanLaunch => !IsClosingAll && ToStartCount > 0;
    /// <summary>"Opens Cirrus, Delta": the tabs Start opens, so the button's count can be checked against the list.</summary>
    public string? LaunchTip => ToStartCount == 0 ? null
        : Loc.Format("launch.tip", string.Join(", ", Tabs.Where(t => t.WillStart).Select(t => t.Name)));
    internal void RaiseLaunchTip() => Raise(nameof(LaunchTip));
    public string LaunchLabel => ToStartCount switch
    {
        0 when EnabledCount > 0 => Loc.Get("launch.allRunning"),
        0 => Loc.Get("launch.none"),
        var n => Loc.FormatCount("launch", n),
    };

    /// <summary>Language setting shown in Settings; the change applies after a restart.</summary>
    public IReadOnlyList<LanguageChoice> LanguageChoices { get; } =
        [new(Loc.Auto), new("sv"), new("en")];

    /// <summary>"After Start" setting: keep the window open, minimize it, or close it.</summary>
    public IReadOnlyList<AfterLaunchChoice> AfterLaunchChoices { get; } = [.. AfterLaunchPolicy.All.Select(m => new AfterLaunchChoice(m))];
    public string AfterLaunch => AfterLaunchPolicy.Normalize(_cfg.AfterLaunch);

    public AfterLaunchChoice SelectedAfterLaunch
    {
        get => AfterLaunchChoices.First(c => c.Id == AfterLaunch);
        set
        {
            if (value is null || value.Id == AfterLaunch) return;
            _cfg.AfterLaunch = value.Id;
            Raise();
        }
    }
    public bool MinimizeToTray { get => _cfg.MinimizeToTray; set { _cfg.MinimizeToTray = value; Raise(); } }
    public bool Notifications { get => _cfg.Notifications; set { _cfg.Notifications = value; Raise(); } }
    public bool SoundEffects { get => _cfg.SoundEffects; set { _cfg.SoundEffects = value; Raise(); } }

    /// <summary>Plays the sound effects (#240); silent by default so tests never make a sound, the app sets the real one at startup.</summary>
    internal ISoundPlayer Sound { get; set; } = new NoSoundPlayer();
    internal Func<DateTime> SoundClock { get; set; } = () => DateTime.UtcNow;
    private readonly Dictionary<SoundEffect, DateTime> _soundPlayed = [];

    /// <summary>Plays an effect when Sound effects is on and the same effect has not played in the last few seconds. May run off the UI thread.</summary>
    internal void PlaySound(SoundEffect effect)
    {
        if (!_cfg.SoundEffects) return;
        var now = SoundClock();
        lock (_soundPlayed)
        {
            if (_soundPlayed.TryGetValue(effect, out var last) && now - last < SoundCatalog.Throttle) return;
            _soundPlayed[effect] = now;
        }
        Sound.Play(effect);
    }
    /// <summary>Settings → General (#211): warn in the main window when a plan window has little quota left.</summary>
    public bool QuotaWarn { get => _cfg.QuotaWarn; set { _cfg.QuotaWarn = value; Raise(); } }
    public decimal? QuotaWarnPercent
    {
        get => QuotaWarnings.Normalize(_cfg.QuotaWarnPercent);
        set { if (value is { } v) _cfg.QuotaWarnPercent = QuotaWarnings.Normalize((int)Math.Round(v)); Raise(); }
    }
    /// <summary>The main window's Advisor switch (#180): shows the Advisor row under every Claude project.</summary>
    public bool ShowAdvisor
    {
        get => _cfg.ShowAdvisor;
        set
        {
            if (_cfg.ShowAdvisor == value) return;
            _cfg.ShowAdvisor = value;
            Raise();
            foreach (var p in Projects) p.PinAdvisor();
        }
    }
    /// <summary>The Advisor switch shows only while some project runs claude; it has nothing to show otherwise.</summary>
    public bool CanShowAdvisor => _canShowAdvisor;
    private bool _canShowAdvisor;
    internal void AdvisorSwitchChanged()
    {
        var can = _projects.Any(p => p.CanAdvisor);
        if (can == _canShowAdvisor) return;
        _canShowAdvisor = can;
        Raise(nameof(CanShowAdvisor));
    }
    public bool AutoUpdateClis { get => _cfg.AutoUpdateClis; set { _cfg.AutoUpdateClis = value; Raise(); } }
    public bool ShowTabLogo { get => _cfg.ShowTabLogo; set { _cfg.ShowTabLogo = value; Raise(); } }
    public bool TabStatusChannel { get => _cfg.TabStatusChannel; set { _cfg.TabStatusChannel = value; Raise(); RaiseChannels(); } }
    public bool AutoClearIdle { get => _cfg.AutoClearIdle; set { _cfg.AutoClearIdle = value; Raise(); RefreshContextMode(); } }
    /// <summary>The auto-clear minutes for the number box (decimal? is what it binds); an empty box keeps the last value.</summary>
    public decimal? AutoClearMinutes
    {
        get => AutoClear.Minutes(_cfg.AutoClearMinutes);
        set { if (value is { } v) { _cfg.AutoClearMinutes = AutoClear.Minutes((int)Math.Round(v)); } Raise(); }
    }

    /// <summary>The update prompt in the main window.</summary>
    public UpdateViewModel Update { get; }

    /// <summary>The offer to allow the commands agy denied in Run once tabs.</summary>
    public DenialViewModel Denial { get; }

    /// <summary>The WSL memory optimization and hardware recommendations (#76).</summary>
    public OptimizationViewModel Optimization { get; }

    public bool CheckForUpdates
    {
        get => _cfg.CheckForUpdates;
        set
        {
            if (_cfg.CheckForUpdates == value) return;
            _cfg.CheckForUpdates = value;
            Raise();
            Update.SettingChanged();
        }
    }

    public string CheckForUpdatesTip => Loc.Format("settings.updates.tip", UpdateLogic.Current);

    public LanguageChoice SelectedLanguage
    {
        get => LanguageChoices.FirstOrDefault(c => c.Id == _cfg.Language) ?? LanguageChoices[0];
        set
        {
            if (value is null || value.Id == _cfg.Language) return;
            _cfg.Language = value.Id;
            Raise();
        }
    }

    /// <summary>Appearance setting shown in Settings; applies to all open windows at once.</summary>
    public IReadOnlyList<ThemeChoice> AppearanceChoices { get; } = [new("auto"), new("light"), new("dark")];

    public ThemeChoice SelectedAppearance
    {
        get => AppearanceChoices.FirstOrDefault(c => c.Id == _cfg.AppTheme) ?? AppearanceChoices[0];
        set
        {
            if (value is null || value.Id == _cfg.AppTheme) return;
            _cfg.AppTheme = value.Id;
            (Avalonia.Application.Current as App)?.ApplyTheme();
            Raise();
        }
    }

    /// <summary>Global CLI zoom shown in Settings: saved at once (a killed app or a second instance must not lose it,
    /// #61); Windows Terminal applies it to open BoosterAI tabs too, other terminals on the next start.</summary>
    public IReadOnlyList<ZoomChoice> ZoomChoices { get; } = [.. Launcher.ZoomChoices.Select(p => new ZoomChoice(p))];

    public ZoomChoice SelectedZoom
    {
        get => ZoomChoices.FirstOrDefault(c => c.Percent == _cfg.ZoomPercent) ?? ZoomChoices[0];
        set
        {
            if (value is null || value.Percent == _cfg.ZoomPercent) return;
            _cfg.ZoomPercent = value.Percent;
            Raise();
            Save();
            Launcher.ApplyZoom(value.Percent);
        }
    }

    /// <summary>
    /// The orchestra column (and its header) shows only while a visible row can use or install the orchestra (#152);
    /// otherwise its width is 0 and the status column gets the room.
    /// </summary>
    public bool ShowOrchestraColumn => _showOrchestraColumn;
    private bool _showOrchestraColumn;

    internal void OrchestraColumnChanged()
    {
        var show = _visibleTabs.Any(t => t.CanOrkester || t.CanInstallOrkester);
        if (show == _showOrchestraColumn) return;
        _showOrchestraColumn = show;
        Raise(nameof(ShowOrchestraColumn));
    }

    public void TabsChanged()
    {
        // Status polling must not replace ItemsSource and destroy an open row editor or popup.
        var projects = Tabs.Where(t => t.IsProject).ToArray();
        if (!_projects.SequenceEqual(projects))
        {
            _projects = projects;
            AdvisorSwitchChanged();
            Raise(nameof(Projects));
        }
        var visible = Tabs.ToArray();
        if (!_visibleTabs.SequenceEqual(visible))
        {
            _visibleTabs = visible;
            Raise(nameof(VisibleTabs));
        }
        foreach (var project in Projects) project.GroupChanged();
        foreach (var tab in Tabs) tab.OrderChanged();
        OrchestraColumnChanged();
        RefreshConversationChoices();
        Raise(nameof(HasTabs));
        Raise(nameof(EnabledCount));
        Raise(nameof(ToStartCount));
        Raise(nameof(CanLaunch));
        Raise(nameof(LaunchLabel));
        Raise(nameof(LaunchTip));
        Raise(nameof(CanCloseAll));
        Raise(nameof(CloseAllTip));
        RestartableChanged();
        SetupRowsChanged();
    }

    /// <summary>CLI --continue/--last selects by folder, so a group with matching CLIs must never resume a sibling's conversation.</summary>
    internal bool SharesConversationDirectory(TabViewModel row, string llm)
    {
        var project = row.Parent ?? row;
        return Tabs.Any(other => other != row && (other.Parent ?? other) == project
            && (other.Llm == llm || other.LaunchedAs?.Llm == llm));
    }

    internal void RefreshConversationChoices()
    {
        foreach (var tab in Tabs) tab.RaiseRestartTip();
    }

    internal void RestartableChanged()
    {
        Raise(nameof(RestartableCount));
        Raise(nameof(CanRestartAll));
        Raise(nameof(RestartAllLabel));
    }

    /// <summary>Moves a whole project group, or a child within its own group; runtime rows keep their identity.</summary>
    public void MoveTab(int oldIndex, int newIndex)
    {
        if (oldIndex < 0 || oldIndex >= Tabs.Count || newIndex < 0 || newIndex >= Tabs.Count || oldIndex == newIndex) return;
        var source = Tabs[oldIndex];
        var target = Tabs[newIndex];
        if (source.Parent is { } parent)
        {
            if (target.Parent != parent) return;
            Tabs.Move(oldIndex, newIndex);
        }
        else
        {
            var targetProject = target.Parent ?? target;
            if (source == targetProject) return;
            var projects = Projects.ToList();
            var group = Tabs.Where(t => t == source || t.Parent == source).ToList();
            var projectIndex = projects.IndexOf(targetProject);
            projects.Remove(source);
            projects.Insert(projectIndex, source);
            var order = projects.SelectMany(p => p == source ? group : Tabs.Where(t => t == p || t.Parent == p).ToList()).ToList();
            for (var i = 0; i < order.Count; i++) Tabs.Move(Tabs.IndexOf(order[i]), i);
        }
        Save();
        ScheduleArrange(reorder: true);
    }

    /// <summary>
    /// The row a keyboard move (Alt+Up/Down, the row menu) swaps with (#156): the neighbouring project for a project,
    /// the neighbouring subtab within the same project for a subtab; null at either end.
    /// </summary>
    internal TabViewModel? StepTarget(TabViewModel tab, int delta)
    {
        if (!Tabs.Contains(tab) || delta == 0) return null;
        var peers = tab.Parent is { } parent ? Tabs.Where(t => t.Parent == parent).ToList() : Projects.ToList();
        var index = peers.IndexOf(tab) + Math.Sign(delta);
        return index >= 0 && index < peers.Count ? peers[index] : null;
    }

    public bool CanMoveStep(TabViewModel tab, int delta) => StepTarget(tab, delta) is not null;

    /// <summary>Moves a row one step up (-1) or down (+1) among its peers, a project with its subtabs, and saves the order.</summary>
    public bool MoveStep(TabViewModel tab, int delta)
    {
        if (StepTarget(tab, delta) is not { } target) return false;
        MoveTabRelative(tab, target, after: delta > 0);
        return true;
    }

    /// <summary>A drop belongs before/after an entire project, even when its main row is followed by children.</summary>
    public void MoveTabRelative(TabViewModel source, TabViewModel target, bool after)
    {
        if (!Tabs.Contains(source) || !Tabs.Contains(target) || source == target) return;
        if (source.Parent is { } parent)
        {
            if (target.Parent != parent) return;
            var siblings = Tabs.Where(t => t.Parent == parent).ToList();
            siblings.Remove(source);
            var index = siblings.IndexOf(target) + (after ? 1 : 0);
            MoveTab(Tabs.IndexOf(source), Tabs.IndexOf(parent) + 1 + index);
        }
        else
        {
            var targetProject = target.Parent ?? target;
            if (targetProject == source) return;
            var projects = Projects.ToList();
            projects.Remove(source);
            projects.Insert(projects.IndexOf(targetProject) + (after ? 1 : 0), source);
            var order = projects.SelectMany(p => Tabs.Where(t => t == p || t.Parent == p).ToList()).ToList();
            for (var i = 0; i < order.Count; i++) Tabs.Move(Tabs.IndexOf(order[i]), i);
            Save();
            ScheduleArrange(reorder: true);
        }
    }

    public TabViewModel AddSubTab(TabViewModel project)
    {
        project = project.Parent ?? project;
        if (!Tabs.Contains(project)) throw new ArgumentException("Project is not configured.", nameof(project));
        var siblings = Tabs.Where(t => t.Parent == project).ToList();
        var number = 2;
        string name;
        do { name = project.Name.Trim() + " · " + number++; } while (Tabs.Any(t => t.Name == name));
        var config = project.ToConfig(forLaunch: false);
        config.Id = Guid.NewGuid().ToString("N");
        config.Name = name;
        config.Expanded = false;
        var child = new TabViewModel(config, this, Tabs.Count) { Parent = project };
        Tabs.Insert(Tabs.IndexOf(project) + siblings.Count + 1, child);
        project.Expanded = true;
        CheckOrkester(child);
        Save();
        return child;
    }

    /// <summary>Running/idle-shell tabs must be closed before removing their monitoring/configuration.</summary>
    public bool RemoveSubTab(TabViewModel tab)
    {
        if (!tab.CanRemoveSubTab || !Tabs.Contains(tab)) return false;
        Tabs.Remove(tab);
        Save();
        return true;
    }

    /// <summary>A new project's colour: the palette's next after the last project's (#152).</summary>
    private int NextProjectColor() => Catalog.NextColorIndex(Tabs.LastOrDefault(t => t.IsProject)?.ColorIndex);

    public void AddTab()
    {
        var tab = new TabViewModel(new TabConfig { Id = Guid.NewGuid().ToString("N"), ColorIndex = NextProjectColor() }, this, Tabs.Count);
        Tabs.Add(tab);
        CheckOrkester(tab);
    }

    /// <summary>Local paths of the projects already added, for leaving them out of "Hitta projekt".</summary>
    public IEnumerable<string> ProjectPaths =>
        Tabs.Select(t => PathMapping.ToLocalPath(t.Directory, _cfg.WslDistro, _wslHome)).OfType<string>();

    /// <summary>Adds the folders picked in "Hitta projekt" as tabs named after their folder.</summary>
    public void Import(IReadOnlyList<FoundProject> projects)
    {
        // The blank row Settings adds for a first project is replaced, not kept next to the imports.
        if (Tabs is [{ } only] && only.Directory.Trim() is "" or "~" && Loc.IsDefaultTabName(only.Name)) Tabs.Clear();
        foreach (var p in projects)
        {
            var tab = new TabViewModel(new TabConfig { Id = Guid.NewGuid().ToString("N"), Name = p.Name, Directory = PathMapping.ToTabPath(p.Path), ColorIndex = NextProjectColor() }, this, Tabs.Count);
            Tabs.Add(tab);
            CheckOrkester(tab);
        }
        if (projects.Count > 0) ShowStatus(Loc.FormatCount("status.imported", projects.Count));
    }

    /// <summary>The latest <see cref="InitAsync"/>; tests wait for it so its lifecycle check cannot land in theirs.</summary>
    internal Task InitTask { get; private set; } = Task.CompletedTask;

    /// <summary>Resolves the WSL home (for ~ paths on Windows), then checks every tab.</summary>
    private async Task InitAsync()
    {
        if (OperatingSystem.IsWindows()) _wslHome = await Task.Run(() => QueryWslHome(_cfg.WslDistro));
        foreach (var t in Tabs) CheckOrkester(t);
        CheckSetup();
    }

    private readonly HashSet<string> _orkesterInFlight = [];

    /// <summary>Number of directory checks currently running (UI thread only).</summary>
    public int OrkesterChecksInFlight => _orkesterInFlight.Count;

    /// <summary>Re-checks every tab, so an orchestra installed or removed outside the app shows up (#189). Directories already being checked are skipped.</summary>
    public void RefreshOrkester()
    {
        foreach (var t in Tabs.ToArray()) CheckOrkester(t);
    }

    public void CheckOrkester(TabViewModel tab)
    {
        var local = PathMapping.ToLocalPath(tab.Directory, _cfg.WslDistro, _wslHome);
        if (local is null) { tab.HasOrkester = null; return; }
        // One check per directory at a time: tabs sharing a directory pick up the result of the first request.
        if (!_orkesterInFlight.Add(local)) return;
        Task.Run(() =>
        {
            bool? has;
            try { has = System.IO.Directory.Exists(local) ? PathMapping.HasOrkester(local) : null; }
            catch (Exception) { has = null; }
            Dispatcher.UIThread.Post(() =>
            {
                _orkesterInFlight.Remove(local);
                // Ignore a result for a directory the user has typed away from.
                foreach (var t in Tabs.ToArray())
                    if (PathMapping.ToLocalPath(t.Directory, _cfg.WslDistro, _wslHome) == local) t.HasOrkester = has;
            });
        });
    }

    private static string? QueryWslHome(string distro)
    {
        try
        {
            var psi = new ProcessStartInfo("wsl.exe") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            foreach (var a in new[] { "-d", distro, "--exec", "printenv", "HOME" }) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi)!;
            var home = p.StandardOutput.ReadToEnd().Trim();
            return p.WaitForExit(5000) && home.StartsWith('/') ? home : null;
        }
        catch (Exception) { return null; }
    }

    private AppConfig Snapshot(bool forLaunch)
    {
        _cfg.Tabs = forLaunch ? Tabs.Select(t => t.ToConfig(true)).ToList() : Projects.Select(project =>
        {
            var config = project.ToConfig(false);
            config.SubTabs = Tabs.Where(t => t.Parent == project).Select(t =>
            {
                var child = t.ToConfig(false);
                child.Directory = ""; // The folder is configured once on the project.
                return child;
            }).ToList();
            return config;
        }).ToList();
        return _cfg;
    }

    public void Save()
    {
        try
        {
            if (ConfigStore.TryRead() is { } disk && ConfigMerge.TakeOthers(_cfg, _saved, disk)) SettingsTakenOver();
            var cfg = Snapshot(forLaunch: false);
            ConfigStore.Save(cfg);
            _saved = ConfigStore.Copy(cfg);
        }
        catch (Exception e) { ShowError(ErrorKind.Save, Loc.Format("status.saveFailed", e.Message), Save); }
    }

    /// <summary>Shows the settings another instance saved (ConfigMerge) in this one's windows.</summary>
    private void SettingsTakenOver()
    {
        if (_saved.WslDistro != _cfg.WslDistro) InvalidateCompactMetadata();
        foreach (var editor in _compactEditors.Values) editor.UpdateSettings(_cfg.AutoCompactSettings);
        RefreshCompactMode();
        foreach (var name in new[] { nameof(WslDistro), nameof(LinuxTerminal), nameof(ProjectSearchRoot), nameof(CanResetProjectSearchRoot), nameof(Theme), nameof(SelectedTheme), nameof(SelectedLanguage),
            nameof(SelectedAppearance), nameof(SelectedAfterLaunch), nameof(MinimizeToTray), nameof(Notifications), nameof(SoundEffects), nameof(QuotaWarn), nameof(QuotaWarnPercent), nameof(ShowAdvisor), nameof(SelectedZoom), nameof(AutoUpdateClis), nameof(ShowTabLogo), nameof(TabStatusChannel), nameof(AutoClearIdle), nameof(AutoClearMinutes), nameof(CheckForUpdates) })
            Raise(name);
        foreach (var t in Tabs) t.RaiseColor();
        foreach (var p in Projects) p.RaiseAdvisorVisible();
        RaiseChannels();
        (Avalonia.Application.Current as App)?.ApplyTheme();
    }

    /// <summary>Saves the choices and opens the chosen tabs that are not already running; true on success.</summary>
    public bool Launch()
    {
        if (IsClosingAll) return false;
        var all = Snapshot(forLaunch: true);
        var rows = Tabs.Zip(all.Tabs).Where(p => p.Second.Enabled && !p.First.IsAlive).ToList();
        var cfg = new AppConfig
        {
            WslDistro = all.WslDistro, LinuxTerminal = all.LinuxTerminal, ZoomPercent = all.ZoomPercent, Tabs = rows.Select(r => r.Second).ToList(),
        };
        // Running tabs joined from an earlier BoosterAI run keep their window: new tabs open next to them.
        var error = Start(cfg, rows, window: _session?.WindowName);
        Save();
        ShowLaunchOutcome(error, cfg.Tabs, () => { if (LaunchFromWindow is { } launch) launch(); else Launch(); });
        return error == "";
    }

    /// <summary>For --screenshot: one of each common state, without starting anything.</summary>
    public void ShowSampleStatus()
    {
        (TabState State, int? Rc, Activity Activity)[] samples =
        [
            (TabState.Running, null, Activity.Busy),
            (TabState.Running, null, Activity.Watching),
            (TabState.CliExited, 0, Activity.Unknown),
            (TabState.Closed, null, Activity.Unknown),
            (TabState.CliExited, 1, Activity.Unknown),
            (TabState.Starting, null, Activity.Unknown),
        ];
        foreach (var (tab, sample) in Tabs.Where(t => t.Enabled).Zip(samples))
        {
            if (sample.State == TabState.CliExited && sample.Rc == 0 || sample.Activity == Activity.Watching)
            {
                var launched = tab.ToConfig(forLaunch: true);
                launched.NonInteractive = true;
                launched.Orkester = true;
                tab.LaunchedAs = launched;
            }
            tab.LaunchedAs ??= tab.ToConfig(forLaunch: true);
            tab.SetState(sample.State, sample.Rc, activity: sample.Activity,
                watchingSince: sample.Activity == Activity.Watching ? DateTimeOffset.UtcNow.AddMinutes(-12).ToUnixTimeMilliseconds() : null);
        }
    }

    /// <summary>
    /// For --screenshot --fake-new-subtab (#198): the last subtab of the first group as if just created while the others
    /// run, never opened, so its row shows the Start icon; returns it. Nothing is saved or started.
    /// </summary>
    public TabViewModel? ShowSampleNewSubTab()
    {
        var group = Tabs.FirstOrDefault(t => t.IsSubTab)?.Parent;
        if (Tabs.LastOrDefault(t => t.Parent is not null && t.Parent == group) is not { } tab) return null;
        tab.ResetSampleState();
        return tab;
    }

    /// <summary>Neutral explicit idle/input state for headless reload screenshots, with no live session or process.</summary>
    public void ShowSampleOrchestraIdle()
    {
        if (Tabs.FirstOrDefault(t => t.Enabled && t.Llm == "claude") is not { } tab) return;
        tab.HasOrkester = true;
        tab.Orkester = true;
        var launched = tab.ToConfig(forLaunch: true);
        launched.NonInteractive = false;
        tab.LaunchedAs = launched;
        var idle = DateTimeOffset.UtcNow.AddMinutes(-2).ToUnixTimeMilliseconds();
        tab.SetState(TabState.Running, null, activity: Activity.Idle);
        tab.RefreshInputState(new TabLog(null, null, true, null, false, Activity: Activity.Idle)
        { InputReady = true, InputProtocol = 2, InputReadyAt = idle - 2000, BusyAt = idle - 1000, IdleAt = idle },
            DateTimeOffset.FromUnixTimeMilliseconds(idle).UtcDateTime);
    }

    /// <summary>
    /// For --screenshot --fake-long-status (#152): the longest fixed status texts, one per AI row, with the orchestra
    /// column shown, so the status column is as narrow as it gets at a given width. Rows beyond the samples stay as they are.
    /// </summary>
    public void ShowSampleLongStatus()
    {
        var now = DateTimeOffset.UtcNow;
        long Ago(int minutes) => now.AddMinutes(-minutes).ToUnixTimeMilliseconds();
        Action<TabViewModel>[] samples =
        [
            t => t.SetState(TabState.CliExited, 127),
            t => t.SetState(TabState.Running, null, activity: Activity.Idle, clearedAt: Ago(3)),
            t => { t.PendingRestart = true; t.SetState(TabState.Running, null, activity: Activity.Busy); },
            t =>
            {
                t.LaunchedAs = t.ToConfig(forLaunch: true);
                t.LaunchedAs.Orkester = t.LaunchedAs.NonInteractive = true;
                t.SetState(TabState.Running, null, activity: Activity.Watching, watchingSince: Ago(23 * 60 + 59)); // "Bevakar · 23 h 59 min" (#208)
            },
            t =>
            {
                t.LaunchedAs = t.ToConfig(forLaunch: true);
                t.LaunchedAs.Model = Catalog.ModelsFor(t.Llm).FirstOrDefault(m => m != t.Model) ?? "other-model";
                t.SetState(TabState.Running, null, activity: Activity.Busy);
            },
            t => t.SetState(TabState.Running, null),
            t => t.SetState(TabState.NotStarted, null),
            t => t.SetState(TabState.CliMissing, null),
            t => t.SetState(TabState.Closed, null),
        ];
        foreach (var (tab, sample) in Tabs.Where(t => t.Enabled && t.IsAi).Zip(samples))
        {
            tab.HasOrkester = true;
            tab.LaunchedAs = tab.ToConfig(forLaunch: true);
            sample(tab);
        }
    }

    /// <summary>For --screenshot --fake-dots (#222): one AI row per status dot shape, in a fixed order.</summary>
    public void ShowSampleDots()
    {
        Action<TabViewModel>[] samples =
        [
            t => t.SetState(TabState.Running, null, activity: Activity.Working),
            t => t.SetState(TabState.Running, null, activity: Activity.Busy),
            t => t.SetState(TabState.Running, null, activity: Activity.Idle),
            t => t.SetState(TabState.Running, null, activity: Activity.Watching, watchingSince: DateTimeOffset.UtcNow.AddMinutes(-4).ToUnixTimeMilliseconds()),
            t => t.SetState(TabState.Starting, null),
            t => t.SetState(TabState.CliExited, 127),
            t => t.SetState(TabState.Closed, null),
        ];
        foreach (var (tab, sample) in Tabs.Where(t => t.Enabled && t.IsAi).Zip(samples))
        {
            tab.LaunchedAs = tab.ToConfig(forLaunch: true);
            sample(tab);
        }
    }

    /// <summary>For --screenshot --fake-changed: the first tab runs a turn with another model than the one now chosen.</summary>
    public void ShowSampleChange()
    {
        if (Tabs.FirstOrDefault(t => t.Enabled && t.IsAi) is not { } tab) return;
        var launched = tab.ToConfig(forLaunch: true);
        launched.Model = Catalog.ModelsFor(tab.Llm).FirstOrDefault(m => m != tab.Model) ?? Catalog.StandardModel;
        tab.LaunchedAs = launched;
        tab.SetState(TabState.Running, null, activity: Activity.Busy);
    }

    /// <summary>
    /// For --screenshot --fake-peek [N]: the Nth tab with a live or error status (0: the first); returns it so its popup can be opened.
    /// </summary>
    public TabViewModel? ShowSamplePeek(int index = 0) =>
        Tabs.Where(t => t.HasStatus && (t.IsExitError || t.IsRunning)).ElementAtOrDefault(index);

    /// <summary>For --screenshot --fake-denied: the first enabled tab had "git status &amp;&amp; gh auth status" denied.</summary>
    public void ShowSampleDenial()
    {
        if (Tabs.FirstOrDefault(t => t.Enabled) is not { } tab) return;
        tab.SetState(TabState.Denied, 0, ["git", "gh"]);
        Denial.Offer(["git", "gh"], [tab.Name.Trim()]);
    }

    /// <summary>
    /// Restarts a running tab whose LLM or model changed, in the same tab: queues the new CLI for the tab's
    /// bs_next, then stops the old CLI. Its conversation is kept for a model change (claude, codex).
    /// </summary>
    public void RestartChanged(TabViewModel tab)
    {
        DismissRecoveredStatus();
        tab.PendingRestart = false;
        if (IsClosingAll || !tab.IsChanged || tab.LaunchedAs is not { } launched) return;
        var next = tab.ToConfig(forLaunch: true);
        var error = Launcher.Validate([next]);
        if (error != "") { ShowError(ErrorKind.InvalidTab, error); return; }
        if (tab.Session is null || tab.Track is null || tab.ShellPid is null)
        {
            ShowError(ErrorKind.RestartUnavailable, Loc.Get("err.restartInPlace"), tab: tab);
            return;
        }
        try
        {
            var resume = tab.RestartResumes;
            tab.Session!.QueueNext(tab.Track!.Id, Launcher.RestartScript(next, tab.Track with { UpdateClis = _cfg.AutoUpdateClis, AutoClear = _cfg.AutoClearIdle, Logo = _cfg.ShowTabLogo, StatusChannel = _cfg.TabStatusChannel }, resume));
            var pid = tab.ShellPid!.Value;
            var distro = _cfg.WslDistro;
            Task.Run(() => Launcher.StopForeground(pid, distro));
            tab.LaunchedAs = next;
            tab.Session.SaveTab(tab.Track.Id, next);
            tab.RaiseRestartTip();
            ShowStatus(Loc.Format(resume ? "status.restartedResume" : "status.restarted", tab.Name.Trim(), next.Llm,
                next.Model == Catalog.StandardModel ? Loc.Get("restart.standardModel") : next.Model));
            Save();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            ShowError(ErrorKind.Restart, Loc.Format("err.launch", e.Message), () => RestartChanged(tab), tab);
        }
    }

    /// <summary>Tabs the per-tab restart button is offered on.</summary>
    public int RestartableCount => Tabs.Count(t => t.CanRestart);
    /// <summary>Start's "Restart all" sits beside it from two restartable tabs (one has its own button).</summary>
    public bool CanRestartAll => !IsClosingAll && RestartableCount >= 2;
    public string RestartAllLabel => Loc.Format("restartAll.label", RestartableCount);

    /// <summary>
    /// What "Restart all" does: tabs that restart without interrupting anything (ended or unchanged ones, and changed ones
    /// whose CLI is idle) and changed tabs that work on a turn, which need one combined question. A changed tab already
    /// waiting for idle stays queued.
    /// </summary>
    internal (List<TabViewModel> Now, List<TabViewModel> Busy) PlanRestartAll()
    {
        List<TabViewModel> now = [], busy = [];
        foreach (var t in Tabs.Where(t => t.CanRestart))
        {
            if (!t.IsChanged || t.Activity == Activity.Idle) now.Add(t);
            else if (!t.PendingRestart) busy.Add(t);
        }
        return (now, busy);
    }

    /// <summary>Restarts the planned tabs like their own buttons; busy ones per the answer to the combined question.</summary>
    internal void RestartAll((List<TabViewModel> Now, List<TabViewModel> Busy) plan, RestartChoice busy)
    {
        if (IsClosingAll || busy == RestartChoice.Cancel && plan.Busy.Count > 0) return;
        var closed = new List<(TabViewModel Row, TabConfig Tab)>();
        foreach (var t in plan.Now)
        {
            if (t.IsChanged) RestartChanged(t);
            else if (t.LaunchedAs is { } launched && !RestartExited(t, launched, resume: false)) closed.Add((t, launched));
        }
        // Closed tabs open again together, in one new window, instead of one window per tab.
        RestartClosed(closed);
        foreach (var t in plan.Busy)
            if (busy == RestartChoice.WhenIdle) t.PendingRestart = true; else RestartChanged(t);
    }

    /// <summary>For --screenshot --fake-restartable: the first AI tabs have ended, so the tab buttons offer a restart.</summary>
    public void ShowSampleRestartable(int count)
    {
        foreach (var tab in Tabs.Where(t => t.Enabled && t.IsAi).Take(count))
        {
            tab.LaunchedAs ??= tab.ToConfig(forLaunch: true);
            tab.SetState(TabState.CliExited, 0);
        }
    }

    /// <summary>Reuses an exited CLI's verified shell, otherwise opens the original choices again.</summary>
    public void Restart(TabViewModel tab, bool resume = false)
    {
        if (IsClosingAll || tab.LaunchedAs is not { } launched || (resume && !tab.CanContinueClosed)) return;
        if (RestartExited(tab, launched, resume)) return;
        RestartClosed([(tab, launched)], resume);
    }

    /// <summary>
    /// Start on one row (#198): opens only this tab, through the same path as a reopened closed tab, so it joins the
    /// session's terminal window when one exists; before any session it opens a new window, like Start with only this
    /// tab ticked. Its tick is left as it is and no other tab is started.
    /// </summary>
    public void StartTab(TabViewModel tab)
    {
        if (IsClosingAll || !tab.CanStart) return;
        var config = tab.ToConfig(forLaunch: true);
        config.Enabled = true;
        var cfg = new AppConfig { WslDistro = _cfg.WslDistro, LinuxTerminal = _cfg.LinuxTerminal, ZoomPercent = _cfg.ZoomPercent, Tabs = [config] };
        var error = Start(cfg, [(tab, config)], _session?.WindowName);
        Save();
        ShowLaunchOutcome(error, cfg.Tabs, () => StartTab(tab));
    }

    internal static bool CanRestartExited(TabLog? log, Func<int, long?>? live) =>
        log is { CanRestartExited: true, CliExit: not null } && live is not null && TabSession.IsAlive(log, live);

    // Returns true when handled, including validation/queue errors: never open a duplicate after an error.
    private bool RestartExited(TabViewModel tab, TabConfig launched, bool resume)
    {
        if (tab.Session is not { } session || tab.Track is not { } track) return false;
        TabLog? log;
        try { log = session.ReadLog(track.Id); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ShowError(ErrorKind.Restart, Loc.Format("err.launch", e.Message), () => Restart(tab, resume), tab);
            return true;
        }
        if (!CanRestartExited(log, TabSession.Liveness(_cfg.WslDistro))) return false;
        DismissRecoveredStatus();
        launched = WithCurrentCompact(launched);
        var error = Launcher.Validate([launched]);
        var invalid = error != "";
        try
        {
            if (error == "")
            {
                session.QueueNext(track.Id, Launcher.RestartScript(launched, track with { UpdateClis = _cfg.AutoUpdateClis, AutoClear = _cfg.AutoClearIdle, Logo = _cfg.ShowTabLogo, StatusChannel = _cfg.TabStatusChannel }, resume));
                tab.LaunchedAs = launched;
                session.SaveTab(track.Id, launched);
                tab.RaiseRestartTip();
                var distro = _cfg.WslDistro;
                var name = tab.Name.Trim();
                // The idle child may still be starting after cli-exit (it sources ~/.bashrc first). Its own PID/start
                // time prevents a delayed wake from interrupting the newly restarted CLI; a restart it cannot deliver
                // is taken back and reported, never run later by surprise.
                Task.Run(() =>
                {
                    try
                    {
                        if (!Launcher.WakeIdleShell(track, distro))
                            Dispatcher.UIThread.Post(() => ShowError(ErrorKind.Restart, Loc.Format("err.restartUndelivered", name), () => Restart(tab), tab));
                    }
                    catch (Exception e) when (e is IOException or System.ComponentModel.Win32Exception)
                    {
                        Dispatcher.UIThread.Post(() => ShowError(ErrorKind.Restart, Loc.Format("err.launch", e.Message), () => Restart(tab), tab));
                    }
                });
                tab.PendingRestart = false;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            error = Loc.Format("err.launch", e.Message);
        }
        if (invalid) ShowError(ErrorKind.InvalidTab, error);
        else if (error != "") ShowError(ErrorKind.Restart, error, () => Restart(tab, resume), tab);
        else ShowStatus(resume
            ? Loc.Format("status.restartedResume", tab.Name.Trim(), launched.Llm,
                launched.Model == Catalog.StandardModel ? Loc.Get("restart.standardModel") : launched.Model)
            : "");
        return true;
    }

    private TabConfig WithCurrentCompact(TabConfig tab)
    {
        if (tab.CompactTokens == CompactTokens(tab.Llm, tab.Model, tab.Orkester, tab.AccountDirectory)) return tab;
        var copy = ConfigStore.Copy(new AppConfig { Tabs = [tab] }).Tabs[0];
        copy.CompactTokens = CompactTokens(tab.Llm, tab.Model, tab.Orkester, tab.AccountDirectory);
        return copy;
    }

    private void RestartClosed(IReadOnlyList<(TabViewModel Row, TabConfig Tab)> rows, bool resume = false)
    {
        if (rows.Count == 0) return;
        rows = rows.Select(r => (r.Row, WithCurrentCompact(r.Tab))).ToList();
        var cfg = new AppConfig { WslDistro = _cfg.WslDistro, LinuxTerminal = _cfg.LinuxTerminal, ZoomPercent = _cfg.ZoomPercent, Tabs = rows.Select(r => r.Tab).ToList() };
        var error = Start(cfg, rows, _session?.WindowName, resume);
        var first = rows[0];
        ShowLaunchOutcome(error, cfg.Tabs, () => RestartClosed(rows, resume), resume
            ? Loc.Format("status.restartedResume", first.Row.Name.Trim(), first.Tab.Llm,
                first.Tab.Model == Catalog.StandardModel ? Loc.Get("restart.standardModel") : first.Tab.Model)
            : "");
    }

    private TabSession? _session;

    /// <summary>
    /// The command that shows <paramref name="tab"/>'s terminal tab (#213): its position in the session window as BoosterAI
    /// last arranged or saw it (#254), among the tabs that still have one; tabs it has no position for count by start order.
    /// </summary>
    internal TerminalFocus.Command? FocusCommand(TabViewModel tab)
    {
        if (tab.Session is not { } session || tab.TrackId is not { } id) return null;
        var at = BelievedWindow(session).IndexOf(id);
        return TerminalFocus.Build(session.WindowName, at >= 0 ? at : null, OperatingSystem.IsWindows());
    }

    /// <summary>Switches to the tab's terminal tab; false when no command could be built.</summary>
    public bool ShowTerminal(TabViewModel tab)
    {
        if (tab.ShowTerminalCommand is not { } command) return false;
        TerminalFocus.Run(command);
        return true;
    }
    private DispatcherTimer? _poll;
    private bool _polling;
    /// <summary>The previous poll's /proc scan, the baseline for each tab's CPU share (#215); null: none yet.</summary>
    private ProcSnapshot? _procs;

    /// <summary>Launches with a status log per tab (one session per window) and starts watching.</summary>
    private string Start(AppConfig cfg, IReadOnlyList<(TabViewModel Row, TabConfig Tab)> rows, string? window, bool resume = false)
    {
        DismissRecoveredStatus();
        try { _session ??= new TabSession(TabSession.DefaultRoot, DateTime.Now, TabSession.Liveness(_cfg.WslDistro)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _session = null; }
        var tracks = rows.ToDictionary(r => r.Tab, r => _session?.NewTrack(_cfg.AutoUpdateClis, _cfg.AutoClearIdle, _cfg.ShowTabLogo, _cfg.TabStatusChannel));
        var target = window ?? _session?.WindowName ?? "new";
        // The window's tabs before this launch (#254): the new ones are appended after them.
        var before = _session is { } current && target == current.WindowName ? LiveRows(current).Select(t => t.TrackId!).ToList() : null;
        var error = Launcher.Launch(cfg, track: t => tracks.GetValueOrDefault(t), window: target, resume: resume);
        if (error != "")
        {
            if (_cfg.Notifications) Notifier.Show("BoosterAI", error);
            PlaySound(SoundEffect.Error);
            return error;
        }
        foreach (var (row, tab) in rows)
        {
            if (tracks[tab] is not { } track) continue;
            _session!.SaveTab(track.Id, tab);
            row.LaunchedAs = tab;
            row.Session = _session;
            row.Track = track;
            row.ShellPid = null;
            row.PendingRestart = false;
            row.LaunchedAt = DateTime.UtcNow;
            row.RaiseRestartTip();
            row.SetState(TabState.Starting, null);
        }
        if (before is not null && TerminalOrder.Supported)
            TabsAppended(_session!, before, TabGroups.Flatten(cfg.Tabs).Where(t => t.Enabled).Select(t => tracks.GetValueOrDefault(t)?.Id).OfType<string>().ToList());
        StartPolling();
        TabsChanged();
        return "";
    }

    private void StartPolling()
    {
        _poll ??= new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, (_, _) => Poll());
        _poll.Start();
    }

    /// <summary>
    /// At start: rejoins live tabs and restores known ended tabs for explicit restart, regardless of orchestra.
    /// Matches stable identity (old metadata: main-tab name and directory). Never starts anything by itself.
    /// </summary>
    public void FindRunningTabs()
    {
        var statusRevision = StatusRevision;
        var distro = _cfg.WslDistro;
        Task.Run(() => TabSession.FindRunning(TabSession.DefaultRoot, TabSession.Liveness(distro), includeEnded: true)).ContinueWith(t =>
        {
            if (!t.IsCompletedSuccessfully || t.Result.Count == 0) return;
            var joined = RestoreTabs(t.Result);
            if (joined.Count == 0) return;
            StartPolling();
            TabsChanged();
            ShowRecoveredStatus(joined, statusRevision);
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    internal List<TabViewModel> RestoreTabs(IReadOnlyList<TabSession.RunningTab> foundTabs)
    {
        var sessions = new Dictionary<string, TabSession>();
        var joined = new List<TabViewModel>();
        foreach (var found in foundTabs)
        {
            if (Match(found.Tab) is not { } row) continue;
            if (!found.ShellAlive)
            {
                row.LaunchedAs = found.Tab;
                row.SetState(found.Log.Exited ? TabState.Closed : TabState.Lost, found.Log.CliExit);
                joined.Add(row);
                continue;
            }
            try
            {
                if (!sessions.TryGetValue(found.SessionDirectory, out var session))
                    sessions[found.SessionDirectory] = session = TabSession.Open(found.SessionDirectory);
                // The newest session is first: new tabs and reopened ones join its window.
                _session ??= session;
                row.LaunchedAs = found.Tab;
                row.Session = session;
                row.Track = session.Track(found.Id);
                row.ShellPid = found.Log.Pid;
                row.LaunchedAt = DateTime.UtcNow - TabStatus.StartTimeout;
                row.RaiseRestartTip();
                row.SetState(TabStatus.Evaluate(found.Log, TabStatus.StartTimeout, null), found.Log.CliExit, null,
                    session.ReadActivity(found.Id, found.Log, DateTime.UtcNow), found.Log.WatchingSince);
                joined.Add(row);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return joined;
    }

    /// <summary>Stable identity survives renames and equal folders/names. Old session metadata falls back to the original project row.</summary>
    internal TabViewModel? Match(TabConfig running) =>
        Tabs.FirstOrDefault(t => t.LaunchedAs is null && (string.IsNullOrWhiteSpace(running.Id)
            ? t.IsProject && t.Name.Trim() == running.Name.Trim() && t.Directory.Trim() == running.Directory.Trim()
            : t.Id == running.Id));

    /// <summary>Reads the logs and checks liveness off the UI thread (UNC reads can take a few hundred ms).</summary>
    private void Poll()
    {
        var watched = Tabs.Where(t => t.TrackId is not null && t.Session is not null)
            .Select(t => (Row: t, Id: t.TrackId!, t.LaunchedAt, Session: t.Session!, Name: t.Name)).ToList();
        if (_polling || watched.Count == 0) return;
        _polling = true;
        var distro = _cfg.WslDistro;
        var settingsPath = AgySettingsFile;
        var previousProcs = _procs;
        var channelOn = _cfg.TabStatusChannel;
        Task.Run(() =>
        {
            var live = TabSession.Liveness(distro);
            var results = watched.Select(w =>
            {
                var log = w.Session.ReadLog(w.Id);
                var denied = log?.Denied?.SelectMany(AgyPermissions.Programs).Distinct().ToList();
                var now = DateTime.UtcNow;
                var state = TabStatus.Evaluate(log, now - w.LaunchedAt, live);
                return (w.Row, w.Id, State: state, log?.CliExit, Denied: denied,
                    log?.Pid, Activity: w.Session.ReadActivity(w.Id, log, now), log?.WatchingSince, Log: log, LastOutput: w.Session.LastOutput(w.Id));
            }).ToList();
            TabTitles.Restore(results.Zip(watched)
                .Where(pair => pair.First.Log is { Pid: > 0, StartTime: > 0, Exited: false }
                    && pair.First.State is not (TabState.Lost or TabState.Closed or TabState.NotStarted))
                .Select(pair => new TabTitles.Target(pair.First.Log!.Pid!.Value,
                    pair.First.Log.StartTime!.Value, pair.Second.Name)).ToList(), distro);
            // agy's settings are only read while a tab has a denial to offer rules for.
            var settings = results.Any(r => r.State == TabState.Denied) ? ReadText(settingsPath) : null;
            // CPU activity (#215): one /proc scan for all running tabs, compared with the previous poll's.
            var procs = results.Any(r => r.State == TabState.Running) ? ProcSnapshot.Read(ProcSnapshot.Root(distro)) : null;
            var cpu = results.Select(r => procs is not null && r is { State: TabState.Running, Log.Pid: { } shell }
                ? TabCpu.Measure(previousProcs, procs, shell, r.Log.StartTime) : null).ToList();
            // The status channel (#249): what each tab's agent reported; nothing is read while the setting is off.
            var channels = watched.Select(w => channelOn ? w.Session.ReadChannel(w.Id) : TabChannelState.Empty).ToList();
            return (results, settings, procs, cpu, channels);
        }).ContinueWith(t =>
        {
            _polling = false;
            if (!t.IsCompletedSuccessfully) return;
            var (results, settings, procs, cpu, channels) = t.Result;
            _procs = procs;
            var anyActivityChanged = false;
            foreach (var ((row, id, state, rc, denied, pid, hookActivity, watchingSince, log, lastOutput), sample) in results.Zip(cpu))
            {
                // Ignore a result for a tab that has been restarted meanwhile.
                if (row.TrackId != id) continue;
                var activity = TabCpu.Combine(hookActivity, row.Cpu.Update(sample, DateTime.UtcNow));
                var previousActivity = row.Activity;
                row.ShellPid = pid;
                if (row.PendingRestart && state == TabState.Running && activity == Activity.Idle) RestartChanged(row);
                var changed = row.SetState(state, rc, denied, activity, activity == Activity.Watching ? watchingSince : null, AutoClear.ClearedAt(log));
                if (changed || row.Activity != previousActivity) anyActivityChanged = true;
                row.RefreshInputState(log, lastOutput);
                RequestAutoClear(row, id, state, log, lastOutput);
                if (!changed) continue;
                if (TabStatus.IsUnexpected(state, rc))
                {
                    if (_cfg.Notifications) Notifier.Show("BoosterAI", Loc.Format("notify.body", row.Name, row.StatusText));
                    PlaySound(SoundEffect.Finished);
                }
            }
            foreach (var ((row, id, _, _, _, _, _, _, _, _), channel) in results.Zip(channels))
                if (row.TrackId == id) UpdateChannel(row, channel);
            if (anyActivityChanged) _ = RefreshAccountLimitsAsync();
            // A tab that closed can be started again: the Start button counts it.
            TabsChanged();
            OfferRules(settings);
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>Queues one prompt to the existing PTY; it never calls Start, Restart or process-control methods.</summary>
    public bool ReloadOrchestra(TabViewModel row)
    {
        if (!row.CanReloadOrchestra || row.Session is not { } session || row.TrackId is not { } id) return false;
        try
        {
            var log = session.ReadLog(id);
            var live = TabSession.Liveness(_cfg.WslDistro);
            if (log is null || live is null || !TabSession.IsAlive(log, live)) return false;
            var due = OrchestraReload.Due(row.LaunchedAs, TabStatus.Evaluate(log, DateTime.UtcNow - row.LaunchedAt, live),
                TabCpu.Combine(session.ReadActivity(id, log, DateTime.UtcNow), row.Cpu.Active), row.PendingRestart, IsClosingAll || row.IsClosing,
                log, session.LastOutput(id), row.OrchestraRequestedFor, DateTime.UtcNow);
            if (due is null || !session.TryRequestInput(id, due.Value.Since, OrchestraReload.Command, log, "orkester")) return false;
            row.OrchestraRequestedFor = due.Value.IdleAt;
            row.RaiseRestartTip();
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>
    /// Types /clear into a tab whose claude has been idle for the configured time (AutoClear.Due), once per idle period.
    /// The tab's bs_pty picks the request up within a second and logs whether it typed it; the status then says so.
    /// </summary>
    private void RequestAutoClear(TabViewModel row, string id, TabState state, TabLog? log, DateTime? lastOutput)
    {
        if (row.Session is not { } session || row.OrchestraRequestedFor == log?.IdleAt && log?.IdleAt is not null) return;
        if (AutoClear.Due(_cfg.AutoClearIdle, _cfg.AutoClearMinutes, row.LaunchedAs, state, row.Activity, row.PendingRestart,
                IsClosingAll || row.IsClosing, log, lastOutput, row.AutoClearRequestedFor, DateTime.UtcNow, row.Cpu.ActiveAt) is not { } due) return;
        // Asked once per idle period even when the write fails: a tab that cannot take it is not asked every poll.
        row.AutoClearRequestedFor = due.IdleAt;
        try
        {
            if (log is { InputProtocol: >= 2 }) session.TryRequestInput(id, due.Since, AutoClear.Command, log, "clear");
            else session.RequestInput(id, due.Since, AutoClear.Command);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>agy's settings.json as this process can read it; null until the WSL home is known.</summary>
    private string? AgySettingsFile => PathMapping.ToLocalPath(AgyPermissions.SettingsPath, _cfg.WslDistro, _wslHome);

    private static string? ReadText(string? path)
    {
        try { return path is not null && File.Exists(path) ? File.ReadAllText(path) : null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    private readonly HashSet<string> _dismissedPrograms = [];

    /// <summary>The denied tabs and the programs agy's settings do not allow yet (minus those dismissed this session).</summary>
    private List<(TabViewModel Row, IReadOnlyList<string> Missing)> DeniedRows(string? settings) =>
        Tabs.Where(t => t.DeniedPrograms.Count > 0 && t.LaunchedAs?.Llm == "agy")
            .Select(t => (Row: t, Missing: AgyPermissions.Missing(settings, t.DeniedPrograms.Except(_dismissedPrograms))))
            .Where(r => r.Missing.Count > 0).ToList();

    private void OfferRules(string? settings)
    {
        var rows = DeniedRows(settings);
        if (rows.Count == 0) { Denial.Hide(); return; }
        Denial.Offer(rows.SelectMany(r => r.Missing).Distinct().ToList(), rows.Select(r => r.Row.Name.Trim()).ToList());
    }

    /// <summary>Adds allow-rules for the offered programs to agy's settings and runs the denied tabs again.</summary>
    public void AllowDenied()
    {
        if (AgySettingsFile is not { } path) { ShowError(ErrorKind.WslHome, Loc.Get("status.wslHomeMissing"), AllowDenied); return; }
        try
        {
            var current = File.Exists(path) ? File.ReadAllText(path) : null;
            var rows = DeniedRows(current);
            var programs = rows.SelectMany(r => r.Missing).Distinct().ToList();
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            // Written in place, so the file keeps its permissions (agy creates it 0600).
            File.WriteAllText(path, AgyPermissions.AddRules(current, programs));
            Denial.Hide();
            ShowStatus(Loc.Format("status.rulesAdded", string.Join(", ", programs)));
            var closed = new List<(TabViewModel Row, TabConfig Tab)>();
            foreach (var (row, _) in rows)
                if (row.LaunchedAs is { } launched && !RestartExited(row, launched, resume: false)) closed.Add((row, launched));
            RestartClosed(closed);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            ShowError(ErrorKind.Rules, Loc.Format("status.rulesFailed", path, e.Message), AllowDenied);
        }
    }

    /// <summary>"Not now": the offered programs are not offered again until BoosterAI restarts.</summary>
    public void DismissDenied()
    {
        _dismissedPrograms.UnionWith(Denial.Programs);
        Denial.Hide();
    }

    /// <summary>Shows the tab's directory in Explorer, Finder or the Linux file manager.</summary>
    public void OpenFolder(TabViewModel tab)
    {
        var local = PathMapping.ToLocalPath(tab.Directory, _cfg.WslDistro, _wslHome);
        if (local is null) { ShowError(ErrorKind.WslHome, Loc.Get("status.wslHomeMissing"), () => OpenFolder(tab)); return; }
        if (!System.IO.Directory.Exists(local)) { ShowError(ErrorKind.Folder, Loc.Format("status.dirMissing", tab.Directory)); return; }
        try
        {
            var opener = OperatingSystem.IsWindows() ? "explorer.exe" : OperatingSystem.IsMacOS() ? "open" : "xdg-open";
            var psi = new ProcessStartInfo(opener) { UseShellExecute = false };
            psi.ArgumentList.Add(local);
            Process.Start(psi);
        }
        catch (Exception e)
        {
            ShowError(ErrorKind.Folder, Loc.Format("status.openFailed", e.Message));
        }
    }

    /// <summary>Opens one tab whose AI is asked to install the orchestra in that project.</summary>
    public void InstallOrkester(TabViewModel tab)
    {
        var cfg = new AppConfig
        {
            WslDistro = _cfg.WslDistro, LinuxTerminal = _cfg.LinuxTerminal, ZoomPercent = _cfg.ZoomPercent,
            Tabs = [tab.ToConfig(forLaunch: false)],
        };
        cfg.Tabs[0].Enabled = true;
        cfg.Tabs[0].Name = tab.Name.Trim() + " (" + Loc.Get("tab.installSuffix") + ")";
        // Tracked like a started tab (not monitored), so its agent gets the same --settings, e.g. agy's quota status line (#212).
        TabTrack? track = null;
        try
        {
            _session ??= new TabSession(TabSession.DefaultRoot, DateTime.Now, TabSession.Liveness(_cfg.WslDistro));
            track = _session.NewTrack(_cfg.AutoUpdateClis, logo: _cfg.ShowTabLogo);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* untracked: the plain command */ }
        var error = Launcher.Launch(cfg, Launcher.InstallOrkesterPrompt, _ => track);
        // The files appear once the agent has worked; reopening BoosterAI or editing the path rechecks.
        ShowLaunchOutcome(error, cfg.Tabs, () => InstallOrkester(tab), Loc.Format("status.installRunning", tab.Name.Trim()));
        if (error != "")
        {
            if (_cfg.Notifications) Notifier.Show("BoosterAI", error);
            PlaySound(SoundEffect.Error);
        }
    }
}

/// <summary>The quiet bar offering allow-rules for commands agy denied (see MainViewModel.AllowDenied).</summary>
public sealed class DenialViewModel : Observable
{
    private bool _visible;
    private string _text = "";

    public bool IsVisible { get => _visible; private set => Set(ref _visible, value); }
    public string Text { get => _text; private set => Set(ref _text, value); }
    public IReadOnlyList<string> Programs { get; private set; } = [];
    public string AllowTip => Loc.Format("denied.allow.tip", AgyPermissions.SettingsPath,
        string.Join(", ", Programs.Select(p => $"command({p})")));

    public void Offer(IReadOnlyList<string> programs, IReadOnlyList<string> tabs)
    {
        Programs = programs;
        Text = Loc.Format("denied.offer", string.Join(", ", programs), string.Join(", ", tabs));
        Raise(nameof(AllowTip));
        IsVisible = true;
    }

    public void Hide() => IsVisible = false;
}

/// <summary>One entry of the language picker: "auto", "sv" or "en".</summary>
public sealed record LanguageChoice(string Id)
{
    public string Label => Loc.Get("language." + Id);
}

/// <summary>One entry of the appearance picker: "auto", "light" or "dark".</summary>
public sealed record ThemeChoice(string Id)
{
    public string Label => Loc.Get("theme." + Id);
}

/// <summary>One entry of the "After Start" picker: "keep", "minimize" or "close".</summary>
public sealed record AfterLaunchChoice(string Id)
{
    public string Label => Loc.Get("afterLaunch." + Id);
}

/// <summary>One entry of the zoom picker; 0 means the terminal's default size.</summary>
public sealed record ZoomChoice(int Percent)
{
    public string Label => Percent == 0 ? Loc.Get("zoom.default") : Loc.Format("zoom.percent", Percent);
}

/// <summary>
/// The discreet "Version X finns" bar in the main window: a background check at start, when the window is
/// activated and every Interval while it stays active, and the verified self-update (only for a copy in the
/// tools/install-* location).
/// </summary>
public sealed class UpdateViewModel : Observable
{
    private readonly AppConfig _cfg;
    private readonly Action _save;
    private readonly bool _windows = OperatingSystem.IsWindows();
    private UpdateClient? _client;
    private DispatcherTimer? _timer;
    private ReleaseInfo? _release;
    private ReleaseAsset? _asset;
    private AppVersion? _laterVersion;
    private DateTime? _laterUtc;
    private bool _active;
    private bool _visible, _busy, _checking, _canInstall, _failed;
    private string _text = "";

    public UpdateViewModel(AppConfig cfg, Action save)
    {
        _cfg = cfg;
        _save = save;
    }

    public bool IsVisible { get => _visible; private set => Set(ref _visible, value); }
    public string Text { get => _text; private set => Set(ref _text, value); }
    public bool IsBusy { get => _busy; private set { if (Set(ref _busy, value)) RaiseButtons(); } }
    /// <summary>Whether this copy can replace itself; otherwise the bar links to the releases page.</summary>
    public bool CanInstall { get => _canInstall; private set { if (Set(ref _canInstall, value)) RaiseButtons(); } }
    public bool Failed { get => _failed; private set { if (Set(ref _failed, value)) RaiseButtons(); } }
    public bool ShowInstall => CanInstall && !IsBusy && !Failed;
    public bool ShowPage => (!CanInstall || Failed) && !IsBusy;
    public bool ShowDismiss => !IsBusy;
    public string PageUrl => _release?.PageUrl ?? UpdateLogic.ReleasesPage;
    /// <summary>The release's one-line summary in the app language; null (nothing shown) for releases without one.</summary>
    public string? Summary => _release?.SummaryFor(Loc.Language);
    public bool HasSummary => Summary is not null && !IsBusy && !Failed;
    /// <summary>"Show more" and the version link: the release page from the running version to the new release, or
    /// without one what is new in the running version since the version it was updated from.</summary>
    public string MoreUrl => UpdateLogic.MoreUrl(_release, UpdateLogic.Current, AppVersion.Parse(_cfg.UpdatedFromVersion), Loc.Language);
    /// <summary>The from/to that "Show more" and the version link open in the What's new view (#252); same rule as <see cref="MoreUrl"/>.</summary>
    public ReleaseRange MoreRange => UpdateLogic.MoreRange(_release, UpdateLogic.Current, AppVersion.Parse(_cfg.UpdatedFromVersion));
    /// <summary>The release the bar offers, if any: the view shows its summary when the release data does not have it yet.</summary>
    public ReleaseInfo? OfferedRelease => _release;
    public string InstallTip => Loc.Format("update.install.tip", _release?.Version.ToString() ?? "");

    private void RaiseButtons()
    {
        Raise(nameof(ShowInstall));
        Raise(nameof(ShowPage));
        Raise(nameof(ShowDismiss));
        Raise(nameof(HasSummary));
    }

    private string InstalledPath => UpdateLogic.InstallPath(_windows,
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    private bool IsInstalledCopy => UpdateLogic.IsInstalledCopy(Environment.ProcessPath, _windows,
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    /// <summary>Called once the main window exists: tidies a previous update, checks now and then each minute while the window is active.</summary>
    public void Start()
    {
        if (UpdateLogic.NoteRun(_cfg, UpdateLogic.Current)) { _save(); Raise(nameof(MoreUrl)); }
        if (IsInstalledCopy)
            foreach (var leftover in new[] { InstalledPath + ".new", InstalledPath + ".new.part", InstalledPath + ".old" })
                try { File.Delete(leftover); } catch (Exception) { /* still locked; the next start tries again */ }
        if (_cfg.CheckForUpdates) _ = CheckAsync();
        _timer = new DispatcherTimer(TimeSpan.FromMinutes(1), DispatcherPriority.Background, (_, _) =>
        {
            if (_active) CheckIfDue();
        });
        _timer.Start();
    }

    /// <summary>The main window gained (true) or lost focus: an inactive or minimized window never checks.</summary>
    public void WindowActive(bool active)
    {
        _active = active;
        if (active && _timer is not null) CheckIfDue();
    }

    private void CheckIfDue()
    {
        if (_cfg.CheckForUpdates && UpdateLogic.IsDue(_cfg.LastUpdateCheck, DateTime.UtcNow)) _ = CheckAsync();
    }

    /// <summary>The setting was toggled: turning it on checks if one is due; turning it off hides the bar.</summary>
    public void SettingChanged()
    {
        if (!_cfg.CheckForUpdates) { if (!IsBusy) IsVisible = false; return; }
        if (_timer is not null && UpdateLogic.IsDue(_cfg.LastUpdateCheck, DateTime.UtcNow)) _ = CheckAsync();
    }

    private async Task CheckAsync()
    {
        if (_checking || IsBusy || IsVisible) return;
        _checking = true;
        try
        {
            _client ??= new UpdateClient(UpdateClient.CreateHttp());
            var client = _client;
            var latest = await Task.Run(() => client.LatestAsync());
            // A failed request is retried at the next tick or activation, not counted as a check.
            if (latest is null) return;
            _cfg.LastUpdateCheck = DateTime.UtcNow;
            _save();
            if (_cfg.CheckForUpdates && UpdateLogic.ShouldOffer(latest, UpdateLogic.Current, _cfg.SkippedVersion)
                && !UpdateLogic.IsSnoozed(latest.Version, _laterVersion, _laterUtc, DateTime.UtcNow))
                Offer(latest, IsInstalledCopy);
        }
        catch (Exception) { /* silent: the check never interrupts */ }
        finally { _checking = false; }
    }

    private void Offer(ReleaseInfo release, bool installedCopy)
    {
        _release = release;
        _asset = UpdateLogic.SelectAsset(release.Assets, release.Version, UpdateLogic.RuntimeId(_windows, RuntimeInformation.OSArchitecture));
        CanInstall = installedCopy && _asset is not null;
        Failed = false;
        Text = Loc.Format(CanInstall ? "update.available" : "update.manual", release.Version);
        Raise(nameof(PageUrl));
        Raise(nameof(InstallTip));
        Raise(nameof(Summary));
        Raise(nameof(MoreUrl));
        RaiseButtons();
        IsVisible = true;
    }

    /// <summary>For --screenshot --fake-update 0.3.0: shows the bar as an installed copy would, without any network;
    /// "manual:0.3.0" shows it as a copy that cannot update itself (releases link); "plain:" leaves out the sample summary.</summary>
    public void ShowFake(string version)
    {
        var manual = version.StartsWith("manual:", StringComparison.Ordinal);
        if (manual) version = version["manual:".Length..];
        var plain = version.StartsWith("plain:", StringComparison.Ordinal);
        if (plain) version = version["plain:".Length..];
        var v = AppVersion.Parse(version) ?? new AppVersion(9, 9, 9, "");
        var rid = UpdateLogic.RuntimeId(_windows, RuntimeInformation.OSArchitecture) ?? "linux-x64";
        var name = $"BoosterAI-{v}-{rid}{(rid.StartsWith("win-") ? ".zip" : ".tar.gz")}";
        Offer(new ReleaseInfo("v" + v, v, false, UpdateLogic.ReleasesPage, [new(name, ""), new(UpdateLogic.SumsName, "")],
            plain ? null : "Nya flikar behåller sitt namn och en omstartad CLI ritas om rätt.",
            plain ? null : "Tabs keep their names and a restarted CLI redraws correctly."), installedCopy: !manual);
    }

    /// <summary>"Senare": this version stays hidden for UpdateLogic.Snooze (until the app restarts); a newer one shows at once.</summary>
    public void Later()
    {
        if (IsBusy) return;
        _laterVersion = _release?.Version;
        _laterUtc = DateTime.UtcNow;
        IsVisible = false;
    }

    /// <summary>"Hoppa över den här versionen": saved, never offered again (a newer one still is).</summary>
    public void Skip()
    {
        if (IsBusy || _release is null) return;
        _cfg.SkippedVersion = _release.Version.ToString();
        _save();
        IsVisible = false;
    }

    /// <summary>Downloads, verifies and stages the new version, then starts the helper; true when the app should now exit.</summary>
    public async Task<bool> InstallAsync()
    {
        if (IsBusy || !CanInstall || _release is not { } release || _asset is not { } asset || !IsInstalledCopy) return false;
        IsBusy = true;
        Failed = false;
        Text = Loc.Format("update.downloading", release.Version);
        try
        {
            var target = InstalledPath;
            var staged = target + ".new";
            var work = Path.Combine(Path.GetTempPath(), "BoosterAI-update-" + release.Version);
            var log = Path.Combine(Path.GetDirectoryName(ConfigStore.PathOnDisk)!, "update.log");
            _client ??= new UpdateClient(UpdateClient.CreateHttp());
            var client = _client;
            await Task.Run(() => client.StageAsync(release, asset, _windows, work, staged));
            UpdateClient.LaunchHelper(_windows, Environment.ProcessId, target, staged, work, log);
            Text = Loc.Format("update.restarting", release.Version);
            return true;
        }
        catch (Exception e)
        {
            Text = Loc.Format("update.failed", e is UpdateException ? e.Message : e.GetType().Name + ": " + e.Message);
            Failed = true;
            IsBusy = false;
            return false;
        }
    }
}

/// <summary>One folder in the "Hitta projekt" list; it starts unticked so nothing is imported unless chosen.</summary>
public sealed class ImportItem(FoundProject project, Action changed) : Observable
{
    private bool _selected;

    public FoundProject Project { get; } = project;
    public string Name => Project.Name;
    public string Path => Project.Path;
    public string Kind => Project switch { { Git: true, Ai: true } => "git · AI", { Git: true } => "git", _ => "AI" };
    /// <summary>"Saknar orkester", "Har orkester, version okänd" or "Har orkester, version 0.9.0".</summary>
    public string Orchestra => Project switch
    {
        { Orchestra: false } => Loc.Get("import.orchestra.none"),
        { OrchestraVersion: { } v } => Loc.Format("import.orchestra.version", v),
        _ => Loc.Get("import.orchestra.unknown"),
    };
    public bool HasOrchestra => Project.Orchestra;
    public bool Selected { get => _selected; set { if (Set(ref _selected, value)) changed(); } }
}

/// <summary>The "Hitta projekt" dialog: scans a folder in the background and lists the projects not added yet.</summary>
public sealed class ImportViewModel : Observable, IProjectList
{
    private readonly HashSet<string> _existing;
    private CancellationTokenSource? _scan;
    private string _root = "", _message = "";
    private bool _scanning, _messageIsError;

    /// <param name="existing">Local paths of the projects already added; they are left out of the list.</param>
    public ImportViewModel(IEnumerable<string> existing) =>
        _existing = [.. existing.Select(ProjectScanner.Key)];

    public ObservableCollection<ImportItem> Items { get; } = [];
    public string Root { get => _root; private set { if (Set(ref _root, value)) Raise(nameof(Hint)); } }
    public string Hint => Loc.Format("import.hint", Root);
    public string Message { get => _message; private set => Set(ref _message, value); }
    public bool MessageIsError { get => _messageIsError; private set => Set(ref _messageIsError, value); }
    public bool IsScanning { get => _scanning; private set => Set(ref _scanning, value); }
    public bool HasItems => Items.Count > 0;
    public int SelectedCount => Items.Count(i => i.Selected);
    public bool CanImport => SelectedCount > 0;
    public string ImportLabel => SelectedCount switch { 0 => Loc.Get("import.none"), var n => Loc.FormatCount("import", n) };
    public IReadOnlyList<FoundProject> Selected => [.. Items.Where(i => i.Selected).Select(i => i.Project)];

    /// <summary>The header check box: on when every row is ticked, mixed when some are; setting it ticks or clears all.</summary>
    public bool? AllSelected
    {
        get => Items.All(i => i.Selected) ? Items.Count > 0 : Items.Any(i => i.Selected) ? null : false;
        set { foreach (var i in Items) i.Selected = value ?? true; }
    }

    private void SelectionChanged()
    {
        Raise(nameof(SelectedCount));
        Raise(nameof(CanImport));
        Raise(nameof(ImportLabel));
        Raise(nameof(AllSelected));
    }

    public async Task ScanAsync(string root, bool includeRoot = true)
    {
        _scan?.Cancel();
        var cancel = (_scan = new CancellationTokenSource()).Token;
        Root = root;
        Show([]);
        MessageIsError = false;
        if (!Directory.Exists(root))
        {
            Message = Loc.Format("import.rootMissing", root);
            MessageIsError = true;
            IsScanning = false;
            return;
        }
        IsScanning = true;
        Message = Loc.Format("import.scanning", root);
        try
        {
            var found = await Task.Run(() => ProjectScanner.Scan(root, cancel: cancel, includeRoot: includeRoot), cancel);
            if (cancel.IsCancellationRequested) return;
            var fresh = found.Where(f => !_existing.Contains(ProjectScanner.Key(f.Path))).ToList();
            Show(fresh);
            Message = Loc.Format(found.Count == 0 ? "import.nothing" : "import.allAdded", root);
        }
        catch (OperationCanceledException) { /* a newer scan replaced this one */ }
        catch (Exception e) { Message = Loc.Format("import.failed", e.Message); MessageIsError = true; }
        finally { if (!cancel.IsCancellationRequested) IsScanning = false; }
    }

    public void Cancel() => _scan?.Cancel();

    /// <summary>For --screenshot --window import: a fixed list, no file system access.</summary>
    public void ShowSample(string root)
    {
        Root = root;
        var sep = System.IO.Path.DirectorySeparatorChar;
        Show([
            new(root + sep + "webshop", "webshop", Git: true, Ai: true, Orchestra: true, OrchestraVersion: "0.9.0"),
            new(root + sep + "recipes", "recipes", Git: true, Ai: false),
            new(root + sep + "notes" + sep + "research", "research", Git: false, Ai: true, Orchestra: true),
            new(root + sep + "work" + sep + "intranet", "intranet", Git: true, Ai: true, Orchestra: true, OrchestraVersion: "0.8.2"),
        ]);
    }

    private void Show(IReadOnlyList<FoundProject> projects)
    {
        Items.Clear();
        foreach (var p in projects) Items.Add(new ImportItem(p, SelectionChanged));
        Raise(nameof(HasItems));
        SelectionChanged();
    }
}

/// <summary>
/// One entry of a tab's Agent box (#194): an agent and one of its accounts (<paramref name="AccountId"/> "" is the default
/// account). <paramref name="ShowAccount"/> is true for every entry of an agent with accounts besides its default one, which then
/// shows <paramref name="AccountName"/> as a label after the agent; <paramref name="Tip"/> describes that account.
/// <paramref name="Limit"/> is the account's plan-limit indicator (#200), null when unknown; the closed box shows it as a meter (#202).
/// <paramref name="Note"/> is the list's "Okänd"/"Troligen återställd" with its reason when a limit-capable entry has no known level (#214).
/// </summary>
public sealed record AgentChoice(string Llm, string AccountId, string AccountName, bool ShowAccount, string? Tip, LimitIndicator? Limit = null, LimitNote? Note = null)
{
    public bool HasLimit => Limit is not null;
    /// <summary>The reading's time beside an old level in the list (#214), "i går 18:10"; empty for a recent one.</summary>
    public string LimitStale => Limit?.Stale ?? "";
    public bool HasLimitStale => LimitStale.Length > 0;
    public bool HasLimitNote => Limit is null && Note is not null;
    public string LimitNoteCaption => Note?.Caption ?? "";
    public string? LimitNoteTip => Note?.Tip;
    public string LimitNoteName => Note?.Name ?? "";
    public bool LimitWarn => Limit?.Warn == true;
    public bool LimitCalm => Limit is { Warn: false };
    public string LimitCaption => Limit?.Caption ?? "";
    /// <summary>The closed box's meter (#202): how full it is (the higher of the two windows), 0 and hidden while unknown.</summary>
    public double LimitPercent => Limit?.Percent ?? 0;
    public string? LimitTip => Limit?.Tip;
    public string LimitName => Limit?.Name ?? "";
    /// <summary>
    /// The two meters under the agent name (#219): the 5 h window on top, the week below, in the closed box and in each list entry.
    /// Shown for a limit-capable entry; a window without a known level (unknown or reset, #214) is an empty, muted track.
    /// </summary>
    public bool HasMeters => Limit is not null || Note is not null;
    public LimitMeter SessionMeter => Limit is { Rows.Count: > 0 } l ? LimitMeter.From(l.Rows[0]) : LimitMeter.Empty;
    public LimitMeter WeekMeter => Limit is { Rows.Count: > 1 } l ? LimitMeter.From(l.Rows[1]) : LimitMeter.Empty;
    public double SessionMeterPercent => SessionMeter.Percent;
    public double WeekMeterPercent => WeekMeter.Percent;
    public bool SessionMeterWarn => SessionMeter.Warn;
    public bool WeekMeterWarn => WeekMeter.Warn;
    public bool SessionMeterUnknown => !SessionMeter.Known && !SessionMeter.Absent;
    public bool WeekMeterUnknown => !WeekMeter.Known && !WeekMeter.Absent;
    /// <summary>A window the agent does not have (#231, Codex with only a weekly window): its track is not drawn, and it is not "unknown".</summary>
    public bool SessionMeterAbsent => SessionMeter.Absent;
    public bool WeekMeterAbsent => WeekMeter.Absent;
    /// <summary>The meters' tooltip: both windows with use and reset time, or why the level is unknown.</summary>
    public string? MeterTip => Limit?.Tip ?? Note?.Tip;
    /// <summary>What a screen reader reads for the meters: both windows by name with remaining, used and reset time.</summary>
    public string MeterName => Limit is { } l ? string.Join("; ", l.Rows.Select(r => r.Name)) : Note?.Name ?? "";
    /// <summary>What a screen reader reads: "claude, konto Work", or only the agent.</summary>
    public string Name => ShowAccount ? Loc.Format("agent.account.name", Loc.Display("llm", Llm), AccountName) : Loc.Display("llm", Llm);
    public override string ToString() => Name;
}
