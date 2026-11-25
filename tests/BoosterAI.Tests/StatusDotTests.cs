using BoosterAI;
using Xunit;

namespace BoosterAI.Tests;

/// <summary>#222: the status dot's shape tells working, waiting and warning/error apart without colour alone.</summary>
[Collection(ConfigFileCollection.Name)]
public sealed class StatusDotTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bs-statusdot-" + Guid.NewGuid().ToString("N"));

    public StatusDotTests()
    {
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

    /// <summary>The working halo's style really animates (it once named a property without an animator): scale and opacity change.</summary>
    [Fact]
    public void TheWorkingHaloPulses()
    {
        var samples = UiSession.Run(() =>
        {
            var pulse = new Avalonia.Controls.Shapes.Ellipse { Width = 9, Height = 9, RenderTransform = new Avalonia.Media.ScaleTransform() };
            pulse.Classes.Add("status-pulse");
            var window = new Avalonia.Controls.Window { Content = pulse, Width = 50, Height = 50 };
            window.Show();
            try
            {
                var seen = new List<(double Opacity, double Scale)>();
                for (var i = 0; i < 6; i++)
                {
                    Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick(10);
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    seen.Add((Math.Round(pulse.Opacity, 3), Math.Round(((Avalonia.Media.ScaleTransform)pulse.RenderTransform!).ScaleX, 3)));
                }
                return seen;
            }
            finally { window.Close(); }
        });
        Assert.True(samples.Select(s => s.Opacity).Distinct().Count() > 1, string.Join(", ", samples));
        Assert.True(samples.Select(s => s.Scale).Distinct().Count() > 1, string.Join(", ", samples));
        Assert.All(samples, s => Assert.InRange(s.Scale, 1, 1.45));
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
