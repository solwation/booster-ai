using System.Globalization;
using BoosterAI;
using Xunit;

namespace BoosterAI.Tests;

[Collection(ConfigFileCollection.Name)]
public class ConverterTests
{
    [Fact]
    public void LlmNameShowsMissingTagWhenCliIsMissing()
    {
        var previousLanguage = Loc.Language;
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
            Loc.Use(previousLanguage);
        }
    }
}
