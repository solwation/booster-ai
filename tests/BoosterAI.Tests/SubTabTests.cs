using BoosterAI;
using Xunit;

[Collection(ConfigFileCollection.Name)]
public sealed class SubTabTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bs-subtabs-" + Guid.NewGuid().ToString("N"));

    public SubTabTests()
    {
        Directory.CreateDirectory(_root);
        ConfigStore.UseFile(Path.Combine(_root, "config.json"));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void LegacyProjectsKeepAllChoicesAndAcquireStableIdsWithoutExtraTabs()
    {
        var config = ConfigStore.Parse("""{"Tabs":[{"Name":"Alpha","Directory":"~/alpha","Enabled":false,"Llm":"codex","Model":"custom-model","Orkester":true,"NonInteractive":true}]}""")!;
        var vm = new MainViewModel(config);
        var root = Assert.Single(vm.Tabs);
        Assert.True(root.IsProject);
        Assert.False(root.Enabled);
        Assert.Equal("~/alpha", root.Directory);
        Assert.Equal("codex", root.Llm);
        Assert.Equal("custom-model", root.Model);
        Assert.True(root.Orkester);
        Assert.True(root.NonInteractive);
        Assert.NotEmpty(root.Id);
        vm.Save();
        var restored = new MainViewModel(ConfigStore.Load());
        Assert.Equal(root.Id, Assert.Single(restored.Tabs).Id);
        Assert.Empty(Assert.Single(ConfigStore.Load().Tabs).SubTabs);
    }

    [Fact]
    public void SubtabsShareOneFolderButKeepIndependentChoicesAndSavedExpansion()
    {
        var vm = new MainViewModel(new AppConfig { Tabs = [new() { Name = "Alpha", Directory = "~/alpha", Llm = "claude", Model = "main-model", Orkester = true }] });
        var parent = vm.Tabs[0];
        var one = vm.AddSubTab(parent);
        var two = vm.AddSubTab(parent);
        one.Llm = "codex";
        one.Model = "other-model";
        one.Orkester = false;
        two.NonInteractive = true;
        two.Enabled = false;
        one.Directory = "~/ignored";
        Assert.Equal("~/alpha", one.Directory);
        parent.Directory = "~/moved";
        Assert.Equal("~/moved", one.Directory);
        Assert.Equal("~/moved", two.Directory);
        vm.Save();
        var saved = Assert.Single(ConfigStore.Load().Tabs);
        Assert.Equal("~/moved", saved.Directory);
        Assert.All(saved.SubTabs, t => Assert.Equal("", t.Directory));
        Assert.True(saved.Expanded);
        var restored = new MainViewModel(ConfigStore.Load());
        Assert.Equal(3, restored.Tabs.Count);
        Assert.Equal(3, restored.VisibleTabs.Count());
        Assert.Equal([parent.Id, one.Id, two.Id], restored.Tabs.Select(t => t.Id));
        Assert.Equal("claude", restored.Tabs[0].Llm);
        Assert.Equal("main-model", restored.Tabs[0].Model);
        Assert.True(restored.Tabs[0].Orkester);
        Assert.Equal("codex", restored.Tabs[1].Llm);
        Assert.Equal("other-model", restored.Tabs[1].Model);
        Assert.False(restored.Tabs[1].Orkester);
        Assert.True(restored.Tabs[2].NonInteractive);
        Assert.False(restored.Tabs[2].Enabled);
        Assert.All(restored.Tabs, t => Assert.Equal("~/moved", t.Directory));
        restored.Tabs[0].Expanded = false;
        Assert.Single(restored.VisibleTabs);
        Assert.Equal(2, restored.EnabledCount);
    }

    [Fact]
    public void MovingProjectMovesItsChildrenAndChildMovesStayWithinTheirGroup()
    {
        var vm = new MainViewModel(new AppConfig { Tabs = [new() { Name = "A" }, new() { Name = "B" }, new() { Name = "C" }] });
        var a = vm.Tabs[0];
        var one = vm.AddSubTab(a);
        var two = vm.AddSubTab(a);
        vm.MoveTab(0, 4);
        Assert.Equal(["B", "C", "A", one.Name, two.Name], vm.Tabs.Select(t => t.Name));
        vm.MoveTab(4, 3);
        Assert.Equal(["B", "C", "A", two.Name, one.Name], vm.Tabs.Select(t => t.Name));
        vm.MoveTab(4, 0);
        Assert.Same(one, vm.Tabs[4]);
        var restored = new MainViewModel(ConfigStore.Load());
        Assert.Equal(vm.Tabs.Select(t => t.Id), restored.Tabs.Select(t => t.Id));
        Assert.Equal(["B", "C", "A"], restored.Projects.Select(t => t.Name));
    }

    [Fact]
    public void DragDropBeforeAndAfterMainRowsMovesWholeGroupsInBothDirections()
    {
        var vm = new MainViewModel(new AppConfig { Tabs = [new() { Name = "A", SubTabs = [new() { Name = "a1" }] }, new() { Name = "B", SubTabs = [new() { Name = "b1" }] }, new() { Name = "C" }] });
        var a = vm.Tabs[0];
        var b = vm.Tabs[2];
        vm.MoveTabRelative(b, a, after: true);
        Assert.Equal(["A", "a1", "B", "b1", "C"], vm.Tabs.Select(t => t.Name));
        vm.MoveTabRelative(b, a, after: false);
        Assert.Equal(["B", "b1", "A", "a1", "C"], vm.Tabs.Select(t => t.Name));
        vm.MoveTabRelative(b, a, after: true);
        Assert.Equal(["A", "a1", "B", "b1", "C"], vm.Tabs.Select(t => t.Name));
        var a2 = vm.AddSubTab(a);
        vm.MoveTabRelative(a2, vm.Tabs[1], after: false);
        Assert.Same(a2, vm.Tabs[1]);
        vm.MoveTabRelative(a2, vm.Tabs[2], after: true);
        Assert.Same(a2, vm.Tabs[2]);
        vm.MoveTabRelative(a2, b, after: true);
        Assert.Same(a2, vm.Tabs[2]);
    }

    [Fact]
    public void StartUsesGroupOrderAndIndependentEnabledChoicesWithUniqueTracks()
    {
        var name = "group-start-" + Guid.NewGuid().ToString("N");
        var vm = new MainViewModel(new AppConfig { Notifications = false, Tabs = [new() { Name = name, Directory = "~/alpha" }, new() { Name = "next" }] });
        var first = vm.AddSubTab(vm.Tabs[0]);
        var second = vm.AddSubTab(vm.Tabs[0]);
        first.Llm = "codex";
        first.Model = "custom";
        second.Llm = Catalog.ShellOnly;
        Assert.True(vm.Launch());
        var request = Assert.Single(TestTerminal.For(name));
        Assert.Equal([name, first.Name, second.Name, "next"], request.Tabs.Select(t => t.Name));
        Assert.Equal(["~/alpha", "~/alpha", "~/alpha", "~"], request.Tabs.Select(t => t.Directory));
        Assert.Equal("codex", request.Tabs[1].Llm);
        Assert.Equal("custom", request.Tabs[1].Model);
        Assert.Equal(4, request.Tracks.Select(t => t!.Id).Distinct().Count());
        first.SetState(TabState.Running, null);
        second.SetState(TabState.CliExited, 0);
        Assert.True(first.IsAlive);
        Assert.False(second.IsAlive);
        Assert.True(second.CanRestart);
        Assert.False(first.CanRemoveSubTab);
    }

    [Fact]
    public void LauncherAlsoFlattensDiskConfigAndUsesParentsFolderForChildren()
    {
        var cfg = new AppConfig { Tabs = [new() { Name = "main", Directory = "~/a", Enabled = false, SubTabs = [new() { Name = "child", Directory = "~/wrong" }] }, new() { Name = "next" }] };
        Launcher.TerminalRequest? result = null;
        Assert.Equal("", Launcher.Launch(cfg, r => result = r));
        Assert.Equal(["child", "next"], result!.Tabs.Select(t => t.Name));
        Assert.Equal("~/a", result.Tabs[0].Directory);
    }

    [Fact]
    public void RecoveryUsesIdentityEvenAfterRenameAndCannotStealAnotherSameNameTab()
    {
        var vm = new MainViewModel(new AppConfig { Tabs = [new() { Name = "same", Directory = "~/a", SubTabs = [new() { Name = "same" }, new() { Name = "same" }] }] });
        var configs = vm.Tabs.Select(t => t.ToConfig(true)).ToList();
        vm.Tabs[1].Name = "renamed";
        Assert.Same(vm.Tabs[1], vm.Match(configs[1]));
        vm.Tabs[1].LaunchedAs = configs[1];
        Assert.Null(vm.Match(configs[1]));
        Assert.Same(vm.Tabs[2], vm.Match(configs[2]));
        Assert.Null(vm.Match(new() { Id = "deleted-tab", Name = "same", Directory = "~/a" }));
        Assert.Same(vm.Tabs[0], vm.Match(new() { Name = "same", Directory = "~/a" }));
        vm.Tabs[0].LaunchedAs = configs[0];
        Assert.Null(vm.Match(new() { Name = "same", Directory = "~/a" }));
    }

    [Fact]
    public void SharedCliHistoryCannotResumeASiblingsLatestConversation()
    {
        var vm = new MainViewModel(new AppConfig { Tabs = [new() { Llm = "claude", Directory = "~/a", SubTabs = [new() { Llm = "claude" }] }] });
        var main = vm.Tabs[0];
        var child = vm.Tabs[1];
        main.LaunchedAs = main.ToConfig(true);
        main.SetState(TabState.Running, null);
        main.Model = "another-model";
        Assert.True(main.IsChanged);
        Assert.False(main.RestartResumes);
        main.SetState(TabState.Closed, null);
        Assert.False(main.CanContinueClosed);
        child.Llm = "codex";
        Assert.True(main.CanContinueClosed);
        main.SetState(TabState.Running, null);
        Assert.True(main.RestartResumes);
        child.LaunchedAs = new() { Llm = "claude" };
        Assert.False(main.RestartResumes);
    }

    [Fact]
    public void DuplicateIdsAreRepairedAndRemovingAClosedChildKeepsItsSiblings()
    {
        var vm = new MainViewModel(new AppConfig { Tabs = [new() { Id = "same", SubTabs = [new() { Id = "same" }, new() { Id = "same" }] }] });
        Assert.Equal(3, vm.Tabs.Select(t => t.Id).Distinct().Count());
        var first = vm.Tabs[1];
        var last = vm.Tabs[2];
        Assert.True(vm.RemoveSubTab(first));
        Assert.Equal(2, vm.Tabs.Count);
        Assert.Same(last, vm.Tabs[1]);
        Assert.Single(Assert.Single(ConfigStore.Load().Tabs).SubTabs);
        Assert.False(vm.RemoveSubTab(vm.Tabs[0]));
    }
}
