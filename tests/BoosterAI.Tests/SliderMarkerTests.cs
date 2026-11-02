using BoosterAI;
using Xunit;

namespace BoosterAI.Tests;

/// <summary>Recommendation marker on the WSL memory slider (#77).</summary>
public class SliderMarkerTests
{
    [Theory]
    [InlineData(22, 4, 30, 18.0 / 26)]
    [InlineData(4, 4, 30, 0.0)]
    [InlineData(30, 4, 30, 1.0)]
    [InlineData(40, 4, 30, 1.0)]
    [InlineData(1, 4, 30, 0.0)]
    [InlineData(2, 2, 2, 0.5)]
    public void SliderFractionPlacesValueBetweenBounds(int value, int min, int max, double expected)
    {
        Assert.Equal(expected, WslOptimization.SliderFraction(value, min, max), 6);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    [InlineData(64)]
    [InlineData(128)]
    public void RecommendationAlwaysLiesWithinSliderBounds(int totalGb)
    {
        var rec = WslOptimization.RecommendMemoryGb(totalGb);
        var (min, max) = WslOptimization.GetSliderBounds(totalGb);
        Assert.InRange(rec, min, max);
    }

    [Theory]
    // 400 px panel, 10 px thumb inset, 2 px tick: the tick centre follows the track.
    [InlineData(400, 10, 0.0, 2, 9)]
    [InlineData(400, 10, 0.5, 2, 199)]
    [InlineData(400, 10, 1.0, 2, 389)]
    // An 80 px label is kept inside the panel at both ends.
    [InlineData(400, 10, 0.0, 80, 0)]
    [InlineData(400, 10, 1.0, 80, 320)]
    [InlineData(400, 10, 0.5, 80, 160)]
    // Out-of-range or NaN fractions are clamped.
    [InlineData(400, 10, 2.0, 2, 389)]
    [InlineData(400, 10, double.NaN, 2, 9)]
    public void ChildLeftCentresOnTrackAndClampsToPanel(double width, double inset, double fraction,
        double childWidth, double expectedLeft)
    {
        Assert.Equal(expectedLeft, SliderMarkerPanel.ChildLeft(width, inset, fraction, childWidth), 6);
    }

    [Fact]
    public void ScaledWidthMovesMarkerProportionally()
    {
        var narrow = SliderMarkerPanel.ChildLeft(300, 10, 0.25, 0) - 10;
        var wide = SliderMarkerPanel.ChildLeft(600, 10, 0.25, 0) - 10;
        Assert.Equal(narrow * 580 / 280, wide, 6);
    }

    [Theory]
    [InlineData("sv", "Rekommenderat")]
    [InlineData("en", "Recommended")]
    public void MarkerLabelIsLocalized(string language, string expected)
    {
        Assert.Equal(expected, Loc.Table(language)["settings.optimization.recommendedMarker"]);
    }
}
