using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Avalonia.Media;
using Avalonia.Threading;

namespace BoosterStart;

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
    private bool _enabled, _orkester;
    private bool? _hasOrkester;

    public TabViewModel(TabConfig c, MainViewModel owner, int index)
    {
        _owner = owner;
        _name = c.Name; _directory = c.Directory; _enabled = c.Enabled;
        _colorIndex = c.ColorIndex >= 0 ? c.ColorIndex : LegacyIndex(c.Color, index);
        _llm = Catalog.Llms.Contains(c.Llm) ? c.Llm : Catalog.DefaultLlm;
        _model = string.IsNullOrWhiteSpace(c.Model) ? Catalog.StandardModel : c.Model;
        _orkester = c.Orkester;
    }

    public string Name { get => _name; set => Set(ref _name, value); }
    public bool Orkester { get => _orkester; set => Set(ref _orkester, value); }
    public string Model { get => _model; set => Set(ref _model, value?.Trim() ?? ""); }

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
            Raise(nameof(CanInstallOrkester));
            // Another CLI's model id rarely fits; fall back to its default.
            if (!Catalog.ModelsFor(value).Contains(Model)) Model = Catalog.StandardModel;
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
    internal TabConfig? LaunchedAs { get; set; }
    internal string? TrackId { get; set; }
    internal DateTime LaunchedAt { get; set; }

    public bool HasStatus => _state is not null;
    public bool CanRestart => _state is TabState.Closed or TabState.CliExited or TabState.CliMissing
        or TabState.NotStarted or TabState.Lost;
    public string StatusText => _state switch
    {
        null => "",
        TabState.CliExited => Loc.Format("tabstate.CliExited", _rc ?? -1),
        var s => Loc.Get("tabstate." + s),
    };
    public IBrush StatusBrush => new SolidColorBrush(Avalonia.Media.Color.Parse(_state switch
    {
        TabState.Running => "#2E9E5B",
        TabState.Starting => "#8A8FA0",
        TabState.CliExited when _rc == 0 => "#8A8FA0",
        TabState.Closed => "#D08A1E",
        _ => "#D2453F",
    }));

    /// <summary>Updates the state; true when it changed.</summary>
    internal bool SetState(TabState state, int? rc)
    {
        if (_state == state && _rc == rc) return false;
        _state = state;
        _rc = rc;
        foreach (var n in new[] { nameof(HasStatus), nameof(CanRestart), nameof(StatusText), nameof(StatusBrush) }) Raise(n);
        return true;
    }

    public TabConfig ToConfig(bool forLaunch) => new()
    {
        Name = Name.Trim(), Color = Color, ColorIndex = _colorIndex, Directory = Directory.Trim(), Enabled = Enabled,
        Llm = Llm, Model = string.IsNullOrWhiteSpace(Model) ? Catalog.StandardModel : Model,
        Orkester = forLaunch ? Orkester && CanOrkester : Orkester,
    };
}

