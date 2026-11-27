using Avalonia.Automation;
using BoosterAI;
using Xunit;

namespace BoosterAI.Tests;

/// <summary>"Show the tab in the terminal" (#213): the command built for a row, its index, and who gets the action.</summary>
[Collection(ConfigFileCollection.Name)]
public sealed class TerminalFocusTests : IDisposable
{
    private readonly string _language = Loc.Language;
    private readonly bool _supported = TerminalFocus.Supported;
    public TerminalFocusTests() { Loc.Use("sv"); TerminalFocus.Supported = true; }
    public void Dispose() { Loc.Use(_language); TerminalFocus.Supported = _supported; }

    [Fact]
    public void WindowsBuildsFocusTabForTheSessionWindow()
    {
        var command = TerminalFocus.Build("BoosterAI-20260101-120000", 2, windows: true)!;
        Assert.Equal("wt.exe", command.Exe);
        Assert.Equal(["-w", "BoosterAI-20260101-120000", "focus-tab", "-t", "2"], command.Args);
        Assert.Equal("wt.exe -w BoosterAI-20260101-120000 focus-tab -t 2", TerminalFocus.Describe(command));
    }

    [Fact]
    public void LinuxAndUnknownPositionsBuildNothing()
    {
        Assert.Null(TerminalFocus.Build("BoosterAI-x", 0, windows: false));
        Assert.Null(TerminalFocus.Build("BoosterAI-x", null, windows: true));
        Assert.Null(TerminalFocus.Build("", 0, windows: true));
    }

    [Fact]
    public void IndexIsTheRankByStartOrderAmongLiveTabs()
    {
        // tab2 was closed: tab3 moved up to position 1; tab10 sorts after tab9, not after tab1.
        string[] live = ["tab10", "tab1", "tab3", "tab9"];
        Assert.Equal(0, TerminalFocus.Index("tab1", live));
        Assert.Equal(1, TerminalFocus.Index("tab3", live));
        Assert.Equal(3, TerminalFocus.Index("tab10", live));
        Assert.Null(TerminalFocus.Index("tab2", live));
    }

    [Fact]
    public void RowCommandUsesItsSessionsLiveTabsOnly()
    {
        var root = Path.Combine(Path.GetTempPath(), "bs-focus-" + Guid.NewGuid().ToString("N"));
        try
        {
            var session = new TabSession(root, new DateTime(2026, 1, 1, 12, 0, 0));
            var vm = new MainViewModel(new AppConfig { Tabs = [new() { Name = "Atlas" }, new() { Name = "Borealis" }, new() { Name = "Cirrus" }] });
            for (var i = 0; i < 3; i++) { vm.Tabs[i].Session = session; vm.Tabs[i].Track = session.Track("tab" + (i + 1)); }
            vm.Tabs[0].SetState(TabState.Closed, null);
            vm.Tabs[1].SetState(TabState.Running, null);
            vm.Tabs[2].SetState(TabState.Running, null);
            Assert.False(vm.Tabs[0].CanShowTerminal);
            Assert.True(vm.Tabs[2].CanShowTerminal);
            // Windows-only: the command itself is built for Windows only; on Linux the row builds none.
            var command = vm.Tabs[2].ShowTerminalCommand;
            if (OperatingSystem.IsWindows()) Assert.Equal(["-w", session.WindowName, "focus-tab", "-t", "1"], command!.Args);
            else Assert.Null(command);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void NeverStartedClosedAndUnsupportedRowsDoNotOfferIt()
    {
        var vm = new MainViewModel(new AppConfig { Tabs = [new() { Name = "Atlas" }] });
        var tab = vm.Tabs[0];
        Assert.False(tab.CanShowTerminal);
        tab.SetState(TabState.Running, null);
        Assert.True(tab.CanShowTerminal);
        tab.SetState(TabState.Closed, null);
        Assert.False(tab.CanShowTerminal);
        tab.SetState(TabState.Running, null);
        TerminalFocus.Supported = false;
        Assert.False(tab.CanShowTerminal);
    }

    [Fact]
    public void TextsExistInBothLanguages()
    {
        var vm = new MainViewModel(new AppConfig { Tabs = [new() { Name = "Atlas" }] });
        Assert.Null(vm.Tabs[0].ShowTerminalHelp); // never started: no terminal tab to show
        vm.Tabs[0].SetState(TabState.Running, null);
        Assert.Equal("Enter visar Atlas i terminalen", vm.Tabs[0].ShowTerminalHelp);
        Loc.Use("en");
        Assert.Equal("Enter shows Atlas in the terminal", vm.Tabs[0].ShowTerminalHelp);
    }

    [Fact]
    public void ClickRunsTheCommandOnWindows()
    {
        var root = Path.Combine(Path.GetTempPath(), "bs-focus-" + Guid.NewGuid().ToString("N"));
        var ran = new List<TerminalFocus.Command>();
        var previous = TerminalFocus.Run;
        try
        {
            TerminalFocus.Run = ran.Add;
            var session = new TabSession(root, new DateTime(2026, 1, 1, 12, 0, 1));
            var vm = new MainViewModel(new AppConfig { Tabs = [new() { Name = "Atlas" }] });
            var tab = vm.Tabs[0];
            tab.Session = session; tab.Track = session.Track("tab1");
            tab.SetState(TabState.Running, null);
            Assert.Equal(OperatingSystem.IsWindows(), vm.ShowTerminal(tab));
            Assert.Equal(OperatingSystem.IsWindows() ? 1 : 0, ran.Count);
        }
        finally { TerminalFocus.Run = previous; Directory.Delete(root, recursive: true); }
    }
}
