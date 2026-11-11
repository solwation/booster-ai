using BoosterAI;
using Xunit;

/// <summary>The overview cards (#158): plain status and permission words, and Install only for a missing agent.</summary>
public class AgentCardTests
{
    private static AgentInfo Agent(bool installed, bool outdated = false, string cli = "claude", string aliasKind = "none") =>
        new(new CliStatus(cli, installed ? aliasKind : "none", installed ? "/usr/bin/" + cli : null) { Version = installed ? "1.0" : null, Outdated = outdated });

    [Theory]
    [InlineData(true, true, false, "agents.card.status.ready")]
    [InlineData(true, false, false, "agents.card.status.missing")]
    [InlineData(true, true, true, "agents.card.status.outdated")]
    [InlineData(false, true, false, "agents.status.unknown.short")]
    public void StatusKeyMapsInstalledMissingOutdated(bool known, bool installed, bool outdated, string key) =>
        Assert.Equal(key, AgentInfo.CardStatusKey(known, installed, outdated));

    [Theory]
    [InlineData(AgentMode.Standard, "agents.card.perm.ask")]
    [InlineData(AgentMode.AutoApprove, "agents.card.perm.auto")]
    [InlineData(AgentMode.Yolo, "agents.card.perm.auto")]
    [InlineData(AgentMode.Custom, "agents.card.perm.custom")]
    public void PermissionKeyMapsModes(AgentMode mode, string key) => Assert.Equal(key, AgentInfo.CardPermissionKey(true, mode));

    [Fact]
    public void MissingAgentHasNoPermissionClaim() => Assert.Equal("agents.card.perm.unknown", AgentInfo.CardPermissionKey(false, AgentMode.Standard));

    [Fact]
    public void SwedishWordsAreThePlainOnes()
    {
        var previous = Loc.Language;
        Loc.Use("sv");
        try
        {
            Assert.Equal("Redo", Agent(true).CardStatusText);
            Assert.Equal("Saknas", Agent(false).CardStatusText);
            Assert.Equal("Behöver uppdateras", Agent(true, outdated: true).CardStatusText);
            Assert.Equal("Behörigheter: Frågar före verktyg", Agent(true).CardPermissionText);
        }
        finally { Loc.Use(previous); }
    }

    [Fact]
    public void InstallIsOfferedOnlyWhenMissing()
    {
        Assert.True(Agent(false).ShowCardInstall);
        Assert.False(Agent(true).ShowCardInstall);
        Assert.False(Agent(true, outdated: true).ShowCardInstall);
        Assert.True(Agent(false, cli: "aider").Advice.IsSupported);
        Assert.False(Agent(false, cli: "aider").ShowCardAdvice);
        Assert.True(Agent(true, cli: "aider").ShowCardAdvice);
        Assert.False(Agent(true).ShowCardAdvice);
    }

    [Fact]
    public void CardCarriesNoAliasJargon()
    {
        var xaml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../../src/BoosterAI/SettingsWindow.axaml"));
        var start = xaml.IndexOf("Classes=\"logo-plate\"", StringComparison.Ordinal);
        var card = xaml[start..xaml.IndexOf("</Panel>", start, StringComparison.Ordinal)];
        Assert.DoesNotContain("CurrentModeText", card);
        Assert.DoesNotContain("SemiBold", card);
        Assert.DoesNotContain("VersionText", card);
    }
}
