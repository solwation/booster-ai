using Avalonia.Controls;

namespace BoosterAI;

/// <summary>The tab's status dot (#227): one drawing for the status column and the status popup.</summary>
public partial class StatusDotView : UserControl
{
    public StatusDotView() => InitializeComponent();
}

/// <summary>
/// How a working tab's halo looks at a CPU level (#286). Animated, the halo grows from the 9 px dot to
/// <see cref="PeakScale"/> and fades from <see cref="PeakOpacity"/> once every <see cref="Seconds"/> (Styles.axaml,
/// Ellipse.status-pulse.low/medium/high, keeps these values); with animations off it stays as a still halo of
/// <see cref="HaloSize"/> px at <see cref="HaloOpacity"/>, so the level still shows.
/// </summary>
public sealed record DotPulse(double Seconds, double PeakOpacity, double PeakScale, double HaloSize, double HaloOpacity)
{
    /// <summary>Calm and faint; the pace of an agent's turn waiting on its model.</summary>
    public static readonly DotPulse Low = new(2.6, 0.35, 1.3, 12, 0.25);
    /// <summary>About #222's pulse, also the default for a working tab without a CPU reading.</summary>
    public static readonly DotPulse Medium = new(1.6, 0.55, 1.5, 14, 0.4);
    /// <summary>Fast and strong: a build or test run on several cores.</summary>
    public static readonly DotPulse High = new(0.8, 0.85, 1.75, 16, 0.6);

    public static DotPulse For(CpuLevel level) => level switch { CpuLevel.Low => Low, CpuLevel.High => High, _ => Medium };
}