public sealed class MainViewModel : Observable
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
        _ = InitAsync();
    }

    public ObservableCollection<TabViewModel> Tabs { get; }
    public bool IsWindows => OperatingSystem.IsWindows();
    public bool IsLinux => !OperatingSystem.IsWindows();
    public IReadOnlyList<string> LinuxTerminals => Launcher.LinuxTerminals;
    public string Status { get => _status; private set => Set(ref _status, value); }
    public bool StatusIsError { get => _statusIsError; private set => Set(ref _statusIsError, value); }

    public void ShowStatus(string text, bool error) { Status = text; StatusIsError = error; }

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
    public bool CanLaunch => EnabledCount > 0;
    public string LaunchLabel => EnabledCount switch { 0 => Loc.Get("launch.none"), 1 => Loc.Get("launch.one"), var n => Loc.Format("launch.many", n) };

    /// <summary>Language setting shown in Settings; the change applies after a restart.</summary>
    public IReadOnlyList<LanguageChoice> LanguageChoices { get; } =
        [new(Loc.Auto), new("sv"), new("en")];

    public bool CloseAfterLaunch { get => _cfg.CloseAfterLaunch; set { _cfg.CloseAfterLaunch = value; Raise(); } }
    public bool Notifications { get => _cfg.Notifications; set { _cfg.Notifications = value; Raise(); } }

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

    public void TabsChanged()
    {
        Raise(nameof(HasTabs));
        Raise(nameof(EnabledCount));
        Raise(nameof(CanLaunch));
        Raise(nameof(LaunchLabel));
    }

    public void AddTab()
    {
        var tab = new TabViewModel(new TabConfig { ColorIndex = Tabs.Count }, this, Tabs.Count);
        Tabs.Add(tab);
        CheckOrkester(tab);
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

    /// <summary>Saves the choices and opens the terminal; true on success.</summary>
    public bool Launch()
    {
        var cfg = Snapshot(forLaunch: true);
        var rows = Tabs.Zip(cfg.Tabs).Where(p => p.Second.Enabled).ToList();
        var error = Start(cfg, rows, window: null);
        Save();
        ShowStatus(error, error: error != "");
        return error == "";
    }

    /// <summary>For --screenshot: one of each common state, without starting anything.</summary>
    public void ShowSampleStatus()
    {
        (TabState, int?)[] samples = [(TabState.Running, null), (TabState.Closed, null), (TabState.CliExited, 1), (TabState.Starting, null)];
        foreach (var (tab, (state, rc)) in Tabs.Where(t => t.Enabled).Zip(samples)) tab.SetState(state, rc);
    }

    /// <summary>Opens the tab again with the choices it was started with, in the session's window.</summary>
    public void Restart(TabViewModel tab)
    {
        if (tab.LaunchedAs is not { } launched) return;
        var cfg = new AppConfig { WslDistro = _cfg.WslDistro, LinuxTerminal = _cfg.LinuxTerminal, Tabs = [launched] };
        var error = Start(cfg, [(tab, launched)], _session?.WindowName);
        ShowStatus(error, error: error != "");
    }

    private TabSession? _session;
    private DispatcherTimer? _poll;
    private bool _polling;

    /// <summary>Launches with a status log per tab (one session per window) and starts watching.</summary>
    private string Start(AppConfig cfg, IReadOnlyList<(TabViewModel Row, TabConfig Tab)> rows, string? window)
    {
        try { _session ??= new TabSession(TabSession.DefaultRoot, DateTime.Now); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _session = null; }
        var tracks = rows.ToDictionary(r => r.Tab, r => _session?.NewTrack());
        var error = Launcher.Launch(cfg, track: t => tracks.GetValueOrDefault(t), window: window ?? _session?.WindowName ?? "new");
        if (error != "") return error;
        foreach (var (row, tab) in rows)
        {
            if (tracks[tab] is not { } track) continue;
            row.LaunchedAs = tab;
            row.TrackId = track.Id;
            row.LaunchedAt = DateTime.UtcNow;
            row.SetState(TabState.Starting, null);
        }
        _poll ??= new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, (_, _) => Poll());
        _poll.Start();
        return "";
    }

    /// <summary>Reads the logs and checks liveness off the UI thread (UNC reads can take a few hundred ms).</summary>
    private void Poll()
    {
        if (_polling || _session is not { } session) return;
        var watched = Tabs.Where(t => t.TrackId is not null).Select(t => (Row: t, Id: t.TrackId!, t.LaunchedAt)).ToList();
        _polling = true;
        var distro = _cfg.WslDistro;
        Task.Run(() =>
        {
            var live = TabSession.Liveness(distro);
            return watched.Select(w =>
            {
                var log = session.ReadLog(w.Id);
                return (w.Row, w.Id, State: TabStatus.Evaluate(log, DateTime.UtcNow - w.LaunchedAt, live), log?.CliExit);
            }).ToList();
        }).ContinueWith(t =>
        {
            _polling = false;
            if (!t.IsCompletedSuccessfully) return;
            foreach (var (row, id, state, rc) in t.Result)
            {
                // Ignore a result for a tab that has been restarted meanwhile.
                if (row.TrackId != id || !row.SetState(state, rc)) continue;
                if (_cfg.Notifications && TabStatus.IsUnexpected(state, rc))
                    Notifier.Show("BoosterStart", Loc.Format("notify.body", row.Name, row.StatusText));
            }
        }, TaskScheduler.FromCurrentSynchronizationContext());
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
        // The files appear once the agent has worked; reopening BoosterStart or editing the path rechecks.
        ShowStatus(error != "" ? error : Loc.Format("status.installRunning", tab.Name.Trim()), error: error != "");
    }
}

/// <summary>One entry of the language picker: "auto", "sv" or "en".</summary>
public sealed record LanguageChoice(string Id)
{
    public string Label => Loc.Get("language." + Id);
}
