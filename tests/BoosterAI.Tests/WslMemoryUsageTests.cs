using BoosterAI;
using Xunit;

namespace BoosterAI.Tests;

public class WslMemoryUsageTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private const long Gb = 1024L * 1024 * 1024;

    [Fact]
    public void RollingWindowEvictsOldPeakAndComputesOnlyObservedSamples()
    {
        var history = new WslMemoryHistory();
        history.Add(Start, new(WslMemoryState.Available, 20 * Gb));
        history.Add(Start.AddSeconds(30), new(WslMemoryState.Available, 4 * Gb));
        history.Add(Start.AddMinutes(30), new(WslMemoryState.Available, 8 * Gb));
        var snapshot = history.Snapshot(Start.AddMinutes(30).AddSeconds(1));
        Assert.Equal(2, snapshot.SampleCount);
        Assert.Equal(6d * Gb, snapshot.AverageBytes);
        Assert.Equal(4 * Gb, snapshot.MinimumBytes);
        Assert.Equal(8 * Gb, snapshot.MaximumBytes);
        Assert.Equal(8 * Gb, snapshot.CurrentBytes);
        Assert.Equal(TimeSpan.Zero, snapshot.CollectedTime);
        snapshot = history.Snapshot(Start.AddMinutes(61));
        Assert.Equal(WslMemoryState.Stale, snapshot.State);
        Assert.Null(snapshot.CurrentBytes);
        Assert.Null(snapshot.AverageBytes);
        Assert.Equal(0, snapshot.SampleCount);
    }

    [Fact]
    public void ContinuousWindowReportsThirtyMinutesAndKeepsBoundarySample()
    {
        var history = new WslMemoryHistory();
        for (var i = 0; i <= 60; i++) history.Add(Start.AddSeconds(i * 30), new(WslMemoryState.Available, (i + 1) * Gb));
        var snapshot = history.Snapshot(Start.AddMinutes(30));
        Assert.Equal(61, snapshot.SampleCount);
        Assert.Equal(TimeSpan.FromMinutes(30), snapshot.CollectedTime);
        Assert.Equal(31d * Gb, snapshot.AverageBytes);
        history.Add(Start.AddMinutes(30.5), new(WslMemoryState.Available, 2 * Gb));
        snapshot = history.Snapshot(Start.AddMinutes(30.5));
        Assert.Equal(61, snapshot.SampleCount);
        Assert.Equal(TimeSpan.FromMinutes(30), snapshot.CollectedTime);
        Assert.Equal(2 * Gb, snapshot.MinimumBytes);
    }

    [Theory]
    [InlineData(WslMemoryState.Unavailable)]
    [InlineData(WslMemoryState.NotRunning)]
    [InlineData(WslMemoryState.LegacyProcess)]
    public void MissingReadIsNotZeroAndDoesNotPretendToCoverTheGap(WslMemoryState missing)
    {
        var history = new WslMemoryHistory();
        history.Add(Start, new(WslMemoryState.Available, 10 * Gb));
        history.Add(Start.AddSeconds(30), new(missing));
        var failed = history.Snapshot(Start.AddSeconds(30));
        Assert.Equal(missing, failed.State);
        Assert.Null(failed.CurrentBytes);
        Assert.Equal(10d * Gb, failed.AverageBytes);
        history.Add(Start.AddSeconds(60), new(WslMemoryState.Available, 6 * Gb));
        history.Add(Start.AddSeconds(90), new(WslMemoryState.Available, 8 * Gb));
        var recovered = history.Snapshot(Start.AddSeconds(90));
        Assert.Equal(8d * Gb, recovered.AverageBytes);
        Assert.Equal(TimeSpan.FromSeconds(30), recovered.CollectedTime);
        Assert.Equal(3, recovered.SampleCount);
    }

    [Fact]
    public void StaleReadingKeepsHistoryButNeverShowsAnOldCurrentValue()
    {
        var history = new WslMemoryHistory();
        history.Add(Start, new(WslMemoryState.Available, 3 * Gb));
        Assert.Equal(WslMemoryState.Available, history.Snapshot(Start.AddSeconds(90)).State);
        var snapshot = history.Snapshot(Start.AddSeconds(91));
        Assert.Equal(WslMemoryState.Stale, snapshot.State);
        Assert.Null(snapshot.CurrentBytes);
        Assert.Equal(3d * Gb, snapshot.AverageBytes);
    }

    [Fact]
    public void DuplicateInstantDoesNotBiasAverageAndInvalidBytesAreUnavailable()
    {
        var history = new WslMemoryHistory();
        history.Add(Start, new(WslMemoryState.Available, 2 * Gb));
        history.Add(Start, new(WslMemoryState.Available, 4 * Gb));
        history.Add(Start.AddSeconds(30), new(WslMemoryState.Available, 0));
        var snapshot = history.Snapshot(Start.AddSeconds(30));
        Assert.Equal(1, snapshot.SampleCount);
        Assert.Equal(4d * Gb, snapshot.AverageBytes);
        Assert.Equal(WslMemoryState.Unavailable, snapshot.State);
        Assert.Null(snapshot.CurrentBytes);
    }

    [Fact]
    public async Task SlowReaderCannotOverlapAndDisposalDropsLateCallback()
    {
        using var entered = new ManualResetEventSlim();
        using var finish = new ManualResetEventSlim();
        var reads = 0;
        var updates = 0;
        using var monitor = new WslMemoryMonitor(() =>
        {
            Interlocked.Increment(ref reads);
            entered.Set();
            finish.Wait(TimeSpan.FromSeconds(5));
            return new(WslMemoryState.Available, Gb);
        });
        monitor.Updated += _ => Interlocked.Increment(ref updates);
        var pending = Task.Run(monitor.Sample);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            monitor.Sample();
            Assert.Equal(1, reads);
            monitor.Dispose();
        }
        finally { finish.Set(); await pending; }
        Assert.Equal(0, updates);
        monitor.Sample();
        Assert.Equal(1, reads);
    }

    [Fact]
    public void ReaderFailureBecomesUnavailableAndNextSampleCanRecover()
    {
        var fail = true;
        using var monitor = new WslMemoryMonitor(() => fail ? throw new InvalidOperationException() : new(WslMemoryState.Available, Gb));
        monitor.Sample();
        Assert.Equal(WslMemoryState.Unavailable, monitor.Snapshot().State);
        fail = false;
        monitor.Sample();
        Assert.Equal(WslMemoryState.Available, monitor.Snapshot().State);
        Assert.Equal(Gb, monitor.Snapshot().CurrentBytes);
    }

    [Fact]
    public void NativeLinuxDoesNotTryToReadOrStartWsl()
    {
        if (OperatingSystem.IsWindows()) return;
        Assert.Equal(WslMemoryState.Unavailable, WslMemorySource.Read().State);
    }
}

