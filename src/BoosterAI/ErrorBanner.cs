namespace BoosterAI;

/// <summary>What failed; decides which recovery actions the error banner offers (#153).</summary>
public enum ErrorKind
{
    /// <summary>The terminal or a tab could not be opened.</summary>
    Launch,
    /// <summary>A tab's name, folder or model cannot be launched as entered.</summary>
    InvalidTab,
    /// <summary>A tab's restart failed or did not reach its shell.</summary>
    Restart,
    /// <summary>A tab cannot be restarted in place (no status log); retrying would fail the same way.</summary>
    RestartUnavailable,
    /// <summary>The settings file could not be written.</summary>
    Save,
    /// <summary>The WSL home is not known yet.</summary>
    WslHome,
    /// <summary>The elevated WSL installation could not be started.</summary>
    WslInstall,
    /// <summary>A project folder is missing or the file manager could not open it.</summary>
    Folder,
    /// <summary>A web page (release page, change list) could not be opened.</summary>
    Link,
    /// <summary>An agent's alias mode could not be written.</summary>
    AgentConfig,
    /// <summary>agy's allow-rules could not be written.</summary>
    Rules,
    /// <summary>The folder picker for a project's folder failed.</summary>
    ProjectPicker,
    /// <summary>The folder picker for the project search root failed.</summary>
    SearchRootPicker,
    /// <summary>A project could not be removed (still open or changed meanwhile).</summary>
    ProjectRemoval,
}

/// <summary>One recovery action in the error banner.</summary>
public enum ErrorAction
{
    /// <summary>"Försök igen": runs the failed action again.</summary>
    Retry,
    /// <summary>"Visa detaljer": opens the tab's status popup with its last lines.</summary>
    Details,
    /// <summary>Settings → Minne och kontext → WSL och miljö (Windows) or Allmänt, where the Linux terminal is chosen.</summary>
    TerminalSettings,
    /// <summary>Settings → Projekt (also where the Find projects base folder is chosen, #157).</summary>
    ProjectSettings,
    /// <summary>Settings → AI-agenter.</summary>
    AgentSettings,
}

/// <summary>
/// The danger banner at the top of the main window: one sentence about the cause, one or two actions and a close
/// button. The status line at the bottom only carries neutral acknowledgements.
/// </summary>
public sealed class ErrorBannerViewModel : Observable
{
    /// <summary>The actions per kind, first one most important; every kind has one or two (ErrorBannerTests).</summary>
    public static IReadOnlyList<ErrorAction> ActionsFor(ErrorKind kind) => kind switch
    {
        ErrorKind.Launch => [ErrorAction.Retry, ErrorAction.TerminalSettings],
        ErrorKind.InvalidTab => [ErrorAction.ProjectSettings],
        ErrorKind.Restart => [ErrorAction.Retry, ErrorAction.Details],
        ErrorKind.RestartUnavailable => [ErrorAction.Details],
        ErrorKind.Save => [ErrorAction.Retry],
        ErrorKind.WslHome => [ErrorAction.Retry, ErrorAction.TerminalSettings],
        ErrorKind.WslInstall => [ErrorAction.Retry],
        ErrorKind.Folder => [ErrorAction.ProjectSettings],
        ErrorKind.Link => [ErrorAction.Retry],
        ErrorKind.AgentConfig => [ErrorAction.AgentSettings],
        ErrorKind.Rules => [ErrorAction.Retry],
        ErrorKind.ProjectPicker => [ErrorAction.ProjectSettings],
        ErrorKind.SearchRootPicker => [ErrorAction.ProjectSettings],
        ErrorKind.ProjectRemoval => [ErrorAction.ProjectSettings],
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>The button text of an action.</summary>
    public static string Label(ErrorAction action) => Label(action, OperatingSystem.IsWindows());

    /// <summary>The button text of an action on Windows or elsewhere; the path names the settings tab and page it opens.</summary>
    internal static string Label(ErrorAction action, bool windows) => action switch
    {
        ErrorAction.Retry => Loc.Get("error.retry"),
        ErrorAction.Details => Loc.Get("error.details"),
        ErrorAction.TerminalSettings => Loc.Format("error.openSettings", windows
            ? Loc.Get("settings.tab.optimization") + " → " + Loc.Get("settings.optimization.nav.environment")
            : Loc.Get("settings.tab.general")),
        ErrorAction.ProjectSettings => Loc.Format("error.openSettings", Loc.Get("settings.tab.projects")),
        ErrorAction.AgentSettings => Loc.Format("error.openSettings", Loc.Get("settings.tab.agents")),
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
    };

    private bool _visible, _primary;
    private string _text = "";
    private ErrorKind _kind;
    private Action? _retry;

    public bool IsVisible { get => _visible; private set => Set(ref _visible, value); }
    public string Text { get => _text; private set => Set(ref _text, value); }
    public ErrorKind Kind { get => _kind; private set => Set(ref _kind, value); }
    /// <summary>The tab the error concerns; "Visa detaljer" opens its status popup.</summary>
    public TabViewModel? Tab { get; private set; }

    public IReadOnlyList<ErrorAction> Actions => ActionsFor(Kind);
    public bool HasFirst => IsVisible;
    public bool HasSecond => IsVisible && Actions.Count > 1;
    public string FirstLabel => Label(Actions[0]);
    public string SecondLabel => Actions.Count > 1 ? Label(Actions[1]) : "";

    /// <summary>The first action is the window's one primary button (MainViewModel.PrimaryBanner); otherwise secondary.</summary>
    public bool FirstIsPrimary { get => _primary; internal set => Set(ref _primary, value); }

    /// <summary>
    /// Shows the error. <paramref name="retry"/> is required when the kind offers Retry, <paramref name="tab"/> when it
    /// offers Details, so no button is ever shown without something to do.
    /// </summary>
    public void Show(ErrorKind kind, string text, Action? retry = null, TabViewModel? tab = null)
    {
        var actions = ActionsFor(kind);
        if (actions.Contains(ErrorAction.Retry) && retry is null)
            throw new ArgumentException($"{kind} offers Retry and needs a retry action.", nameof(retry));
        if (actions.Contains(ErrorAction.Details) && tab is null)
            throw new ArgumentException($"{kind} offers Details and needs the tab.", nameof(tab));
        _retry = retry;
        Tab = tab;
        Kind = kind;
        Text = text;
        IsVisible = true;
        RaiseActions();
    }

    /// <summary>The close button, a newer status or a successful retry.</summary>
    public void Hide()
    {
        if (!IsVisible) return;
        IsVisible = false;
        _retry = null;
        Tab = null;
        RaiseActions();
    }

    /// <summary>Hides the banner and runs the failed action again (which may show a new error).</summary>
    internal void Retry()
    {
        var retry = _retry;
        Hide();
        retry?.Invoke();
    }

    private void RaiseActions()
    {
        foreach (var n in new[] { nameof(Tab), nameof(Actions), nameof(HasFirst), nameof(HasSecond), nameof(FirstLabel), nameof(SecondLabel) })
            Raise(n);
    }
}

/// <summary>The banners at the top of the main window that can carry its one primary button.</summary>
public enum PrimaryBanner { None, Error, Update, Denial, Wsl, Missing, Optimization }

public sealed partial class MainViewModel
{
    private PrimaryBanner _primaryBanner;

