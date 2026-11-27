using System.Text.RegularExpressions;
using Activity = BoosterAI.Activity;
using BoosterAI;
using Xunit;

namespace BoosterAI.Tests;

/// <summary>#272: unticking a running row closes its tab through Close all's save-and-close path, asking first when it is active.</summary>
[Collection(ConfigFileCollection.Name)]
public class UntickCloseTests
{
    private static MainViewModel Vm(string llm = "claude") => new(new AppConfig
    {
        Tabs =
        [
            new() { Name = "Atlas", Directory = "/srv/atlas", Llm = llm, Enabled = true, SubTabs = [new() { Name = "Atlas-api", Llm = "codex", Enabled = true }] },
            new() { Name = "Boreal", Directory = "/srv/boreal", Llm = llm, Enabled = true },
        ],
    });

    private static void Run(TabViewModel tab, Activity activity, TabState state = TabState.Running)
    {
        tab.LaunchedAs = tab.ToConfig(forLaunch: true);
        tab.SetState(state, null, activity: activity);
    }

    private static void CancelAll(MainViewModel vm)
    {
        foreach (var op in vm.TabCloses.ToList()) op.Cancel();
    }

    [Theory]
    [InlineData(Activity.Idle)]
    [InlineData(Activity.Watching)]
    [InlineData(Activity.Unknown)]
    public void AnIdleRunningTabClosesWithoutAsking(Activity activity)
    {
        var vm = Vm();
        var tab = vm.Tabs.Single(t => t.Name == "Boreal");
        Run(tab, activity);
        try
        {
            Assert.Equal(UntickOutcome.Closing, tab.RequestUntick());
            Assert.False(tab.Enabled);
            var op = Assert.Single(vm.TabCloses);
            Assert.True(op.Single);
            Assert.True(op.Started);
            Assert.False(op.Forced);
            Assert.Same(tab, Assert.Single(op.Tabs).Tab);
            Assert.True(tab.IsClosing);
        }
        finally { CancelAll(vm); }
    }

    [Theory]
    [InlineData(TabState.Running, Activity.Working)]
    [InlineData(TabState.Running, Activity.Busy)]
    [InlineData(TabState.Starting, Activity.Unknown)]
    public void AnActiveRunningTabAsksFirstAndKeepsItsTick(TabState state, Activity activity)
    {
        var vm = Vm();
        var tab = vm.Tabs.Single(t => t.Name == "Boreal");
        Run(tab, activity, state);
        Assert.Equal(UntickOutcome.Confirm, tab.RequestUntick());
        Assert.True(tab.Enabled);
        Assert.Empty(vm.TabCloses);
        Assert.False(tab.IsClosing);
    }

