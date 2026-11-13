using System.IO;
using BoosterAI;
using Xunit;

namespace BoosterAI.Tests;

[Collection(ConfigFileCollection.Name)]
public class WslOptimizationTests
{
    [Theory]
    [InlineData("16GB", 16)]
    [InlineData("16gb", 16)]
    [InlineData("16G", 16)]
    [InlineData("  24 GB  ", 24)]
    [InlineData("8192MB", 8)]
    [InlineData("16384mb", 16)]
    [InlineData("16.5GB", 17)]
    [InlineData("1TB", 1024)]
    [InlineData("4194304KB", 4)]
    [InlineData("4194304k", 4)]
    [InlineData("8589934592B", 8)]
    [InlineData("1024KB", 1)]
    [InlineData("1B", 1)]
    public void ParseMemoryStringParsesValidUnits(string input, int expectedGb)
    {
        Assert.Equal(expectedGb, WslConfigFile.ParseMemoryString(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("invalid")]
    [InlineData("GB")]
    [InlineData("16XB")]
    [InlineData("16PB")]
    [InlineData("99999999999GB")]
    [InlineData("99999999999TB")]
    [InlineData("1e400")]
    public void ParseMemoryStringReturnsNullForInvalidInput(string? input)
    {
        Assert.Null(WslConfigFile.ParseMemoryString(input));
    }

    [Fact]
    public void ParseMemoryFromIniExtractsConfiguredValue()
    {
        var ini = """
            # Global WSL config
            [boot]
            systemd=true

            [wsl2]
            # Previous setting
            processors=4
            memory=22GB
            swap=8GB

            [experimental]
            autoMemoryReclaim=gradual
            """;

        Assert.Equal(22, WslConfigFile.ParseMemoryFromIni(ini));
    }

    [Fact]
    public void ParseMemoryFromIniIgnoresCommentsAndOtherSections()
    {
        var ini = """
            [other]
            memory=64GB

            [wsl2]
            # memory=32GB
            processors=8
            """;

        Assert.Null(WslConfigFile.ParseMemoryFromIni(ini));
    }

    [Fact]
    public void ApplyMemoryCreatesSectionInEmptyContent()
    {
        var updated = WslConfigFile.ApplyMemory("", 16);
        Assert.Contains("[wsl2]", updated);
        Assert.Contains("memory=16GB", updated);
        Assert.Equal(16, WslConfigFile.ParseMemoryFromIni(updated));
    }

    [Fact]
    public void ApplyMemoryReplacesExistingSettingPreservingSurroundingLines()
    {
        var ini = """
            # Header comment
            [wsl2]
            processors=4
            memory=8GB
            swap=4GB

            [boot]
            systemd=true
            """;

        var updated = WslConfigFile.ApplyMemory(ini, 24);

        Assert.Contains("# Header comment", updated);
        Assert.Contains("processors=4", updated);
        Assert.Contains("memory=24GB", updated);
        Assert.DoesNotContain("memory=8GB", updated);
        Assert.Contains("swap=4GB", updated);
        Assert.Contains("[boot]", updated);
        Assert.Equal(24, WslConfigFile.ParseMemoryFromIni(updated));
    }

    [Fact]
    public void ApplyMemoryInsertsIntoExistingSectionWithoutMemory()
    {
        var ini = """
            [wsl2]
            processors=4
            swap=4GB
            """;

        var updated = WslConfigFile.ApplyMemory(ini, 12);

        Assert.Contains("[wsl2]", updated);
        Assert.Contains("memory=12GB", updated);
        Assert.Contains("processors=4", updated);
        Assert.Equal(12, WslConfigFile.ParseMemoryFromIni(updated));
    }

    [Fact]
    public void ApplyMemoryAppendsSectionWhenMissing()
    {
        var ini = """
            [boot]
            systemd=true
            """;

        var updated = WslConfigFile.ApplyMemory(ini, 32);

        Assert.Contains("[boot]", updated);
        Assert.Contains("[wsl2]", updated);
        Assert.Contains("memory=32GB", updated);
        Assert.Equal(32, WslConfigFile.ParseMemoryFromIni(updated));
    }

    [Fact]
    public void ApplyMemoryPreservesWindowsCrLf()
    {
        var ini = "[wsl2]\r\nprocessors=4\r\n";
        var updated = WslConfigFile.ApplyMemory(ini, 16);
        Assert.Contains("\r\n", updated);
        Assert.DoesNotContain("\n\n", updated.Replace("\r", ""));
    }

    [Theory]
    [InlineData(8, 3)]
    [InlineData(16, 6)]
    [InlineData(32, 12)]
    [InlineData(64, 24)]
    [InlineData(128, 48)]
    public void RecommendMemoryGbCalculatesSensibleHardwareValues(int totalGb, int expectedRecommendGb)
    {
        Assert.Equal(expectedRecommendGb, WslOptimization.RecommendMemoryGb(totalGb));
    }

    [Theory]
    [InlineData(2, 2, 2)]
    [InlineData(4, 2, 2)]
    [InlineData(8, 3, 4)]
    [InlineData(16, 6, 8)]
    [InlineData(32, 12, 16)]
    [InlineData(64, 24, 32)]
    [InlineData(128, 48, 64)]
    public void RecommendedRangeLeavesRoomForOtherWorkloads(int total, int lower, int upper)
    {
        Assert.Equal((lower, upper), WslOptimization.RecommendMemoryRangeGb(total));
    }

    [Fact]
    public void RangeAndStartingValueStayWithinTheSliderForSmallAndLargeHosts()
    {
        for (var total = 2; total <= 512; total++)
        {
            var (lower, upper) = WslOptimization.RecommendMemoryRangeGb(total);
            var (min, max) = WslOptimization.GetSliderBounds(total);
            Assert.InRange(lower, min, max);
            Assert.InRange(upper, lower, max);
            Assert.Equal(lower, WslOptimization.RecommendMemoryGb(total));
        }
    }

    [Fact]
    public void NewSelectionUsesLowerLimitAndRecommendationDoesNotSilentlySave()
    {
        var file = Path.GetTempFileName();
        try
        {
            WslConfigFile.UseCustomPath(file);
            var vm = new OptimizationViewModel(new AppConfig());
            Assert.Equal(vm.RecommendedLowerGb, vm.SliderMemoryGb);
            vm.SliderMemoryGb = vm.MaxMemoryGb;
            vm.ApplyRecommendation();
            Assert.Equal(vm.RecommendedLowerGb, vm.SliderMemoryGb);
            Assert.Null(WslConfigFile.Load().MemoryGb);
        }
        finally { WslConfigFile.UseCustomPath(null); File.Delete(file); }
    }

    [Fact]
    public void SavedCapOutsideTheRangeIsRetainedUntilTheUserChoosesAnother()
    {
        var file = Path.GetTempFileName();
        try
        {
            WslConfigFile.UseCustomPath(file);
            File.WriteAllText(file, "[wsl2]\nmemory=48GB\nprocessors=4\n");
            var vm = new OptimizationViewModel(new AppConfig());
            Assert.Equal(48, vm.SliderMemoryGb);
            Assert.Equal(48, vm.CurrentAllocatedGb);
            vm.ShowFakeUnoptimized(64, 48);
            Assert.Equal(48, vm.SliderMemoryGb);
            Assert.True(vm.SliderMemoryGb > vm.RecommendedUpperGb);
            vm.ApplyRecommendation();
            Assert.Equal(24, vm.SliderMemoryGb);
            Assert.Equal(48, WslConfigFile.Load().MemoryGb);
            vm.SliderMemoryGb = 48;
            vm.SaveConfig();
            Assert.Equal(48, WslConfigFile.Load().MemoryGb);
            Assert.Contains("processors=4", File.ReadAllText(file));
        }
        finally { WslConfigFile.UseCustomPath(null); File.Delete(file); }
    }

    [Fact]
    public void GetSliderBoundsCalculatesSafeLimits()
    {
        var (min8, max8) = WslOptimization.GetSliderBounds(8);
        Assert.Equal(2, min8);
        Assert.Equal(6, max8);

        var (min32, max32) = WslOptimization.GetSliderBounds(32);
        Assert.Equal(4, min32);
        Assert.Equal(30, max32);
    }

    [Fact]
    public void OptimizationViewModelDismissBannerHidesBannerAndSavesConfig()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            ConfigStore.UseFile(tempFile);
            var cfg = new AppConfig { DismissedWslOptimization = false };
            var vm = new OptimizationViewModel(cfg);

            vm.ShowFakeUnoptimized();
            Assert.True(vm.BannerVisible);

            vm.DismissBanner();
            Assert.False(vm.BannerVisible);
            Assert.True(cfg.DismissedWslOptimization);

            var reloaded = ConfigStore.Load();
            Assert.True(reloaded.DismissedWslOptimization);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void OptimizationViewModelSaveConfigWritesToFile()
    {
        var tempIni = Path.GetTempFileName();
        try
        {
            WslConfigFile.UseCustomPath(tempIni);
            File.WriteAllText(tempIni, "[wsl2]\r\nprocessors=4\r\n");

            var cfg = new AppConfig();
            var vm = new OptimizationViewModel(cfg);
            var targetMem = vm.MinMemoryGb;
            vm.SliderMemoryGb = targetMem;
            vm.SaveConfig();

            Assert.False(vm.StatusIsError);
            Assert.True(vm.HasStatusMessage);
            Assert.Equal(targetMem, vm.CurrentAllocatedGb);
            Assert.Equal(targetMem, WslConfigFile.ParseMemoryFromIni(File.ReadAllText(tempIni)));
        }
        finally
        {
            WslConfigFile.UseCustomPath(null);
            if (File.Exists(tempIni)) File.Delete(tempIni);
        }
    }

    [Fact]
    public void SaveIsOnlyEnabledWhenTheSelectedCapDiffersFromTheSavedOne()
    {
        var file = Path.GetTempFileName();
        try
        {
            WslConfigFile.UseCustomPath(file);
            // Values inside this machine's slider bounds: CI runners have less RAM than a desktop.
            var (min, max) = WslOptimization.GetSliderBounds(WslOptimization.GetTotalMemoryGb());
            Assert.True(max > min);
            File.WriteAllText(file, $"[wsl2]\nmemory={min}GB\nprocessors=4\n");
            var vm = new OptimizationViewModel(new AppConfig());
            var changes = new List<string?>();
            vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
            Assert.Equal(min, vm.SliderMemoryGb);
            Assert.False(vm.CanSave);

            var before = File.GetLastWriteTimeUtc(file);
            var content = File.ReadAllText(file);
            vm.SaveConfig(); // unchanged: nothing is written and no status appears
            Assert.Equal(content, File.ReadAllText(file));
            Assert.Equal(before, File.GetLastWriteTimeUtc(file));
            Assert.False(vm.HasStatusMessage);

            vm.SliderMemoryGb = min + 1;
            Assert.True(vm.CanSave);
            Assert.Contains(nameof(OptimizationViewModel.CanSave), changes);
            vm.SliderMemoryGb = min;
            Assert.False(vm.CanSave);

            vm.SliderMemoryGb = min + 1;
            vm.SaveConfig();
            Assert.Equal(min + 1, WslConfigFile.Load().MemoryGb);
            Assert.False(vm.CanSave);
        }
        finally { WslConfigFile.UseCustomPath(null); File.Delete(file); }
    }

    [Fact]
    public void SaveIsEnabledWhenWslconfigHasNoMemoryCap()
    {
        var file = Path.GetTempFileName();
        try
        {
            WslConfigFile.UseCustomPath(file);
            File.WriteAllText(file, "[wsl2]\nprocessors=4\n");
            var vm = new OptimizationViewModel(new AppConfig());
            Assert.Null(vm.CurrentAllocatedGb);
            Assert.True(vm.CanSave);
        }
        finally { WslConfigFile.UseCustomPath(null); File.Delete(file); }
    }
}