    /// <summary>
    /// At most one primary button in the main window (#148, #153): Start is the boost button, the empty state's
    /// "Hitta projekt" is primary when there are no tabs, and otherwise the first visible banner with an action
    /// gets it, in this order: error, update, denied commands, WSL, a single missing agent (several offers stay
    /// secondary rather than pick one), memory. Every other banner action is secondary.
    /// </summary>
    public PrimaryBanner PrimaryBanner
    {
        get => _primaryBanner;
        private set
        {
            if (!Set(ref _primaryBanner, value)) return;
            Error.FirstIsPrimary = value == PrimaryBanner.Error;
            foreach (var n in new[] { nameof(UpdatePrimary), nameof(DenialPrimary), nameof(WslPrimary), nameof(MissingPrimary), nameof(OptimizationPrimary) })
                Raise(n);
        }
    }

    public bool UpdatePrimary => PrimaryBanner == PrimaryBanner.Update;
    public bool DenialPrimary => PrimaryBanner == PrimaryBanner.Denial;
    public bool WslPrimary => PrimaryBanner == PrimaryBanner.Wsl;
    public bool MissingPrimary => PrimaryBanner == PrimaryBanner.Missing;
    public bool OptimizationPrimary => PrimaryBanner == PrimaryBanner.Optimization;

    internal static PrimaryBanner ChoosePrimary(bool emptyState, bool error, bool update, bool denial, bool wsl, int missing, bool optimization) =>
        emptyState ? PrimaryBanner.None
        : error ? PrimaryBanner.Error
        : update ? PrimaryBanner.Update
        : denial ? PrimaryBanner.Denial
        : wsl ? PrimaryBanner.Wsl
        : missing == 1 ? PrimaryBanner.Missing
        : optimization ? PrimaryBanner.Optimization
        : PrimaryBanner.None;

    private void RefreshPrimary() => PrimaryBanner = ChoosePrimary(!HasTabs, Error.IsVisible,
        Update.IsVisible && (Update.ShowInstall || Update.ShowPage), Denial.IsVisible, Setup.WslVisible && Setup.CanInstallWsl,
        Setup.MissingVisible ? Setup.Missing.Count : 0, Optimization.BannerVisible);

    private void WatchBanners()
    {
        Error.PropertyChanged += (_, _) => RefreshPrimary();
        Update.PropertyChanged += (_, _) => RefreshPrimary();
        Denial.PropertyChanged += (_, _) => RefreshPrimary();
        Setup.PropertyChanged += (_, _) => RefreshPrimary();
        Optimization.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(OptimizationViewModel.BannerVisible)) RefreshPrimary(); };
        PropertyChanged += (_, e) => { if (e.PropertyName == nameof(HasTabs)) RefreshPrimary(); };
        RefreshPrimary();
    }
}
