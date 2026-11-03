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
    [InlineData(8, 6)]
    [InlineData(16, 10)]
    [InlineData(32, 22)]
    [InlineData(64, 48)]
    [InlineData(128, 96)]
    public void RecommendMemoryGbCalculatesSensibleHardwareValues(int totalGb, int expectedRecommendGb)
    {
        Assert.Equal(expectedRecommendGb, WslOptimization.RecommendMemoryGb(totalGb));
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
}
