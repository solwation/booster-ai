using BoosterAI;
using Xunit;

public class AgentNavigationTests
{
    [Fact]
    public void NavigationPreservesEditorsDraftsAndHasExactlyOneSelection()
    {
        var vm = new MainViewModel(new AppConfig { Tabs = [] });
        vm.ShowSampleAgents();
        var agents = vm.Setup.Agents.ToArray();
        var claude = agents.Single(a => a.Cli == "claude");
        var editor = claude.Compact!;
        claude.SelectedMode = claude.ModeChoices.Single(c => c.Mode == AgentMode.Yolo);
        editor.Mode = editor.Modes.Single(m => m.Id == "custom");
        editor.Custom = "654321";
        foreach (var agent in agents.Concat(agents.Reverse()))
        {
            vm.Setup.SelectedAgent = agent;
            Assert.Same(agent, Assert.Single(agents, a => a.IsSelected));
            Assert.Same(agent, vm.Setup.SelectedAgent);
            Assert.Same(agent, vm.Setup.Agents.Single(a => a.Cli == agent.Cli));
        }
        Assert.Same(editor, claude.Compact);
        Assert.Equal(AgentMode.Yolo, claude.SelectedMode.Mode);
        Assert.Equal("custom", editor.Mode.Id);
        Assert.Equal("654321", editor.Custom);
    }

    [Fact]
    public void RefreshKeepsAgentSectionAndOverviewPreservesDrafts()
    {
        var setup = new SetupViewModel();
        setup.SelectedAgent = setup.Agents.Single(a => a.Cli == "interpreter");
        setup.SelectedAgent!.Section = 2;
        setup.SelectedAgent = new AgentInfo(new("unknown", "none", null));
        Assert.Equal("interpreter", setup.SelectedAgent!.Cli);
        setup.Apply(SetupStatus.Empty, "Ubuntu");
        Assert.Same(setup.SelectedAgent, setup.Agents.Single(a => a.Cli == "interpreter"));
        Assert.Single(setup.Agents, a => a.IsSelected);
        Assert.Equal(2, setup.SelectedAgent!.Section);
        setup.SelectedAgent = null;
        Assert.True(setup.IsAgentOverview);
        Assert.DoesNotContain(setup.Agents, a => a.IsSelected);
        setup.Apply(SetupStatus.Empty, "Ubuntu");
        Assert.True(setup.IsAgentOverview);
    }
    [Fact]
    public void BackgroundRefreshKeepsAgentIdentityAliasAndContextDrafts()
    {
        var vm = new MainViewModel(new AppConfig { Tabs = [] });
        vm.ShowSampleAgents();
        var agents = vm.Setup.Agents;
        var claude = agents.Single(a => a.Cli == "claude");
        var codex = agents.Single(a => a.Cli == "codex");
        var editor = claude.Compact!;
        vm.Setup.SelectedAgent = claude;
        claude.Section = 1;
        claude.SelectedMode = claude.ModeChoices.Single(c => c.Mode == AgentMode.Yolo);
        editor.Mode = editor.Modes.Single(m => m.Id == "custom");
        editor.Custom = "654321";
        editor.Context = "765432";
        editor.IsExpanded = true;
        var refreshed = vm.Setup.Status with
        {
            Clis = vm.Setup.Status.Clis.Select(c => c.Cli == "claude" ? c with { Version = "2.2.0" }
                : c.Cli == "codex" ? c with { Kind = "alias", AliasDefinition = "alias codex='codex --ask-for-approval never --sandbox workspace-write'" } : c).ToArray(),
        };
        vm.Setup.Apply(refreshed, "Ubuntu");
        vm.Setup.RowsChanged(["claude"]);
        Assert.Same(agents, vm.Setup.Agents);
        Assert.Same(claude, vm.Setup.SelectedAgent);
        Assert.Same(claude, Assert.Single(agents, a => a.IsSelected));
        Assert.Equal(1, claude.Section);
        Assert.Equal("2.2.0", claude.VersionText);
        Assert.Equal(AgentMode.Yolo, claude.SelectedMode.Mode);
        Assert.Same(editor, claude.Compact);
        Assert.Equal("custom", editor.Mode.Id);
        Assert.Equal("654321", editor.Custom);
        Assert.Equal("765432", editor.Context);
        Assert.True(editor.IsExpanded);
        Assert.Equal(codex.CurrentMode, codex.SelectedMode.Mode);
        vm.Setup.SelectedAgent = null;
        vm.Setup.Apply(refreshed, "Ubuntu");
        Assert.True(vm.Setup.IsAgentOverview);
        Assert.DoesNotContain(agents, a => a.IsSelected);
    }
}
