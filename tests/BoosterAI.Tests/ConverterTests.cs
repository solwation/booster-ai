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

    [Fact]
    public void LlmNameShowsMissingTagWhenCliIsMissing()
    {
        Loc.Use("sv");
        Converters.IsCliMissing = id => id == "codex";
        try
        {
            var result = Converters.LlmName.Convert("codex", typeof(string), null, CultureInfo.InvariantCulture);
            Assert.Equal("codex (saknas)", result);

            var claude = Converters.LlmName.Convert("claude", typeof(string), null, CultureInfo.InvariantCulture);
            Assert.Equal("claude", claude);

            var shell = Converters.LlmName.Convert(Catalog.ShellOnly, typeof(string), null, CultureInfo.InvariantCulture);
            Assert.Equal("skal", shell);
        }
        finally
        {
            Converters.IsCliMissing = null;
        }
    }
}
