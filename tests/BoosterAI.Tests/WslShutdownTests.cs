using BoosterAI;
using Xunit;

public class WslShutdownTests
{
    [Fact]
    public void ShutdownRequiresExplicitConfirmationAndRunsOnce()
    {
        var calls = 0;
        var vm = new OptimizationViewModel(new AppConfig()) { ShutdownWsl = () => calls++ };
        vm.RestartWsl();
        vm.RestartWsl(confirmed: false);
        Assert.Equal(0, calls);
        vm.RestartWsl(confirmed: true);
        Assert.Equal(1, calls);
        Assert.False(vm.StatusIsError);
        Assert.Equal(Loc.Get("settings.optimization.restarted"), vm.StatusMessage);
        vm.RestartWsl(confirmed: false);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void ConfirmedFailureIsReportedWithoutClaimingShutdown()
    {
        var vm = new OptimizationViewModel(new AppConfig())
        {
            ShutdownWsl = () => throw new InvalidOperationException("synthetic shutdown failure")
        };
        vm.RestartWsl(confirmed: true);
        Assert.True(vm.StatusIsError);
        Assert.Contains("synthetic shutdown failure", vm.StatusMessage);
        Assert.NotEqual(Loc.Get("settings.optimization.restarted"), vm.StatusMessage);
    }
}
