using System.Xml.Linq;
using BoosterAI;
using Xunit;

namespace BoosterAI.Tests;

/// <summary>#151: the Start button tooltip lists what Start opens, the row checkbox names say what ticking means
/// (and why a running row is left alone), and the accent-filled boxes are exactly the ones Start counts.</summary>
[Collection(ConfigFileCollection.Name)]
public class StartClarityTests : IDisposable
{
    private readonly string _language = Loc.Language;

    public StartClarityTests() => Loc.Use("sv");
    public void Dispose() => Loc.Use(_language);

    private static MainViewModel Build(params string[] names) =>
        new(new AppConfig { Tabs = names.Select(n => new TabConfig { Name = n, Directory = "/srv/" + n, Enabled = true }).ToList() });

    [Fact]
    public void StartTooltipListsTheTabsStartOpens()
    {
        var vm = Build("Atlas", "Boreal", "Cirrus", "Delta");
        vm.Tabs[0].SetState(TabState.Running, null, activity: Activity.Busy);
        vm.Tabs[1].Enabled = false;
        vm.TabsChanged();
        Assert.Equal("Öppnar Cirrus, Delta", vm.LaunchTip);
        Assert.Equal(vm.ToStartCount, vm.Tabs.Count(t => t.WillStart));
        Assert.Equal("Starta 2 flikar", vm.LaunchLabel);
    }

    [Fact]
    public void StartTooltipFollowsRenamesAndIsEmptyWhenNothingStarts()
    {
        var vm = Build("A", "B");
        Assert.Equal("Öppnar A, B", vm.LaunchTip);
        vm.Tabs[1].Name = "Beta";
        Assert.Equal("Öppnar A, Beta", vm.LaunchTip);
        foreach (var t in vm.Tabs) t.Enabled = false;
        Assert.Null(vm.LaunchTip);
    }

    [Fact]
    public void StartTooltipIsEnglishToo()
    {
        Loc.Use("en");
        Assert.Equal("Opens A, B", Build("A", "B").LaunchTip);
    }

    [Fact]
    public void CheckboxNameSaysWhatTickingMeansOrWhyARunningRowIsLeftAlone()
    {
        var vm = Build("Cirrus");
        var tab = vm.Tabs[0];
        Assert.Equal("Ta med Cirrus vid Starta", tab.OpenTip);
        Assert.True(tab.WillStart);
        tab.SetState(TabState.Running, null, activity: Activity.Busy);
        Assert.Equal("Cirrus körs redan och startas inte om. Ingår vid nästa start när fliken är stängd.", tab.OpenTip);
        Assert.False(tab.WillStart);
        tab.SetState(TabState.Closed, null, activity: Activity.Unknown);
        Assert.Equal("Ta med Cirrus vid Starta", tab.OpenTip);
    }

    [Fact]
    public void CheckboxNameIsRaisedWhenTheRowStartsRunning()
    {
        var tab = Build("Cirrus").Tabs[0];
        var raised = new List<string?>();
        tab.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        tab.SetState(TabState.Running, null, activity: Activity.Busy);
        Assert.Contains(nameof(TabViewModel.OpenTip), raised);
        Assert.Contains(nameof(TabViewModel.WillStart), raised);
    }

    [Fact]
    public void RowCheckboxUsesTheNameForTooltipAndAutomationAndMarksLiveRows()
    {
        var src = Path.Combine(RepoRoot(), "src", "BoosterAI");
        var check = XDocument.Load(Path.Combine(src, "MainWindow.axaml")).Descendants()
            .Single(e => e.Name.LocalName == "CheckBox" && (string?)e.Attribute("IsChecked") == "{Binding Enabled}");
        Assert.Equal("{Binding OpenTip}", (string?)check.Attribute("AutomationProperties.Name"));
        Assert.Equal("{Binding OpenTip}", (string?)check.Attribute("ToolTip.Tip"));
        Assert.Equal("{Binding IsAlive}", (string?)check.Attribute("Classes.alive"));
        var styles = File.ReadAllText(Path.Combine(src, "Styles.axaml"));
        Assert.Contains("CheckBox.alive:checked /template/ Border#NormalRectangle", styles);
        Assert.Contains("CheckBox.alive:checked /template/ Path#CheckGlyph", styles);
    }

    [Fact]
    public void ColumnHeaderIsStartaInSwedish()
    {
        Assert.Equal("Starta", Loc.Get("col.with"));
        Loc.Use("en");
        Assert.Equal("Start", Loc.Get("col.with"));
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "BoosterAI.slnx"))) return dir.FullName;
        throw new InvalidOperationException("Repository root not found.");
    }
}
