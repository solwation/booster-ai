using System.IO;
using System.Text;
using BoosterAI;
using Xunit;

namespace BoosterAI.Tests;

/// <summary>[experimental] autoMemoryReclaim in an isolated .wslconfig (#183).</summary>
[Collection(ConfigFileCollection.Name)]
public class WslMemoryReclaimTests
{
    private const string Sample =
        "# Global WSL settings\r\n[wsl2]\r\nmemory=24GB\r\n; keep swap small\r\nswap=4GB\r\n\r\n[boot]\r\nsystemd=true\r\n";

    [Theory]
    [InlineData("", MemoryReclaimMode.NotSet, null)]
    [InlineData("[wsl2]\nmemory=8GB\n", MemoryReclaimMode.NotSet, null)]
    [InlineData("[wsl2]\nautoMemoryReclaim=gradual\n", MemoryReclaimMode.NotSet, null)] // wrong section
    [InlineData("[experimental]\n# autoMemoryReclaim=gradual\n", MemoryReclaimMode.NotSet, null)]
    [InlineData("[experimental]\nautoMemoryReclaim=gradual\n", MemoryReclaimMode.Gradual, "gradual")]
    [InlineData("[Experimental]\r\n  AutoMemoryReclaim = DropCache \r\n", MemoryReclaimMode.DropCache, "DropCache")]
    [InlineData("[experimental]\nautoMemoryReclaim=dropcache\n", MemoryReclaimMode.DropCache, "dropcache")]
    [InlineData("[experimental]\nautoMemoryReclaim=disabled\n", MemoryReclaimMode.Disabled, "disabled")]
    [InlineData("[experimental]\nautoMemoryReclaim=\"gradual\"\n", MemoryReclaimMode.Gradual, "gradual")]
    [InlineData("[experimental]\nautoMemoryReclaim=sometimes", MemoryReclaimMode.Other, "sometimes")]
    [InlineData("[experimental]\nautoMemoryReclaim=gradual\n[experimental]\nautoMemoryReclaim=disabled\n", MemoryReclaimMode.Disabled, "disabled")]
    public void ParseReadsTheCurrentState(string ini, MemoryReclaimMode mode, string? raw)
    {
        Assert.Equal(new MemoryReclaimSetting(mode, raw), WslMemoryReclaim.Parse(ini));
    }

    [Fact]
    public void SettingGradualAppendsTheSectionAndKeepsEveryOtherByte()
    {
        var updated = WslMemoryReclaim.Apply(Sample, WslMemoryReclaim.Gradual);
        Assert.Equal(Sample + "\r\n[experimental]\r\nautoMemoryReclaim=gradual\r\n", updated);
        Assert.Equal(MemoryReclaimMode.Gradual, WslMemoryReclaim.Parse(updated).Mode);
        Assert.Equal(24, WslConfigFile.ParseMemoryFromIni(updated));
        Assert.Equal("+ \n+ [experimental]\n+ autoMemoryReclaim=gradual", WslMemoryReclaim.Preview(Sample, updated));
    }

    [Fact]
    public void SettingGradualIntoAnExistingSectionInsertsOnlyTheKeyLine()
    {
        var ini = "[experimental]\r\nsparseVhd=true\r\n# note\r\n\r\n[wsl2]\r\nmemory=8GB\r\n";
        var updated = WslMemoryReclaim.Apply(ini, WslMemoryReclaim.Gradual);
        Assert.Equal("[experimental]\r\nautoMemoryReclaim=gradual\r\nsparseVhd=true\r\n# note\r\n\r\n[wsl2]\r\nmemory=8GB\r\n", updated);
        Assert.Equal("+ autoMemoryReclaim=gradual", WslMemoryReclaim.Preview(ini, updated));
    }

    [Fact]
    public void NewAndLfFilesGetMatchingLineEndings()
    {
        Assert.Equal("[experimental]\r\nautoMemoryReclaim=gradual\r\n", WslMemoryReclaim.Apply("", WslMemoryReclaim.Gradual));
        Assert.Equal("[wsl2]\nmemory=8GB\n\n[experimental]\nautoMemoryReclaim=gradual\n",
            WslMemoryReclaim.Apply("[wsl2]\nmemory=8GB\n", WslMemoryReclaim.Gradual));
        // An unterminated last line gets the file's line ending before the new section.
        Assert.Equal("[wsl2]\r\nmemory=8GB\r\n\r\n[experimental]\r\nautoMemoryReclaim=gradual\r\n",
            WslMemoryReclaim.Apply("[wsl2]\r\nmemory=8GB", WslMemoryReclaim.Gradual));
    }

    [Fact]
    public void SwitchingToDropCacheReplacesOnlyThatLine()
    {
        var ini = Sample + "\r\n[experimental]\r\nsparseVhd=true\r\nautoMemoryReclaim = gradual\r\n# trailing comment\r\n";
        var updated = WslMemoryReclaim.Apply(ini, WslMemoryReclaim.DropCache);
        Assert.Equal(ini.Replace("autoMemoryReclaim = gradual", "autoMemoryReclaim=dropCache"), updated);
        Assert.Equal(MemoryReclaimMode.DropCache, WslMemoryReclaim.Parse(updated).Mode);
        Assert.Equal("- autoMemoryReclaim = gradual\n+ autoMemoryReclaim=dropCache", WslMemoryReclaim.Preview(ini, updated));
    }

