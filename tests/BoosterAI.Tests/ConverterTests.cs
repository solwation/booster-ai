using System.Globalization;
using Avalonia;
using Avalonia.Media;
using BoosterAI;
using Xunit;

namespace BoosterAI.Tests;

public class ConverterTests
{
    [Fact]
    public void ErrorBrushIsRedForErrors() =>
        Assert.IsAssignableFrom<IBrush>(Converters.ErrorBrush.Convert(true, typeof(IBrush), null, CultureInfo.InvariantCulture));

    // null would become a local Foreground that hides the text; it must be unset so the muted style applies (#26).
    [Fact]
    public void ErrorBrushIsUnsetNotNullForNormalStatus() =>
        Assert.Same(AvaloniaProperty.UnsetValue, Converters.ErrorBrush.Convert(false, typeof(IBrush), null, CultureInfo.InvariantCulture));
}
