using System.Collections.ObjectModel;
using Avalonia.Threading;

namespace BoosterAI;

public enum ClosePhase { Waiting, Saving, Closing, Closed, Failed }

/// <summary>What unticking a row did (#272): only left Start, began closing its running tab, or needs the user's confirmation first.</summary>
public enum UntickOutcome { Unticked, Closing, Confirm }

/// <summary>Decisions use an explicit save-turn token, never the previous CLI's exit or the output idle heuristic.</summary>
public static class CloseAllLogic
{
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(10);
    public static bool NeedsSave(TabConfig tab, TabState state, Activity activity) =>
        state == TabState.Running && tab.Llm != Catalog.ShellOnly
        && !(tab.NonInteractive && tab.Orkester && activity == Activity.Watching);
    public static bool SaveTurnDone(TabLog log, string token) =>
        log.CloseSaveToken == token && (log.CliExit is not null || log.WatchingSince is not null);
    public static bool HasTimedOut(DateTime began, DateTime now) => now - began >= Timeout;
}

/// <summary>
/// The question before an active running tab closes on untick (#272): why it is asked, what closes (only this row: a project's
/// running subtabs, or a subtab's project, keep running) and whether a save turn and Force close apply (not for a shell).
/// </summary>
public sealed record CloseTabQuestion(string Heading, string Body, string Scope, bool CanSave)
{
    public bool HasScope => Scope != "";
    public string Confirm => Loc.Get(CanSave ? "close.start" : "closeTab.close");

    public static CloseTabQuestion For(TabViewModel tab, IEnumerable<TabViewModel> rows)
    {
        var name = tab.Name.Trim();
        var shell = (tab.LaunchedAs?.Llm ?? tab.Llm) == Catalog.ShellOnly;
        var body = Loc.Get(shell ? "closeTab.shell" : tab.IsRunning ? "closeTab.working" : "closeTab.starting");
        var scope = "";
        if (tab.Parent is { } parent) scope = Loc.Format("closeTab.subtab", name, parent.Name.Trim());
        else if (rows.Where(t => t.Parent == tab && t.IsAlive).Select(t => t.Name.Trim()).ToList() is { Count: > 0 } running)
            scope = Loc.Format("closeTab.subtabs." + (running.Count == 1 ? "one" : "other"), name, string.Join(", ", running));
        if (!shell) scope = scope == "" ? Loc.Get("closeTab.force") : scope + "\n" + Loc.Get("closeTab.force");
        return new(Loc.Format("closeTab.heading", name), body, scope, !shell);
    }
}

public sealed class ClosingTab : Observable
{
    internal ClosingTab(TabViewModel tab) { Tab = tab; Name = tab.Name; }
    internal TabViewModel Tab { get; }
    internal string Token { get; } = Guid.NewGuid().ToString("N");
    internal bool Requested { get; set; }
    public string Name { get; }
    private ClosePhase _phase;
    public ClosePhase Phase
    {
        get => _phase;
        internal set
        {
            if (!Set(ref _phase, value)) return;
            if (value != ClosePhase.Failed) _error = null;
            Raise(nameof(Text));
            Raise(nameof(IsBusy));
            Raise(nameof(IsFailed));
            Raise(nameof(IsIdle));
            Tab.SetCloseProgress(value == ClosePhase.Closed ? null : Text);
        }
    }
    private string? _error;
    public string Text => _error ?? Loc.Get("close.phase." + Phase);
    /// <summary>Saving or closing: an info spinner. Failed: a danger dot. Otherwise (waiting, closed): a neutral dot.</summary>
    public bool IsBusy => Phase is ClosePhase.Saving or ClosePhase.Closing;
    public bool IsFailed => Phase == ClosePhase.Failed;
    public bool IsIdle => !IsBusy && !IsFailed;
    internal void Fail(string message) { _error = message; Phase = ClosePhase.Failed; Tab.SetCloseProgress(Text); Raise(nameof(Text)); }
}

