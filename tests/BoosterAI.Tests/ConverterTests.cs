using System.Globalization;
using BoosterAI;
using Xunit;

namespace BoosterAI.Tests;

[Collection(ConfigFileCollection.Name)]
public class ConverterTests
{
    /// <summary>A missing CLI keeps its plain name; a warning marker after it says "codex saknas" (#152).</summary>
    [Fact]
    public void MissingCliGetsAMarkerInsteadOfATextSuffix()
    {
        var previousLanguage = Loc.Language;
        Loc.Use("sv");
        Converters.IsCliMissing = id => id is "codex" or Catalog.ShellOnly;
        try
        {
            object? Convert(Avalonia.Data.Converters.IValueConverter c, string id) => c.Convert(id, typeof(object), null, CultureInfo.InvariantCulture);
            Assert.Equal("codex", Convert(Converters.LlmName, "codex"));
            Assert.Equal(true, Convert(Converters.LlmMissing, "codex"));
            Assert.Equal("codex saknas", Convert(Converters.LlmMissingName, "codex"));

            Assert.Equal("claude", Convert(Converters.LlmName, "claude"));
            Assert.Equal(false, Convert(Converters.LlmMissing, "claude"));

            // The plain shell is never "missing".
            Assert.Equal("skal", Convert(Converters.LlmName, Catalog.ShellOnly));
            Assert.Equal(false, Convert(Converters.LlmMissing, Catalog.ShellOnly));
        }
        finally
        {
            Converters.IsCliMissing = null;
            Loc.Use(previousLanguage);
        }
    }
}
