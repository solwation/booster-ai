using BoosterAI;
using Xunit;

/// <summary>Choosing a mode in the list asks first, writes only on yes and reverts on cancel (#187).</summary>
public class AgentModeSelectionTests
{
    private static CliStatus Claude => new("claude", "file", "/sample/claude") { Help = "--permission-mode --dangerously-skip-permissions", Version = "2.1.0" };
    private static AgentModeChoice Yolo(AgentInfo a) => a.ModeChoices.Single(c => c.Mode == AgentMode.Yolo);

    private sealed class Home : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("booster-mode-select-").FullName;
        public string Bashrc => Path.Combine(Root, ".bashrc");
        public MainViewModel ViewModel() => new(new AppConfig()) { PermissionConfigPath = Bashrc };
        public void Dispose() => Directory.Delete(Root, true);
    }

    [Fact]
    public async Task ConfirmedSelectionWrites()
    {
        using var home = new Home();
        var vm = home.ViewModel();
        var agent = new AgentInfo(Claude);
        agent.SelectedMode = Yolo(agent);
        var asked = 0;
        Assert.True(await vm.ChooseAgentModeAsync(agent, _ => { asked++; return Task.FromResult(true); }));
        Assert.Equal(1, asked);
        Assert.Contains("alias claude", File.ReadAllText(home.Bashrc));
        Assert.False(agent.ModeChangePending);
    }

    [Fact]
    public async Task CancelledSelectionWritesNothingAndRevertsTheList()
    {
        using var home = new Home();
        var vm = home.ViewModel();
        var agent = new AgentInfo(Claude);
        var effective = agent.SelectedMode;
        agent.SelectedMode = Yolo(agent);
        var asked = 0;
        // The revert changes the selection again; that must not ask a second time.
        agent.PropertyChanged += async (_, e) =>
        {
            if (e.PropertyName == nameof(AgentInfo.SelectedMode)) await vm.ChooseAgentModeAsync(agent, _ => { asked++; return Task.FromResult(false); });
        };
        Assert.False(await vm.ChooseAgentModeAsync(agent, _ => { asked++; return Task.FromResult(false); }));
        Assert.Equal(1, asked);
        Assert.Equal(effective, agent.SelectedMode);
        Assert.False(File.Exists(home.Bashrc));
    }

    [Fact]
    public async Task SelectionEqualToTheCurrentModeAndRefreshesOpenNoDialog()
    {
        using var home = new Home();
        var vm = home.ViewModel();
        var agent = new AgentInfo(Claude);
        var asked = 0;
        Task<bool> Ask(AgentInfo _) { asked++; return Task.FromResult(true); }
        // Initial binding: the list shows the current mode.
        Assert.False(await vm.ChooseAgentModeAsync(agent, Ask));
        // A background refresh resets the facts and selection programmatically.
        agent.RefreshDetection(Claude with { Version = "2.1.1" }, true);
        Assert.False(await vm.ChooseAgentModeAsync(agent, Ask));
        Assert.Equal(0, asked);
        Assert.False(File.Exists(home.Bashrc));
    }

    [Fact]
    public async Task NoSecondDialogWhileOneIsOpen()
    {
        using var home = new Home();
        var vm = home.ViewModel();
        var agent = new AgentInfo(Claude);
        agent.SelectedMode = Yolo(agent);
        var gate = new TaskCompletionSource<bool>();
        var first = vm.ChooseAgentModeAsync(agent, _ => gate.Task);
        Assert.True(agent.ModeChangePending);
        Assert.False(await vm.ChooseAgentModeAsync(agent, _ => throw new InvalidOperationException("second dialog")));
        gate.SetResult(false);
        Assert.False(await first);
    }
}
