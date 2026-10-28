using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;

namespace BoosterAI;

/// <summary>
/// The tab's last lines in the status popup: monospace, newest at the bottom, in a box of fixed size
/// (TabSession.OutputLines × TabViewModel.OutputColumns) so a live update never resizes or moves the popup.
/// </summary>
public sealed class TabOutputView : Border
{
    private const double FontSizeValue = 11.5;
    private const double LineHeightValue = 15;

    /// <summary>The box width, which the popup's other texts wrap to as well.</summary>
    public static double PopupWidth => Math.Ceiling(TabViewModel.OutputColumns * FontSizeValue * 0.6) + 20;

    public TabOutputView()
    {
        Width = PopupWidth;
        Height = TabSession.OutputLines * LineHeightValue + 16;
        Padding = new Thickness(10, 8);
        CornerRadius = new CornerRadius(6);
        ClipToBounds = true;
        Bind(BackgroundProperty, this.GetResourceObservable("OutputBackgroundBrush"));
        var text = new TextBlock
        {
            FontSize = FontSizeValue,
            LineHeight = LineHeightValue,
            TextWrapping = TextWrapping.NoWrap,
            VerticalAlignment = VerticalAlignment.Bottom,
            [!TextBlock.TextProperty] = new Binding(nameof(TabViewModel.OutputText)),
        };
        text.Bind(TextBlock.FontFamilyProperty, text.GetResourceObservable("MonoFont"));
        text.Bind(TextBlock.ForegroundProperty, text.GetResourceObservable("TextBrush"));
        Child = text;
    }
}