[Collection(ConfigFileCollection.Name)]
public class WslMemoryPresentationTests
{
    [Fact]
    public void ObservationsDoNotChangeSelectedCapAndOutsideValuesStayReadable()
    {
        var vm = new OptimizationViewModel(new AppConfig());
        vm.ShowFakeUnoptimized(64, 48);
        vm.SetMemorySnapshot(new(WslMemoryState.Available, 70L * 1024 * 1024 * 1024,
            35d * 1024 * 1024 * 1024, 1024, 70L * 1024 * 1024 * 1024, TimeSpan.FromMinutes(2), 5));
        Assert.Equal(48, vm.SliderMemoryGb);
        Assert.InRange(vm.MemoryLowerFraction, 0, 0.000001);
        Assert.Equal(1, vm.MemoryUpperFraction);
        Assert.InRange(vm.MemoryAverageFraction, 0, 1);
        Assert.Contains("70.0", vm.MemoryCurrentText);
        Assert.Contains("70.0", vm.MemoryStatisticsText);
        Assert.Contains("2/30", vm.MemoryPeriodText);
        vm.SetMemorySnapshot(new(WslMemoryState.NotRunning, null, null, null, null, TimeSpan.Zero, 0));
        Assert.False(vm.HasMemoryHistory);
        Assert.DoesNotContain("70.0", vm.MemoryCurrentText);
        Assert.Equal(48, vm.SliderMemoryGb);
    }

