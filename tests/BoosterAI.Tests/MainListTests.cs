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
    [InlineData(false, false, 0)]
    [InlineData(false, true, 0)]
    [InlineData(true, false, 1)]
    [InlineData(true, true, 2)]
    public void OrchestraModeReadsTheSavedSettings(bool orkester, bool nonInteractive, int mode)
    {
        var vm = new MainViewModel(new AppConfig { Tabs = [new() { Name = "A", Orkester = orkester, NonInteractive = nonInteractive }] });
        var tab = vm.Tabs[0];
        Assert.Equal(mode, tab.OrchestraMode);
        // Saving without touching the choice keeps both settings exactly as they were.
        var saved = tab.ToConfig(forLaunch: false);
        Assert.Equal((orkester, nonInteractive), (saved.Orkester, saved.NonInteractive));
    }

    [Theory]
    [InlineData(1, true, false)]
    [InlineData(2, true, true)]
    [InlineData(0, false, true)]
    public void OrchestraModeWritesTheSameSettingsTheTwoTogglesDid(int mode, bool orkester, bool nonInteractive)
    {
        // Start from "Orchestra, once" so Off shows that a stored Run once survives (ignored while off, as before).
        var vm = new MainViewModel(new AppConfig { Tabs = [new() { Name = "A", Orkester = true, NonInteractive = true }] });
        var tab = vm.Tabs[0];
        tab.OrchestraMode = mode;
        var saved = tab.ToConfig(forLaunch: false);
        Assert.Equal((orkester, nonInteractive), (saved.Orkester, saved.NonInteractive));
        Assert.Equal(mode, tab.OrchestraMode);
        Assert.Equal(mode == 2, tab.OnceChecked);
        // The round trip through the saved file gives the same choice back.
        var reloaded = new MainViewModel(new AppConfig { Tabs = [saved] });
        Assert.Equal(mode, reloaded.Tabs[0].OrchestraMode);
    }

    [Fact]
    public void OrchestraModesAreOffOrchestraAndOnce()
    {
        var previous = Loc.Language;
        Loc.Use("sv");
        try { Assert.Equal(["Av", "Orkester", "Orkester, en gång"], TabViewModel.OrchestraModes); }
        finally { Loc.Use(previous); }
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
        Assert.Equal("4,20,46,4*,5*,166,195,168", MainWindow.ProjectColumns(1100, orchestra: true));
        Assert.Equal("4,20,46,4*,5*,166,195,0", MainWindow.ProjectColumns(1100, orchestra: false));
        Assert.EndsWith(",168", MainWindow.ProjectColumns(900, orchestra: true));
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
    /// narrowest status column), end in no "…" (#152). Runs the app's own --screenshot mode in a child process,
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
            Tabs = names.Zip(llms, (n, l) => new { Name = n, Directory = "/srv/" + n.ToLowerInvariant(), Llm = l }),
        }));
        var app = typeof(Screenshot).Assembly.Location;
        var psi = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in new[] { app, "--screenshot", Path.Combine(_root, "long.png"), "--config", config, "--fake-long-status", "--check-clipping", "--height", "760" })
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
