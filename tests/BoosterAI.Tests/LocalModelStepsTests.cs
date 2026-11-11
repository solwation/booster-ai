using System.Xml.Linq;
using BoosterAI;
using Xunit;

namespace BoosterAI.Tests;

/// <summary>#155: Local models in three steps with a reason beside an inactive Hämta modell, one size unit (GB),
/// and the advice window's result card (Passar / Osäker / Passar inte) on top.</summary>
[Collection(ConfigFileCollection.Name)]
public class LocalModelStepsTests : IDisposable
{
    private const long GiB = LocalModelAdvice.GiB;
    private readonly string _language = Loc.Language;

    public LocalModelStepsTests() => Loc.Use("sv");
    public void Dispose() => Loc.Use(_language);

    private static SettingsHardwareAdviceState Hardware()
    {
        var state = new SettingsHardwareAdviceState(_ => throw new InvalidOperationException("Unexpected live hardware read"));
        state.SetSnapshot(new(64 * GiB, 40 * GiB, "Example CPU", 16, []));
        return state;
    }

    private static void AssertReasonMatches(LocalModelsViewModel vm)
    {
        if (vm.CanDownload) Assert.Equal("", vm.DownloadBlockedReason);
        else Assert.False(string.IsNullOrWhiteSpace(vm.DownloadBlockedReason), "Inactive Hämta modell without a reason");
    }

    [Theory]
    [InlineData("sv")]
    [InlineData("en")]
    public void NoWindowStringUsesGiB(string language)
    {
        foreach (var (key, text) in Loc.Table(language))
            if (key.StartsWith("models.", StringComparison.Ordinal) || key.StartsWith("localAdvice.", StringComparison.Ordinal)
                || key.StartsWith("settingsAdvice.", StringComparison.Ordinal))
                Assert.DoesNotContain("GiB", text);
    }

    [Fact]
    public void SizesAreFormattedInDecimalGb()
    {
        Assert.Equal($"{3.3:0.0} GB", LocalModelViewModel.Memory(3_300_000_000));
        var hardware = LocalModelViewModel.HardwareText(new(16 * GiB, 3 * GiB, "Example CPU", 8, [new("Example GPU", 8 * GiB, 6 * GiB)]));
        Assert.DoesNotContain("GiB", hardware);
        Assert.Contains("GB", hardware);
        var vm = new LocalModelsViewModel(Hardware());
        foreach (var text in new[] { vm.SelectedPlan, vm.DownloadTarget, vm.Hardware }.Concat(vm.Models.Select(m => m.Caption)))
            Assert.DoesNotContain("GiB", text);
        foreach (var section in new[] { "cpu", "unknown", "missing", "download", null })
        {
            var sample = new LocalModelsViewModel(Hardware());
            sample.ShowSample(section);
            Assert.DoesNotContain("GiB", sample.DownloadStatus + sample.InstalledModels + sample.Hardware);
        }
    }

    [Fact]
    public async Task InactiveDownloadAlwaysSaysWhy()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;
        var vm = new LocalModelsViewModel(Hardware(), (_, _) => Task.FromResult(new OllamaSnapshot(
                ++reads >= 3 ? [new(DownloadableModels.All[1].Id, "digest", DownloadableModels.All[1].DownloadBytes, false)] : [], [], true)),
            async (_, _, _, cancellation) => { started.SetResult(); await release.Task.WaitAsync(cancellation); });
        AssertReasonMatches(vm);
        Assert.Equal(Loc.Get("models.blocked.server"), vm.DownloadBlockedReason);
        Assert.False(vm.ServerOk); Assert.False(vm.ServerFailed);

        await vm.RefreshAsync(CancellationToken.None);
        Assert.True(vm.ServerOk);
        AssertReasonMatches(vm);
        Assert.Equal(Loc.Get("models.blocked.consent"), vm.DownloadBlockedReason);

        vm.Confirmed = true;
        Assert.True(vm.CanDownload);
        AssertReasonMatches(vm);

        var download = vm.DownloadAsync(CancellationToken.None);
        await started.Task;
        AssertReasonMatches(vm);
        Assert.Equal(Loc.Get("models.blocked.downloading"), vm.DownloadBlockedReason);
        release.SetResult();
        await download;

        await vm.RefreshAsync(CancellationToken.None);
        Assert.True(vm.Selected.Installed);
        AssertReasonMatches(vm);
        Assert.Equal(Loc.Get("models.blocked.installed"), vm.DownloadBlockedReason);

        vm.Endpoint = "not a url";
        AssertReasonMatches(vm);
        await vm.RefreshAsync(CancellationToken.None);
        Assert.True(vm.ServerFailed); Assert.False(vm.ServerOk);
        AssertReasonMatches(vm);

