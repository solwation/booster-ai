using BoosterAI;
using Xunit;

public class EnvironmentSummaryTests
{
    [Fact]
    public void EnvironmentResultsDescribeInstallationWithoutAliasDetails()
    {
        var setup = new SetupViewModel();
        var statuses = new[]
        {
            new CliStatus("claude", "alias", "/sample/claude") { AliasDefinition = "alias claude='custom'" },
            new CliStatus("codex", "function", "/sample/codex") { FunctionDefinition = "codex () { custom; }" },
            new CliStatus("agy", "file", "/sample/agy"),
        };
        setup.Apply(new SetupStatus(WslState.Ok, ["Ubuntu"], statuses, true, false), "Ubuntu");
        foreach (var cli in statuses)
            Assert.Contains(Loc.Format("setup.summary.installed", cli.Cli), setup.Summary);
        Assert.Contains(Loc.Format("setup.summary.missing", "aider"), setup.Summary);
        Assert.DoesNotContain(Loc.Format("setup.summary.alias", "claude"), setup.Summary);
        Assert.DoesNotContain("custom", setup.Summary);
        Assert.Equal(statuses[0].AliasDefinition, setup.Agents.Single(a => a.Cli == "claude").CurrentAliasCommand);
        setup.IsChecking = true;
        Assert.Equal(Loc.Get("setup.summary.checking"), setup.Summary);
        setup.IsChecking = false;
        Assert.Contains(Loc.Format("setup.summary.installed", "claude"), setup.Summary);
    }

    [Fact]
    public void EnvironmentResultsDistinguishNotCheckedFailedAndMissingWsl()
    {
        var setup = new SetupViewModel();
        Assert.Equal(Loc.Get("setup.summary.none"), setup.Summary);
        setup.Apply(SetupStatus.Empty, "Ubuntu");
        Assert.Equal(Loc.Get("setup.summary.failed"), setup.Summary);
        setup.Apply(new SetupStatus(WslState.DistroMissing, ["Other"], [], false, false), "Ubuntu");
        Assert.Equal(setup.WslText, setup.Summary);
    }
}
