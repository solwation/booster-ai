using Avalonia;
using Avalonia.Controls;

namespace BoosterAI;

/// <summary>
/// Places each child centred on a point along a horizontal slider track, so a marker
/// follows the slider's width and Min/Max proportions (#77). <see cref="Inset"/> is the
/// distance from each edge to where the thumb's centre stops; children are clamped
/// inside the panel so a label at either end never spills past the slider.
/// </summary>
public sealed class SliderMarkerPanel : Panel
{
    public static readonly StyledProperty<double> FractionProperty =
        AvaloniaProperty.Register<SliderMarkerPanel, double>(nameof(Fraction));

    public static readonly StyledProperty<double> InsetProperty =
        AvaloniaProperty.Register<SliderMarkerPanel, double>(nameof(Inset), 10);

    static SliderMarkerPanel()
    {
        AffectsArrange<SliderMarkerPanel>(FractionProperty, InsetProperty);
    }

    /// <summary>Position along the track, 0 = minimum, 1 = maximum.</summary>
    public double Fraction
    {
        get => GetValue(FractionProperty);
        set => SetValue(FractionProperty, value);
    }

    public double Inset
    {
        get => GetValue(InsetProperty);
        set => SetValue(InsetProperty, value);
    }

    /// <summary>Left edge of a child of <paramref name="childWidth"/> centred on the track point.</summary>
    public static double ChildLeft(double panelWidth, double inset, double fraction, double childWidth)
    {
        var f = double.IsFinite(fraction) ? Math.Clamp(fraction, 0, 1) : 0;
        var track = Math.Max(0, panelWidth - 2 * inset);
        var centre = inset + f * track;
        var left = centre - childWidth / 2;
        return Math.Max(0, Math.Min(left, panelWidth - childWidth));
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = 0.0;
        var height = 0.0;
        foreach (var child in Children)
        {
            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            width = Math.Max(width, child.DesiredSize.Width);
            height = Math.Max(height, child.DesiredSize.Height);
        }
        return new Size(double.IsFinite(availableSize.Width) ? availableSize.Width : width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var child in Children)
        {
            var size = child.DesiredSize;
            var left = ChildLeft(finalSize.Width, Inset, Fraction, size.Width);
            child.Arrange(new Rect(left, (finalSize.Height - size.Height) / 2, size.Width, size.Height));
        }
        return finalSize;
    }
}