    [Fact]
    public void SettingLeavesASingleKeyWhenTheFileHadDuplicates()
    {
        var ini = "[experimental]\nautoMemoryReclaim=disabled\nsparseVhd=true\nautoMemoryReclaim=dropcache\n";
        Assert.Equal("[experimental]\nautoMemoryReclaim=gradual\nsparseVhd=true\n", WslMemoryReclaim.Apply(ini, WslMemoryReclaim.Gradual));
    }

    [Fact]
    public void RemovingAfterSettingRestoresTheOriginalFile()
    {
        var set = WslMemoryReclaim.Apply(Sample, WslMemoryReclaim.Gradual);
        var removed = WslMemoryReclaim.Apply(set, null);
        Assert.Equal(Sample, removed);
        Assert.Equal("- \n- [experimental]\n- autoMemoryReclaim=gradual", WslMemoryReclaim.Preview(set, removed));
        Assert.Equal("", WslMemoryReclaim.Apply("[experimental]\r\nautoMemoryReclaim=gradual\r\n", null));
    }

    [Fact]
    public void RemovingKeepsASectionThatHasOtherContent()
    {
        var ini = "[experimental]\r\nautoMemoryReclaim=gradual\r\nsparseVhd=true\r\n\r\n[wsl2]\r\nmemory=8GB\r\n";
        Assert.Equal("[experimental]\r\nsparseVhd=true\r\n\r\n[wsl2]\r\nmemory=8GB\r\n", WslMemoryReclaim.Apply(ini, null));
        var commented = "[experimental]\nautoMemoryReclaim=gradual\n# my note\n";
        Assert.Equal("[experimental]\n# my note\n", WslMemoryReclaim.Apply(commented, null));
    }

    [Fact]
    public void RemovingAnEmptiedSectionInTheMiddleKeepsTheNeighbours()
    {
        var ini = "[boot]\nsystemd=true\n\n[experimental]\nautoMemoryReclaim=gradual\n\n[wsl2]\nmemory=8GB\n";
        Assert.Equal("[boot]\nsystemd=true\n\n[wsl2]\nmemory=8GB\n", WslMemoryReclaim.Apply(ini, null));
    }

    [Fact]
    public void RemovingWhenNotSetChangesNothing()
    {
        Assert.Same(Sample, WslMemoryReclaim.Apply(Sample, null));
    }

