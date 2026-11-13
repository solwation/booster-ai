using BoosterAI;
using Xunit;

namespace BoosterAI.Tests;

public class ScreenshotTests
{
    [Fact]
    public void ParseReadsTabOption()
    {
        var shot = Screenshot.Parse(["--screenshot", "out.png", "--window", "settings", "--tab", "appearance"]);
        Assert.NotNull(shot);
        Assert.Equal("appearance", shot.Tab);
        Assert.Equal("settings", shot.Window);
    }

    [Fact]
    public void ParseDefaultsTabToNull()
    {
        var shot = Screenshot.Parse(["--screenshot", "out.png", "--window", "settings"]);
        Assert.NotNull(shot);
        Assert.Null(shot.Tab);
    }

    [Fact]
    public void WindowNamesMatchDocsAndDocComment()
    {
        var names = string.Join("|", Screenshot.Windows.Keys);
        var root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "docs", "DEVELOPMENT.md"))) root = Path.GetDirectoryName(root);
        Assert.NotNull(root);
        Assert.Contains("[--window " + names + "]", File.ReadAllText(Path.Combine(root, "docs", "DEVELOPMENT.md")));
        Assert.Contains("[--window " + names + "]", File.ReadAllText(Path.Combine(root, "src", "BoosterAI", "Screenshot.cs")));
    }

    [Fact]
    public void WindowNamesListEveryNameOnce()
    {
        Assert.EndsWith(" or uninstall", Screenshot.WindowNames);
        foreach (var name in Screenshot.Windows.Keys) Assert.Contains(name, Screenshot.WindowNames);
    }
}