        vm.Dispose();
        AssertReasonMatches(vm);
    }

    [Theory]
    [InlineData("cpu")]
    [InlineData("unknown")]
    [InlineData("missing")]
    [InlineData("download")]
    [InlineData(null)]
    public void EverySampleStateHasAReasonWhenInactive(string? section)
    {
        var vm = new LocalModelsViewModel(Hardware());
        vm.ShowSample(section);
        AssertReasonMatches(vm);
    }

    [Fact]
    public void ReasonSitsLeftOfTheDownloadButtonInTheFixedFooter()
    {
        var doc = XDocument.Load(Path.Combine(RepoRoot(), "src", "BoosterAI", "LocalModelsWindow.axaml"));
        var button = doc.Descendants().Single(e => (string?)e.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == "DownloadButton");
        var reason = doc.Descendants().Single(e => (string?)e.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == "DownloadReason");
        Assert.Same(button.Parent, reason.Parent);
        Assert.Equal("{Binding DownloadBlockedReason}", (string?)reason.Attribute("Text"));
        Assert.True(int.Parse((string?)reason.Attribute("Grid.Column") ?? "0") < int.Parse((string)button.Attribute("Grid.Column")!));
        // The footer is outside the ScrollViewer; the list is in its own * row with MinHeight 180.
        Assert.DoesNotContain(button.Ancestors(), a => a.Name.LocalName == "ScrollViewer");
        var list = doc.Descendants().Single(e => e.Name.LocalName == "ListBox");
        Assert.Equal("180", (string?)list.Attribute("MinHeight"));
        var rows = ((string)list.Parent!.Attribute("RowDefinitions")!).Split(',');
        Assert.Equal("*", rows[int.Parse((string)list.Attribute("Grid.Row")!)]);
    }

    [Theory]
    [InlineData("fits", "fits")]
    [InlineData("limited", "limited")]
    [InlineData("remote", "uncertain")]
    public void ResultCardHasOneHeadlineAndSemanticState(string sample, string card)
    {
        var vm = new LocalModelViewModel("aider", "ollama/example-coder:7b-q4");
        Assert.Equal(Loc.Get("localAdvice.card.none"), vm.ResultTitle);
        Assert.False(vm.ResultFits || vm.ResultUncertain || vm.ResultLimited);
        vm.ShowSample(sample);
        Assert.Equal(Loc.Get("localAdvice.card." + card), vm.ResultTitle);
        Assert.Equal(card == "fits", vm.ResultFits);
        Assert.Equal(card == "uncertain", vm.ResultUncertain);
        Assert.Equal(card == "limited", vm.ResultLimited);
        Assert.Equal(Loc.Get("localAdvice.why." + vm.Assessment!.Reason), vm.ResultSummary);
        Assert.Contains(Loc.Get("localAdvice.reason." + vm.Assessment.Reason), vm.ResultDetails);
        vm.Model = "other:tag";
        Assert.Equal(Loc.Get("localAdvice.card.none"), vm.ResultTitle);
        Assert.Equal("", vm.ResultDetails);
    }

    [Theory]
    [InlineData("sv")]
    [InlineData("en")]
    public void EveryReasonHasOneSentenceSummary(string language)
    {
        var table = Loc.Table(language);
        foreach (var reason in table.Keys.Where(k => k.StartsWith("localAdvice.reason.", StringComparison.Ordinal)).Select(k => k["localAdvice.reason.".Length..]))
        {
            var why = table["localAdvice.why." + reason];
            Assert.Equal(1, why.Count(c => c == '.'));
            Assert.EndsWith(".", why);
        }
    }

    [Fact]
    public void AdviceMeasurementsAreCollapsedBelowTheCardAndInputs()
    {
        var doc = XDocument.Load(Path.Combine(RepoRoot(), "src", "BoosterAI", "LocalModelWindow.axaml"));
        var panel = doc.Descendants().First(e => e.Name.LocalName == "ScrollViewer").Elements().Single();
        var children = panel.Elements().ToList();
        var card = children.FindIndex(e => e.Name.LocalName == "Border" && ((string?)e.Attribute("Classes"))?.Contains("callout") == true);
        var expander = children.FindIndex(e => e.Name.LocalName == "Expander");
        Assert.Equal(1, card); // right after the title
        Assert.Equal(children.Count - 1, expander);
        Assert.Equal("{local:T localAdvice.details}", (string?)children[expander].Attribute("Header"));
        Assert.Null(children[expander].Attribute("IsExpanded"));
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "BoosterAI.slnx"))) return dir.FullName;
        throw new InvalidOperationException("Repository root not found.");
    }
}
