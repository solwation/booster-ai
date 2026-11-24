using Avalonia.Threading;
using BoosterAI;
using BoosterAI.Tests;
using Xunit;

public class OrchestraRefreshTests
{
    private static void Settle(MainViewModel vm)
    {
        // A generous deadline: under the full suite's load a pooled directory check can take seconds to start (#190).
        var deadline = Environment.TickCount64 + 30000;
        while (vm.OrkesterChecksInFlight > 0 && Environment.TickCount64 < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
        }
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public void RefreshPicksUpAnOrchestraCreatedLater()
    {
        var dir = Directory.CreateTempSubdirectory("booster-orch-refresh").FullName;
        try
        {
            var result = UiSession.Run(() =>
            {
                var vm = new MainViewModel(new AppConfig { Tabs = [new TabConfig { Directory = dir }] });
                vm.RefreshOrkester();
                Settle(vm);
                var before = vm.Tabs[0].HasOrkester;
                Directory.CreateDirectory(Path.Combine(dir, ".orchestra"));
                vm.RefreshOrkester();
                Settle(vm);
                return (before, vm.Tabs[0].HasOrkester);
            });
            Assert.False(result.before);
            Assert.True(result.Item2);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void MissingDirectoryChangesNothingAndDoesNotThrow()
    {
        var missing = Path.Combine(Path.GetTempPath(), "booster-orch-missing-" + Guid.NewGuid().ToString("N"));
        var result = UiSession.Run(() =>
        {
            var vm = new MainViewModel(new AppConfig { Tabs = [new TabConfig { Directory = missing }] });
            vm.RefreshOrkester();
            Settle(vm);
            return vm.Tabs[0].HasOrkester;
        });
        Assert.Null(result);
    }

    [Fact]
    public void ADirectoryAlreadyBeingCheckedIsSkipped()
    {
        var dir = Directory.CreateTempSubdirectory("booster-orch-inflight").FullName;
        try
        {
            var inFlight = UiSession.Run(() =>
            {
                var vm = new MainViewModel(new AppConfig { Tabs = [new TabConfig { Directory = dir }, new TabConfig { Directory = dir }] });
                vm.RefreshOrkester();
                vm.RefreshOrkester();
                var count = vm.OrkesterChecksInFlight;
                Settle(vm);
                return (count, vm.OrkesterChecksInFlight);
            });
            Assert.Equal(1, inFlight.count);
            Assert.Equal(0, inFlight.Item2);
        }
        finally { Directory.Delete(dir, true); }
    }
}
