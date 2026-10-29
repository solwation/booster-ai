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

public sealed class TabViewModel : Observable
{
    private readonly MainViewModel _owner;
    private string _name, _directory, _llm, _model;
    private int _colorIndex;
    private bool _enabled, _orkester, _nonInteractive;
    private bool? _hasOrkester;

    public TabViewModel(TabConfig c, MainViewModel owner, int index)
    {
        _owner = owner;
        _name = c.Name; _directory = c.Directory; _enabled = c.Enabled;
        _colorIndex = c.ColorIndex >= 0 ? c.ColorIndex : LegacyIndex(c.Color, index);
        _llm = Catalog.Llms.Contains(c.Llm) ? c.Llm : Catalog.DefaultLlm;
        _model = string.IsNullOrWhiteSpace(c.Model) ? Catalog.StandardModel : c.Model;
        _orkester = c.Orkester;
        _nonInteractive = c.NonInteractive;
    }

    public string Name { get => _name; set => Set(ref _name, value); }
    public bool Orkester
    {
        get => _orkester;
        set { if (Set(ref _orkester, value)) { Raise(nameof(CanNonInteractive)); Raise(nameof(OnceChecked)); } }
    }

    public bool NonInteractive { get => _nonInteractive; set { if (Set(ref _nonInteractive, value)) Raise(nameof(OnceChecked)); } }

    /// <summary>What the toggle shows: a stored choice looks off while it cannot apply (orchestra off).</summary>
    public bool OnceChecked { get => _nonInteractive && _orkester; set => NonInteractive = value; }

    /// <summary>One-shot mode applies to the orchestra command only, so it needs the orchestra on.</summary>
    public bool CanNonInteractive => _orkester && CanOrkester;
    public string Model { get => _model; set { if (Set(ref _model, value?.Trim() ?? "")) ChoiceChanged(); } }

