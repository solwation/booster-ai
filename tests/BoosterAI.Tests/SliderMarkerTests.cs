using BoosterAI;
using Xunit;

namespace BoosterAI.Tests;

/// <summary>Recommendation markers on the WSL memory slider (#77, #80).</summary>
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
    [InlineData(400, 10, 0.25, 0.5, 105, 95)]
    [InlineData(400, 10, 0, 1, 10, 380)]
    [InlineData(400, 10, -1, 2, 10, 380)]
    [InlineData(400, 10, double.NaN, double.NaN, 10, 2)]
    [InlineData(0, 10, 0, 1, 0, 0)]
    public void RangeBandUsesTheSlidersInsetScale(double width, double inset, double lower, double upper,
        double expectedLeft, double expectedWidth)
    {
        var actual = SliderRangePanel.BandBounds(width, inset, lower, upper);
        Assert.Equal(expectedLeft, actual.left, 6);
        Assert.Equal(expectedWidth, actual.width, 6);
    }

    [Fact]
    public void RangeBoundariesFollowSliderResizeAndBothRemainInsideThePanel()
    {
        var narrow = SliderRangePanel.BandBounds(300, 10, 0.25, 0.5);
        var wide = SliderRangePanel.BandBounds(600, 10, 0.25, 0.5);
        Assert.Equal(narrow.width * 580 / 280, wide.width, 6);
        var collapsed = SliderRangePanel.BandBounds(5, 10, 1, 0);
        Assert.InRange(collapsed.left, 0, 5);
        Assert.InRange(collapsed.left + collapsed.width, 0, 5);
    }

    [Theory]
    [InlineData("sv", "Vägledande spann")]
    [InlineData("en", "Suggested range")]
    public void MarkerLabelIsLocalized(string language, string expected)
    {
        Assert.Equal(expected, Loc.Table(language)["settings.optimization.recommendedMarker"]);
    }
}
