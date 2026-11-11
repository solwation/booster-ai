using BoosterAI;
using Xunit;

public class RecoveryNoticeTests
{
    private static MainViewModel RunningTabs(int count)
    {
        var vm = new MainViewModel(new AppConfig
        {
            Tabs = Enumerable.Range(0, count).Select(i => new TabConfig { Name = "project" + i }).ToList()
        });
        foreach (var row in vm.Tabs) row.SetState(TabState.Running, null, activity: Activity.Busy);
        return vm;
    }

    [Theory]
    [InlineData(1, TabState.Closed)]
    [InlineData(2, TabState.CliExited)]
    [InlineData(3, TabState.Lost)]
    [InlineData(2, TabState.Starting)]
    public void FirstRecoveredTabStateChangeRetiresTheStartupCount(int count, TabState next)
    {
        var vm = RunningTabs(count);
        vm.ShowRecoveredStatus(vm.Tabs, vm.StatusRevision);
        Assert.Equal(Loc.Format(count == 1 ? "status.foundRunning.one" : "status.foundRunning.many", count), vm.Status);
        Assert.False(vm.StatusIsError);
        vm.Tabs[0].SetState(next, next == TabState.CliExited ? 0 : null);
        Assert.Equal("", vm.Status);
        foreach (var row in vm.Tabs) row.SetState(TabState.Closed, null);
        Assert.Equal("", vm.Status);
    }

    [Fact]
    public void PollingActivityAndUnrelatedTabsDoNotRetireTheNotice()
    {
        var vm = RunningTabs(2);
        vm.ShowRecoveredStatus([vm.Tabs[0]], vm.StatusRevision);
        var notice = vm.Status;
        vm.Tabs[0].SetState(TabState.Running, null, activity: Activity.Idle);
        vm.Tabs[1].SetState(TabState.CliExited, 0);
        Assert.Equal(notice, vm.Status);
        vm.Tabs[0].SetState(TabState.CliExited, 0);
        Assert.Equal("", vm.Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LaterStatusKeepsOwnershipWhenRecoveredTabsEnd(bool error)
    {
        var vm = RunningTabs(2);
        var initialRevision = vm.StatusRevision;
        vm.ShowRecoveredStatus(vm.Tabs, initialRevision);
        Show(vm, "new message", error);
        foreach (var row in vm.Tabs) row.SetState(TabState.Closed, null);
        Assert.Equal("new message", Shown(vm));
        Assert.Equal(error, vm.StatusIsError);
        // The same recovery result arriving late must not resurrect its old count.
        vm.ShowRecoveredStatus(vm.Tabs, initialRevision);
        Assert.Equal("new message", Shown(vm));
        Assert.Equal(error, vm.StatusIsError);
    }

    [Fact]
    public void DelayedRecoveryCannotOverwriteEvenANewerEmptyStatus()
    {
        var vm = RunningTabs(1);
        var revisionBeforeLookup = vm.StatusRevision;
        vm.ShowStatus(""); // A launch completed with no message while discovery was pending.
        vm.ShowRecoveredStatus(vm.Tabs, revisionBeforeLookup);
        Assert.Equal("", vm.Status);
        Show(vm, "launch failed", error: true);
        vm.ShowRecoveredStatus(vm.Tabs, revisionBeforeLookup);
        Assert.Equal("launch failed", vm.Error.Text);
        Assert.Equal("", vm.Status);
        Assert.True(vm.StatusIsError);
    }

    [Fact]
    public void RestartValidationReplacesTheNoticeAndPreventsItsLateReturn()
    {
        var vm = RunningTabs(1);
        var row = vm.Tabs[0];
        row.LaunchedAs = row.ToConfig(forLaunch: true);
        vm.ShowRecoveredStatus(vm.Tabs, vm.StatusRevision);
        var recoveryRevision = vm.StatusRevision;
        row.Model = "unsafe;model";
        // Validation fails before any process is launched, but this is the real restart entry point.
        vm.RestartChanged(row);
        Assert.True(vm.StatusIsError);
        Assert.Equal(ErrorKind.InvalidTab, vm.Error.Kind);
        Assert.NotEmpty(vm.Error.Text);
        var error = vm.Error.Text;
        vm.ShowRecoveredStatus(vm.Tabs, recoveryRevision);
        row.SetState(TabState.Closed, null);
        Assert.Equal(error, vm.Error.Text);
        Assert.Equal("", vm.Status);
        Assert.True(vm.StatusIsError);
    }

    [Fact]
    public void AlreadyExitedCliDoesNotProduceARunningCountAtRecovery()
    {
        var vm = RunningTabs(2);
        vm.Tabs[0].SetState(TabState.CliExited, 0);
        vm.ShowRecoveredStatus(vm.Tabs, vm.StatusRevision);
        Assert.Equal(Loc.Format("status.foundRunning.one", 1), vm.Status);
        vm.Tabs[1].SetState(TabState.Closed, null);
        vm.ShowRecoveredStatus(vm.Tabs, vm.StatusRevision);
        Assert.Equal("", vm.Status);
    }

    private static void Show(MainViewModel vm, string text, bool error)
    {
        if (error) vm.ShowError(ErrorKind.InvalidTab, text);
        else vm.ShowStatus(text);
    }

    /// <summary>The newest outcome's text: the error banner's while it shows, otherwise the status line's.</summary>
    private static string Shown(MainViewModel vm) => vm.Error.IsVisible ? vm.Error.Text : vm.Status;
}