    public string Directory
    {
        get => _directory;
        set { if (Set(ref _directory, value)) _owner.CheckOrkester(this); }
    }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (!Set(ref _enabled, value)) return;
            Raise(nameof(RowOpacity));
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
        }
    }

    public bool CanOrkester => _hasOrkester == true && IsAi;

    /// <summary>What the "Installera" link will do, for the confirmation dialog.</summary>
    public string InstallSummary => Loc.Format("confirm.summary", Llm,
        Model == Catalog.StandardModel ? Loc.Get("confirm.summary.standardModel") : Loc.Format("confirm.summary.model", Model));
    public string InstallTitle => Loc.Format("confirm.heading", Name);
    public string InstallButton => Loc.Format("confirm.button", Loc.Display("llm", Llm));
    public bool CanInstallOrkester => _hasOrkester == false && IsAi;

    public string Color => _owner.Theme.ColorAt(_colorIndex);
    public IBrush ColorBrush => new SolidColorBrush(Avalonia.Media.Color.Parse(Color));

    /// <summary>Called when the theme changes.</summary>
    public void RaiseColor()
    {
        Raise(nameof(Color));
        Raise(nameof(ColorBrush));
    }

    private static int LegacyIndex(string color, int fallback)
    {
        var i = Catalog.Themes[0].Colors.ToList().FindIndex(p => string.Equals(p, color, StringComparison.OrdinalIgnoreCase));
        return i >= 0 ? i : fallback;
    }

    /// <summary>Example shown in an empty directory field.</summary>
    public string DirectoryHint => Loc.Get(OperatingSystem.IsWindows() ? "dir.hint" : "dir.hint.unix");
    public IReadOnlyList<string> Llms => Catalog.Llms;
    public IReadOnlyList<string> Models => Catalog.ModelsFor(_llm);
    public bool IsAi => _llm != Catalog.ShellOnly;

    public string Llm
    {
        get => _llm;
        set
        {
            if (value is null || !Set(ref _llm, value)) return;
            Raise(nameof(Models));
            Raise(nameof(IsAi));
            Raise(nameof(CanOrkester));
            Raise(nameof(CanNonInteractive));
            Raise(nameof(CanInstallOrkester));
            // Another CLI's model id rarely fits; fall back to its default.
            if (!Catalog.ModelsFor(value).Contains(Model)) Model = Catalog.StandardModel;
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
    private bool _pending;
    private IReadOnlyList<string> _denied = [];
    internal TabConfig? LaunchedAs { get; set; }
    /// <summary>The session the tab's log is in; an earlier BoosterAI run's for a tab found running at start.</summary>
    internal TabSession? Session { get; set; }
    internal TabTrack? Track { get; set; }
    internal string? TrackId => Track?.Id;
    internal DateTime LaunchedAt { get; set; }
    /// <summary>The tab's shell, from its log; what an in-place restart stops the foreground of.</summary>
    internal int? ShellPid { get; set; }
    internal void RaiseRestartTip() => RaiseRestart();

    public bool HasStatus => _state is not null;
    internal bool HasOpenTerminal => Track is not null && _state is not null and not (TabState.Closed or TabState.Lost);
    public bool IsRunning => _state is TabState.Running;
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
        && (l.Llm != Llm || l.Model != (string.IsNullOrWhiteSpace(Model) ? Catalog.StandardModel : Model));

    /// <summary>"Starta om när den är ledig" is waiting for the CLI's next idle.</summary>
    public bool PendingRestart
    {
        get => _pending;
        internal set { if (Set(ref _pending, value)) RaiseRestart(); }
    }

    /// <summary>Whether the in-place restart keeps the conversation (model change on claude/codex).</summary>
    public bool RestartResumes => LaunchedAs is { } l && Launcher.CanResume(l, ToConfig(forLaunch: true));

    /// <summary>A one-shot tab is "run again" rather than "open again"; a changed one restarts with the new choices.</summary>
    public string RestartTip =>
        PendingRestart ? Loc.Get("restart.pending.tip")
        : IsChanged ? Loc.Format(RestartResumes ? "restart.changed.resume.tip" : "restart.changed.tip", Llm, ModelLabel)
        : Loc.Get(LaunchedAs is { NonInteractive: true, Orkester: true } ? "restart.once.tip"
            : LaunchedAs is { Llm: Catalog.ShellOnly } ? "restart.shell.tip" : "restart.tip");

    private string ModelLabel => string.IsNullOrWhiteSpace(Model) || Model == Catalog.StandardModel ? Loc.Get("restart.standardModel") : Model;

    public bool CanRestart => IsChanged || _state is TabState.Closed or TabState.CliExited or TabState.CliMissing
        or TabState.NotStarted or TabState.Lost or TabState.Denied;

    /// <summary>A separate action: reopen an ended interactive session with the latest conversation in its directory.</summary>
    public bool CanContinueClosed => CanRestart && !IsAlive && LaunchedAs is { } launched && Launcher.CanContinue(launched);
    public string ContinueTip => Loc.Get("restart.continue.tip");

    private void ChoiceChanged()
    {
        if (!IsChanged) PendingRestart = false;
        RaiseRestart();
        Raise(nameof(StatusText));
    }

    private void RaiseRestart()
    {
        foreach (var n in new[] { nameof(IsChanged), nameof(CanRestart), nameof(RestartTip), nameof(PendingRestart), nameof(CanContinueClosed) }) Raise(n);
        _owner.RestartableChanged();
    }
    /// <summary>The programs of the commands agy denied (state Denied).</summary>
    internal IReadOnlyList<string> DeniedPrograms => _denied;
    private string? _closeProgress;
    internal void SetCloseProgress(string? text)
    {
        _closeProgress = text;
        Raise(nameof(StatusText));
        Raise(nameof(StatusTip));
    }
    public string StatusText => _closeProgress ?? (_state switch
    {
        null => "",
        TabState.Running when PendingRestart => Loc.Get("tabstate.waitingIdle"),
        TabState.Running when IsChanged => Loc.Get("tabstate.changed"),
        TabState.Running when Activity == Activity.Busy => Loc.Get("tabstate.busy"),
        TabState.Running when Activity == Activity.Idle => Loc.Get("tabstate.idle"),
        TabState.Running when Activity == Activity.Watching => Loc.Format("tabstate.watching", TabStatus.WatchingMinutes(_watchingSince, DateTime.UtcNow)),
        TabState.Running => Loc.Get("tabstate.unknownActivity"),
        TabState.CliExited when _rc == 0 => Loc.Get("tabstate.idle"),
        TabState.CliExited => Loc.Format("tabstate.CliExited", _rc ?? -1),
        TabState.Denied => Loc.Format("tabstate.Denied", _denied.Count > 0 ? string.Join(", ", _denied) : "?"),
        var s => Loc.Get("tabstate." + s),
    });
    /// <summary>The status tooltip: what Watching means and when the last turn ended; otherwise the status itself.</summary>
    public string StatusTip => _state is TabState.Running && Activity == Activity.Watching && _watchingSince is { } since
        ? Loc.Format("tabstate.watching.tip", DateTimeOffset.FromUnixTimeMilliseconds(since).ToLocalTime().ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture))
        : StatusText;
    /// <summary>Widest line the output popup shows; longer ones end in "…" so the popup keeps its size.</summary>
    public const int OutputColumns = 100;

    private string _outputText = "";
    private bool _hasOutput;

    /// <summary>The status popup shows the tab's last lines only while its CLI lives (Running, whatever the activity).</summary>
    public bool ShowsOutput => _state is TabState.Running;

    /// <summary>The tab's last lines (see TabSession.ReadOutput), read only while the popup is open.</summary>
    public string OutputText { get => _outputText; private set => Set(ref _outputText, value); }

    /// <summary>Why the popup shows no output: nothing captured yet, or the CLI does not run; null when output is shown.</summary>
    public string? OutputNote => _state switch
    {
        TabState.Running => _hasOutput ? null : Loc.Get("output.none"),
        null or TabState.Starting => null,
        _ => Loc.Get("output.notRunning"),
    };

    public bool HasOutputNote => OutputNote is not null;

    /// <summary>The output box is shown: the CLI lives and something was captured.</summary>
    public bool HasOutput => ShowsOutput && _hasOutput;

    /// <summary>The popup's text; null lines: nothing captured.</summary>
    internal void SetOutput(IReadOnlyList<string>? lines)
    {
        var had = _hasOutput;
        _hasOutput = lines is { Count: > 0 };
        OutputText = lines is null ? "" : string.Join('\n', lines.Select(l => l.Length > OutputColumns ? l[..(OutputColumns - 1)] + "…" : l));
        if (had != _hasOutput) { Raise(nameof(OutputNote)); Raise(nameof(HasOutputNote)); Raise(nameof(HasOutput)); }
    }

    public IBrush StatusBrush => new SolidColorBrush(Avalonia.Media.Color.Parse(_state switch
    {
        TabState.Running when Activity == Activity.Watching => "#3B7DD8",
        TabState.Running => "#2E9E5B",
        TabState.Starting => "#8A8FA0",
        TabState.CliExited when _rc == 0 => "#8A8FA0",
        TabState.Closed => "#D08A1E",
        _ => "#D2453F",
    }));

    /// <summary>Updates the state; true when it changed.</summary>
    internal bool SetState(TabState state, int? rc, IReadOnlyList<string>? denied = null, Activity activity = Activity.Unknown,
        long? watchingSince = null)
    {
        denied ??= [];
        if (_state == state && _rc == rc && _denied.SequenceEqual(denied) && _activity == activity && _watchingSince == watchingSince)
        {
            // The elapsed minutes of Watching move on without a new event.
            if (activity == Activity.Watching) { Raise(nameof(StatusText)); Raise(nameof(StatusTip)); }
            return false;
        }
        var stateChanged = _state != state || _rc != rc || !_denied.SequenceEqual(denied);
        _state = state;
        _rc = rc;
        _denied = denied;
        _activity = activity;
        _watchingSince = watchingSince;
        if (state != TabState.Running) PendingRestart = false;
        foreach (var n in new[] { nameof(HasStatus), nameof(IsRunning), nameof(IsAlive), nameof(Activity), nameof(StatusText), nameof(StatusTip), nameof(StatusBrush),
            nameof(ShowsOutput), nameof(HasOutput), nameof(OutputNote), nameof(HasOutputNote) }) Raise(n);
        RaiseRestart();
        if (stateChanged) _owner.RecoveredTabChanged(this);
        return stateChanged;
    }

    public TabConfig ToConfig(bool forLaunch) => new()
    {
        Name = Name.Trim(), Color = Color, ColorIndex = _colorIndex, Directory = Directory.Trim(), Enabled = Enabled,
        Llm = Llm, Model = string.IsNullOrWhiteSpace(Model) ? Catalog.StandardModel : Model,
        Orkester = forLaunch ? Orkester && CanOrkester : Orkester,
        NonInteractive = NonInteractive,
    };
}

