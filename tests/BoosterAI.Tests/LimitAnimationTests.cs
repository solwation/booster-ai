using Xunit;

namespace BoosterAI.Tests;

/// <summary>#234: when a changed plan-limit reading is animated, the counted value, the "(-5%)" text and its fade.</summary>
[Collection(ConfigFileCollection.Name)]
public class LimitAnimationTests : IDisposable
{
    private readonly string _language = Loc.Language;

    public LimitAnimationTests() => Loc.Use("sv");

    public void Dispose() => Loc.Use(_language);

    private static readonly DateTimeOffset T0 = new(2026, 11, 26, 9, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset At(double seconds) => T0.AddSeconds(seconds);

    [Theory]
    [InlineData(95.0, 90.0)]
    [InlineData(10.0, 60.0)] // an increase, e.g. after a reset
    public void AChangeBeginsATransition(double from, double to)
    {
        var t = LimitTransition.Begin(from, to, T0);
        Assert.NotNull(t);
        Assert.Equal(to - from, t.Diff);
    }

    [Theory]
    [InlineData(90.0, 90.0)]
    [InlineData(null, 90.0)]
    [InlineData(90.0, null)]
    [InlineData(null, null)]
    public void NothingToAnimateWhenUnchangedOrUnknown(double? from, double? to) => Assert.Null(LimitTransition.Begin(from, to, T0));

    [Fact]
    public void TheValueCountsMonotonicallyFromOldToNew()
    {
        var down = new LimitTransition(95, 90, T0);
        Assert.Equal(95, down.ValueAt(T0));
        var previous = 95.0;
        foreach (var s in new[] { 0.2, 0.5, 1.0, 1.5, 1.9 })
        {
            var v = down.ValueAt(At(s));
            Assert.InRange(v, 90, previous);
            previous = v;
        }
        Assert.Equal(90, down.ValueAt(At(LimitTransition.CountSeconds)));
        Assert.Equal(90, down.ValueAt(At(10)));
        var up = new LimitTransition(0, 50, T0);
        Assert.InRange(up.ValueAt(At(1)), 1, 49);
        Assert.Equal(50, up.ValueAt(At(5)));
    }

    [Fact]
    public void TheDiffTextIsHeldThenFadesAndEnds()
    {
        var t = new LimitTransition(95, 90, T0);
        Assert.Equal(1, t.DiffOpacityAt(At(0)));
        Assert.Equal(1, t.DiffOpacityAt(At(LimitTransition.HoldSeconds)));
        Assert.InRange(t.DiffOpacityAt(At(LimitTransition.HoldSeconds + 0.25)), 0.01, 0.99);
        Assert.Equal(0, t.DiffOpacityAt(At(LimitTransition.TotalSeconds)));
        Assert.True(t.IsActive(At(2.9)));
        Assert.False(t.IsActive(At(3.0)));
    }

    [Fact]
    public void TheDiffTextHasASignAndTheLanguagesSpacing()
    {
        Assert.Equal("(-5%)", LimitTransition.DiffText(-5));
        Assert.Equal("(+12%)", LimitTransition.DiffText(12));
        Assert.Equal("(-0,7%)", LimitTransition.DiffText(-0.7));
        Assert.Equal("", LimitTransition.DiffText(0.01));
        Loc.Use("en");
        Assert.Equal("(-5%)", LimitTransition.DiffText(-5));
    }

    [Fact]
    public void OnlyAnArmedChangeWithAPreviousLevelAnimates()
    {
        var a = new LimitAnimations();
        Assert.False(a.Observe("k", 95, T0)); // first reading: no previous level
        Assert.False(a.Observe("k", 90, At(1))); // not armed (the window never left)
        a.Arm(At(100));
        Assert.False(a.Observe("k", 90, At(101))); // armed but unchanged
        Assert.True(a.Observe("k", 85, At(102)));
        Assert.True(a.AnyActive(At(103)));
        Assert.Equal(85, a.Value("k", 85, At(110)));
        Assert.False(a.AnyActive(At(110)));
    }

    [Fact]
    public void TheArmedWindowEnds()
    {
        var a = new LimitAnimations();
        a.Observe("k", 50, T0);
        a.Arm(At(10));
        Assert.False(a.Observe("k", 40, At(10) + LimitAnimations.ArmedFor + TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void AnUnknownReadingKeepsTheLastKnownLevel()
    {
        var a = new LimitAnimations();
        a.Observe("k", 50, T0);
        a.Arm(At(1));
        Assert.False(a.Observe("k", null, At(2)));
        Assert.True(a.Observe("k", 40, At(3))); // compared with the 50 from before the unknown reading
        Assert.Equal("(-10%)", a.Diff("k", 40, At(3)).Text);
    }

    [Fact]
    public void ValueAndDiffFollowTheTargetOnly()
    {
        var a = new LimitAnimations();
        a.Observe("k", 95, T0);
        a.Arm(T0);
        Assert.True(a.Observe("k", 90, At(1)));
        Assert.True(a.IsCounting("k", At(1.5)));
        var v = a.Value("k", 90, At(1.5));
        Assert.InRange(v, 90.01, 94.99);
        Assert.Equal(1, a.Diff("k", 90, At(1.5)).Opacity);
        // A level that is not the animation's target is shown as it is, without a diff.
        Assert.Equal(70, a.Value("k", 70, At(1.5)));
        Assert.Equal(("", 0), a.Diff("k", 70, At(1.5)));
        Assert.Equal(90, a.Value("k", 90, At(1 + LimitTransition.TotalSeconds)));
        Assert.False(a.IsCounting("k", At(1 + LimitTransition.TotalSeconds)));
        Assert.Equal(("", 0), a.Diff("k", 90, At(1 + LimitTransition.TotalSeconds)));
    }

    [Fact]
    public void AChangeDuringAnAnimationContinuesFromTheShownValue()
    {
        var a = new LimitAnimations();
        a.Observe("k", 100, T0);
        a.Arm(T0);
        a.Observe("k", 80, At(0));
        var shown = a.Value("k", 80, At(0.5));
        a.Observe("k", 60, At(0.5));
        Assert.Equal(shown, a.Value("k", 60, At(0.5)), 6);
        Assert.Equal(60, a.Value("k", 60, At(5)));
    }

    [Fact]
    public void LevelsSurviveARestart()
    {
        var file = Path.Combine(Path.GetTempPath(), "boosterai-limit-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            new LimitAnimations(file).Observe("k", 95, T0);
            var next = new LimitAnimations(file);
            next.Arm(T0);
            Assert.True(next.Observe("k", 90, At(1))); // the first reading of the new run is compared with the saved level
            Assert.False(new LimitAnimations(Path.Combine(Path.GetTempPath(), "boosterai-missing-" + Guid.NewGuid().ToString("N") + ".json")).Observe("k", 90, T0));
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void ACorruptFileIsIgnored()
    {
        var file = Path.Combine(Path.GetTempPath(), "boosterai-limit-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(file, "not json");
            var a = new LimitAnimations(file);
            a.Arm(T0);
            Assert.False(a.Observe("k", 90, T0));
        }
        finally { File.Delete(file); }
    }
}
