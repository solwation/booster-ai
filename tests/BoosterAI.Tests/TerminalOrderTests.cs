using System.Text;
using BoosterAI;
using Xunit;

namespace BoosterAI.Tests;

/// <summary>Terminal tabs follow the row order (#254): target positions, the wt.exe commands and the row model.</summary>
[Collection(ConfigFileCollection.Name)]
public sealed class TerminalOrderTests : IDisposable
{
    private readonly bool _supported = TerminalOrder.Supported;
    private readonly Func<string, TerminalOrder.Snapshot?> _read = TerminalOrder.Read;
    private readonly Action<TerminalFocus.Command> _run = TerminalOrder.Run;
    private readonly Func<bool> _appends = TerminalOrder.Appends;
    private readonly List<TerminalFocus.Command> _ran = [];

    public TerminalOrderTests()
    {
        TerminalOrder.Supported = true;
        TerminalOrder.Run = _ran.Add;
        TerminalOrder.Read = _ => null;
        TerminalOrder.Appends = () => true;
    }

    public void Dispose()
    {
        TerminalOrder.Supported = _supported;
        TerminalOrder.Read = _read;
        TerminalOrder.Run = _run;
        TerminalOrder.Appends = _appends;
    }

    private static List<string?> Apply(IReadOnlyList<string?> window, IReadOnlyList<int> moves)
    {
        var slots = window.ToList();
        foreach (var at in moves) { var s = slots[at]; slots.RemoveAt(at); slots.Add(s); }
        return slots;
    }

    [Fact]
    public void NewTabAppendedAtTheEndMovesTheTabsThatBelongAfterIt()
    {
        // Rows A(tab1) N(tab4, just started) B(tab2) C(tab3): new-tab put tab4 last.
        string?[] window = ["tab1", "tab2", "tab3", "tab4"];
        var plan = TerminalOrder.Arrange(window, ["tab1", "tab4", "tab2", "tab3"]);
        Assert.Equal([1, 1], plan.Moves);
        Assert.Equal(["tab1", "tab4", "tab2", "tab3"], plan.Slots);
        Assert.Equal(plan.Slots, Apply(window, plan.Moves));
    }

    [Fact]
    public void NewTabThatBelongsLastAndOrderedWindowsNeedNoMove()
    {
        Assert.Empty(TerminalOrder.Arrange(["tab1", "tab2", "tab3"], ["tab1", "tab2", "tab3"]).Moves);
        Assert.Null(TerminalOrder.Build("BoosterAI-x", TerminalOrder.Arrange(["tab1", "tab2"], ["tab1", "tab2"]), 0));
    }

    [Fact]
    public void ReorderedRowsMoveTheirTabs()
    {
        string?[] window = ["tab1", "tab2", "tab3", "tab4"];
        // Swap the first two (Alt+Down on the first row).
        var swap = TerminalOrder.Arrange(window, ["tab2", "tab1", "tab3", "tab4"]);
        Assert.Equal(["tab2", "tab1", "tab3", "tab4"], swap.Slots);
        Assert.Equal(swap.Slots, Apply(window, swap.Moves));
        // The last row dragged to the top: the others move behind it, the dragged tab stays.
        var top = TerminalOrder.Arrange(window, ["tab4", "tab1", "tab2", "tab3"]);
        Assert.Equal([0, 0, 0], top.Moves);
        Assert.Equal(["tab4", "tab1", "tab2", "tab3"], top.Slots);
        // The first row dragged to the bottom: one move.
        Assert.Equal([0], TerminalOrder.Arrange(window, ["tab2", "tab3", "tab4", "tab1"]).Moves);
    }

    [Fact]
    public void ClosedTabsLeaveNoGap()
    {
        // tab2 was closed: the window no longer has it, so tab3 is at 1 and tab4 at 2.
        var believed = TerminalOrder.Believed(["tab1", "tab2", "tab3", "tab4"], ["tab4", "tab3", "tab1"]);
        Assert.Equal(["tab1", "tab3", "tab4"], believed);
        var plan = TerminalOrder.Arrange(believed, ["tab1", "tab4", "tab3"]);
        Assert.Equal([1], plan.Moves);
        // A live tab the saved order does not know (older session) is appended by start order: tab10 after tab9.
        Assert.Equal(["tab3", "tab9", "tab10"], TerminalOrder.Believed(["tab3"], ["tab10", "tab9", "tab3"]));
    }

