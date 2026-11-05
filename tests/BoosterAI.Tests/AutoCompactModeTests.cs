using System.Text.Json.Nodes;
using BoosterAI;
using Xunit;

[Collection(ConfigFileCollection.Name)]
public class AutoCompactModeTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("booster-compact-mode-").FullName;
    public AutoCompactModeTests() => ConfigStore.UseFile(Path.Combine(_root, "config.json"));
    public void Dispose() => Directory.Delete(_root, true);
    private static AutoCompactSetting Manual(string cli = "claude", string model = "claude-sonnet-5-5") =>
        new() { Cli = cli, Model = model, Mode = "custom", CustomTokens = 123000, ContextTokens = 1000000 };
    private static void Select(MainViewModel vm, string mode) => vm.SelectedCompactMode = vm.CompactGlobalModes.Single(m => m.Id == mode);

    [Theory]
    [InlineData("custom", true, 123000, "manual")]
    [InlineData("custom", false, 123000, "off")]
    [InlineData("custom", true, -1, "off")]
    [InlineData("default", true, 123000, "off")]
    [InlineData("recommended", true, 123000, "manual")]
    public void MissingGlobalModeMigratesOnlyEffectiveOverrides(string mode, bool active, int tokens, string expected)
    {
        var node = JsonNode.Parse("""
            {"AutoCompactSettings":[{"Cli":"claude","Model":"claude-sonnet-5-5","ContextTokens":1000000,"Profile":"code"}]}
            """)!;
        var setting = node["AutoCompactSettings"]![0]!;
        setting["Mode"] = mode; setting["Active"] = active; setting["CustomTokens"] = tokens;
        var config = ConfigStore.Parse(node.ToJsonString())!;
        Assert.Equal(expected, config.AutoCompactMode);
        ConfigStore.Save(config);
        Assert.Equal(expected, ConfigStore.Load().AutoCompactMode);
        Assert.Equal(config.AutoCompactSettings, ConfigStore.Load().AutoCompactSettings);
    }

    [Theory]
    [InlineData("off", "off")]
    [InlineData("auto", "auto")]
    [InlineData("manual", "manual")]
    [InlineData("invalid", "off")]
    public void ExplicitModeWinsOverMigrationAndUnknownModeFailsToDefaults(string supplied, string expected)
    {
        var json = """
            {"AutoCompactMode":"MODE","AutoCompactSettings":[{"Cli":"claude","Model":"claude-sonnet-5-5","Mode":"custom","CustomTokens":123000}]}
            """.Replace("MODE", supplied);
        Assert.Equal(expected, ConfigStore.Parse(json)!.AutoCompactMode);
        Assert.Equal("off", ConfigStore.Parse("{}")!.AutoCompactMode);
        Assert.Equal("off", new AppConfig().AutoCompactMode);
    }

    [Fact]
    public void AutoUsesKnownCapacityAndTaskReserveWithoutReadingOrChangingManualValues()
    {
        var config = new AppConfig { AutoCompactSettings = [Manual()] };
        Assert.Null(AutoCompactPolicy.Resolve(config, "claude", "claude-sonnet-5-5", false));
        config.AutoCompactMode = AutoCompactPolicy.Auto;
        Assert.Equal(750000, AutoCompactPolicy.Resolve(config, "claude", "claude-sonnet-5-5", false));
        Assert.Equal(600000, AutoCompactPolicy.Resolve(config, "claude", "claude-sonnet-5-5", true));
        Assert.Null(AutoCompactPolicy.Resolve(config, "codex", "example-codex", false));
        var metadata = new Dictionary<string, int> { ["example-codex"] = 200000 };
        Assert.Equal(150000, AutoCompactPolicy.Resolve(config, "codex", "example-codex", false, metadata));
        Assert.Equal(120000, AutoCompactPolicy.Resolve(config, "codex", "example-codex", true, metadata));
        Assert.Equal(Manual(), Assert.Single(config.AutoCompactSettings));
        config.AutoCompactMode = AutoCompactPolicy.Manual;
        Assert.Equal(123000, AutoCompactPolicy.Resolve(config, "claude", "claude-sonnet-5-5", false));
    }

    [Theory]
    [InlineData("claude", "standard")]
    [InlineData("claude", "gateway-alias")]
    [InlineData("codex", "standard")]
    [InlineData("codex", "ollama/local")]
    [InlineData("aider", "ollama/local")]
    [InlineData("interpreter", "local")]
    [InlineData("openhands", "claude-sonnet-5-5")]
    [InlineData("agy", "claude-sonnet-5-5")]
    public void AutoDoesNotInventCapacityFromManualOverridesOrFriendlyIds(string cli, string model)
    {
        var config = new AppConfig { AutoCompactMode = AutoCompactPolicy.Auto, AutoCompactSettings = [Manual(cli, model)] };
        Assert.Null(AutoCompactPolicy.Resolve(config, cli, model, true));
    }

    [Fact]
    public void GlobalModeMergePreservesAnotherInstancesChoiceAndEditors()
    {
        ConfigStore.Save(new AppConfig());
        var tray = new MainViewModel(ConfigStore.Load());
        var settings = new MainViewModel(ConfigStore.Load());
        var editor = tray.Setup.Agents.Single(a => a.Cli == "claude").Compact!;
        editor.Mode = editor.Modes.Single(m => m.Id == "custom");
        editor.Custom = "456789";
        Select(settings, AutoCompactPolicy.Manual);
        settings.Save();
        tray.Save();
        Assert.Equal(AutoCompactPolicy.Manual, tray.SelectedCompactMode.Id);
        Assert.True(editor.CanEdit);
        Assert.Same(editor, tray.Setup.Agents.Single(a => a.Cli == "claude").Compact);
        Assert.Equal("456789", editor.Custom);
        Select(tray, AutoCompactPolicy.Auto);
        tray.Save();
        settings.Save();
        Assert.Equal(AutoCompactPolicy.Auto, ConfigStore.Load().AutoCompactMode);
    }

    [Fact]
    public void ResetRemovesAllSelectedAgentModelsProfilesAndDraftsButPreservesOtherAgentAndPermissions()
    {
        var hidden = Manual(model: "hidden-model") with { Profile = "orchestra", Active = false };
        var config = new AppConfig { AutoCompactMode = AutoCompactPolicy.Manual, AutoCompactSettings = [Manual(), hidden, Manual("codex", "sample")] };
        var vm = new MainViewModel(config);
        vm.ShowSampleAgents();
        var claude = vm.Setup.Agents.Single(a => a.Cli == "claude");
        var codex = vm.Setup.Agents.Single(a => a.Cli == "codex");
        var editor = claude.Compact!;
        claude.SelectedMode = claude.ModeChoices.Single(c => c.Mode == AgentMode.Yolo);
        codex.Compact!.Custom = "345678";
        editor.Model = "unsaved-model";
        editor.Mode = editor.Modes.Single(m => m.Id == "custom");
        editor.Custom = "456789";
        Select(vm, AutoCompactPolicy.Off);
        Assert.False(editor.CanEdit);
        Assert.False(editor.CanSave);
        editor.Save(); editor.Reset();
        Assert.Equal(3, config.AutoCompactSettings.Count);
        Assert.Equal("456789", editor.Custom);
        Select(vm, AutoCompactPolicy.Auto);
        Assert.False(editor.CanEdit);
        Select(vm, AutoCompactPolicy.Manual);
        Assert.Equal("456789", editor.Custom);
        editor.Reset();
        Assert.Equal(Manual("codex", "sample"), Assert.Single(config.AutoCompactSettings));
        Assert.Equal("default", editor.Mode.Id);
        Assert.Equal("", editor.Custom);
        Assert.Equal("code", editor.Profile.Id);
        Assert.NotEqual("unsaved-model", editor.Model);
        Assert.Equal("345678", codex.Compact.Custom);
        Assert.Equal(AgentMode.Yolo, claude.SelectedMode.Mode);
        Assert.Equal(config.AutoCompactSettings, ConfigStore.Load().AutoCompactSettings);
        Assert.Same(editor, claude.Compact);
    }

    [Theory]
    [InlineData("off", null)]
    [InlineData("auto", 750000)]
    [InlineData("manual", 123000)]
    public void ModeReachesStartAndRestartOnlyThroughLaunchSnapshot(string mode, int? expected)
    {
        var config = new AppConfig { AutoCompactMode = mode, AutoCompactSettings = [Manual()],
            Tabs = [new TabConfig { Llm = "claude", Model = "claude-sonnet-5-5" }] };
        var vm = new MainViewModel(config);
        var row = Assert.Single(vm.Tabs);
        var tab = row.ToConfig(forLaunch: true);
        Assert.Equal(expected, tab.CompactTokens);
        var command = Launcher.TabScript(tab);
        var restart = Launcher.RestartScript(tab, new TabTrack("/s/lib.sh", "/s", "tab1"), resume: true);
        Assert.Equal(expected.HasValue, command.Contains("--autocompact"));
        Assert.Equal(expected.HasValue, restart.Contains("--autocompact"));
        if (expected.HasValue) Assert.Contains("--autocompact " + expected, restart);
        row.LaunchedAs = tab;
        row.SetState(TabState.Running, null, activity: BoosterAI.Activity.Busy);
        Assert.False(row.IsChanged);
        Select(vm, mode == "off" ? "manual" : "off");
        Assert.True(row.IsChanged);
        Assert.False(row.PendingRestart);
        Assert.Equal(expected, row.LaunchedAs.CompactTokens);
    }

    [Fact]
    public void AutoOrchestraProfileFollowsTheActualLaunchChoiceAndNotifiesRestart()
    {
        var vm = new MainViewModel(new AppConfig { AutoCompactMode = AutoCompactPolicy.Auto,
            Tabs = [new TabConfig { Llm = "claude", Model = "claude-sonnet-5-5", Orkester = true }] });
        var row = Assert.Single(vm.Tabs);
        row.HasOrkester = false;
        row.LaunchedAs = row.ToConfig(true);
        Assert.Equal(750000, row.LaunchedAs.CompactTokens);
        row.SetState(TabState.Running, null, activity: BoosterAI.Activity.Idle);
        var notified = false;
        row.PropertyChanged += (_, e) => notified |= e.PropertyName == nameof(row.IsChanged);
        row.HasOrkester = true;
        Assert.True(notified);
        Assert.True(row.IsChanged);
        Assert.Equal(600000, row.ToConfig(true).CompactTokens);
        Assert.Contains("--autocompact 600000", Launcher.TabScript(row.ToConfig(true)));
        Assert.False(row.PendingRestart);
    }

    [Fact]
    public async Task AsyncMetadataFallbackIgnoresOldResultsAndUpdatesOnlyFutureBudget()
    {
        var config = new AppConfig { AutoCompactMode = AutoCompactPolicy.Auto,
            Tabs = [new TabConfig { Llm = "codex", Model = "sample" }], AutoCompactSettings = [Manual("codex", "sample")] };
        var vm = new MainViewModel(config);
        var row = Assert.Single(vm.Tabs);
        row.LaunchedAs = row.ToConfig(true);
        row.SetState(TabState.Running, null, activity: BoosterAI.Activity.Idle);
        var older = new TaskCompletionSource<IReadOnlyDictionary<string, int>>();
        var newer = new TaskCompletionSource<IReadOnlyDictionary<string, int>>();
        var a = vm.LoadCompactMetadataAsync("Ubuntu", _ => older.Task);
        var b = vm.LoadCompactMetadataAsync("Ubuntu", _ => newer.Task);
        newer.SetResult(new Dictionary<string, int> { ["sample"] = 200000 });
        await b;
        Assert.Equal(150000, row.ToConfig(true).CompactTokens);
        older.SetResult(new Dictionary<string, int> { ["sample"] = 500000 });
        await a;
        Assert.Equal(150000, row.ToConfig(true).CompactTokens);
        Assert.True(row.IsChanged);
        Assert.Null(row.LaunchedAs.CompactTokens);
        Assert.False(row.PendingRestart);
        Assert.Equal(Manual("codex", "sample"), Assert.Single(config.AutoCompactSettings));
        await vm.LoadCompactMetadataAsync("Ubuntu", _ => Task.FromResult<IReadOnlyDictionary<string, int>>(new Dictionary<string, int>()));
        Assert.Null(row.ToConfig(true).CompactTokens);
        Assert.False(row.IsChanged);
    }
}
