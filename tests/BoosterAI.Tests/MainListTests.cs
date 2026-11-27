using System.Diagnostics;
using System.Text.Json;
using BoosterAI;
using Xunit;

namespace BoosterAI.Tests;

/// <summary>The main list (#152): the orchestra choice, its column, the status width and new tab colours.</summary>
[Collection(ConfigFileCollection.Name)]
public sealed class MainListTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bs-mainlist-" + Guid.NewGuid().ToString("N"));

    public MainListTests()
    {
        Directory.CreateDirectory(_root);
        ConfigStore.UseFile(Path.Combine(_root, "config.json"));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Theory]
    [InlineData(false, false, StartChoiceKind.None, false)]
    [InlineData(false, true, StartChoiceKind.None, false)]
    [InlineData(true, false, StartChoiceKind.Orkester, false)]
    [InlineData(true, true, StartChoiceKind.Orkester, true)]
    public void StartCommandReadsTheOldOrchestraSettings(bool orkester, bool nonInteractive, StartChoiceKind kind, bool once)
    {
        // #255: the old Off/On/Run once (#152) is the list's no command, /orkester and Run once.
        var vm = new MainViewModel(new AppConfig { Tabs = [new() { Name = "A", Orkester = orkester, NonInteractive = nonInteractive }] });
        var tab = vm.Tabs[0];
        tab.HasOrkester = true;
        Assert.Equal(kind, tab.SelectedStartChoice!.Kind);
        Assert.Equal(once, tab.RunOnce);
        var saved = tab.ToConfig(forLaunch: false);
        Assert.Equal((orkester, once, ""), (saved.Orkester, saved.NonInteractive, saved.Command));
    }

    [Fact]
    public void ChoosingEntriesWritesTheSettingsAndSurvivesTheRoundTrip()
    {
        var vm = new MainViewModel(new AppConfig { Tabs = [new() { Name = "A", Orkester = true, NonInteractive = true }] });
        var tab = vm.Tabs[0];
        tab.HasOrkester = true;
        tab.SelectedStartChoice = new(StartChoiceKind.Command, "/deploy", "/deploy");
        tab.CommandArgs = "staging  ";
        var saved = tab.ToConfig(forLaunch: false);
        // Run once now belongs to any command: it stays with the new one.
        Assert.Equal((false, "/deploy", "staging", true), (saved.Orkester, saved.Command, saved.CommandArgs.Trim(), saved.NonInteractive));
        var reloaded = new MainViewModel(new AppConfig { Tabs = [saved] }).Tabs[0];
        Assert.Equal(StartChoiceKind.Missing, reloaded.SelectedStartChoice!.Kind); // kept visible until a discovery finds it
        Assert.Equal("/deploy", reloaded.SelectedStartChoice.Command);
        Assert.True(reloaded.RunOnce);

        tab.SelectedStartChoice = tab.StartChoices.First(c => c.Kind == StartChoiceKind.Orkester);
        Assert.Equal((true, ""), (tab.ToConfig(false).Orkester, tab.ToConfig(false).Command));
        tab.SelectedStartChoice = tab.StartChoices[0];
        saved = tab.ToConfig(false);
        // Without a command Run once means nothing and is not saved.
        Assert.Equal((false, "", false), (saved.Orkester, saved.Command, saved.NonInteractive));
        Assert.False(tab.HasStartCommand);
    }

    [Fact]
    public void ArgumentsAndRunOnceMarkersFollowTheSavedValues()
    {
        // #260: saved values are kept as they are; the row's marker and the menu entry show them.
        Loc.Use("en");
        var tab = new MainViewModel(new AppConfig { Tabs = [new() { Name = "A", Command = "/review", CommandArgs = "#42", NonInteractive = true }] }).Tabs[0];
        Assert.Equal(("1× args", "Arguments: #42…"), (tab.CommandBadge, tab.ArgumentsMenu));
        Assert.Equal(("#42", true), (tab.ToConfig(false).CommandArgs, tab.ToConfig(false).NonInteractive));
        tab.CommandArgs = "";
        tab.RunOnce = false;
        Assert.Equal(("", "Arguments…", false), (tab.CommandBadge, tab.ArgumentsMenu, tab.HasCommandBadge));
        tab.SelectedStartChoice = tab.StartChoices[0];
        tab.CommandArgs = "x";
        Assert.False(tab.HasCommandBadge); // no command: nothing to mark
    }

    [Fact]
    public void HeadersAndTheInstallEntryDoNotChangeTheChoice()
    {
        var vm = new MainViewModel(new AppConfig { Tabs = [new() { Name = "A", Command = "/review" }] });
        var tab = vm.Tabs[0];
        TabViewModel? asked = null;
        vm.InstallOrkesterRequested += t => asked = t;
        tab.HasOrkester = false;
        tab.SelectedStartChoice = new(StartChoiceKind.Header, "", "Projekt");
        Assert.Equal("/review", tab.Command);
        tab.SelectedStartChoice = tab.StartChoices.First(c => c.Kind == StartChoiceKind.InstallOrkester);
        Assert.Same(tab, asked);
        Assert.Equal("/review", tab.Command);
        Assert.False(tab.Orkester);
    }

    [Fact]
    public void WithoutTheOrchestraTheListOffersInstallAndReadMoreAndKeepsTheChoice()
    {
        var vm = new MainViewModel(new AppConfig { Tabs = [new() { Name = "A", Llm = "claude", Directory = "~/proj" }] });
        var tab = vm.Tabs[0];
        tab.HasOrkester = false;
        TabViewModel? asked = null;
        vm.InstallOrkesterRequested += t => asked = t;
        string? opened = null;
        vm.OpenLink = url => { opened = url; return true; };
        Assert.True(tab.ShowStartCommands);
        Assert.Equal([StartChoiceKind.None, StartChoiceKind.InstallOrkester, StartChoiceKind.ReadMoreOrkester], tab.StartChoices.Select(c => c.Kind));
        var before = tab.SelectedStartChoice;
        tab.SelectedStartChoice = tab.StartChoices.First(c => c.Kind == StartChoiceKind.InstallOrkester);
        Assert.Same(tab, asked);
        Assert.False(tab.Orkester);
        Assert.Equal("", tab.Command);
        Assert.Equal(before, tab.SelectedStartChoice); // restored, never saved as the start command
        tab.SelectedStartChoice = tab.StartChoices.First(c => c.Kind == StartChoiceKind.ReadMoreOrkester);
        Assert.Equal("https://solwation.github.io/dirigenten-och-orkestern/", opened);
        Assert.Equal(before, tab.SelectedStartChoice);
        Assert.False(vm.Error.IsVisible);
        vm.OpenLink = _ => false;
        tab.SelectedStartChoice = tab.StartChoices.First(c => c.Kind == StartChoiceKind.ReadMoreOrkester);
        Assert.True(vm.Error.IsVisible);
    }

    [Fact]
    public void OrkesterIsFirstAfterNoCommandThenTheGroups()
    {
        var previous = Loc.Language;
        Loc.Use("sv");
        try
        {
            var vm = new MainViewModel(new AppConfig { Tabs = [new() { Name = "A", Llm = "claude", Directory = "~/proj" }] });
            var tab = vm.Tabs[0];
            tab.HasOrkester = true;
            vm.ShowSampleCommands();
            var labels = tab.StartChoices.Select(c => c.Label).ToList();
            Assert.Equal(["Inget kommando", "/orkester", "Projekt", "/deploy-preview", "/docs:check-links", "/review", "Användare", "/standup", "Plugin", "/frontend-kit:component"], labels);
            Assert.All(tab.StartChoices.Where(c => c.IsHeader), c => Assert.False(c.IsSelectable));
            Assert.True(tab.ShowStartCommands);
        }
        finally { Loc.Use(previous); }
    }

    [Fact]
    public async Task DiscoveryRunsBatchedAndFillsTheRowsOfTheSameFolder()
    {
        var vm = new MainViewModel(new AppConfig { Tabs = [new() { Name = "A", Llm = "claude", Directory = "~/proj" }, new() { Name = "B", Llm = "aider", Directory = "~/proj" }] });
        await vm.InitTask;
        var calls = new List<IReadOnlyList<StartCommandRequest>>();
        vm.DiscoverCommandsAsync = (_, requests) =>
        {
            calls.Add(requests);
            return Task.FromResult<IReadOnlyList<IReadOnlyList<StartCommand>?>>(requests.Select(_ => (IReadOnlyList<StartCommand>?)[new StartCommand("/ship", StartCommandSource.Project)]).ToArray());
        };
        vm.EnableCommandDiscovery();
        await vm.FlushCommandsAsync();
        // aider has no command files: only the claude row is asked for, once.
        Assert.Single(calls);
        Assert.Equal([new StartCommandRequest("claude", "~/proj", null)], calls[0]);
        Assert.Equal("/ship", Assert.Single(vm.Tabs[0].Commands).Name);
        Assert.Empty(vm.Tabs[1].Commands);
        Assert.True(vm.Tabs[0].ShowStartCommands);
        Assert.False(vm.Tabs[1].ShowStartCommands);
    }

    [Fact]
    public void OrchestraColumnShowsOnlyWhileAVisibleRowCanUseOrInstallIt()
    {
        var vm = new MainViewModel(new AppConfig { Tabs = [new() { Name = "A" }, new() { Name = "B", Llm = Catalog.ShellOnly }] });
        Assert.False(vm.ShowOrchestraColumn);
        var changes = 0;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.ShowOrchestraColumn)) changes++; };

        vm.Tabs[1].HasOrkester = true; // a plain shell never uses the orchestra
        Assert.False(vm.ShowOrchestraColumn);
        vm.Tabs[0].HasOrkester = false; // "Install" counts
        Assert.True(vm.ShowOrchestraColumn);
        vm.Tabs[0].Llm = Catalog.ShellOnly;
        Assert.False(vm.ShowOrchestraColumn);
        vm.Tabs[1].Llm = "codex";
        Assert.True(vm.ShowOrchestraColumn);
        Assert.Equal(3, changes);
    }

    [Fact]
    public void ProjectColumnsHideTheOrchestraAndKeepTheFixedWidths()
    {
        // The project takes all the free room and the status is a narrow fixed column (#209); the row's ⋯ (#196) stays
        // last, at the far right, whether or not the orchestra column shows.
        Assert.Equal("4,20,46,*,160,200,195,168,32", MainWindow.ProjectColumns(1100, orchestra: true));
        Assert.Equal("4,20,46,*,160,200,195,0,32", MainWindow.ProjectColumns(1100, orchestra: false));
        Assert.Equal("4,20,46,*,160,150,140,168,32", MainWindow.ProjectColumns(900, orchestra: true));
    }

    [Fact]
    public void TwoNewlyAddedProjectsGetDifferentColours()
    {
        var vm = new MainViewModel(new AppConfig { Tabs = [new() { Name = "A", ColorIndex = 3 }] });
        vm.AddTab();
        vm.AddTab();
        var colors = vm.Tabs.Select(t => t.Color).ToList();
        Assert.Equal(colors.Count, colors.Distinct().Count());
        Assert.Equal(vm.Theme.ColorAt(4), colors[1]);
        Assert.Equal(vm.Theme.ColorAt(5), colors[2]);

        vm.Import([new("/work/one", "one", Git: true, Ai: false), new("/work/two", "two", Git: true, Ai: false)]);
        Assert.Equal(5, vm.Tabs.Select(t => t.Color).Distinct().Count());
    }

    [Fact]
    public void FirstProjectAndColourlessConfigsRotateFromTheSecondColour()
    {
        var empty = new MainViewModel(new AppConfig());
        empty.AddTab();
        Assert.Equal(empty.Theme.ColorAt(1), empty.Tabs[0].Color);

        // A hand-written config without colours (as the review configs): rotate, and a subtab keeps its project's colour.
        var cfg = JsonSerializer.Deserialize<AppConfig>("""{"Tabs":[{"Name":"A","SubTabs":[{"Name":"A2"}]},{"Name":"B"},{"Name":"C"}]}""")!;
        var vm = new MainViewModel(cfg);
        Assert.Equal(["A", "A2", "B", "C"], vm.Tabs.Select(t => t.Name));
        Assert.Equal([vm.Theme.ColorAt(1), vm.Theme.ColorAt(1), vm.Theme.ColorAt(2), vm.Theme.ColorAt(3)], vm.Tabs.Select(t => t.Color));
        // A stored palette colour still wins.
        var legacy = new MainViewModel(new AppConfig { Tabs = [new() { Name = "L", Color = "#D20F39" }] });
        Assert.Equal("#D20F39", legacy.Tabs[0].Color);
    }

    [Fact]
    public void TheLauncherFallsBackToAColourForAColourlessTab()
    {
        var args = Launcher.WindowsTerminalArgs([new TabConfig { Name = "x" }], "Ubuntu");
        Assert.Equal(Catalog.Themes[0].Colors[0], args[args.IndexOf("--tabColor") + 1]);
    }

    /// <summary>
    /// The longest fixed status texts, rendered headless at the default width with the orchestra column shown (the
    /// narrowest status column), and the Agent box with an account label of about 8 characters (#194) and sample plan limits with
    /// their usage meter and warning icon (#202), end in no "…" (#152). Runs the app's own --screenshot mode in a child process,
    /// which owns its Avalonia UI thread; nothing is shown and the process exits by itself.
    /// </summary>
    [Theory]
    [InlineData("sv")]
    [InlineData("en")]
    public async Task LongestStatusTextsFitAtTheDefaultWidth(string language)
    {
        string[] names = ["Atlas", "Boreal", "Cirrus", "Delta", "Eko", "Fjord", "Glimt", "Hamn", "Iris"];
        string[] llms = ["claude", "codex", "claude", "agy", "codex", "claude", "codex", "agy", "claude"];
        var config = Path.Combine(_root, "long.json");
        File.WriteAllText(config, JsonSerializer.Serialize(new
        {
            Language = language,
            ProjectSearchRoot = "/home/me/Projects",
            // Accounts with names of about 8 characters show as labels in the Agent box, which must fit them too (#194).
            Accounts = new[]
            {
                new { Id = "claude-kundjobb", Agent = "claude", Name = "Kundjobb", Directory = "~/.claude-kundjobb" },
                new { Id = "codex-kund", Agent = "codex", Name = "Kund", Directory = "~/.codex-kund" },
            },
            Tabs = names.Zip(llms, (n, l) => new
            {
                Name = n, Directory = "/srv/" + n.ToLowerInvariant(), Llm = l,
                Account = n is "Atlas" ? "claude-kundjobb" : n is "Boreal" ? "codex-kund" : "",
                // A group card (#196) insets its rows; its subtabs must fit as well as plain rows.
                SubTabs = new[] { new { Name = n + "-api", Llm = "codex" }, new { Name = n + "-test", Llm = "claude" } }.Take(n is "Atlas" ? 2 : 0),
            }),
        }));
        var app = typeof(Screenshot).Assembly.Location;
        var psi = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in new[] { app, "--screenshot", Path.Combine(_root, "long.png"), "--config", config, "--fake-long-status", "--fake-limits", "--check-clipping", "--height", "760" })
            psi.ArgumentList.Add(a);
        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail("The screenshot process did not finish");
        }
        Assert.True(process.ExitCode == 0, $"exit {process.ExitCode}\n{await stdout}\n{await stderr}");
        Assert.True(File.Exists(Path.Combine(_root, "long.png")));
    }
}