    [Fact]
    public void ForeignTabsAreNeverMovedAndCountInIndices()
    {
        // The user's own tab sits between BoosterAI's, another at the end; null = not BoosterAI's.
        string?[] window = ["tab1", null, "tab2", "tab3", null];
        var plan = TerminalOrder.Arrange(window, ["tab2", "tab1", "tab3"]);
        Assert.Equal([0, 2], plan.Moves); // tab1 at 0; then tab3, now at 2 behind the foreign tab
        Assert.Equal([null, "tab2", null, "tab1", "tab3"], plan.Slots);
        Assert.Equal(plan.Slots, Apply(window, plan.Moves));
        // Every step moves one of BoosterAI's own tabs, never a foreign one.
        var slots = window.ToList();
        foreach (var at in plan.Moves) { Assert.NotNull(slots[at]); var s = slots[at]; slots.RemoveAt(at); slots.Add(s); }
        // A foreign tab that was selected is shown again where it ended up.
        Assert.Equal(0, plan.After(1));
        Assert.Equal(2, plan.After(4));
    }

    [Fact]
    public void BootstrapAndUnknownTitlesMatchAsForeign()
    {
        (string, string)[] own = [("tab1", "Atlas"), ("tab2", "Atlas"), ("tab3", "Cirrus ”x”")];
        var slots = TerminalOrder.Match(["Atlas", "BoosterAI-placement-0123", "Cirrus ”x”", "Atlas", "pwsh"], own);
        // Equal titles take the own tabs in believed order.
        Assert.Equal(["tab1", null, "tab3", "tab2", null], slots);
        Assert.Equal("Cirrus ”x”", TerminalOrder.Title("Cirrus \"x\""));
    }

    [Fact]
    public void ARenamedTabMatchesByItsLaunchOrItsNewNameOnce()
    {
        // #281: tab2 was renamed from Atlas to Borealis; its terminal tab shows either until the status poll retitles it.
        (string, string)[] own = [("tab1", "Cirrus"), ("tab2", "Borealis"), ("tab2", "Atlas")];
        Assert.Equal(["tab1", "tab2"], TerminalOrder.Match(["Cirrus", "Atlas"], own));
        Assert.Equal(["tab2", "tab1"], TerminalOrder.Match(["Borealis", "Cirrus"], own));
        Assert.Equal(["tab2", null], TerminalOrder.Match(["Atlas", "Borealis"], own));
    }

    [Fact]
    public void CommandFocusesAndMovesEachTabThenShowsTheChosenOne()
    {
        var plan = TerminalOrder.Arrange(["tab1", "tab2", "tab3", "tab4"], ["tab1", "tab4", "tab2", "tab3"]);
        var command = TerminalOrder.Build("BoosterAI-20260101-120000", plan, 1)!;
        Assert.Equal("wt.exe", command.Exe);
        Assert.Equal(["-w", "BoosterAI-20260101-120000", "focus-tab", "-t", "1", ";", "move-pane", "-t", "9999",
            ";", "focus-tab", "-t", "1", ";", "move-pane", "-t", "9999", ";", "focus-tab", "-t", "1"], command.Args);
        // wt splits on ';' anywhere: only the separators may contain one.
        Assert.All(command.Args.Where(a => a != ";"), a => Assert.DoesNotContain(';', a));
    }

    [Fact]
    public void SavedOrderRoundTripsWithForeignMarks()
    {
        var text = TerminalOrder.Format(["tab2", null, "tab1"]);
        Assert.Equal("tab2\n-\ntab1\n", text);
        Assert.Equal(["tab2", null, "tab1"], TerminalOrder.Parse(text.Replace("\n", "\r\n")));
    }

