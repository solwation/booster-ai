using Avalonia;
using Avalonia.Headless;
using Avalonia.Media;
using BoosterAI;
using Xunit;

namespace BoosterAI.Tests;

/// <summary>Starting the Avalonia platform in this process must not overlap other tests (the Catalog brushes need a settled one).</summary>
[CollectionDefinition("Avalonia platform", DisableParallelization = true)]
public sealed class AvaloniaPlatformCollection;

/// <summary>The agent CLIs print glyphs IBM Plex Mono lacks (⎿ ⏺ ✻ ● ─); the MonoFont chain must resolve each (#154).</summary>
[Collection("Avalonia platform")]
public class MonoFontTests
{
    private static readonly object Gate = new();

    private static FontFamily MonoFont()
    {
        lock (Gate)
        {
            if (Application.Current is null)
                AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            Assert.True(Application.Current!.TryGetResource("MonoFont", null, out var value));
            return Assert.IsType<FontFamily>(value);
        }
    }

    [Theory]
    [InlineData(0x23BF, "⎿")]
    [InlineData(0x23FA, "⏺")]
    [InlineData(0x273B, "✻")]
    [InlineData(0x25CF, "●")]
    [InlineData(0x2500, "─")]
    [InlineData(0x2193, "↓")]
    public void MonoFontChainResolvesAGlyph(int codePoint, string glyph)
    {
        var chain = MonoFont().ToString().Split(',', StringSplitOptions.TrimEntries);
        Assert.True(chain.Length >= 3, "MonoFont is a fallback chain");
        var found = chain.Any(entry =>
            FontManager.Current.TryGetGlyphTypeface(new Typeface(new FontFamily(entry)), out var face)
            && face.TryGetGlyph((uint)codePoint, out var id) && id != 0);
        Assert.True(found, $"No font in the MonoFont chain has {glyph} (U+{codePoint:X4})");
    }

    [Theory]
    [InlineData(0x23BF)]
    [InlineData(0x23FA)]
    public void BundledSymbolsFontCarriesTheGlyphsNoSystemFontHas(int codePoint)
    {
        MonoFont();
        Assert.True(FontManager.Current.TryGetGlyphTypeface(new Typeface(new FontFamily("avares://BoosterAI/Assets/Fonts#BoosterAI Symbols")), out var face));
        Assert.True(face.TryGetGlyph((uint)codePoint, out var id) && id != 0);
    }
}
