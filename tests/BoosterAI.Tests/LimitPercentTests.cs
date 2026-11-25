using BoosterAI;
using Xunit;

namespace BoosterAI.Tests;

/// <summary>#217: one percent formatter for plan limits: whole numbers, one decimal below 3 % left, 100/0 only at 100 or more.</summary>
[Collection(ConfigFileCollection.Name)]
public class LimitPercentTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0, "0", "100")]
    [InlineData(50.5, "51", "49")]
    [InlineData(96.99, "97", "3")]
    [InlineData(97.0, "97", "3")]
    [InlineData(97.04, "97", "3")]
    [InlineData(97.4, "97,4", "2,6")]
    [InlineData(99.29, "99,3", "0,7")]
    [InlineData(99.5, "99,5", "0,5")]
    [InlineData(99.96, "99,9", "0,1")]
    [InlineData(99.999, "99,9", "0,1")]
    [InlineData(100, "100", "0")]
    [InlineData(130, "130", "0")]
    public void SwedishUsesDecimalCommaOnlyNearTheLimit(double used, string usedText, string leftText)
    {
        var previous = Loc.Language;
        try
        {
            Loc.Use("sv");
            Assert.Equal(usedText, LimitPercent.UsedText(used));
            Assert.Equal(leftText, LimitPercent.RemainingText(used));
        }
        finally { Loc.Use(previous); }
    }

    [Fact]
    public void EnglishUsesDecimalPointAndTheRowsAndChipAgree()
    {
        var previous = Loc.Language;
        try
        {
            Loc.Use("en");
            var row = LimitRow.For("5 h", new LimitWindow(99.29, Now.AddHours(1), 300), Now);
            Assert.Equal("0.7% left", row.RemainingText);
            Assert.Equal("99.3% used", row.UsedText);
            Assert.Equal(0.7, row.Remaining, 6);
            var chip = LimitIndicator.For(new AccountLimits(new LimitWindow(99.96, Now.AddHours(1), 300), null, null, Now), Now)!;
            Assert.Equal("99.9% used".Replace(" used", ""), chip.Caption.Split(' ')[0]); // caption starts with the level
            Assert.Equal("0.1% left", chip.RemainingCaption);
            Assert.Equal("3% left", LimitIndicator.For(new AccountLimits(new LimitWindow(97, Now.AddHours(1), 300), null, null, Now), Now)!.RemainingCaption);
            Assert.Contains("99.3% used", LimitText.Used(new LimitWindow(99.3, Now.AddHours(1), 300), Now));
            Assert.Contains("100% used", LimitText.Used(new LimitWindow(100, Now.AddHours(1), 300), Now));
        }
        finally { Loc.Use(previous); }
    }

    [Fact]
    public void QuotaWarningNearTheLimitShowsTheDecimalAndComparesTheDisplayedValue()
    {
        var previous = Loc.Language;
        try
        {
            Loc.Use("sv");
            var q = new QuotaWarnings();
            var l = new AccountLimits(new LimitWindow(99.29, Now.AddHours(1), 300), null, null, Now);
            var w = Assert.Single(q.Update("claude", "a", null, l, Now, 1)); // 0,7 % left is below 1
            Assert.Equal(0.7, w.RemainingPercent, 6);
            Assert.Contains("0,7 % kvar", w.Text(Now));
            // 97.0 shows 3 % left: not below 3, but 97.4 shows 2,6 % left: below 3.
            Assert.Empty(new QuotaWarnings().Update("claude", "a", null, new AccountLimits(new LimitWindow(97, Now.AddHours(1), 300), null, null, Now), Now, 3));
            Assert.Single(new QuotaWarnings().Update("claude", "a", null, new AccountLimits(new LimitWindow(97.4, Now.AddHours(1), 300), null, null, Now), Now, 3));
            // 99.96 never reads 100 % used / 0 % left.
            var near = Assert.Single(new QuotaWarnings().Update("claude", "a", null, new AccountLimits(new LimitWindow(99.96, Now.AddHours(1), 300), null, null, Now), Now, 5));
            Assert.Contains("0,1 % kvar", near.Text(Now));
        }
        finally { Loc.Use(previous); }
    }
}