    [Theory]
    [InlineData("WSL version: 2.6.3.0\r\nKernel version: 6.6.87.2-1\r\nWSLg version: 1.0.71\r\n", "2.6.3")]
    [InlineData("WSL-version: 2.0.0.0\r\nKernelversion: 5.15.123.1-1\r\n", "2.0.0")]
    [InlineData("﻿\r\nWSL version: 1.2.5.0\r\n", "1.2.5")]
    [InlineData("W\0S\0L\0 \0v\0e\0r\0s\0i\0o\0n\0:\0 \02\0.\03\0.\02\06\0.\00\0\r\0\n\0", "2.3.26")]
    public void ParseWslVersionReadsTheFirstLineInAnyLanguageOrEncoding(string output, string expected)
    {
        Assert.Equal(Version.Parse(expected), WslMemoryReclaim.ParseWslVersion(output));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Copyright (c) Microsoft Corporation. All rights reserved.\r\n\r\nUsage: wsl.exe [Argument] [Options...] [CommandLine]\r\n")]
    public void ParseWslVersionGivesNullForInboxHelpOrNothing(string? output)
    {
        Assert.Null(WslMemoryReclaim.ParseWslVersion(output));
    }

    [Theory]
    [InlineData("1.2.5", false)]
    [InlineData("1.3.17", false)]
    [InlineData("2.0.0", true)]
    [InlineData("2.6.3", true)]
    public void VersionGateRequiresWsl2(string version, bool supported)
    {
        Assert.Equal(supported, WslMemoryReclaim.Supports(Version.Parse(version)));
        Assert.False(WslMemoryReclaim.Supports(null));
    }

    private static void WithFile(string? content, Action<string> test, byte[]? bytes = null)
    {
        var dir = Path.Combine(Path.GetTempPath(), "booster-reclaim-" + Guid.NewGuid().ToString("N"));
        var file = Path.Combine(dir, ".wslconfig");
        Directory.CreateDirectory(dir);
        if (bytes is not null) File.WriteAllBytes(file, bytes);
        else if (content is not null) File.WriteAllBytes(file, new UTF8Encoding(false).GetBytes(content));
        WslConfigFile.UseCustomPath(file);
        try { test(file); }
        finally
        {
            WslConfigFile.UseCustomPath(null);
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void WriteKeepsTheFileBytesAndItsByteOrderMark()
    {
        var bom = new byte[] { 0xEF, 0xBB, 0xBF };
        WithFile(null, file =>
        {
            WslMemoryReclaim.Write(WslMemoryReclaim.Gradual);
            var written = File.ReadAllBytes(file);
            Assert.Equal(bom, written[..3]);
            Assert.Equal(Sample + "\r\n[experimental]\r\nautoMemoryReclaim=gradual\r\n", Encoding.UTF8.GetString(written[3..]));
            WslMemoryReclaim.Write(null);
            Assert.Equal([.. bom, .. Encoding.UTF8.GetBytes(Sample)], File.ReadAllBytes(file));
        }, bytes: [.. bom, .. Encoding.UTF8.GetBytes(Sample)]);
    }

    [Fact]
    public void WriteCreatesAMissingFileAndRemovingFromAMissingFileWritesNothing()
    {
        WithFile(null, file =>
        {
            WslMemoryReclaim.Write(null);
            Assert.False(File.Exists(file));
            WslMemoryReclaim.Write(WslMemoryReclaim.Gradual);
            Assert.Equal("[experimental]\r\nautoMemoryReclaim=gradual\r\n", File.ReadAllText(file));
        });
    }

    [Fact]
    public void MemoryCapAndReclaimEditsLeaveEachOtherAlone()
    {
        WithFile("[wsl2]\r\nmemory=8GB\r\n\r\n[experimental]\r\nautoMemoryReclaim=gradual\r\n", file =>
        {
            WslConfigFile.Save(12);
            Assert.Equal(MemoryReclaimMode.Gradual, WslMemoryReclaim.Load().Mode);
            WslMemoryReclaim.Write(WslMemoryReclaim.DropCache);
            Assert.Equal(12, WslConfigFile.Load().MemoryGb);
            Assert.Equal("[wsl2]\r\nmemory=12GB\r\n\r\n[experimental]\r\nautoMemoryReclaim=dropCache\r\n", File.ReadAllText(file));
        });
    }

    [Fact]
    public void ViewModelShowsStateAndWritesOnlyAfterConfirmation()
    {
        WithFile(Sample, file =>
        {
            var vm = new OptimizationViewModel(new AppConfig()).Reclaim;
            vm.SetVersion(new Version(2, 6, 3));
            Assert.Equal(MemoryReclaimMode.NotSet, vm.Mode);
            Assert.True(vm.CanSetGradual);
            Assert.True(vm.CanSetDropCache);
            Assert.False(vm.CanRemove);
            Assert.Equal("+ \n+ [experimental]\n+ autoMemoryReclaim=gradual", vm.Preview(WslMemoryReclaim.Gradual));

            vm.Write(WslMemoryReclaim.Gradual, confirmed: false);
            Assert.Equal(Sample, File.ReadAllText(file));
            Assert.False(vm.HasStatusMessage);

            vm.Write(WslMemoryReclaim.Gradual, confirmed: true);
            Assert.Equal(MemoryReclaimMode.Gradual, vm.Mode);
            Assert.False(vm.CanSetGradual);
            Assert.True(vm.CanRemove);
            Assert.False(vm.StatusIsError);
            Assert.Contains("wsl --shutdown", Loc.Get("settings.optimization.reclaim.restart"));

            vm.Write(MemoryReclaimViewModel.ValueFor("dropCache"), confirmed: true);
            Assert.Equal(MemoryReclaimMode.DropCache, WslMemoryReclaim.Load().Mode);

            vm.Write(MemoryReclaimViewModel.ValueFor("remove"), confirmed: true);
            Assert.Equal(MemoryReclaimMode.NotSet, vm.Mode);
            Assert.Equal(Sample, File.ReadAllText(file));
        });
    }

    [Theory]
    [InlineData("1.2.5")]
    [InlineData(null)]
    public void ViewModelRefusesToWriteOnAnOldOrUnknownWslButStillRemoves(string? version)
    {
        var ini = "[experimental]\r\nautoMemoryReclaim=dropCache\r\n";
        WithFile(ini, file =>
        {
            var vm = new OptimizationViewModel(new AppConfig()).Reclaim;
            vm.SetVersion(version is null ? null : Version.Parse(version));
            Assert.True(vm.VersionBlocks);
            Assert.False(vm.CanSetGradual);
            Assert.False(vm.CanWrite(WslMemoryReclaim.Gradual));
            Assert.True(vm.StatusIsError);
            Assert.Contains("2.0.0", vm.StatusMessage);

            vm.Write(WslMemoryReclaim.Gradual, confirmed: true);
            Assert.Equal(ini, File.ReadAllText(file));

            vm.Write(null, confirmed: true);
            Assert.Equal("", File.ReadAllText(file));
        });
    }

    [Fact]
    public async Task VersionCheckUsesTheInjectedDetectorAndNoProcess()
    {
        var vm = new MemoryReclaimViewModel { DetectVersion = () => new Version(2, 0, 0) };
        Assert.False(vm.CanSetGradual); // not checked yet
        Assert.False(vm.VersionBlocks);
        await vm.CheckVersionAsync();
        Assert.True(vm.VersionSupported);
        Assert.Contains("2.0.0", vm.VersionText);
    }
}
