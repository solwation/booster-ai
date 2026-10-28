using System.Collections.ObjectModel;
using Avalonia.Threading;

namespace BoosterAI;

public enum ClosePhase { Waiting, Saving, Closing, Closed, Failed }

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
            Tab.SetCloseProgress(value == ClosePhase.Closed ? null : Text);
        }
    }
    private string? _error;
    public string Text => _error ?? Loc.Get("close.phase." + Phase);
    internal void Fail(string message) { _error = message; Phase = ClosePhase.Failed; Tab.SetCloseProgress(Text); Raise(nameof(Text)); }
}

public sealed class CloseAllOperation : Observable
{
    private readonly string _distro;
    private readonly Action _changed;
    private readonly string? _wslHome;
    private bool _active, _started, _timedOut, _force, _busy, _sample;
    private DateTime _began;
    private readonly object _controlGate = new();
    private readonly DispatcherTimer _timer;
    public ObservableCollection<ClosingTab> Tabs { get; }
    public bool Started { get => _started; private set { Set(ref _started, value); Raise(nameof(NotStarted)); } }
    public bool NotStarted => !Started;
    public bool Active { get => _active; private set { Set(ref _active, value); Raise(nameof(CanForce)); } }
    public bool CanForce => Active;
    public string Message => _timedOut ? Loc.Get("close.timeout") : Loc.Get(Active ? "close.waiting" : Started ? "close.finished" : "close.confirm");

    internal CloseAllOperation(IEnumerable<TabViewModel> tabs, string distro, Action changed, string? wslHome = null)
    {
        Tabs = new(tabs.Select(t => new ClosingTab(t)));
        _distro = distro;
        _wslHome = wslHome;
        _changed = changed;
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, (_, _) => Tick());
        _timer.Stop();
    }

    public void Start()
    {
        if (Started) return;
        Started = Active = true;
        _began = DateTime.UtcNow;
        foreach (var t in Tabs) t.Tab.PendingRestart = false;
        _changed();
        Raise(nameof(Message));
        _timer.Start();
        Tick();
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
        Tick();
    }

    private async void Tick()
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
        if (item.Phase == ClosePhase.Failed && !_force) return;
        var session = item.Tab.Session;
        var id = item.Tab.TrackId;
        if (session is null || id is null) { item.Fail(Loc.Get("close.noTrack")); return; }
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
            // --continue/--last is per directory, so never choose between two open conversations implicitly.
            if (Tabs.Count(t => t.Tab.LaunchedAs is { } other && other.Llm == tab.Llm
                && t.Phase != ClosePhase.Closed && SameDirectory(other.Directory, tab.Directory)) > 1)
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
        foreach (var (tab, i) in Tabs.Select((t, i) => (t, i))) tab.Phase = i == 0 ? ClosePhase.Saving : ClosePhase.Closed;
        Raise(nameof(Message));
    }
}

public sealed partial class MainViewModel
{
    public CloseAllOperation? ClosingAll { get; private set; }
    public bool IsClosingAll => ClosingAll is { Active: true };
    public bool CanCloseAll => !IsClosingAll && Tabs.Any(t => t.HasOpenTerminal);
    public CloseAllOperation PrepareCloseAll(bool sample = false)
    {
        ClosingAll = new CloseAllOperation(Tabs.Where(t => sample ? t.HasStatus : t.HasOpenTerminal), _cfg.WslDistro,
            () => { Raise(nameof(IsClosingAll)); TabsChanged(); }, _wslHome);
        if (sample) ClosingAll.ShowSample();
        return ClosingAll;
    }
}
