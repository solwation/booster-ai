using BoosterAI;
using Xunit;

namespace BoosterAI.Tests;

/// <summary>#222: the status dot's shape tells working, waiting and warning/error apart without colour alone.</summary>
[Collection(ConfigFileCollection.Name)]
public sealed class StatusDotTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bs-statusdot-" + Guid.NewGuid().ToString("N"));

    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public StatusDotTests(Xunit.Abstractions.ITestOutputHelper output)
    {
        _output = output;
        Directory.CreateDirectory(_root);
        ConfigStore.UseFile(Path.Combine(_root, "config.json"));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static TabViewModel Tab() =>
        new MainViewModel(new AppConfig { Tabs = [new TabConfig { Name = "Atlas", Directory = "/srv/atlas", Enabled = true }] }).Tabs[0];

    public static TheoryData<string, StatusDotKind> States => new()
    {
        { "none", StatusDotKind.Plain },
        { "working", StatusDotKind.Working },
        { "busy", StatusDotKind.Working },
        { "idle", StatusDotKind.Idle },
        { "exited-ok", StatusDotKind.Idle },
        { "watching", StatusDotKind.Plain },
        { "starting", StatusDotKind.Plain },
        { "exited-error", StatusDotKind.Warn },
        { "missing", StatusDotKind.Warn },
        { "closed", StatusDotKind.Warn },
        { "waiting-restart", StatusDotKind.Plain },
    };

    [Theory]
    [MemberData(nameof(States))]
    public void EachStateHasItsShape(string state, StatusDotKind expected)
    {
        var tab = Tab();
        switch (state)
        {
            case "working": tab.SetState(TabState.Running, null, activity: Activity.Working); break;
            case "busy": tab.SetState(TabState.Running, null, activity: Activity.Busy); break;
            case "idle": tab.SetState(TabState.Running, null, activity: Activity.Idle); break;
            case "exited-ok": tab.SetState(TabState.CliExited, 0); break;
            case "watching": tab.SetState(TabState.Running, null, activity: Activity.Watching, watchingSince: 1); break;
            case "starting": tab.SetState(TabState.Starting, null); break;
            case "exited-error": tab.SetState(TabState.CliExited, 1); break;
            case "missing": tab.SetState(TabState.CliMissing, null); break;
            case "closed": tab.SetState(TabState.Closed, null); break;
            case "waiting-restart": tab.PendingRestart = true; tab.SetState(TabState.Running, null, activity: Activity.Busy); break;
        }
        Assert.Equal(expected, tab.StatusDot);
        // Exactly one shape is drawn: the filled dot (plain or working), the ring or the diamond.
        Assert.Equal(1, new[] { tab.StatusDotFilled, tab.StatusDotRing, tab.StatusDotDiamond }.Count(b => b));
        Assert.Equal(expected == StatusDotKind.Working, tab.StatusPulse || tab.StatusHalo);
        Assert.False(tab.StatusPulse && tab.StatusHalo);
    }

    [Fact]
    public void TheDotFollowsTheStateAndRaisesItsShapes()
    {
        var tab = Tab();
        var raised = new List<string?>();
        tab.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        tab.SetState(TabState.Running, null, activity: Activity.Working);
        Assert.Equal(StatusDotKind.Working, tab.StatusDot);
        raised.Clear();
        tab.SetState(TabState.Running, null, activity: Activity.Idle);
        Assert.Equal(StatusDotKind.Idle, tab.StatusDot);
        Assert.Contains(nameof(TabViewModel.StatusDot), raised);
        Assert.Contains(nameof(TabViewModel.StatusPulse), raised);
        Assert.Contains(nameof(TabViewModel.StatusDotRing), raised);
    }

    /// <summary>
    /// The working halo's style really animates (it once named a property without an animator), and each CPU level (#286) has its
    /// own pace and strength: the axaml style's period, peak opacity and peak scale equal DotPulse's for that level.
    /// </summary>
    [Theory]
    [InlineData(CpuLevel.Low)]
    [InlineData(CpuLevel.Medium)]
    [InlineData(CpuLevel.High)]
    public void EachLevelPulsesAtItsOwnPaceAndStrength(CpuLevel level)
    {
        var expected = DotPulse.For(level);
        var cls = level.ToString().ToLowerInvariant();
        var (samples, style) = UiSession.Run(() =>
        {
            var pulse = new Avalonia.Controls.Shapes.Ellipse { Width = 9, Height = 9, RenderTransform = new Avalonia.Media.ScaleTransform() };
            pulse.Classes.Add("status-pulse");
            pulse.Classes.Add(cls);
            var window = new Avalonia.Controls.Window { Content = pulse, Width = 50, Height = 50 };
            window.Show();
            try
            {
                // The headless animation clock follows real time: watch the halo for a moment.
                var seen = new List<(double Opacity, double Scale)>();
                var clock = System.Diagnostics.Stopwatch.StartNew();
                while (clock.ElapsedMilliseconds < 300)
                {
                    Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    seen.Add((pulse.Opacity, ((Avalonia.Media.ScaleTransform)pulse.RenderTransform!).ScaleX));
                    Thread.Sleep(10);
                }
                return (seen, PulseStyle(Avalonia.Application.Current!.Styles, $"Ellipse.status-pulse.{cls}"));
            }
            finally { window.Close(); }
        });
        Assert.True(samples.Select(s => Math.Round(s.Opacity, 3)).Distinct().Count() > 1, string.Join(", ", samples));
        Assert.True(samples.Select(s => Math.Round(s.Scale, 3)).Distinct().Count() > 1, string.Join(", ", samples));
        Assert.All(samples, s => Assert.InRange(s.Scale, 1, expected.PeakScale + 0.001));
        Assert.All(samples, s => Assert.InRange(s.Opacity, 0, expected.PeakOpacity + 0.001));
        Assert.InRange(samples[0].Opacity, expected.PeakOpacity - 0.1, expected.PeakOpacity + 0.001);
        Assert.NotNull(style);
        Assert.Equal(expected.Seconds, style.Value.Seconds, 3);
        Assert.Equal(expected.PeakOpacity, style.Value.StartOpacity, 3);
        Assert.Equal(expected.PeakScale, style.Value.EndScale, 3);
    }

    /// <summary>The duration, first key frame's opacity and last key frame's scale of the named pulse style.</summary>
    private static (double Seconds, double StartOpacity, double EndScale)? PulseStyle(Avalonia.Styling.Styles styles, string selector)
    {
        foreach (var item in styles)
        {
            var found = item switch
            {
                Avalonia.Styling.Styles nested => PulseStyle(nested, selector),
                Avalonia.Markup.Xaml.Styling.StyleInclude { Loaded: Avalonia.Styling.Styles loaded } => PulseStyle(loaded, selector),
                Avalonia.Styling.Style style when style.Selector?.ToString() == selector
                    && style.Animations.OfType<Avalonia.Animation.Animation>().FirstOrDefault() is { } a => (a.Duration.TotalSeconds,
                        Value(a.Children[0], Avalonia.Visual.OpacityProperty), Value(a.Children[^1], Avalonia.Media.ScaleTransform.ScaleXProperty)),
                _ => ((double, double, double)?)null,
            };
            if (found is not null) return found;
        }
        return null;

        static double Value(Avalonia.Animation.KeyFrame frame, Avalonia.AvaloniaProperty property) =>
            Convert.ToDouble(frame.Setters.OfType<Avalonia.Styling.Setter>().Single(s => s.Property == property).Value, System.Globalization.CultureInfo.InvariantCulture);
    }

    [Fact]
    public void TheLevelsAreClearlyApart()
    {
        DotPulse[] levels = [DotPulse.Low, DotPulse.Medium, DotPulse.High];
        // Each step up is faster, stronger and larger, animated and still, by a margin one sees at a glance.
        foreach (var (lower, higher) in levels.Zip(levels.Skip(1)))
        {
            Assert.True(higher.Seconds <= lower.Seconds * 0.65, $"{lower} {higher}");
            Assert.True(higher.PeakOpacity >= lower.PeakOpacity + 0.15, $"{lower} {higher}");
            Assert.True(higher.PeakScale >= lower.PeakScale + 0.2, $"{lower} {higher}");
            Assert.True(higher.HaloSize >= lower.HaloSize + 2, $"{lower} {higher}");
            Assert.True(higher.HaloOpacity >= lower.HaloOpacity + 0.1, $"{lower} {higher}");
        }
        Assert.Equal(DotPulse.Medium, DotPulse.For(CpuLevel.Medium));
    }

    /// <summary>#286: the working dot follows the CPU level, Medium without a reading; the other shapes and the accessible name stay.</summary>
    [Fact]
    public void TheWorkingDotShowsTheCpuLevel()
    {
        var tab = Tab();
        tab.SetState(TabState.Running, null, activity: Activity.Busy);
        // A turn without a CPU reading: the default level, #222's pulse, and no load line in the popup.
        Assert.Equal(CpuLevel.Medium, tab.StatusLevel);
        Assert.True(tab.StatusLevelMedium);
        Assert.Equal(DotPulse.Medium, tab.StatusDotPulse);
        Assert.False(tab.HasPeekLevel);
        var name = tab.StatusActionName;
        var raised = new List<string?>();
        tab.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        tab.SetCpuLevel(CpuLevel.High, 2.34);
        Assert.Equal(CpuLevel.High, tab.StatusLevel);
        Assert.Equal([false, false, true], new[] { tab.StatusLevelLow, tab.StatusLevelMedium, tab.StatusLevelHigh });
        Assert.Equal(DotPulse.High, tab.StatusDotPulse);
        Assert.Contains(nameof(TabViewModel.StatusLevelHigh), raised);
        Assert.Contains(nameof(TabViewModel.StatusDotPulse), raised);
        Assert.Equal(StatusDotKind.Working, tab.StatusDot);
        Assert.Equal(name, tab.StatusActionName);
        Assert.True(tab.HasPeekLevel);
        Assert.Contains("234", tab.PeekLevel);
        tab.SetCpuLevel(CpuLevel.Low, 0.12);
        Assert.Equal(DotPulse.Low, tab.StatusDotPulse);
        // Waiting keeps its ring and has no load line, whatever the level.
        tab.SetState(TabState.Running, null, activity: Activity.Idle);
        Assert.Equal(StatusDotKind.Idle, tab.StatusDot);
        Assert.False(tab.StatusPulse || tab.StatusHalo);
        Assert.False(tab.HasPeekLevel);
        // No reading (scan failed, tab stopped): back to the default.
        tab.SetCpuLevel(null, null);
        Assert.Equal(CpuLevel.Medium, tab.StatusLevel);
    }

    /// <summary>#227: the status column and the status popup draw the dot from the one shared control, so they never drift apart.</summary>
    [Theory]
    [InlineData("MainWindow.axaml")]
    [InlineData("StatusPeekView.axaml")]
    public void TheColumnAndThePopupShareTheDot(string file)
    {
        var src = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "BoosterAI");
        var xaml = File.ReadAllText(Path.Combine(src, file));
        Assert.Contains("<local:StatusDotView", xaml);
        Assert.DoesNotContain("{Binding StatusBrush}", xaml);
    }
}
