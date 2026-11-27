using BoosterAI;
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