public sealed partial class MainViewModel : Observable
{
    private readonly AppConfig _cfg;
    private string _status = "";
    private bool _statusIsError;
    private string? _wslHome;

    public MainViewModel(AppConfig cfg)
    {
        _cfg = cfg;
        Tabs = new ObservableCollection<TabViewModel>(cfg.Tabs.Select((t, i) => new TabViewModel(t, this, i)));
        Tabs.CollectionChanged += (_, _) => TabsChanged();
        Update = new UpdateViewModel(cfg, Save);
        Denial = new DenialViewModel();
        _ = InitAsync();
    }

    public ObservableCollection<TabViewModel> Tabs { get; }
    public bool IsWindows => OperatingSystem.IsWindows();
    public bool IsLinux => !OperatingSystem.IsWindows();
    public IReadOnlyList<string> LinuxTerminals => Launcher.LinuxTerminals;
    /// <summary>The window title and the bottom-left label name the running build, so any report says which version it was.</summary>
    public string AppTitle => "BoosterAI " + UpdateLogic.Current;
    public string VersionText => "v" + UpdateLogic.Current;
    public string Status { get => _status; private set => Set(ref _status, value); }
    public bool StatusIsError { get => _statusIsError; private set => Set(ref _statusIsError, value); }

    // Every status publication invalidates pending recovery notices, even when its text is unchanged.
    internal long StatusRevision { get; private set; }
    private HashSet<TabViewModel>? _recoveredStatusRows;

    public void ShowStatus(string text, bool error)
    {
        StatusRevision++;
        _recoveredStatusRows = null;
        Status = text;
        StatusIsError = error;
    }