    [Fact]
    public void AnAgentReportingWorkIsActiveEvenWhenTheCliLooksIdle()
    {
        var vm = Vm();
        var tab = vm.Tabs.Single(t => t.Name == "Boreal");
        Run(tab, Activity.Idle);
        tab.SetChannel(new TabChannelState(new AgentStatus(AgentState.Working, "Running the tests", 1), [], []));
        Assert.Equal(UntickOutcome.Confirm, tab.RequestUntick());
        Assert.True(tab.Enabled);
        tab.SetChannel(new TabChannelState(new AgentStatus(AgentState.Done, "Done", 2), [], []));
        Assert.False(tab.IsActiveForClose);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConfirmingClosesThroughTheSaveAndClosePath(bool force)
    {
        var vm = Vm();
        var tab = vm.Tabs.Single(t => t.Name == "Boreal");
        Run(tab, Activity.Working);
        try
        {
            Assert.Equal(UntickOutcome.Confirm, tab.RequestUntick());
            tab.ConfirmUntick(force);
            Assert.False(tab.Enabled);
            var op = Assert.Single(vm.TabCloses);
            Assert.True(op.Started);
            Assert.Equal(force, op.Forced);
            Assert.Same(tab, Assert.Single(op.Tabs).Tab);
        }
        finally { CancelAll(vm); }
    }

    [Fact]
    public void CancellingRestoresTheTickAndLeavesTheTabRunning()
    {
        var vm = Vm();
        var tab = vm.Tabs.Single(t => t.Name == "Boreal");
        Run(tab, Activity.Busy);
        Assert.Equal(UntickOutcome.Confirm, tab.RequestUntick());
        var raised = new List<string?>();
        tab.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        tab.CancelUntick();
        // The one-way bound box reads Enabled again; nothing else changed, so nothing is saved.
        Assert.Equal([nameof(TabViewModel.Enabled)], raised);
        Assert.True(tab.Enabled);
        Assert.True(tab.IsRunning);
        Assert.False(tab.IsClosing);
        Assert.Empty(vm.TabCloses);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(TabState.Closed)]
    [InlineData(TabState.CliExited)]
    public void ANonRunningTabOnlyLeavesStart(TabState? state)
    {
        var vm = Vm();
        var tab = vm.Tabs.Single(t => t.Name == "Boreal");
        if (state is { } s) Run(tab, Activity.Unknown, s);
        Assert.Equal(UntickOutcome.Unticked, tab.RequestUntick());
        Assert.False(tab.Enabled);
        Assert.Empty(vm.TabCloses);
        Assert.False(tab.IsClosing);
    }

    [Fact]
    public void ProgrammaticEnabledChangesCloseNothing()
    {
        var vm = Vm();
        var tab = vm.Tabs.Single(t => t.Name == "Boreal");
        Run(tab, Activity.Idle);
        tab.Enabled = false;
        Assert.Empty(vm.TabCloses);
        Assert.True(tab.IsRunning);
    }

    [Fact]
    public void SubtabsCloseAloneAndCloseAllSkipsATabAlreadyClosing()
    {
        var vm = Vm();
        var project = vm.Tabs.Single(t => t.Name == "Atlas");
        var sub = vm.Tabs.Single(t => t.Name == "Atlas-api");
        var other = vm.Tabs.Single(t => t.Name == "Boreal");
        Run(project, Activity.Idle);
        Run(sub, Activity.Idle);
        Run(other, Activity.Idle);
        foreach (var t in new[] { project, sub, other }) t.Track = new TabTrack("lib.sh", "/tmp", Guid.NewGuid().ToString("N"));
        CloseAllOperation? all = null;
        try
        {
            Assert.Equal(UntickOutcome.Closing, sub.RequestUntick());
            Assert.Same(sub, Assert.Single(Assert.Single(vm.TabCloses).Tabs).Tab);
            Assert.True(project.Enabled);
            Assert.False(project.IsClosing);
            all = vm.PrepareCloseAll();
            Assert.Equal(["Atlas", "Boreal"], all.Tabs.Select(t => t.Name));
        }
        finally { all?.Cancel(); CancelAll(vm); }
    }

    [Fact]
    public void TheQuestionSaysWhatCloses()
    {
        var vm = Vm();
        var project = vm.Tabs.Single(t => t.Name == "Atlas");
        var sub = vm.Tabs.Single(t => t.Name == "Atlas-api");
        Run(project, Activity.Working);
        Run(sub, Activity.Idle);
        var q = CloseTabQuestion.For(project, vm.Tabs);
        Assert.Equal(Loc.Format("closeTab.heading", "Atlas"), q.Heading);
        Assert.Equal(Loc.Get("closeTab.working"), q.Body);
        Assert.StartsWith(Loc.Format("closeTab.subtabs.one", "Atlas", "Atlas-api"), q.Scope);
        Assert.Contains(Loc.Get("closeTab.force"), q.Scope);
        Assert.True(q.CanSave);
        Assert.Equal(Loc.Get("close.start"), q.Confirm);

        var forSub = CloseTabQuestion.For(sub, vm.Tabs);
        Assert.StartsWith(Loc.Format("closeTab.subtab", "Atlas-api", "Atlas"), forSub.Scope);

        sub.SetState(TabState.Closed, null);
        Assert.Equal(Loc.Get("closeTab.force"), CloseTabQuestion.For(project, vm.Tabs).Scope);
    }

    [Fact]
    public void AShellIsClosedWithoutSaveOrForceChoice()
    {
        var vm = Vm(Catalog.ShellOnly);
        var tab = vm.Tabs.Single(t => t.Name == "Boreal");
        Run(tab, Activity.Busy);
        var q = CloseTabQuestion.For(tab, vm.Tabs);
        Assert.Equal(Loc.Get("closeTab.shell"), q.Body);
        Assert.False(q.CanSave);
        Assert.False(q.HasScope);
        Assert.Equal(Loc.Get("closeTab.close"), q.Confirm);
    }

    [Fact]
    public void TheTooltipOfARunningRowSaysUntickingClosesIt()
    {
        var vm = Vm();
        var tab = vm.Tabs.Single(t => t.Name == "Boreal");
        Assert.Equal(Loc.Format("tab.open.tip", "Boreal"), tab.OpenTip);
        Run(tab, Activity.Idle);
        Assert.Equal(Loc.Format("tab.open.alive", "Boreal"), tab.OpenTip);
        foreach (var language in new[] { "sv", "en" })
            Assert.DoesNotContain("startas inte om", Loc.Table(language)["tab.open.alive"]);
    }

    [Fact]
    public async Task ASingleCloseRefusesAConversationAnotherOpenTabSharesAndAsksForAttention()
    {
        if (!OperatingSystem.IsLinux()) return;
        var root = Directory.CreateTempSubdirectory("booster-untick-").FullName;
        var vm = Vm();
        // A stand-in shell: nothing may signal it, since the close must stop before any process control.
        using var shell = System.Diagnostics.Process.Start("sleep", "60")!;
        try
        {
            var project = vm.Tabs.Single(t => t.Name == "Atlas");
            var sub = vm.Tabs.Single(t => t.Name == "Atlas-api");
            sub.Llm = "claude";
            var session = new TabSession(root, DateTime.UtcNow);
            // Both rows run claude in the project folder: --continue could pick the other tab's conversation.
            var start = TabStatus.ParseProcStartTime(File.ReadAllText($"/proc/{shell.Id}/stat"))!.Value;
            foreach (var row in new[] { project, sub })
            {
                row.Session = session;
                row.Track = session.NewTrack()!;
                row.LaunchedAt = DateTime.UtcNow;
                File.WriteAllText(Path.Combine(session.Directory, row.Track.Id + ".log"), $"1 start pid={shell.Id} starttime={start}\n2 cli-start\n");
                Run(row, Activity.Idle);
            }
            CloseAllOperation? attention = null;
            vm.TabCloseNeedsAttention += op => attention = op;
            Assert.Equal(UntickOutcome.Closing, project.RequestUntick());
            var op = Assert.Single(vm.TabCloses);
            Assert.True(SpinWait.SpinUntil(() => attention is not null, 10000));
            await op.TickAsync();
            Assert.Equal(Loc.Get("close.ambiguous"), op.Tabs[0].Text);
            Assert.False(op.Tabs[0].Requested);
            Assert.Same(op, attention);
            Assert.False(File.Exists(Path.Combine(session.Directory, project.Track!.Id + ".next")));
            Assert.False(shell.HasExited);
        }
        finally
        {
            CancelAll(vm);
            shell.Kill(entireProcessTree: true);
            shell.WaitForExit(5000);
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void TheCheckboxIsBoundOneWayAndRoutedThroughTheHandler()
    {
        var xaml = File.ReadAllText(Path.Combine(RepoRoot(), "src", "BoosterAI", "MainWindow.axaml"));
        var box = Regex.Match(xaml, @"<CheckBox Grid.Column=""2""[^>]*/>", RegexOptions.Singleline).Value;
        Assert.Contains(@"IsChecked=""{Binding Enabled, Mode=OneWay}""", box);
        Assert.Contains(@"Click=""OnEnabledClick""", box);
        Assert.Contains(@"AutomationProperties.Name=""{Binding OpenTip}""", box);
        var dialog = File.ReadAllText(Path.Combine(RepoRoot(), "src", "BoosterAI", "ConfirmCloseTabWindow.axaml"));
        Assert.Contains(@"Classes=""danger""", dialog);
        Assert.Contains(@"Classes=""primary""", dialog);
        Assert.Contains(@"IsCancel=""True""", dialog);
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "BoosterAI.slnx"))) return dir.FullName;
        throw new InvalidOperationException("Repository root not found.");
    }
}
