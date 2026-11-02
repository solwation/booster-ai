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
}