    /// <summary>A startup notice only: a newer status or the first recovered tab's state change retires it.</summary>
    internal void ShowRecoveredStatus(IEnumerable<TabViewModel> rows, long expectedRevision)
    {
        if (StatusRevision != expectedRevision) return;
        var running = rows.Where(row => row.IsAlive).ToHashSet();
        if (running.Count == 0) return;
        ShowStatus(Loc.Format(running.Count == 1 ? "status.foundRunning.one" : "status.foundRunning.many", running.Count), error: false);
        _recoveredStatusRows = running;
    }

    internal void RecoveredTabChanged(TabViewModel row)
    {
        if (_recoveredStatusRows?.Contains(row) == true) DismissRecoveredStatus();
    }

    private void DismissRecoveredStatus()
    {
        if (_recoveredStatusRows is not null) ShowStatus("", error: false);
    }

    public string WslDistro
    {
        get => _cfg.WslDistro;
        set { _cfg.WslDistro = value.Trim(); Raise(); _ = InitAsync(); }
    }

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
    public string LaunchLabel => ToStartCount switch
    {
        0 when EnabledCount > 0 => Loc.Get("launch.allRunning"),
        0 => Loc.Get("launch.none"),
        1 => Loc.Get("launch.one"),
        var n => Loc.Format("launch.many", n),
    };

    /// <summary>Language setting shown in Settings; the change applies after a restart.</summary>
    public IReadOnlyList<LanguageChoice> LanguageChoices { get; } =
        [new(Loc.Auto), new("sv"), new("en")];

    public bool CloseAfterLaunch { get => _cfg.CloseAfterLaunch; set { _cfg.CloseAfterLaunch = value; Raise(); } }
    public bool Notifications { get => _cfg.Notifications; set { _cfg.Notifications = value; Raise(); } }

    /// <summary>The update prompt in the main window.</summary>
    public UpdateViewModel Update { get; }

    /// <summary>The offer to allow the commands agy denied in Run once tabs.</summary>
    public DenialViewModel Denial { get; }

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

    /// <summary>Global CLI zoom shown in Settings; applies on the next start.</summary>
    public IReadOnlyList<ZoomChoice> ZoomChoices { get; } = [.. Launcher.ZoomChoices.Select(p => new ZoomChoice(p))];

    public ZoomChoice SelectedZoom
    {
        get => ZoomChoices.FirstOrDefault(c => c.Percent == _cfg.ZoomPercent) ?? ZoomChoices[0];
        set
        {
            if (value is null || value.Percent == _cfg.ZoomPercent) return;
            _cfg.ZoomPercent = value.Percent;
            Raise();
        }
    }

    public void TabsChanged()
    {
        Raise(nameof(HasTabs));
        Raise(nameof(EnabledCount));
        Raise(nameof(ToStartCount));
        Raise(nameof(CanLaunch));
        Raise(nameof(LaunchLabel));
        Raise(nameof(CanCloseAll));
        RestartableChanged();
    }

    internal void RestartableChanged()
    {
        Raise(nameof(RestartableCount));
        Raise(nameof(CanRestartAll));
        Raise(nameof(RestartAllLabel));
    }

    /// <summary>Moves one project tab to a new position and persists its order.</summary>
    public void MoveTab(int oldIndex, int newIndex)
    {
        if (oldIndex < 0 || oldIndex >= Tabs.Count || newIndex < 0 || newIndex >= Tabs.Count || oldIndex == newIndex) return;
        Tabs.Move(oldIndex, newIndex);
        Save();
    }

