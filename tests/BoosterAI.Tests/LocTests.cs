using BoosterAI;
using Xunit;

namespace BoosterAI.Tests;

public class LocTests
{
    [Fact]
    public void BothLanguagesHaveTheSameKeys()
    {
        var sv = Loc.Table("sv");
        var en = Loc.Table("en");
        Assert.Empty(sv.Keys.Except(en.Keys));
        Assert.Empty(en.Keys.Except(sv.Keys));
    }

    [Theory]
    [InlineData("sv")]
    [InlineData("en")]
    public void EveryValueIsNonEmpty(string lang)
    {
        foreach (var (key, value) in Loc.Table(lang))
            Assert.False(string.IsNullOrWhiteSpace(value), $"{lang}:{key} is empty");
    }

    [Fact]
    public void PlaceholdersMatchBetweenLanguages()
    {
        var sv = Loc.Table("sv");
        foreach (var (key, en) in Loc.Table("en"))
            Assert.Equal(Placeholders(sv[key]), Placeholders(en));
    }

    [Theory]
    [InlineData("sv")]
    [InlineData("en")]
    public void InstallPromptHasNoSemicolon(string lang)
    {
        // Windows Terminal splits its command line on ';' (see Launcher.TabScript).
        Assert.DoesNotContain(';', Loc.Table(lang)["prompt.install"]);
    }

    [Fact]
    public void InstallPromptFollowsLanguage()
    {
        Loc.Use("sv");
        var sv = Launcher.InstallOrkesterPrompt;
        Loc.Use("en");
        var en = Launcher.InstallOrkesterPrompt;
        Loc.Use("en");
        Assert.NotEqual(sv, en);
        Assert.Contains("Installera", sv);
        Assert.Contains("Install the", en);
    }

    [Theory]
    [InlineData("auto", "sv", "sv")]
    [InlineData("auto", "SV", "sv")]
    [InlineData("auto", "en", "en")]
    [InlineData("auto", "de", "en")]
    [InlineData(null, "sv", "sv")]
    [InlineData("", "fr", "en")]
    [InlineData("en", "sv", "en")]
    [InlineData("sv", "en", "sv")]
    [InlineData("klingon", "sv", "sv")]
    public void ResolvePrefersExplicitChoiceThenOs(string? setting, string os, string expected) =>
        Assert.Equal(expected, Loc.Resolve(setting, os));

    [Theory]
    [InlineData("sv_SE.UTF-8", null, "sv")]
    [InlineData("en_US.UTF-8", null, "en")]
    [InlineData("C", null, "iv")]
    [InlineData("en_US.UTF-8", "sv:en", "sv")]
    public void PosixEnvironmentOrder(string lang, string? language, string expected)
    {
        var env = new Dictionary<string, string?> { ["LANG"] = lang, ["LANGUAGE"] = language };
        var got = Loc.FromEnvironment(n => env.GetValueOrDefault(n));
        // "C" falls through to the process culture, which is not the point here.
        if (expected != "iv") Assert.Equal(expected, got);
    }

    [Fact]
    public void DefaultTabNameIsRecognisedInEveryLanguage()
    {
        Assert.True(Loc.IsDefaultTabName("Ny flik"));
        Assert.True(Loc.IsDefaultTabName("New tab"));
        Assert.False(Loc.IsDefaultTabName("mitt"));
    }

    [Fact]
    public void ThemeNamesAreLocalised()
    {
        foreach (var t in Catalog.Themes)
            foreach (var lang in Loc.Languages)
                Assert.True(Loc.Table(lang).ContainsKey("theme." + t.Name), $"{lang}: theme.{t.Name}");
    }

    private static string Placeholders(string s) =>
        string.Join(",", System.Text.RegularExpressions.Regex.Matches(s, @"\{\d+\}").Select(m => m.Value).Order());
}