public sealed class CloseAllOperation : Observable
{
    private readonly string _distro;
    private readonly Action _changed;
    private readonly string? _wslHome;
    private readonly Func<Func<int, long?>?> _liveness;
    private readonly Func<TabViewModel, IEnumerable<TabConfig>>? _others;
    private bool _active, _started, _timedOut, _force, _busy, _sample, _attention;
    private DateTime _began;
    private readonly object _controlGate = new();
    private readonly DispatcherTimer _timer;
    public ObservableCollection<ClosingTab> Tabs { get; }
    public bool Started { get => _started; private set { Set(ref _started, value); Raise(nameof(NotStarted)); } }
    public bool NotStarted => !Started;
    public bool Active { get => _active; private set { Set(ref _active, value); Raise(nameof(CanForce)); } }
    public bool CanForce => Active;
    /// <summary>Closing one unticked tab (#272) rather than all of them.</summary>
    public bool Single { get; }
    public string Title => Loc.Get(Single ? "closeTab.title" : "close.title");
    internal bool Forced => _force;
    /// <summary>Raised once when a tab fails to close or the timeout passes, so a background single close can show its dialog.</summary>
    internal event Action? Attention;
    public string Message => _timedOut ? Loc.Get("close.timeout") : Loc.Get(Active ? "close.waiting" : Started ? "close.finished" : "close.confirm");

