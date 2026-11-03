using Avalonia;
using Avalonia.Controls;

namespace BoosterAI;

/// <summary>A band along the same inset track as the slider, independent of its selected value.</summary>
public sealed class SliderRangePanel : Panel
{
    public static readonly StyledProperty<double> LowerFractionProperty =
        AvaloniaProperty.Register<SliderRangePanel, double>(nameof(LowerFraction));
    public static readonly StyledProperty<double> UpperFractionProperty =
        AvaloniaProperty.Register<SliderRangePanel, double>(nameof(UpperFraction));
    public static readonly StyledProperty<double> InsetProperty =
        AvaloniaProperty.Register<SliderRangePanel, double>(nameof(Inset), 10);

    static SliderRangePanel() => AffectsArrange<SliderRangePanel>(LowerFractionProperty, UpperFractionProperty, InsetProperty);

    public double LowerFraction { get => GetValue(LowerFractionProperty); set => SetValue(LowerFractionProperty, value); }
    public double UpperFraction { get => GetValue(UpperFractionProperty); set => SetValue(UpperFractionProperty, value); }
    public double Inset { get => GetValue(InsetProperty); set => SetValue(InsetProperty, value); }

    /// <summary>The true proportional interval; a collapsed interval still has a visible two-pixel boundary.</summary>
    public static (double left, double width) BandBounds(double width, double inset, double lower, double upper)
    {
        width = Math.Max(0, width);
        inset = Math.Clamp(inset, 0, width / 2);
        var lo = double.IsFinite(lower) ? Math.Clamp(lower, 0, 1) : 0;
        var hi = double.IsFinite(upper) ? Math.Clamp(upper, lo, 1) : lo;
        var track = width - 2 * inset;
        var left = inset + lo * track;
        var bandWidth = Math.Min(width, Math.Max(2, (hi - lo) * track));
        return (Math.Min(left, width - bandWidth), bandWidth);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children) child.Measure(availableSize);
        return new Size(double.IsFinite(availableSize.Width) ? availableSize.Width : 0,
            Children.Select(c => c.DesiredSize.Height).DefaultIfEmpty(0).Max());
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var (left, width) = BandBounds(finalSize.Width, Inset, LowerFraction, UpperFraction);
        foreach (var child in Children) child.Arrange(new Rect(left, 0, width, finalSize.Height));
        return finalSize;
    }
}
