using System.Text.RegularExpressions;
using System.Xml.Linq;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BoosterAI;
using Xunit;

namespace BoosterAI.Tests;

/// <summary>Icons and row controls (#156): one meaning per icon, drawn icons instead of text glyphs, icon buttons at full
/// opacity, and the keyboard alternatives to dragging (Alt+Up/Down and the row menu).</summary>
[Collection(ConfigFileCollection.Name)]
public sealed class RowControlsTests : IDisposable
{
    private static readonly string Src = Path.Combine(RepoRoot(), "src", "BoosterAI");
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bs-rowcontrols-" + Guid.NewGuid().ToString("N"));
    private readonly string _language = Loc.Language;

    public RowControlsTests()
    {
        Directory.CreateDirectory(_root);
        ConfigStore.UseFile(Path.Combine(_root, "config.json"));
        Loc.Use("sv");
    }

    public void Dispose()
    {
        Loc.Use(_language);
        Directory.Delete(_root, recursive: true);
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "BoosterAI.slnx"))) return dir.FullName;
        throw new InvalidOperationException("Repository root not found.");
    }

    private static MainViewModel Build(params string[] names) =>
        new(new AppConfig { Tabs = names.Select(n => new TabConfig { Name = n, Directory = "/srv/" + n.ToLowerInvariant(), Enabled = true }).ToList() });

    /// <summary>The buttons (and menu items) in a window that hold the folder icon or open the folder.</summary>
    private static List<Control> FolderControls(Window window) =>
        window.GetVisualDescendants().OfType<Button>()
            .Where(b => b.IsVisible && (b.Content is Avalonia.Controls.Shapes.Path p && ReferenceEquals(p.Data, window.FindResource("FolderIcon"))
                || AutomationProperties.GetName(b) == TabViewModel.OpenFolderName))
            .Cast<Control>().ToList();

    [Fact]
    public void FolderIconMeansOpenTheFolderInBothWindows()
    {
        var names = UiSession.Run(() =>
        {
            Loc.Use("sv");
            var vm = Build("Atlas", "Boreal");
            var main = new MainWindow(vm);
            var settings = new SettingsWindow(vm);
            main.Show();
            settings.Show();
            Dispatcher.UIThread.RunJobs();
            try
            {
                return new[] { main, (Window)settings }.Select(w => FolderControls(w)
                    .Select(c => (Tip: ToolTip.GetTip(c) as string, Name: AutomationProperties.GetName(c),
                        Icon: c is Button { Content: Avalonia.Controls.Shapes.Path p } && ReferenceEquals(p.Data, w.FindResource("FolderIcon"))))
                    .ToList()).ToList();
            }
            finally
            {
                settings.Close();
                main.Close();
            }
        });
        foreach (var window in names)
        {
            Assert.Equal(2, window.Count); // one per project row
            Assert.All(window, c => Assert.Equal((TabViewModel.OpenFolderName, TabViewModel.OpenFolderName, true), c));
        }
        Assert.Equal(OperatingSystem.IsWindows() ? "Öppna i Utforskaren" : "Öppna i filhanteraren", TabViewModel.OpenFolderName);
        Assert.Equal("Öppna i Utforskaren", Loc.Table("sv")["folder.open.name"]);
        Assert.Equal("Open in Explorer", Loc.Table("en")["folder.open.name"]);
    }

    [Fact]
    public void ChangingTheFolderIsAWordAndTheOpenExternalArrowIsGone()
    {
        var settings = File.ReadAllText(Path.Combine(Src, "SettingsWindow.axaml"));
        var browse = Regex.Match(settings, "<Button[^>]*Click=\"OnBrowse\"[^>]*>\\s*<TextBlock Text=\"\\{local:T folder.change\\}\"");
        Assert.True(browse.Success, "Change folder is the text button Byt…");
        Assert.Equal("Byt…", Loc.Table("sv")["folder.change"]);
        foreach (var file in Directory.GetFiles(Src, "*.axaml"))
            Assert.DoesNotContain("OpenExternalIcon", File.ReadAllText(file));
    }

    [Theory]
    [InlineData("MainWindow.axaml")]
    [InlineData("ViewModels.cs")]
    [InlineData("SettingsWindow.axaml")]
    public void RowsUseDrawnIconsNotTextGlyphs(string file)
    {
        var text = File.ReadAllText(Path.Combine(Src, file));
        foreach (var glyph in new[] { "▸", "▾", "⋮", "↳", "▷", "↗" })
            Assert.DoesNotContain(glyph, text);
    }

    [Fact]
    public void GearHasTeethAndRowIconsExist()
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var geometries = XDocument.Load(Path.Combine(Src, "App.axaml")).Descendants()
            .Where(e => e.Name.LocalName == "StreamGeometry").ToDictionary(e => (string)e.Attribute(x + "Key")!, e => e.Value);
        // The old gear was a ring with eight spokes (read as a sun or a theme switch); the new one has arced teeth.
        Assert.True(Regex.Matches(geometries["GearIcon"], "[aA]").Count >= 20, "GearIcon is an outlined gear with teeth");
        Assert.DoesNotContain("L8,3 M8,13", geometries["GearIcon"]);
        foreach (var key in new[] { "ChevronRightIcon", "ChevronDownIcon", "GripIcon", "FolderIcon" })
            Assert.True(geometries.ContainsKey(key), key);
        var main = File.ReadAllText(Path.Combine(Src, "MainWindow.axaml"));
        foreach (var key in new[] { "ChevronRightIcon", "ChevronDownIcon", "GripIcon" })
            Assert.Contains($"{{StaticResource {key}}}", main);
    }

    private static Dictionary<string, string> Setters(string selector)
    {
        var style = XDocument.Load(Path.Combine(Src, "Styles.axaml")).Root!.Elements()
            .SingleOrDefault(e => e.Name.LocalName == "Style" && (string?)e.Attribute("Selector") == selector);
        return style?.Elements().Where(e => e.Name.LocalName == "Setter")
            .ToDictionary(e => (string)e.Attribute("Property")!, e => (string)e.Attribute("Value")!) ?? [];
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void IconButtonsAreFullyOpaqueFaintAndOnlyDimmedWhenInactive(string variant)
    {
        var styles = XDocument.Load(Path.Combine(Src, "Styles.axaml")).Root!.Elements().Where(e => e.Name.LocalName == "Style").ToList();
        // No resting-state opacity on icon or remove buttons (the old Button.remove was 0.35 until hovered).
        Assert.DoesNotContain(styles, s => ((string)s.Attribute("Selector")!).Contains("Button.remove"));
        foreach (var style in styles.Where(s => ((string)s.Attribute("Selector")!).Contains("Button.icon") && !((string)s.Attribute("Selector")!).Contains(":disabled")))
            Assert.DoesNotContain(style.Elements(), e => (string?)e.Attribute("Property") == "Opacity");
        Assert.Equal("{DynamicResource FaintBrush}", Setters("Button.icon Path")["Stroke"]);
        Assert.Equal("1.5", Setters("Button.icon Path")["StrokeThickness"]);
        Assert.Equal("16", Setters("Button.icon Path")["Width"]);
        Assert.Equal("{DynamicResource TextBrush}", Setters("Button.icon:pointerover Path, Button.icon:focus-visible Path")["Stroke"]);
        Assert.True(double.Parse(Setters("Button.icon:disabled Path")["Opacity"], System.Globalization.CultureInfo.InvariantCulture) < 1);
        foreach (var file in new[] { "MainWindow.axaml", "SettingsWindow.axaml" })
            Assert.DoesNotContain("icon remove", File.ReadAllText(Path.Combine(Src, file)));

        // The remove × (Settings → Projects, the subtab's ×) is drawn in FaintBrush: >= 3:1 against both row surfaces.
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var theme = XDocument.Load(Path.Combine(Src, "App.axaml")).Descendants()
            .Single(e => e.Name.LocalName == "ResourceDictionary" && (string?)e.Attribute(x + "Key") == variant)
            .Elements().ToDictionary(e => (string)e.Attribute(x + "Key")!, e => (string?)e.Attribute("Color") ?? "");
        foreach (var surface in new[] { "BackgroundBrush", "SurfaceBrush" })
            Assert.True(ThemeTokenTests.Contrast(theme["FaintBrush"], theme[surface]) >= 3, $"{variant} FaintBrush on {surface}");
    }

    [Fact]
    public void ContinueIsAWordWithItsTargetInTheAccessibleName()
    {
        var main = File.ReadAllText(Path.Combine(Src, "MainWindow.axaml"));
        var link = Regex.Match(main, "<Button[^>]*Classes=\"link\"[^>]*Click=\"OnContinue\"[^>]*IsVisible=\"\\{Binding CanContinueClosed\\}\"[^>]*>\\s*<TextBlock Classes=\"caption\" Text=\"\\{local:T restart.continue.link\\}\"");
        Assert.True(link.Success, "Fortsätt is a 12 px link shown only when the conversation can be continued");
        Assert.Contains("AutomationProperties.Name=\"{Binding ContinueName}\"", main);
        var tab = Build("Cirrus").Tabs[0];
        Assert.Equal("Fortsätt", Loc.Get("restart.continue.link"));
        Assert.Equal("Fortsätt senaste samtalet i Cirrus", tab.ContinueName);
        var raised = new List<string?>();
        tab.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        tab.Name = "Ceres";
        Assert.Contains(nameof(TabViewModel.ContinueName), raised);
        Assert.Equal("Fortsätt senaste samtalet i Ceres", tab.ContinueName);
    }

    [Fact]
    public void MoveStepSwapsProjectsWithTheirSubtabsAndSubtabsWithinTheirProject()
    {
        var vm = Build("A", "B", "C");
        var a = vm.Tabs[0];
        var a2 = vm.AddSubTab(a);
        var a3 = vm.AddSubTab(a);
        Assert.False(vm.CanMoveStep(a, -1));
        Assert.True(vm.MoveStep(a, 1));
        Assert.Equal(["B", "A", a2.Name, a3.Name, "C"], vm.Tabs.Select(t => t.Name));
        Assert.False(vm.CanMoveStep(a2, -1)); // a subtab never leaves its project
        Assert.True(vm.MoveStep(a3, -1));
        Assert.Equal(["B", "A", a3.Name, a2.Name, "C"], vm.Tabs.Select(t => t.Name));
        Assert.False(vm.MoveStep(a2, 1));
        Assert.True(vm.MoveStep(vm.Tabs.Single(t => t.Name == "C"), -1));
        Assert.Equal(["B", "C", "A", a3.Name, a2.Name], ConfigStore.Load().Tabs.SelectMany(t => new[] { t.Name }.Concat(t.SubTabs?.Select(s => s.Name) ?? [])));
    }

    private static Border Row(Window window, TabViewModel tab) =>
        window.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("row") && b.DataContext == tab);

    private static Border Grip(Window window, TabViewModel tab) =>
        window.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("grip") && b.DataContext == tab);

    [Fact]
    public void AltDownOnTheFirstRowMovesItOneStepAndSavesTheOrder()
    {
        var (order, focused) = UiSession.Run(() =>
        {
            Loc.Use("sv");
            var vm = Build("Atlas", "Boreal", "Cirrus");
            var atlas = vm.Tabs[0];
            var window = new MainWindow(vm);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            try
            {
                Assert.True(window.FocusGrip(atlas));
                window.KeyPress(Key.Down, RawInputModifiers.Alt, PhysicalKey.ArrowDown, null);
                Dispatcher.UIThread.RunJobs();
                // The rebuilt row's grip has the focus again, so a second Alt+Down would move it on.
                return (vm.Tabs.Select(t => t.Name).ToList(), window.FocusManager?.GetFocusedElement() == Grip(window, atlas));
            }
            finally { window.Close(); }
        });
        Assert.Equal(["Boreal", "Atlas", "Cirrus"], order);
        Assert.Equal(["Boreal", "Atlas", "Cirrus"], ConfigStore.Load().Tabs.Select(t => t.Name));
        Assert.True(focused);
    }

    [Fact]
    public void RowMenuOffersMovesRestartContinueAndOpenFolder()
    {
        var (headers, enabled, gripName, gripFocusable) = UiSession.Run(() =>
        {
            Loc.Use("sv");
            var vm = Build("Atlas", "Boreal");
            var window = new MainWindow(vm);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            try
            {
                var atlas = vm.Tabs[0];
                var menu = Assert.IsType<ContextMenu>(Row(window, atlas).ContextMenu);
                menu.Open(Row(window, atlas));
                Dispatcher.UIThread.RunJobs();
                var items = menu.Items.OfType<MenuItem>().ToList();
                var result = (items.Select(i => i.Header as string).ToList(), items.Select(i => i.IsEnabled).ToList(),
                    AutomationProperties.GetName(Grip(window, atlas)), Grip(window, atlas).Focusable);
                menu.Close();
                return result;
            }
            finally { window.Close(); }
        });
        Assert.Equal(["Flytta upp", "Flytta ned", "Starta om", "Fortsätt samtal", TabViewModel.OpenFolderName], headers);
        // The first row cannot move up; a never-started tab has nothing to restart or continue.
        Assert.Equal([false, true, false, false, true], enabled);
        Assert.Equal("Flytta Atlas", gripName);
        Assert.True(gripFocusable);
    }
}
