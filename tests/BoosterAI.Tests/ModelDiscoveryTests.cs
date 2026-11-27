using BoosterAI;
using BoosterAI.Tests;
using Avalonia.Controls;
using Xunit;

/// <summary>Model lists found from the agent CLIs are merged with the built-in ones (#265).</summary>
[Collection(ConfigFileCollection.Name)]
public sealed class ModelDiscoveryTests : IDisposable
{
    public void Dispose() => Catalog.SetDiscovered(new Dictionary<string, string[]>());

    [Fact]
    public void AgyOutputSkipsTheFetchingLineGarbageAndUnsafeIds()
    {
        var ids = ModelDiscovery.ParseAgy("Fetching available models...\ngemini-4-neon-high\tGemini 4 Neon (High)\r\nno tab here\n\tNoId\nbad id;rm\tX\n$(x)\tY\ngemini-4-neon-high\tDup\nclaude-opus-5-5-low\tClaude\n");
        Assert.Equal(["gemini-4-neon-high", "claude-opus-5-5-low"], ids);
        Assert.Empty(ModelDiscovery.ParseAgy(null));
        Assert.Empty(ModelDiscovery.ParseAgy("Fetching available models..."));
    }

    [Fact]
    public void CodexCacheGivesListedModelsInPriorityOrder()
    {
        const string json = """
            {"models":[
              {"slug":"b","visibility":"list","priority":2},
              {"slug":"hidden","visibility":"hide","priority":0},
              {"slug":"a","visibility":"list","priority":1},
              {"slug":"bad;id","visibility":"list","priority":3},
              {"visibility":"list"},{"slug":5,"visibility":"list"},"x",
              {"slug":"a","visibility":"list","priority":9}]}
            """;
        Assert.Equal(["a", "b"], ModelDiscovery.ParseCodex(json));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{\"models\":5}")]
    [InlineData("{}")]
    public void BrokenCodexCacheGivesNothing(string json) => Assert.Empty(ModelDiscovery.ParseCodex(json));

    [Fact]
    public void ModelsForMergesWithoutDuplicatesAndKeepsStandardFirst()
    {
        Catalog.SetDiscovered(new Dictionary<string, string[]>
        {
            ["codex"] = ["gpt-9-new", "gpt-6-sol", Catalog.StandardModel, "gpt-9-new", "bad id"],
        });
        var list = Catalog.ModelsFor("codex");
        Assert.Equal(Catalog.StandardModel, list[0]);
        Assert.Equal(list.Count, list.Distinct().Count());
        Assert.Equal("gpt-9-new", list[1]);
        Assert.Contains("gpt-6-luna", list);
        Assert.DoesNotContain("bad id", list);
        Assert.Equal(Catalog.ModelsFor("claude").Count, Catalog.ModelsFor("claude").Distinct().Count());
    }

    [Fact]
    public void CacheRoundTripsAndBrokenFileIsIgnored()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bs-models-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(dir, "models-cache.json");
            Assert.Null(ModelDiscovery.LoadCache(path));
            var at = new DateTime(2026, 11, 27, 10, 0, 0, DateTimeKind.Utc);
            ModelDiscovery.SaveCache(path, at, new Dictionary<string, string[]> { ["agy"] = ["a-1", "b-2"] });
            var c = ModelDiscovery.LoadCache(path)!.Value;
            Assert.Equal(at, c.Fetched);
            Assert.Equal(["a-1", "b-2"], c.Models["agy"]);
            File.WriteAllText(path, "{broken");
            Assert.Null(ModelDiscovery.LoadCache(path));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}

/// <summary>A tab keeps its model when the list under the editable model box changes (#265).</summary>
[Collection(ConfigFileCollection.Name)]
public sealed class ModelListChangeUiTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bs-modellist-" + Guid.NewGuid().ToString("N"));

    public ModelListChangeUiTests()
    {
        Directory.CreateDirectory(_root);
        ConfigStore.UseFile(Path.Combine(_root, "config.json"));
    }

    public void Dispose()
    {
        Catalog.SetDiscovered(new Dictionary<string, string[]>());
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void TabsAndSubTabsKeepTheirModelWhenTheListChanges()
    {
        var result = UiSession.Run(() =>
        {
            var vm = new MainViewModel(new AppConfig
            {
                Tabs =
                [
                    new TabConfig
                    {
                        Name = "Atlas", Directory = "/srv/atlas", Enabled = true, Llm = "codex", Model = "gpt-6-sol",
                        SubTabs = [new TabConfig { Name = "Sub", Llm = "codex", Model = "my-own-model", Enabled = true }],
                    },
                    new TabConfig { Name = "Boreal", Directory = "/srv/boreal", Enabled = true, Llm = "codex", Model = "typed-id-9" },
                ],
            });
            var window = new MainWindow(vm);
            window.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            try
            {
                Assert.Equal(3, vm.Tabs.Count); // sub-tabs are in the list, so RaiseModels reaches them
                Catalog.SetDiscovered(new Dictionary<string, string[]> { ["codex"] = ["gpt-9-new"] });
                foreach (var t in vm.Tabs) t.RaiseModels();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                var combos = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<Avalonia.Controls.ComboBox>()
                    .Where(c => c.IsEditable && c.ItemsSource is IReadOnlyList<string> l && l.Contains("gpt-9-new")).ToList();
                return (vm.Tabs.Select(t => t.Model).ToList(), combos.Select(c => c.Text).ToList());
            }
            finally { window.Close(); }
        });
        Assert.Equal(["gpt-6-sol", "my-own-model", "typed-id-9"], result.Item1.Order());
        Assert.NotEmpty(result.Item2);
        Assert.All(result.Item2, text => Assert.Contains(text, new[] { "gpt-6-sol", "my-own-model", "typed-id-9" }));
    }
}