    [Fact]
    public void SnapshotOutputParsesTitlesAndSelection()
    {
        string Line(bool selected, string title) => (selected ? "1 " : "0 ") + Convert.ToBase64String(Encoding.UTF8.GetBytes(title));
        var snapshot = TerminalOrder.ParseSnapshot(Line(false, "Åsa · 2") + "\r\n" + Line(true, "Borealis") + "\n")!;
        Assert.Equal(["Åsa · 2", "Borealis"], snapshot.Titles);
        Assert.Equal(1, snapshot.Selected);
        Assert.Null(TerminalOrder.ParseSnapshot("Add-Type : failed"));
        Assert.Empty(TerminalOrder.ParseSnapshot("")!.Titles);
        var script = TerminalOrder.SnapshotScript(66966);
        Assert.Contains("FromHandle([IntPtr]66966)", script);
        Assert.Contains("ControlType]::TabItem", script);
    }

    [Fact]
    public void AfterCurrentTabSettingTurnsArrangingOff()
    {
        Assert.True(TerminalOrder.NewTabsAppend(["", "{ \"profiles\": {} }", "not json"]));
        Assert.True(TerminalOrder.NewTabsAppend(["{ // comment\n \"newTabPosition\": \"afterLastTab\", }"]));
        Assert.False(TerminalOrder.NewTabsAppend(["{}", "{\n  // mine\n  \"newTabPosition\": \"afterCurrentTab\",\n}"]));
        TerminalOrder.Appends = () => false;
        var outcome = TerminalOrder.ArrangeWindow("w", ["tab2", "tab1"], ["tab1", "tab2"], Titles(("tab1", "A"), ("tab2", "B")), [], null, _ => { });
        Assert.Null(outcome.Slots);
        Assert.Empty(_ran);
    }

    private static Dictionary<string, IReadOnlyList<string>> Titles(params (string Id, string Title)[] t) =>
        t.ToDictionary(x => x.Id, x => (IReadOnlyList<string>)[x.Title]);

    [Fact]
    public void SnapshotRevealsUserDragsAndForeignTabs()
    {
        // BoosterAI believes tab1, tab2, tab3; the user dragged tab3 first and opened a PowerShell tab after it.
        TerminalOrder.Read = _ => new(["Cirrus", "PowerShell", "Atlas", "Borealis"], 1);
        var outcome = TerminalOrder.ArrangeWindow("BoosterAI-w", ["tab1", "tab2", "tab3"], ["tab1", "tab2", "tab3"],
            Titles(("tab1", "Atlas"), ("tab2", "Borealis"), ("tab3", "Cirrus")), [], null, _ => { });
        Assert.True(outcome.Ran);
        Assert.Equal([null, "tab1", "tab2", "tab3"], outcome.Slots);
        // Cirrus (0) to the end; the PowerShell tab the user had selected is shown again.
        Assert.Equal(["-w", "BoosterAI-w", "focus-tab", "-t", "0", ";", "move-pane", "-t", "9999", ";", "focus-tab", "-t", "0"], Assert.Single(_ran).Args);
    }

    [Fact]
    public void LaunchedTabsAreAwaitedInTheSnapshot()
    {
        var reads = 0;
        TerminalOrder.Read = _ => ++reads < 3 ? new(["Atlas", "Cirrus"], 0) : new(["Atlas", "Cirrus", "Borealis"], 2);
        var sleeps = 0;
        var outcome = TerminalOrder.ArrangeWindow("w", ["tab1", "tab3", "tab4"], ["tab1", "tab4", "tab3"],
            Titles(("tab1", "Atlas"), ("tab3", "Cirrus"), ("tab4", "Borealis")), ["tab4"], "tab4", _ => sleeps++);
        Assert.Equal(3, reads);
        Assert.Equal(2, sleeps);
        Assert.Equal(["tab1", "tab4", "tab3"], outcome.Slots);
        // tab3 moves behind the new tab, which is then shown.
        Assert.Equal(["-w", "w", "focus-tab", "-t", "1", ";", "move-pane", "-t", "9999", ";", "focus-tab", "-t", "1"], Assert.Single(_ran).Args);

        // A new tab that never shows up: nothing is moved.
        _ran.Clear();
        TerminalOrder.Read = _ => new(["Atlas"], 0);
        Assert.Null(TerminalOrder.ArrangeWindow("w", ["tab1", "tab4"], ["tab4", "tab1"], Titles(("tab1", "Atlas"), ("tab4", "Borealis")), ["tab4"], "tab4", _ => { }).Slots);
        Assert.Empty(_ran);
    }

