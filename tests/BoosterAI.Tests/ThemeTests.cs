using Avalonia.Styling;
using BoosterAI;
using Xunit;

namespace BoosterAI.Tests;

public class ThemeTests
{
    [Theory]
    [InlineData("light", "Light")]
    [InlineData("dark", "Dark")]
    [InlineData("auto", "Default")]
    [InlineData("", "Default")]
    [InlineData(null, "Default")]
    [InlineData("neon", "Default")]
    public void ConfigValueMapsToThemeVariant(string? value, string expected) =>
        Assert.Equal(expected, App.ThemeFor(value).Key.ToString());

    [Fact]
    public void MissingAppThemeInOldConfigIsAuto() =>
        Assert.Equal("auto", new AppConfig().AppTheme);

    [Theory]
    [InlineData("dark", "light", "Dark")]
    [InlineData("light", "dark", "Light")]
    [InlineData("light", "auto", "Light")]
    [InlineData("bogus", "dark", "Dark")]
    public void CommandLineFlagBeatsConfig(string flag, string config, string expected) =>
        Assert.Equal(expected, App.ResolveTheme(["x", "--theme", flag], config).Key.ToString());

    [Fact]
    public void WithoutFlagConfigDecides()
    {
        Assert.Equal("Dark", App.ResolveTheme(["x"], "dark").Key.ToString());
        Assert.Equal("Default", App.ResolveTheme(["x", "--theme"], "auto").Key.ToString());
    }
}
