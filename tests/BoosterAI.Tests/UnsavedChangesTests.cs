using BoosterAI;
using Xunit;

/// <summary>Unsaved compact and WSL-memory edits are asked about on Done/close, never lost silently (#188).</summary>
[Collection(ConfigFileCollection.Name)]
public class UnsavedChangesTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("booster-unsaved-").FullName;
    private readonly string _wsl;

    public UnsavedChangesTests()
    {
        ConfigStore.UseFile(Path.Combine(_root, "config.json"));
        _wsl = Path.Combine(_root, ".wslconfig");
        WslConfigFile.UseCustomPath(_wsl);
    }

    public void Dispose()
    {
        WslConfigFile.UseCustomPath(null);
        Directory.Delete(_root, true);
    }

    private AppConfig _cfg = new();

    private MainViewModel Manual()
    {
        _cfg = new AppConfig { Tabs = [], AutoCompactMode = AutoCompactPolicy.Manual };
        var vm = new MainViewModel(_cfg);
        vm.ShowSampleAgents();
        return vm;
    }

    private static AutoCompactViewModel Editor(MainViewModel vm) => vm.Setup.Agents.Single(a => a.Cli == "claude").Compact!;

    private static Task<UnsavedChoice> Never(IReadOnlyList<UnsavedItem> _) => throw new InvalidOperationException("asked without changes");

    [Fact]
    public async Task NothingIsAskedWithoutChanges()
    {
        var vm = Manual();
        _ = Editor(vm);
        Assert.True(await vm.ResolveUnsavedAsync(Never));
    }

    [Fact]
    public async Task UntouchedLoadingAndMetadataAreNotChanges()
    {
        var vm = Manual();
        _ = Editor(vm);
        vm.ApplyCompactMetadata(new Dictionary<string, int> { ["gpt-5"] = 400000 });
        Assert.True(await vm.ResolveUnsavedAsync(Never));
    }

    [Fact]
    public async Task ChangedCompactModeAsksAndSaveWrites()
    {
        var vm = Manual();
        var editor = Editor(vm);
        editor.Mode = editor.Modes.Single(m => m.Id == "custom");
        editor.Context = "200000"; editor.Custom = "150000";
        IReadOnlyList<UnsavedItem>? shown = null;
        Assert.True(await vm.ResolveUnsavedAsync(items => { shown = items; return Task.FromResult(UnsavedChoice.Save); }));
        Assert.Single(shown!);
        Assert.Contains(_cfg.AutoCompactSettings, s => s.Cli == "claude" && s.Mode == "custom" && s.CustomTokens == 150000);
        Assert.Empty(vm.UnsavedChanges());
    }

    [Fact]
    public async Task DiscardKeepsTheSavedSettingsAndTheConfigFile()
    {
        var vm = Manual();
        var editor = Editor(vm);
        var before = _cfg.AutoCompactSettings.ToList();
        editor.Mode = editor.Modes.Single(m => m.Id == "custom");
        editor.Context = "200000"; editor.Custom = "150000";
        Assert.True(await vm.ResolveUnsavedAsync(_ => Task.FromResult(UnsavedChoice.Discard)));
        Assert.Equal(before, _cfg.AutoCompactSettings);
        Assert.Equal("default", editor.Mode.Id);
        Assert.False(File.Exists(Path.Combine(_root, "config.json")));
        Assert.Empty(vm.UnsavedChanges());
    }

    [Fact]
    public async Task CancelKeepsTheDraftAndTheWindowOpen()
    {
        var vm = Manual();
        var editor = Editor(vm);
        editor.Mode = editor.Modes.Single(m => m.Id == "custom");
        editor.Context = "200000"; editor.Custom = "150000";
        Assert.False(await vm.ResolveUnsavedAsync(_ => Task.FromResult(UnsavedChoice.Cancel)));
        Assert.Equal("custom", editor.Mode.Id);
        Assert.Single(vm.UnsavedChanges());
    }

    [Fact]
    public async Task InvalidDraftIsListedButNotSaved()
    {
        var vm = Manual();
        var editor = Editor(vm);
        editor.Mode = editor.Modes.Single(m => m.Id == "custom");
        editor.Context = "200000"; editor.Custom = "12";
        IReadOnlyList<UnsavedItem>? shown = null;
        Assert.True(await vm.ResolveUnsavedAsync(items => { shown = items; return Task.FromResult(UnsavedChoice.Save); }));
        Assert.False(shown![0].CanSave);
        Assert.DoesNotContain(_cfg.AutoCompactSettings, s => s.Mode == "custom");
    }

    [Fact]
    public async Task MovedMemorySliderAsksAndOnlySaveWritesTheFile()
    {
        File.WriteAllText(_wsl, "[wsl2]\nmemory=8GB\n");
        var vm = new MainViewModel(new AppConfig());
        var opt = vm.Optimization;
        Assert.True(await vm.ResolveUnsavedAsync(Never));
        var target = opt.MaxMemoryGb != 8 ? opt.MaxMemoryGb : opt.MinMemoryGb;
        opt.SliderMemoryGb = target;
        Assert.Equal(target, opt.SliderMemoryGb);
        Assert.True(opt.HasUnsavedCap);

        Assert.True(await vm.ResolveUnsavedAsync(_ => Task.FromResult(UnsavedChoice.Discard)));
        Assert.Equal(8, WslConfigFile.Load().MemoryGb);
        Assert.False(opt.HasUnsavedCap);

        opt.SliderMemoryGb = target;
        Assert.True(await vm.ResolveUnsavedAsync(_ => Task.FromResult(UnsavedChoice.Save)));
        Assert.Equal(target, WslConfigFile.Load().MemoryGb);
    }
}
