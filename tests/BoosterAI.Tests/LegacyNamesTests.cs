using BoosterAI;
using Xunit;

namespace BoosterAI.Tests;

public class LegacyNamesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "boosterai-legacy-" + Guid.NewGuid().ToString("N"));

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }

    private string Write(string rel, string text)
    {
        var p = Path.Combine(_root, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, text);
        return p;
    }

    [Fact]
    public void OldConfigIsCopiedWhenNewIsMissing_AndOldIsKept()
    {
        var old = Write("BoosterStart/config.json", "old");
        var @new = Path.Combine(_root, "BoosterAI", "config.json");
        Assert.True(LegacyNames.MigrateConfig(old, @new));
        Assert.Equal("old", File.ReadAllText(@new));
        Assert.True(File.Exists(old));
    }

    [Fact]
    public void ExistingNewConfigIsUntouched()
    {
        var old = Write("BoosterStart/config.json", "old");
        var @new = Write("BoosterAI/config.json", "new");
        Assert.False(LegacyNames.MigrateConfig(old, @new));
        Assert.Equal("new", File.ReadAllText(@new));
    }

    [Fact]
    public void NothingHappensWithoutAnOldConfig()
    {
        var @new = Path.Combine(_root, "BoosterAI", "config.json");
        Assert.False(LegacyNames.MigrateConfig(Path.Combine(_root, "BoosterStart", "config.json"), @new));
        Assert.False(File.Exists(@new));
    }

    [Fact]
    public void OldLocalDataIsRemovedButForeignFilesStay()
    {
        var dir = Path.Combine(_root, "BoosterStart");
        Write("BoosterStart/sessions/20260101-000000/tab1.log", "x");
        Write("BoosterStart/boosterstart.png", "x");
        LegacyNames.RemoveOldLocalData(dir);
        Assert.False(Directory.Exists(dir));

        var keep = Write("BoosterStart/other.txt", "x");
        Write("BoosterStart/sessions/a/b.log", "x");
        LegacyNames.RemoveOldLocalData(dir);
        Assert.True(File.Exists(keep));
        Assert.False(Directory.Exists(Path.Combine(dir, "sessions")));
    }

    [Fact]
    public void OldFragmentFileAndFolderAreRemoved()
    {
        var dir = Path.Combine(_root, "Fragments", "BoosterStart");
        Write("Fragments/BoosterStart/boosterstart.json", "{}");
        LegacyNames.RemoveOldFragment(dir);
        Assert.False(Directory.Exists(dir));
        LegacyNames.RemoveOldFragment(dir); // missing folder is fine
    }
}
