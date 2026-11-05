using BoosterAI;
using Xunit;

[Collection(ConfigFileCollection.Name)]
public sealed class ProjectRemovalTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bs-remove-project-" + Guid.NewGuid().ToString("N"));

    public ProjectRemovalTests()
    {
        Directory.CreateDirectory(_root);
        ConfigStore.UseFile(Path.Combine(_root, "config.json"));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static MainViewModel Group() => new(new AppConfig
    {
        Tabs = [new() { Name = "Atlas", Directory = "/sample/atlas", SubTabs = [new() { Name = "Review" }] }, new() { Name = "Harbor" }],
    });

    [Theory]
    [InlineData(false, TabState.Starting)]
    [InlineData(false, TabState.Running)]
    [InlineData(true, TabState.Starting)]
    [InlineData(true, TabState.Running)]
    [InlineData(false, TabState.CliExited)]
    [InlineData(true, TabState.CliExited)]
    [InlineData(true, TabState.CliMissing)]
    public async Task ActiveMainChildOrOpenIdleShellCannotBeRemoved(bool child, TabState state)
    {
        var vm = Group();
        var project = vm.Tabs[0];
        var active = vm.Tabs[child ? 1 : 0];
        active.Track = new TabTrack("/sample/tab-lib.sh", "/sample", "synthetic");
        active.SetState(state, state == TabState.CliExited ? 0 : null);
        var track = active.Track;
        var rows = vm.Tabs.ToArray();
        var confirmations = 0;

        Assert.False(project.CanRemoveProject);
        Assert.False(vm.RemoveProject(project, confirmed: true));
        Assert.False(await vm.RemoveProjectAsync(project, _ => { confirmations++; return Task.FromResult(true); }));

        Assert.Equal(0, confirmations);
        Assert.Equal(rows, vm.Tabs);
        Assert.Same(track, active.Track);
        Assert.True(active.IsAlive || active.HasOpenTerminal);
        Assert.False(File.Exists(ConfigStore.PathOnDisk));
    }

    [Fact]
    public async Task UnconfirmedAndCancelledRemovalPreservesConfiguration()
    {
        var vm = Group();
        var project = vm.Tabs[0];
        vm.Save();
        var before = File.ReadAllText(ConfigStore.PathOnDisk);
        Assert.True(project.CanRemoveProject);
        Assert.False(vm.RemoveProject(project));
        Assert.False(await vm.RemoveProjectAsync(project, request =>
        {
            Assert.Equal("Atlas", request.ProjectName);
            Assert.Equal(1, request.SubTabCount);
            Assert.Contains("Atlas", request.Heading);
            Assert.Contains("1", request.Body);
            return Task.FromResult(false);
        }));
        Assert.Equal(3, vm.Tabs.Count);
        Assert.Equal(before, File.ReadAllText(ConfigStore.PathOnDisk));
    }

    [Theory]
    [InlineData(TabState.Closed)]
    [InlineData(TabState.Lost)]
    public async Task ConfirmedClosedGroupIsRemovedAndSavedWithoutTouchingOtherGroups(TabState state)
    {
        var vm = Group();
        var project = vm.Tabs[0];
        var other = vm.Tabs[2];
        other.SetState(TabState.Running, null);
        foreach (var tab in vm.Tabs.Take(2))
        {
            tab.Track = new TabTrack("/sample/tab-lib.sh", "/sample", tab.Id);
            tab.SetState(state, null);
        }
        Assert.True(project.CanRemoveProject);
        Assert.True(await vm.RemoveProjectAsync(project, _ => Task.FromResult(true)));
        Assert.Same(other, Assert.Single(vm.Tabs));
        Assert.True(other.IsAlive);
        Assert.Equal("Harbor", Assert.Single(ConfigStore.Load().Tabs).Name);
    }

    [Fact]
    public async Task RuntimeBecomingActiveDuringConfirmationKeepsTheEntireGroup()
    {
        var vm = Group();
        var project = vm.Tabs[0];
        Assert.False(await vm.RemoveProjectAsync(project, _ =>
        {
            vm.Tabs[1].SetState(TabState.Running, null);
            return Task.FromResult(true);
        }));
        Assert.Equal(3, vm.Tabs.Count);
        Assert.False(project.CanRemoveProject);
        Assert.True(vm.StatusIsError);
        Assert.False(File.Exists(ConfigStore.PathOnDisk));
    }

    [Fact]
    public async Task NewlyAddedChildIsNotRemovedUnderAnOldConfirmation()
    {
        var vm = Group();
        var project = vm.Tabs[0];
        Assert.False(await vm.RemoveProjectAsync(project, request =>
        {
            Assert.Equal(1, request.SubTabCount);
            vm.AddSubTab(project);
            return Task.FromResult(true);
        }));
        Assert.Equal(4, vm.Tabs.Count);
        Assert.True(vm.StatusIsError);
    }

    [Fact]
    public async Task ChildAndRemovedProjectsNeverOpenAConfirmation()
    {
        var vm = Group();
        var removed = vm.Tabs[2];
        vm.Tabs.Remove(removed);
        foreach (var row in new[] { vm.Tabs[1], removed })
        {
            Assert.False(vm.CanRemoveProject(row));
            Assert.False(await vm.RemoveProjectAsync(row, _ => throw new InvalidOperationException("Must not confirm.")));
        }
        Assert.Equal(2, vm.Tabs.Count);
    }

    [Fact]
    public void ParentRemovalBindingTracksChildRuntimeAndOpenTerminalChanges()
    {
        var vm = Group();
        var parent = vm.Tabs[0];
        var child = vm.Tabs[1];
        var changes = new List<string?>();
        parent.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        Assert.True(parent.CanRemoveProject);

        child.SetState(TabState.CliExited, 0);
        Assert.True(parent.CanRemoveProject);
        child.Track = new TabTrack("/sample/tab-lib.sh", "/sample", "synthetic");
        Assert.False(parent.CanRemoveProject);
        child.SetState(TabState.Closed, 0);
        Assert.True(parent.CanRemoveProject);
        child.SetState(TabState.Running, null);
        Assert.False(parent.CanRemoveProject);
        Assert.True(changes.Count(name => name == nameof(TabViewModel.CanRemoveProject)) >= 4);
    }
}