    [Fact]
    public void WithoutSnapshotTheBelievedOrderIsUsed()
    {
        var outcome = TerminalOrder.ArrangeWindow("w", ["tab1", "tab2"], ["tab2", "tab1"], Titles(("tab1", "A"), ("tab2", "B")), ["tab2"], null, _ => { });
        Assert.Equal(["tab2", "tab1"], outcome.Slots);
        Assert.Equal(["-w", "w", "focus-tab", "-t", "0", ";", "move-pane", "-t", "9999"], Assert.Single(_ran).Args);
    }

    [Fact]
    public async Task RowsStartedMidOrderAndSwappedRowsArrangeTheSessionWindow()
    {
        var root = Path.Combine(Path.GetTempPath(), "bs-order-" + Guid.NewGuid().ToString("N"));
        try
        {
            var vm = new MainViewModel(new AppConfig { Tabs = [new() { Name = "Atlas", Directory = "~" }, new() { Name = "Borealis", Directory = "~" }, new() { Name = "Cirrus", Directory = "~" }] });
            var session = new TabSession(root, new DateTime(2026, 1, 1, 12, 0, 0));
            // Atlas and Cirrus run (tab1, tab2); Borealis, between them, is started later as tab3.
            string[] ids = ["tab1", "tab3", "tab2"];
            for (var i = 0; i < 3; i++)
            {
                vm.Tabs[i].Session = session;
                vm.Tabs[i].Track = session.Track(ids[i]);
                vm.Tabs[i].LaunchedAs = vm.Tabs[i].ToConfig(forLaunch: true);
                vm.Tabs[i].SetState(TabState.Running, null);
            }
            session.WindowOrder = ["tab1", "tab2", "tab3"];
            Assert.Equal(["tab1", "tab2", "tab3"], vm.BelievedWindow(session));
            vm.ArrangeNow();
            await vm.ArrangeTask;
            Assert.Equal(["-w", session.WindowName, "focus-tab", "-t", "1", ";", "move-pane", "-t", "9999"], Assert.Single(_ran).Args);
            Assert.Equal(["tab1", "tab3", "tab2"], session.WindowOrder);
            Assert.Equal(["tab1", "tab3", "tab2"], vm.BelievedWindow(session));

            // Alt+Down on Atlas: Borealis, Atlas, Cirrus.
            _ran.Clear();
            Assert.True(vm.MoveStep(vm.Tabs[0], 1));
            vm.ArrangeNow();
            await vm.ArrangeTask;
            Assert.Equal(["tab3", "tab1", "tab2"], session.WindowOrder);
            Assert.Equal(["-w", session.WindowName, "focus-tab", "-t", "0", ";", "move-pane", "-t", "9999", ";", "focus-tab", "-t", "1", ";", "move-pane", "-t", "9999"], Assert.Single(_ran).Args);

            // Cirrus closed: it leaves no gap, and moving rows that have no tab does nothing.
            vm.Tabs[2].SetState(TabState.Closed, null);
            Assert.Equal(["tab3", "tab1"], vm.BelievedWindow(session));
            _ran.Clear();
            vm.ArrangeNow();
            await vm.ArrangeTask;
            Assert.Empty(_ran);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void LaunchIntoTheSessionWindowRecordsTheAppendedTabs()
    {
        var name = "order-" + Guid.NewGuid().ToString("N");
        var vm = new MainViewModel(new AppConfig { Notifications = false, Tabs = [new() { Name = "a-" + name, Directory = "~" }, new() { Name = "b-" + name, Directory = "~", Enabled = false }] });
        Assert.True(vm.Launch());
        var session = vm.Tabs[0].Session!;
        var first = vm.Tabs[0].TrackId!;
        Assert.Equal([first], session.WindowOrder);
        vm.StartTab(vm.Tabs[1]);
        Assert.Equal([first, vm.Tabs[1].TrackId], session.WindowOrder);
        Assert.Equal(session.WindowName, Assert.Single(TestTerminal.For("b-" + name)).Window);
    }
}
