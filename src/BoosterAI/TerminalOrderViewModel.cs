using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace BoosterAI;

/// <summary>Keeps each session window's tabs in row order (#254); see <see cref="TerminalOrder"/>.</summary>
public sealed partial class MainViewModel
{
    private DispatcherTimer? _arrangeTimer;
    private bool _arrangeReturnFocus;
    private readonly List<string> _arrangeLaunched = [];

    /// <summary>The rows whose terminal tab lives in <paramref name="session"/>'s window, in row order.</summary>
    internal List<TabViewModel> LiveRows(TabSession session) =>
        Tabs.Where(t => t.CanShowTerminalState && t.Session?.WindowName == session.WindowName && t.TrackId is not null).ToList();

    /// <summary>The window as BoosterAI believes it to be (own ids, null for a foreign tab), from the saved order.</summary>
    internal List<string?> BelievedWindow(TabSession session) =>
        TerminalOrder.Believed(session.WindowOrder, LiveRows(session).Select(t => t.TrackId!));

    /// <summary>After a launch into <paramref name="session"/>'s window: the new tabs were appended at its end.</summary>
    private void TabsAppended(TabSession session, IReadOnlyList<string> before, IReadOnlyList<string> launched)
    {
        if (launched.Count == 0) return;
        var slots = TerminalOrder.Believed(session.WindowOrder, before);
        slots.AddRange(launched.Where(id => !slots.Contains(id)));
        session.WindowOrder = slots;
        // A fresh window got its tabs in row order already; only tabs added to a window with tabs may be out of place.
        if (before.Count > 0) ScheduleArrange(reorder: false, launched);
    }

    /// <summary>
    /// Arranges the windows a moment after the last request, so Alt+Up pressed several times, or the tabs of one launch,
    /// cause one terminal command. A reorder gives the focus back to BoosterAI afterwards.
    /// </summary>
    internal void ScheduleArrange(bool reorder, IReadOnlyList<string>? launched = null)
    {
        if (!TerminalOrder.Supported || Screenshot.Active) return;
        _arrangeReturnFocus |= reorder;
        if (launched is not null) _arrangeLaunched.AddRange(launched);
        _arrangeTimer ??= new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => { _arrangeTimer!.Stop(); ArrangeNow(); });
        _arrangeTimer.Stop();
        // New tabs need a moment before Windows Terminal shows them.
        _arrangeTimer.Interval = TimeSpan.FromMilliseconds(_arrangeLaunched.Count > 0 ? 1500 : 800);
        _arrangeTimer.Start();
    }

    /// <summary>A completed arrange; tests wait for it.</summary>
    internal Task ArrangeTask { get; private set; } = Task.CompletedTask;

    internal void ArrangeNow()
    {
        var returnFocus = _arrangeReturnFocus;
        var launched = _arrangeLaunched.ToList();
        _arrangeReturnFocus = false;
        _arrangeLaunched.Clear();
        var main = (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        var giveBack = returnFocus && main is { IsActive: true };
        var work = new List<(TabSession Session, Func<TerminalOrder.Outcome> Run)>();
        foreach (var session in Tabs.Select(t => t.Session).OfType<TabSession>().DistinctBy(s => s.WindowName).ToList())
        {
            var rows = LiveRows(session);
            if (rows.Count < 2) continue;
            var believed = BelievedWindow(session);
            var desired = rows.Select(r => r.TrackId!).ToList();
            // A renamed tab shows its launch name until the status poll retitles it (#281), then its current name.
            var titles = rows.ToDictionary(r => r.TrackId!, r => (IReadOnlyList<string>)new[] { r.Name, r.LaunchedAs?.Name ?? r.Name }
                .Select(TerminalOrder.Title).Distinct().ToList());
            var fresh = launched.Where(desired.Contains).ToList();
            var window = session.WindowName;
            work.Add((session, () => TerminalOrder.ArrangeWindow(window, believed, desired, titles, fresh, fresh.FirstOrDefault(), Thread.Sleep)));
        }
        if (work.Count == 0) return;
        var previous = ArrangeTask;
        ArrangeTask = previous.ContinueWith(_ => work.Select(w => (w.Session, Outcome: w.Run())).ToList(), TaskScheduler.Default)
            .ContinueWith(t =>
            {
                if (!t.IsCompletedSuccessfully) return;
                foreach (var (session, outcome) in t.Result)
                    if (outcome.Slots is { } slots) session.WindowOrder = slots;
                // Windows Terminal brings its window forward for every command; a reorder in BoosterAI should not leave it there.
                if (giveBack && t.Result.Any(r => r.Outcome.Ran) && main is not null)
                    DispatcherTimer.RunOnce(() => main.Activate(), TimeSpan.FromMilliseconds(400));
            }, Dispatcher.UIThread.CheckAccess() && SynchronizationContext.Current is not null
                ? TaskScheduler.FromCurrentSynchronizationContext() : TaskScheduler.Default);
    }
}