    [Fact]
    public void OverviewUsesWholeHostScaleAndDoesNotTreatSliderSelectionAsSavedCap()
    {
        var vm = new OptimizationViewModel(new AppConfig());
        vm.ShowFakeUnoptimized(64, 48);
        const long gb = 1024L * 1024 * 1024;
        vm.SetMemorySnapshot(new(WslMemoryState.Available, 16 * gb, 12 * gb,
            gb / 2, 20 * gb, TimeSpan.FromMinutes(2), 5));
        Assert.Equal(0.25, vm.MemoryCurrentFraction);
        Assert.Equal(0.5 / 64, vm.MemoryLowerFraction);
        Assert.Equal(12d / 64, vm.MemoryAverageFraction);
        Assert.Equal(20d / 64, vm.MemoryUpperFraction);
        Assert.Equal(24d / 64, vm.SuggestedLowerFraction);
        Assert.Equal(32d / 64, vm.SuggestedUpperFraction);
        Assert.Equal(0.5, vm.MemoryCapFraction);
        Assert.Contains("32", vm.MemoryCapText);
        vm.SliderMemoryGb = 56;
        Assert.Equal(0.5, vm.MemoryCapFraction);
        Assert.Equal(0.25, vm.MemoryCurrentFraction);
        Assert.Contains("16.0", vm.MemoryCurrentValue);
    }

    [Fact]
    public void StaleCurrentIsBlankWhileRetainedHistoryRemainsLabelledAndReadable()
    {
        var vm = new OptimizationViewModel(new AppConfig());
        vm.ShowFakeUnoptimized();
        const long gb = 1024L * 1024 * 1024;
        vm.SetMemorySnapshot(new(WslMemoryState.Stale, null, 12 * gb, 8 * gb, 16 * gb, TimeSpan.FromMinutes(2), 5));
        Assert.False(vm.HasCurrentMemory);
        Assert.Equal("—", vm.MemoryCurrentValue);
        Assert.True(vm.HasMemoryHistory);
        Assert.Equal("8.0 GB", vm.MemoryMinimumValue);
        Assert.Equal("12.0 GB", vm.MemoryAverageValue);
        Assert.Equal("16.0 GB", vm.MemoryMaximumValue);
        Assert.Contains("2/30", vm.MemoryPeriodText);
        vm.SetMemorySnapshot(new(WslMemoryState.NotRunning, null, null, null, null, TimeSpan.Zero, 0));
        Assert.False(vm.HasMemoryHistory);
        Assert.Equal("—", vm.MemoryAverageValue);
        Assert.Equal("—", vm.MemoryMinimumValue);
        Assert.Equal("—", vm.MemoryMaximumValue);
    }

    [Fact]
    public void SavingExplicitlyUpdatesTheConfiguredCapAndNotifiesTheOverview()
    {
        var path = Path.Combine(Path.GetTempPath(), "booster-memory-overview-" + Guid.NewGuid().ToString("N"), ".wslconfig");
        WslConfigFile.UseCustomPath(path);
        try
        {
            var vm = new OptimizationViewModel(new AppConfig());
            vm.ShowFakeUnoptimized(64);
            var changed = new List<string?>();
            vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
            vm.SliderMemoryGb = 16;
            Assert.False(File.Exists(path));
            Assert.Equal(0.5, vm.MemoryCapFraction);
            vm.SaveConfig();
            Assert.False(vm.StatusIsError);
            Assert.Equal(16, WslConfigFile.Load().MemoryGb);
            Assert.Equal("16 GB", vm.MemoryCapText);
            Assert.Equal(0.25, vm.MemoryCapFraction);
            Assert.Contains(nameof(vm.MemoryCapFraction), changed);
            Assert.Contains(nameof(vm.MemoryCapText), changed);
        }
        finally
        {
            WslConfigFile.UseCustomPath(null);
            if (Directory.Exists(Path.GetDirectoryName(path))) Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }
}
