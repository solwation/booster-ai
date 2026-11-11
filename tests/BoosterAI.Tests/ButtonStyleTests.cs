using System.Text.RegularExpressions;
using System.Xml.Linq;
using BoosterAI;
using Xunit;

namespace BoosterAI.Tests;

/// <summary>The button system (#148): primary, secondary and danger are inactive the same, clearly different way
/// (no fill, half-opaque border, FaintBrush text), and Close all follows its visibility rule.</summary>
[Collection(ConfigFileCollection.Name)]
public class ButtonStyleTests
{
    private static readonly string Src = Path.Combine(RepoRoot(), "src", "BoosterAI");

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "BoosterAI.slnx"))) return dir.FullName;
        throw new InvalidOperationException("Repository root not found.");
    }

    private static Dictionary<string, string> Setters(string selector)
    {
        var style = XDocument.Load(Path.Combine(Src, "Styles.axaml")).Root!.Elements()
            .Single(e => e.Name.LocalName == "Style" && (string?)e.Attribute("Selector") == selector);
        return style.Elements().Where(e => e.Name.LocalName == "Setter")
            .ToDictionary(e => (string)e.Attribute("Property")!, e => (string)e.Attribute("Value")!);
    }

    [Theory]
    [InlineData("secondary")]
    [InlineData("primary")]
    [InlineData("danger")]
    public void InactiveVariantDiffersFromItsActiveLook(string variant)
    {
        var active = Setters($"Button.{variant}");
        var inactive = Setters($"Button.{variant}:disabled /template/ ContentPresenter#PART_ContentPresenter");
        Assert.NotEqual("Transparent", active["Background"]);
        Assert.Equal("Transparent", inactive["Background"]);
        Assert.Equal("{DynamicResource DisabledBorderBrush}", inactive["BorderBrush"]);
        Assert.Equal("{DynamicResource FaintBrush}", inactive["Foreground"]);
        Assert.NotEqual(active["Foreground"], inactive["Foreground"]);
    }

    [Fact]
    public void VariantsUseTheAgreedTokens()
    {
        Assert.Equal("{DynamicResource SurfaceStrongBrush}", Setters("Button.secondary")["Background"]);
        Assert.Equal("{DynamicResource BorderBrush}", Setters("Button.secondary")["BorderBrush"]);
        Assert.Equal("{DynamicResource PrimaryFillBrush}", Setters("Button.primary")["Background"]);
        Assert.Equal("{DynamicResource DangerFillBrush}", Setters("Button.danger")["Background"]);
        Assert.Equal("Center", Setters("Button.main-action")["VerticalContentAlignment"]);
    }

    private static Dictionary<string, string> Theme(string variant)
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var dict = XDocument.Load(Path.Combine(Src, "App.axaml")).Descendants()
            .Single(e => e.Name.LocalName == "ResourceDictionary" && (string?)e.Attribute(x + "Key") == variant);
        return dict.Elements().Where(e => e.Name.LocalName == "SolidColorBrush")
            .ToDictionary(e => (string)e.Attribute(x + "Key")!, e => (string)e.Attribute("Color")!);
    }

    // Plain buttons are secondary through the Fluent keys, so their inactive look must match the classes'.
    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void PlainButtonsShareTheInactiveLook(string variant)
    {
        var t = Theme(variant);
        Assert.StartsWith("#00", t["ButtonBackgroundDisabled"]);
        Assert.Equal(t["FaintBrush"], t["ButtonForegroundDisabled"]);
        Assert.Equal(t["DisabledBorderBrush"], t["ButtonBorderBrushDisabled"]);
        Assert.Equal(t["BorderBrush"], t["ButtonBorderBrush"]);
        Assert.StartsWith("#80", t["DisabledBorderBrush"]);
        Assert.Equal("#5B5FF2", t["PrimaryFillBrush"]);
        Assert.Equal("#4C4FDB", t["PrimaryFillHoverBrush"]);
        foreach (var fill in new[] { "PrimaryFillBrush", "PrimaryFillHoverBrush", "PrimaryFillPressedBrush", "DangerFillBrush", "DangerFillHoverBrush", "DangerFillPressedBrush" })
            Assert.True(ThemeTokenTests.Contrast("#FFFFFF", t[fill]) >= 4.5, $"{variant} white on {fill}");
    }

    [Fact]
    public void CloseAllIsHiddenOnlyWithoutProjectsAndExplainsWhyItIsInactive()
    {
        var xaml = File.ReadAllText(Path.Combine(Src, "MainWindow.axaml"));
        var button = Regex.Match(xaml, @"<Button[^>]*Click=""OnCloseAll""[^>]*/>").Value;
        Assert.Contains(@"IsVisible=""{Binding HasTabs}""", button);
        Assert.Contains(@"IsEnabled=""{Binding CanCloseAll}""", button);
        Assert.Contains(@"ToolTip.Tip=""{Binding CloseAllTip}""", button);
        Assert.Contains(@"ToolTip.ShowOnDisabled=""True""", button);

        Assert.False(new MainViewModel(new AppConfig { Tabs = [] }).HasTabs);
        var vm = new MainViewModel(new AppConfig { Tabs = [new TabConfig()] });
        Assert.True(vm.HasTabs);
        Assert.False(vm.CanCloseAll);
        Assert.Equal(Loc.Get("close.none"), vm.CloseAllTip);
    }
}