    public void AddTab()
    {
        var tab = new TabViewModel(new TabConfig { ColorIndex = Tabs.Count }, this, Tabs.Count);
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
            var tab = new TabViewModel(new TabConfig { Name = p.Name, Directory = PathMapping.ToTabPath(p.Path), ColorIndex = Tabs.Count }, this, Tabs.Count);
            Tabs.Add(tab);
            CheckOrkester(tab);
        }
        if (projects.Count > 0) ShowStatus(Loc.Format(projects.Count == 1 ? "status.imported.one" : "status.imported.many", projects.Count), error: false);
    }

    /// <summary>Resolves the WSL home (for ~ paths on Windows), then checks every tab.</summary>
    private async Task InitAsync()
    {
        if (OperatingSystem.IsWindows()) _wslHome = await Task.Run(() => QueryWslHome(_cfg.WslDistro));
        foreach (var t in Tabs) CheckOrkester(t);
    }

    public void CheckOrkester(TabViewModel tab)
    {
        var local = PathMapping.ToLocalPath(tab.Directory, _cfg.WslDistro, _wslHome);
        if (local is null) { tab.HasOrkester = null; return; }
        Task.Run(() =>
        {
            bool? has;
            try { has = System.IO.Directory.Exists(local) ? PathMapping.HasOrkester(local) : null; }
            catch (Exception) { has = null; }
            Dispatcher.UIThread.Post(() =>
            {
                // Ignore a result for a directory the user has typed away from.
                if (PathMapping.ToLocalPath(tab.Directory, _cfg.WslDistro, _wslHome) == local) tab.HasOrkester = has;
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
        _cfg.Tabs = Tabs.Select(t => t.ToConfig(forLaunch)).ToList();
        return _cfg;
    }

    public void Save()
    {
        try { ConfigStore.Save(Snapshot(forLaunch: false)); }
        catch (Exception e) { ShowStatus(Loc.Format("status.saveFailed", e.Message), error: true); }
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
        ShowStatus(error, error: error != "");
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
    /// For --screenshot --fake-output: the first running tab's popup text, replayed like a captured claude screen
    /// (static lines, then a live area redrawn in place); returns that tab.
    /// </summary>
    public TabViewModel? ShowSampleOutput(int index = 0)
    {
        if (Tabs.Where(t => t.ShowsOutput).ElementAtOrDefault(index) is not { } tab) return null;
        if (tab.LaunchedAs is { NonInteractive: true })
        {
            // A Run once tab shows bs_stream's formatter lines.
            tab.SetOutput(TerminalText.Render(string.Join('\n',
                "Picking up #31 (priority: medium): rate limiter returns 500 instead of 429.",
                "> Read src/middleware/limit.go",
                "> Bash go test ./middleware/...",
                "The handler panics when the bucket is empty, so the recover middleware turns it into a 500.",
                "> Edit src/middleware/limit.go",
                "> Bash go test ./... && git commit -am 'Return 429 when the bucket is empty (Fixes #31)'",
                "Delivered #31. No eligible issues remain, entering the idle watch (polls every 5 min).",
                "> Bash sleep 300 && gh issue list --state open --label priority",
                "== turn done: 38 turns, 412 s") + "\n", 40, 100, TabSession.OutputLines));
            return tab;
        }
        var bar = new string('─', 96);
        var screen = new System.Text.StringBuilder()
            .Append("\x1b[1m●\x1b[0m Picking up #12 (priority: high): the export button ignores the date filter.\r\n\r\n")
            .Append("\x1b[32m●\x1b[0m Read(src/export/csv.ts)\r\n  ⎿  Read 142 lines\r\n\r\n")
            .Append("\x1b[32m●\x1b[0m Bash(npm test -- export)\r\n  ⎿  Tests: 1 failed, 23 passed, 24 total\r\n\r\n")
            .Append("● The filter is applied after paging, so rows outside the range leak into page 2.\r\n  Moving it into the query.\r\n\r\n")
            .Append("\x1b[32m●\x1b[0m Update(src/export/csv.ts)\r\n  ⎿  Updated src/export/csv.ts with 6 additions and 3 removals\r\n\r\n");
        // Two frames of the live area: the second overwrites the first, as Ink does.
        foreach (var (seconds, tokens) in new[] { (41, "1.9k"), (44, "2.3k") })
            screen.Append(seconds == 41 ? "" : string.Concat(Enumerable.Repeat("\x1b[2K\x1b[1A", 5)) + "\x1b[2K\r")
                .Append($"\x1b[38;5;174m✻ Running tests… ({seconds}s · ↓ {tokens} tokens · esc to interrupt)\x1b[0m\r\n\r\n")
                .Append(bar).Append("\r\n> \r\n").Append(bar).Append("\r\n");
        tab.SetOutput(TerminalText.Render(screen.ToString(), 40, 100, TabSession.OutputLines));
        return tab;
    }

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
        if (error == "" && (tab.Session is null || tab.Track is null || tab.ShellPid is null)) error = Loc.Get("err.restartInPlace");
        if (error != "") { ShowStatus(error, error: true); return; }
        try
        {
            var resume = Launcher.CanResume(launched, next);
            tab.Session!.QueueNext(tab.Track!.Id, Launcher.RestartScript(next, tab.Track, resume));
            var pid = tab.ShellPid!.Value;
            var distro = _cfg.WslDistro;
            Task.Run(() => Launcher.StopForeground(pid, distro));
            tab.LaunchedAs = next;
            tab.RaiseRestartTip();
            ShowStatus(Loc.Format(resume ? "status.restartedResume" : "status.restarted", tab.Name.Trim(), next.Llm,
                next.Model == Catalog.StandardModel ? Loc.Get("restart.standardModel") : next.Model), error: false);
            Save();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            ShowStatus(Loc.Format("err.launch", e.Message), error: true);
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
        foreach (var t in plan.Now)
            if (t.IsChanged) RestartChanged(t); else Restart(t);
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
            ShowStatus(Loc.Format("err.launch", e.Message), error: true);
            return true;
        }
        if (!CanRestartExited(log, TabSession.Liveness(_cfg.WslDistro))) return false;
        DismissRecoveredStatus();
        var error = Launcher.Validate([launched]);
        try
        {
            if (error == "")
            {
                session.QueueNext(track.Id, Launcher.RestartScript(launched, track, resume));
                var distro = _cfg.WslDistro;
                // The idle child may still be starting after cli-exit. Its own PID/start time
                // prevents a delayed wake from interrupting the newly restarted CLI.
                var wake = Launcher.WakeIdleShellScript(track);
                Task.Run(() =>
                {
                    try { Launcher.RunTabControl(wake, distro); }
                    catch (Exception e) when (e is IOException or System.ComponentModel.Win32Exception)
                    {
                        Dispatcher.UIThread.Post(() => ShowStatus(Loc.Format("err.launch", e.Message), error: true));
                    }
                });
                tab.PendingRestart = false;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            error = Loc.Format("err.launch", e.Message);
        }
        ShowStatus(error != "" ? error : resume
            ? Loc.Format("status.restartedResume", tab.Name.Trim(), launched.Llm,
                launched.Model == Catalog.StandardModel ? Loc.Get("restart.standardModel") : launched.Model)
            : "", error: error != "");
        return true;
    }

    private void RestartClosed(IReadOnlyList<(TabViewModel Row, TabConfig Tab)> rows, bool resume = false)
    {
        if (rows.Count == 0) return;
        var cfg = new AppConfig { WslDistro = _cfg.WslDistro, LinuxTerminal = _cfg.LinuxTerminal, ZoomPercent = _cfg.ZoomPercent, Tabs = rows.Select(r => r.Tab).ToList() };
        var error = Start(cfg, rows, _session?.WindowName, resume);
        var first = rows[0];
        ShowStatus(error != "" ? error : resume
            ? Loc.Format("status.restartedResume", first.Row.Name.Trim(), first.Tab.Llm,
                first.Tab.Model == Catalog.StandardModel ? Loc.Get("restart.standardModel") : first.Tab.Model)
            : "", error: error != "");
    }

    private TabSession? _session;
    private DispatcherTimer? _poll;
    private bool _polling;

    /// <summary>Launches with a status log per tab (one session per window) and starts watching.</summary>
    private string Start(AppConfig cfg, IReadOnlyList<(TabViewModel Row, TabConfig Tab)> rows, string? window, bool resume = false)
    {
        DismissRecoveredStatus();
        try { _session ??= new TabSession(TabSession.DefaultRoot, DateTime.Now, TabSession.Liveness(_cfg.WslDistro)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _session = null; }
        var tracks = rows.ToDictionary(r => r.Tab, r => _session?.NewTrack());
        var error = Launcher.Launch(cfg, track: t => tracks.GetValueOrDefault(t), window: window ?? _session?.WindowName ?? "new", resume: resume);
        if (error != "")
        {
            if (_cfg.Notifications) Notifier.Show("BoosterAI", error);
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
    /// At start: finds the tabs an earlier BoosterAI run opened that still run, and watches them again on the
    /// configured tab with the same name and directory. Start then opens only the others, in their window.
    /// </summary>
    public void FindRunningTabs()
    {
        var statusRevision = StatusRevision;
        var distro = _cfg.WslDistro;
        Task.Run(() => TabSession.FindRunning(TabSession.DefaultRoot, TabSession.Liveness(distro))).ContinueWith(t =>
        {
            if (!t.IsCompletedSuccessfully || t.Result.Count == 0) return;
            var sessions = new Dictionary<string, TabSession>();
            var joined = new List<TabViewModel>();
            foreach (var found in t.Result)
            {
                if (Match(found.Tab) is not { } row) continue;
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
            if (joined.Count == 0) return;
            StartPolling();
            TabsChanged();
            ShowRecoveredStatus(joined, statusRevision);
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private TabViewModel? _outputTab;
    private DispatcherTimer? _outputTimer;
    private bool _readingOutput;

    /// <summary>
    /// While a tab's status popup is open (<paramref name="open"/>), reads its output tail every second and
    /// updates the popup; nothing is read while no popup is open.
    /// </summary>
    public void WatchOutput(TabViewModel tab, bool open)
    {
        if (!open)
        {
            if (_outputTab != tab) return;
            _outputTab = null;
            _outputTimer?.Stop();
            return;
        }
        _outputTab = tab;
        _outputTimer ??= new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => ReadOutput());
        _outputTimer.Start();
        ReadOutput();
    }

    private void ReadOutput()
    {
        if (_readingOutput || _outputTab is not { ShowsOutput: true, Session: { } session, TrackId: { } id } tab) return;
        _readingOutput = true;
        Task.Run(() => session.ReadOutput(id)).ContinueWith(t =>
        {
            _readingOutput = false;
            // Skip a result for a tab restarted meanwhile; an unchanged text leaves the popup as it is.
            if (t.IsCompletedSuccessfully && tab.TrackId == id) tab.SetOutput(t.Result);
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>The configured tab a running one belongs to: same name and directory, not already watched.</summary>
    internal TabViewModel? Match(TabConfig running) =>
        Tabs.FirstOrDefault(t => t.LaunchedAs is null && t.Name.Trim() == running.Name.Trim() && t.Directory.Trim() == running.Directory.Trim());

    /// <summary>Reads the logs and checks liveness off the UI thread (UNC reads can take a few hundred ms).</summary>
    private void Poll()
    {
        var watched = Tabs.Where(t => t.TrackId is not null && t.Session is not null)
            .Select(t => (Row: t, Id: t.TrackId!, t.LaunchedAt, Session: t.Session!)).ToList();
        if (_polling || watched.Count == 0) return;
        _polling = true;
        var distro = _cfg.WslDistro;
        var settingsPath = AgySettingsFile;
        Task.Run(() =>
        {
            var live = TabSession.Liveness(distro);
            var results = watched.Select(w =>
            {
                var log = w.Session.ReadLog(w.Id);
                var denied = log?.Denied?.SelectMany(AgyPermissions.Programs).Distinct().ToList();
                var now = DateTime.UtcNow;
                var state = TabStatus.Evaluate(log, now - w.LaunchedAt, live);
                // A shell killed without its exit trap (wsl --shutdown) leaves its output capture behind.
                if (state == TabState.Lost) w.Session.DeleteOutput(w.Id);
                return (w.Row, w.Id, State: state, log?.CliExit, Denied: denied,
                    log?.Pid, Activity: w.Session.ReadActivity(w.Id, log, now), log?.WatchingSince);
            }).ToList();
            // agy's settings are only read while a tab has a denial to offer rules for.
            var settings = results.Any(r => r.State == TabState.Denied) ? ReadText(settingsPath) : null;
            return (results, settings);
        }).ContinueWith(t =>
        {
            _polling = false;
            if (!t.IsCompletedSuccessfully) return;
            var (results, settings) = t.Result;
            foreach (var (row, id, state, rc, denied, pid, activity, watchingSince) in results)
            {
                // Ignore a result for a tab that has been restarted meanwhile.
                if (row.TrackId != id) continue;
                row.ShellPid = pid;
                if (row.PendingRestart && state == TabState.Running && activity == Activity.Idle) RestartChanged(row);
                if (!row.SetState(state, rc, denied, activity, activity == Activity.Watching ? watchingSince : null)) continue;
                if (_cfg.Notifications && TabStatus.IsUnexpected(state, rc))
                    Notifier.Show("BoosterAI", Loc.Format("notify.body", row.Name, row.StatusText));
            }
            // A tab that closed can be started again: the Start button counts it.
            TabsChanged();
            OfferRules(settings);
        }, TaskScheduler.FromCurrentSynchronizationContext());
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
        if (AgySettingsFile is not { } path) { ShowStatus(Loc.Get("status.wslHomeMissing"), error: true); return; }
        try
        {
            var current = File.Exists(path) ? File.ReadAllText(path) : null;
            var rows = DeniedRows(current);
            var programs = rows.SelectMany(r => r.Missing).Distinct().ToList();
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            // Written in place, so the file keeps its permissions (agy creates it 0600).
            File.WriteAllText(path, AgyPermissions.AddRules(current, programs));
            Denial.Hide();
            ShowStatus(Loc.Format("status.rulesAdded", string.Join(", ", programs)), error: false);
            var closed = new List<(TabViewModel Row, TabConfig Tab)>();
            foreach (var (row, _) in rows)
                if (row.LaunchedAs is { } launched && !RestartExited(row, launched, resume: false)) closed.Add((row, launched));
            RestartClosed(closed);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            ShowStatus(Loc.Format("status.rulesFailed", path, e.Message), error: true);
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
        if (local is null) { ShowStatus(Loc.Get("status.wslHomeMissing"), error: true); return; }
        if (!System.IO.Directory.Exists(local)) { ShowStatus(Loc.Format("status.dirMissing", tab.Directory), error: true); return; }
        try
        {
            var opener = OperatingSystem.IsWindows() ? "explorer.exe" : OperatingSystem.IsMacOS() ? "open" : "xdg-open";
            var psi = new ProcessStartInfo(opener) { UseShellExecute = false };
            psi.ArgumentList.Add(local);
            Process.Start(psi);
        }
        catch (Exception e)
        {
            ShowStatus(Loc.Format("status.openFailed", e.Message), error: true);
        }
    }

    /// <summary>Opens one tab whose AI is asked to install the orchestra in that project.</summary>
    public void InstallOrkester(TabViewModel tab)
    {
        var cfg = new AppConfig
        {
            WslDistro = _cfg.WslDistro, LinuxTerminal = _cfg.LinuxTerminal,
            Tabs = [tab.ToConfig(forLaunch: false)],
        };
        cfg.Tabs[0].Enabled = true;
        cfg.Tabs[0].Name = tab.Name.Trim() + " (" + Loc.Get("tab.installSuffix") + ")";
        var error = Launcher.Launch(cfg, Launcher.InstallOrkesterPrompt);
        // The files appear once the agent has worked; reopening BoosterAI or editing the path rechecks.
        ShowStatus(error != "" ? error : Loc.Format("status.installRunning", tab.Name.Trim()), error: error != "");
        if (error != "" && _cfg.Notifications) Notifier.Show("BoosterAI", error);
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
    public string InstallTip => Loc.Format("update.install.tip", _release?.Version.ToString() ?? "");

    private void RaiseButtons()
    {
        Raise(nameof(ShowInstall));
        Raise(nameof(ShowPage));
        Raise(nameof(ShowDismiss));
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
        IsVisible = true;
    }

    /// <summary>For --screenshot --fake-update 0.3.0: shows the bar as an installed copy would, without any network;
    /// "manual:0.3.0" shows it as a copy that cannot update itself (releases link).</summary>
    public void ShowFake(string version)
    {
        var manual = version.StartsWith("manual:", StringComparison.Ordinal);
        if (manual) version = version["manual:".Length..];
        var v = AppVersion.Parse(version) ?? new AppVersion(9, 9, 9, "");
        var rid = UpdateLogic.RuntimeId(_windows, RuntimeInformation.OSArchitecture) ?? "linux-x64";
        var name = $"BoosterAI-{v}-{rid}{(rid.StartsWith("win-") ? ".zip" : ".tar.gz")}";
        Offer(new ReleaseInfo("v" + v, v, false, UpdateLogic.ReleasesPage, [new(name, ""), new(UpdateLogic.SumsName, "")]), installedCopy: !manual);
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

/// <summary>One folder in the "Hitta projekt" list.</summary>
public sealed class ImportItem(FoundProject project, Action changed) : Observable
{
    private bool _selected = true;

    public FoundProject Project { get; } = project;
    public string Name => Project.Name;
    public string Path => Project.Path;
    public string Kind => Project switch { { Git: true, Ai: true } => "git · AI", { Git: true } => "git", _ => "AI" };
    public bool Selected { get => _selected; set { if (Set(ref _selected, value)) changed(); } }
}

/// <summary>The "Hitta projekt" dialog: scans a folder in the background and lists the projects not added yet.</summary>
public sealed class ImportViewModel : Observable
{
    private readonly HashSet<string> _existing;
    private CancellationTokenSource? _scan;
    private string _root = "", _message = "";
    private bool _scanning;

    /// <param name="existing">Local paths of the projects already added; they are left out of the list.</param>
    public ImportViewModel(IEnumerable<string> existing) =>
        _existing = [.. existing.Select(ProjectScanner.Key)];

    public ObservableCollection<ImportItem> Items { get; } = [];
    public string Root { get => _root; private set { if (Set(ref _root, value)) Raise(nameof(Hint)); } }
    public string Hint => Loc.Format("import.hint", Root);
    public string Message { get => _message; private set => Set(ref _message, value); }
    public bool IsScanning { get => _scanning; private set => Set(ref _scanning, value); }
    public bool HasItems => Items.Count > 0;
    public int SelectedCount => Items.Count(i => i.Selected);
    public bool CanImport => SelectedCount > 0;
    public string ImportLabel => SelectedCount switch { 0 => Loc.Get("import.none"), 1 => Loc.Get("import.one"), var n => Loc.Format("import.many", n) };
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

    public async Task ScanAsync(string root)
    {
        _scan?.Cancel();
        var cancel = (_scan = new CancellationTokenSource()).Token;
        Root = root;
        Show([]);
        IsScanning = true;
        Message = Loc.Format("import.scanning", root);
        try
        {
            var found = await Task.Run(() => ProjectScanner.Scan(root, cancel: cancel), cancel);
            if (cancel.IsCancellationRequested) return;
            var fresh = found.Where(f => !_existing.Contains(ProjectScanner.Key(f.Path))).ToList();
            Show(fresh);
            Message = Loc.Format(found.Count == 0 ? "import.nothing" : "import.allAdded", root);
        }
        catch (OperationCanceledException) { /* a newer scan replaced this one */ }
        catch (Exception e) { Message = Loc.Format("import.failed", e.Message); }
        finally { if (!cancel.IsCancellationRequested) IsScanning = false; }
    }

    public void Cancel() => _scan?.Cancel();

    /// <summary>For --screenshot --window import: a fixed list, no file system access.</summary>
    public void ShowSample(string root)
    {
        Root = root;
        var sep = System.IO.Path.DirectorySeparatorChar;
        Show([
            new(root + sep + "webshop", "webshop", Git: true, Ai: true),
            new(root + sep + "recipes", "recipes", Git: true, Ai: false),
            new(root + sep + "notes" + sep + "research", "research", Git: false, Ai: true),
            new(root + sep + "work" + sep + "intranet", "intranet", Git: true, Ai: false),
        ]);
        Items[3].Selected = false;
    }

    private void Show(IReadOnlyList<FoundProject> projects)
    {
        Items.Clear();
        foreach (var p in projects) Items.Add(new ImportItem(p, SelectionChanged));
        Raise(nameof(HasItems));
        SelectionChanged();
    }
}