    /// <param name="others">The launch configs of the other open tabs (not in this operation) that a save turn must not be confused with.</param>
    internal CloseAllOperation(IEnumerable<TabViewModel> tabs, string distro, Action changed, string? wslHome = null, Func<Func<int, long?>?>? liveness = null,
        Func<TabViewModel, IEnumerable<TabConfig>>? others = null, bool single = false)
    {
        _others = others;
        Single = single;
        Tabs = new(tabs.Select(t => new ClosingTab(t)));
        _distro = distro;
        _wslHome = wslHome;
        _liveness = liveness ?? (() => TabSession.Liveness(distro));
        _changed = changed;
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, (_, _) => _ = TickAsync());
        _timer.Stop();
    }

    public void Start() => _ = StartAsync();

    /// <summary>Starts closing without a save turn, as Force close does (#272: the confirmation's Force close).</summary>
    internal Task StartForcedAsync()
    {
        _force = true;
        return StartAsync();
    }

    internal async Task StartAsync()
    {
        if (Started) return;
        Started = Active = true;
        _began = DateTime.UtcNow;
        foreach (var t in Tabs) t.Tab.PendingRestart = false;
        _changed();
        Raise(nameof(Message));
        _timer.Start();
        await TickAsync();
    }

    // Cancel stops future automatic closes; a save turn already running may finish in its original tab.
    public void Cancel()
    {
        _timer.Stop();
        lock (_controlGate)
        {
            Active = false;
            foreach (var t in Tabs.Where(t => t.Requested && t.Phase != ClosePhase.Closed))
            {
                try { t.Tab.Session?.CancelNext(t.Tab.TrackId!); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
        }
        foreach (var t in Tabs) t.Tab.SetCloseProgress(null);
        _changed();
        Raise(nameof(Message));
    }

    public void Force()
    {
        if (!Active) return;
        _force = true;
        _ = TickAsync();
    }

    internal async Task TickAsync()
    {
        if (_sample || !Active || _busy) return;
        _busy = true;
        if (!_timedOut && CloseAllLogic.HasTimedOut(_began, DateTime.UtcNow))
        { _timedOut = true; Raise(nameof(Message)); }
        try
        {
            foreach (var item in Tabs.Where(t => t.Phase != ClosePhase.Closed).ToList())
            {
                try { await ProcessItem(item); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
                { item.Fail(Loc.Format("close.error", e.Message)); }
            }
            if (Tabs.All(t => t.Phase == ClosePhase.Closed))
            { Active = false; _timer.Stop(); _changed(); Raise(nameof(Message)); }
            else if (!_attention && (_timedOut || Tabs.Any(t => t.IsFailed)))
            { _attention = true; Attention?.Invoke(); }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            foreach (var item in Tabs.Where(t => t.Phase is ClosePhase.Waiting or ClosePhase.Saving))
                item.Fail(Loc.Format("close.error", e.Message));
        }
        finally { _busy = false; }
    }

    private async Task ProcessItem(ClosingTab item)
    {
        if (!Active) return;
        var session = item.Tab.Session;
        var id = item.Tab.TrackId;
        if (session is null || id is null)
        {
            if (item.Phase != ClosePhase.Failed) item.Fail(Loc.Get("close.noTrack"));
            return;
        }
        // Failed rows only observe recovery. Do not retry saving or signal a shell on an ordinary poll.
        if (item.Phase == ClosePhase.Failed && !_force)
        {
            var closed = await Task.Run(() =>
            {
                try
                {
                    var failedLog = session.ReadLog(id);
                    if (failedLog?.Pid is not { } failedPid || failedLog.StartTime is not { } failedStart) return false;
                    if (failedLog.Exited) return true;
                    // A null result also means unreadable process data: only a different known identity proves exit.
                    return _liveness()?.Invoke(failedPid) is { } current && current != failedStart;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
            });
            if (Active && closed)
            {
                item.Phase = ClosePhase.Closed;
                item.Tab.SetState(TabState.Closed, null);
            }
            return;
        }
        var snapshot = await Task.Run(() =>
        {
            var log = session.ReadLog(id);
            var live = TabSession.Liveness(_distro);
            var state = TabStatus.Evaluate(log, DateTime.UtcNow - item.Tab.LaunchedAt, live);
            var alive = log?.Pid is { } pid && live is not null && live(pid) == log.StartTime;
            return (log, state, alive);
        });
        if (!Active) return;
        var (log, state, alive) = snapshot;
        if (state is TabState.Closed or TabState.Lost || item.Phase == ClosePhase.Closing && !alive)
        {
            item.Phase = ClosePhase.Closed;
            item.Tab.SetState(TabState.Closed, null);
            return;
        }
        if (log?.Pid is not { } pid || log.StartTime is not { } start)
        { if (state != TabState.Starting) item.Fail(Loc.Get("close.noTrack")); return; }
        if (!alive) { item.Fail(Loc.Get("close.noTrack")); return; }
        if (item.Phase == ClosePhase.Closing && !_force) return;
        if (_force)
        {
            await Close(item, session, id, pid, start, log.CanCloseCleanly);
            return;
        }
        if (!item.Requested)
        {
            var tab = item.Tab.LaunchedAs;
            if (tab is null) { item.Fail(Loc.Get("close.noTrack")); return; }
            if (!CloseAllLogic.NeedsSave(tab, state, item.Tab.Activity))
            { await Close(item, session, id, pid, start, log.CanCloseCleanly); return; }
            if (!Launcher.CanSaveAndClose(tab))
            { item.Fail(Loc.Get("close.manualSave")); return; }
            // --continue/--last is per directory, so never choose between two open conversations implicitly.
            var others = Tabs.Where(t => t != item && t.Phase != ClosePhase.Closed).Select(t => t.Tab.LaunchedAs).OfType<TabConfig>()
                .Concat(_others?.Invoke(item.Tab) ?? []);
            if (others.Any(other => other.Llm == tab.Llm && SameDirectory(other.Directory, tab.Directory)))
            { item.Fail(Loc.Get("close.ambiguous")); return; }
            var validation = Launcher.Validate([tab]);
            if (validation != "") { item.Fail(validation); return; }
            item.Requested = true;
            await Task.Run(() =>
            {
                System.Diagnostics.Process control;
                lock (_controlGate)
                {
                    if (!Active) return;
                    session.QueueNext(id, Launcher.SaveAndCloseScript(tab, item.Token));
                    control = Launcher.StartTabControl(Launcher.StopTrackedForegroundScript(pid, start), _distro);
                }
                using (control) Launcher.WaitTabControl(control);
            });
            if (Active) item.Phase = ClosePhase.Saving;
        }
        else if (CloseAllLogic.SaveTurnDone(log, item.Token))
        {
            if (log.CliExit is not (null or 0) || log.Denied is { Count: > 0 })
            { item.Fail(Loc.Get("close.saveFailed")); return; }
            await Close(item, session, id, pid, start, log.CanCloseCleanly);
        }
    }

    private bool SameDirectory(string a, string b)
    {
        string Normalize(string path)
        {
            var local = PathMapping.ToLocalPath(path, _distro, _wslHome);
            if (local is null) return path.Trim().TrimEnd('/', '\\');
            try { return Path.GetFullPath(local).TrimEnd('/', '\\'); }
            catch (ArgumentException) { return local; }
        }
        return string.Equals(Normalize(a), Normalize(b), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private async Task Close(ClosingTab item, TabSession session, string id, int pid, long start, bool exitCleanly)
    {
        await Task.Run(() =>
        {
            System.Diagnostics.Process control;
            lock (_controlGate)
            {
                if (!Active) return;
                session.CancelNext(id);
                control = Launcher.StartTabControl(Launcher.CloseTabScript(pid, start, exitCleanly), _distro);
            }
            using (control) Launcher.WaitTabControl(control);
        });
        if (Active) item.Phase = ClosePhase.Closing;
    }

    internal void ShowSample()
    {
        _sample = true;
        _timer.Stop();
        _began = DateTime.UtcNow;
        Started = Active = true;
        foreach (var (tab, i) in Tabs.Select((t, i) => (t, i))) tab.Phase = i switch { 0 => ClosePhase.Saving, 2 => ClosePhase.Failed, _ => ClosePhase.Closed };
        Raise(nameof(Message));
    }
}

public sealed partial class MainViewModel
{
    public CloseAllOperation? ClosingAll { get; private set; }
    public bool IsClosingAll => ClosingAll is { Active: true };
    public bool CanCloseAll => !IsClosingAll && Tabs.Any(t => t.HasOpenTerminal);
    /// <summary>Why Close all is inactive when no tab is open; no tip otherwise.</summary>
    public string? CloseAllTip => Tabs.Any(t => t.HasOpenTerminal) ? null : Loc.Get("close.none");
    /// <param name="sample">Screenshots: the sample tabs, shown mid-closing unless <paramref name="confirm"/> keeps the confirmation step.</param>
    public CloseAllOperation PrepareCloseAll(bool sample = false, bool confirm = false)
    {
        // A tab an untick is already closing (#272) stays with that operation; another save turn would repeat its work.
        var closing = _tabCloses.Where(o => o.Active || !o.Started).SelectMany(o => o.Tabs).Select(t => t.Tab).ToHashSet();
        ClosingAll = new CloseAllOperation(Tabs.Where(t => sample ? t.HasStatus : t.HasOpenTerminal && !closing.Contains(t)), _cfg.WslDistro,
            () => { Raise(nameof(IsClosingAll)); TabsChanged(); }, _wslHome,
            others: _ => closing.Where(t => t.HasOpenTerminal).Select(t => t.LaunchedAs).OfType<TabConfig>());
        if (sample && !confirm) ClosingAll.ShowSample();
        return ClosingAll;
    }

    private readonly List<CloseAllOperation> _tabCloses = [];
    /// <summary>The single-tab closes that unticking started (#272) and that have not finished or been cancelled.</summary>
    internal IReadOnlyList<CloseAllOperation> TabCloses => _tabCloses;
    /// <summary>A background single close needs the user: a tab could not close or the timeout passed. The main window shows its dialog.</summary>
    internal event Action<CloseAllOperation>? TabCloseNeedsAttention;

    /// <summary>
    /// Closes one running tab the user unticked (#272) through Close all's path: an agent saves, commits, pushes and exits, a
    /// shell closes directly; <paramref name="force"/> skips the save turn. Null when the tab is not running or already closing.
    /// </summary>
    internal CloseAllOperation? CloseTab(TabViewModel tab, bool force)
    {
        if (IsClosingAll || tab.IsClosing || !tab.IsAlive) return null;
        CloseAllOperation? operation = null;
        operation = new CloseAllOperation([tab], _cfg.WslDistro, () =>
        {
            if (operation is { Active: false }) _tabCloses.Remove(operation);
            TabsChanged();
        }, _wslHome, others: t => Tabs.Where(o => o != t && o.HasOpenTerminal).Select(o => o.LaunchedAs).OfType<TabConfig>(), single: true);
        operation.Attention += () => TabCloseNeedsAttention?.Invoke(operation);
        _tabCloses.Add(operation);
        tab.SetCloseProgress(Loc.Get("close.phase.Waiting"));
        _ = force ? operation.StartForcedAsync() : operation.StartAsync();
        return operation;
    }
}
