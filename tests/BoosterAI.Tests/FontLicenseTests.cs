using Avalonia.Platform;
using Xunit;

namespace BoosterAI.Tests;

/// <summary>The font licence texts must travel inside every binary, including the standalone setup exe (#161).</summary>
[Collection("Avalonia platform")]
public class FontLicenseTests
{
    [Theory]
    [InlineData("OFL.txt")]
    [InlineData("LICENSE-BoosterAISymbols.txt")]
    public void FontLicenseIsEmbedded(string name)
    {
        var uri = new Uri($"avares://BoosterAI/Assets/Fonts/{name}");
        var exists = UiSession.Run(() => AssetLoader.Exists(uri));
        Assert.True(exists, $"{name} is not embedded in the BoosterAI assembly");
    }
}
