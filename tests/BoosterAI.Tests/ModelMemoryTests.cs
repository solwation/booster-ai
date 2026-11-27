using BoosterAI;
using Xunit;

/// <summary>The model last used per agent and account on a tab comes back when the agent is chosen again (#257).</summary>
[Collection(ConfigFileCollection.Name)]
public sealed class ModelMemoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bs-modelmem-" + Guid.NewGuid().ToString("N"));
    private static readonly AgentAccount Work = new() { Id = "claude-work", Agent = "claude", Name = "Work", Directory = "~/.claude-work" };

    public ModelMemoryTests()
    {
        Directory.CreateDirectory(_root);
        ConfigStore.UseFile(Path.Combine(_root, "config.json"));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static MainViewModel Vm(params TabConfig[] tabs) => new(new AppConfig { Tabs = [.. tabs], Accounts = [Work] });

    private static void Pick(TabViewModel t, string llm, string account = "") =>
        t.SelectedAgentChoice = t.AgentChoices.First(c => c.Llm == llm && c.AccountId == account);

    [Fact]
    public void SwitchingBackAndForthRestoresEachAgentsModel()
    {
        var t = Vm(new TabConfig { Name = "Lunden", Llm = "codex", Model = "gpt-6-astra" }).Tabs[0];
        Pick(t, "claude");
        Assert.Equal(Catalog.StandardModel, t.Model);
        t.Model = "claude-sonnet-5-5";
        Pick(t, "codex");
        Assert.Equal("gpt-6-astra", t.Model);
        Pick(t, "claude");
        Assert.Equal("claude-sonnet-5-5", t.Model);
        Pick(t, "agy");
        t.Model = "gemini-4-argon-high"; // the level is part of the id
        Pick(t, "codex");
        Pick(t, "agy");
        Assert.Equal("gemini-4-argon-high", t.Model);
    }

    [Fact]
    public void EachAccountOfAnAgentKeepsItsOwnModel()
    {
        var t = Vm(new TabConfig { Llm = "claude", Model = "claude-opus-5-5" }).Tabs[0];
        Pick(t, "claude", "claude-work");
        t.Model = "claude-haiku-5-5";
        Pick(t, "codex");
        Pick(t, "claude", "claude-work");
        Assert.Equal("claude-haiku-5-5", t.Model);
        Pick(t, "claude");
        Assert.Equal("claude-opus-5-5", t.Model);
        Assert.Equal("", t.Account);
        Pick(t, "claude", "claude-work");
        Assert.Equal("claude-haiku-5-5", t.Model);
    }

    [Fact]
    public void AModelThatIsNoLongerOfferedFallsBackToTheDefaultAndIsNotSaved()
    {
        var t = Vm(new TabConfig { Llm = "claude", ModelMemory = new() { ["codex"] = "gpt-gone", ["agy"] = "gemini-4-argon-low" } }).Tabs[0];
        Pick(t, "codex");
        Assert.Equal(Catalog.StandardModel, t.Model);
        Pick(t, "agy");
        Assert.Equal("gemini-4-argon-low", t.Model);
        t.Model = "typed-custom-id";
        Pick(t, "claude");
        Assert.DoesNotContain("typed-custom-id", t.ToConfig(false).ModelMemory!.Values);
        Assert.Equal("gemini-4-argon-low", t.ToConfig(false).ModelMemory!["agy"]);
    }

    [Fact]
    public void ANewTabWithoutMemoryUsesTodaysDefaultAndWritesNone()
    {
        var t = Vm(new TabConfig { Llm = "claude" }).Tabs[0];
        Assert.Null(t.ToConfig(false).ModelMemory);
        Pick(t, "codex");
        Assert.Equal(Catalog.StandardModel, t.Model);
        Assert.Equal(Catalog.StandardModel, t.ToConfig(false).ModelMemory!["claude"]);
    }

    [Fact]
    public void OldConfigsLoadUnchangedAndMemorySurvivesSaveCopyAndSubtabs()
    {
        var old = ConfigStore.Parse("""{"Tabs":[{"Name":"A","Llm":"codex","Model":"gpt-6-sol"}]}""")!;
        Assert.Null(old.Tabs[0].ModelMemory);
        var vm = new MainViewModel(old);
        var t = vm.Tabs[0];
        Assert.Equal("gpt-6-sol", t.Model);
        Pick(t, "claude");
        t.Model = "claude-opus-5-5";
        var child = vm.AddSubTab(t); // starts as a copy of the project, then has its own memory
        Pick(child, "codex");
        Assert.Equal("gpt-6-sol", child.Model);
        child.Model = "gpt-6-luna";
        Pick(t, "codex");
        Assert.Equal("gpt-6-sol", t.Model);
        Pick(child, "claude");
        Assert.Equal("claude-opus-5-5", child.Model);
        vm.Save();
        var reloaded = ConfigStore.Parse(File.ReadAllText(ConfigStore.PathOnDisk))!;
        Assert.Equal("claude-opus-5-5", reloaded.Tabs[0].ModelMemory!["claude"]);
        Assert.Equal("gpt-6-luna", reloaded.Tabs[0].SubTabs[0].ModelMemory!["codex"]);
        var copy = ConfigStore.Copy(reloaded).Tabs[0];
        Assert.Equal(reloaded.Tabs[0].ModelMemory, copy.ModelMemory);
    }
}
