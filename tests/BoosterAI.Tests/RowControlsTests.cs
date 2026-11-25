using System.Text.RegularExpressions;
using System.Xml.Linq;
using Avalonia;
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
        foreach (var key in new[] { "MoreIcon", "GripIcon", "FolderIcon", "CloseIcon" })
            Assert.True(geometries.ContainsKey(key), key);
        var main = File.ReadAllText(Path.Combine(Src, "MainWindow.axaml"));
        foreach (var key in new[] { "MoreIcon", "GripIcon", "CloseIcon" })
            Assert.Contains($"{{StaticResource {key}}}", main);
        // No expander any more (#196): subtabs are always shown in their group card.
        Assert.DoesNotContain("Chevron", main);
        Assert.DoesNotContain("subtab-guide", main + File.ReadAllText(Path.Combine(Src, "Styles.axaml")));
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

    /// <summary>The group card's tint (#196): every palette colour at the theme's tint opacity over the background keeps
    /// text at 4.5:1 and icons and field edges at 3:1.</summary>
    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void GroupTintKeepsTextAndIconContrastForEveryPalette(string variant)
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var dictionary = XDocument.Load(Path.Combine(Src, "App.axaml")).Descendants()
            .Single(e => e.Name.LocalName == "ResourceDictionary" && (string?)e.Attribute(x + "Key") == variant).Elements().ToList();
        var theme = dictionary.Where(e => e.Attribute("Color") is not null).ToDictionary(e => (string)e.Attribute(x + "Key")!, e => (string)e.Attribute("Color")!);
        var opacity = double.Parse(dictionary.Single(e => (string?)e.Attribute(x + "Key") == "GroupTintOpacity").Value, System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(opacity, 0.05, 0.2);
        static int[] Rgb(string hex) => [.. new[] { 0, 2, 4 }.Select(i => Convert.ToInt32(hex.TrimStart('#')[^6..].Substring(i, 2), 16))];
        string Over(string background, string color)
        {
            var (b, c) = (Rgb(background), Rgb(color));
            return "#" + string.Concat(b.Zip(c, (bc, cc) => ((int)Math.Round(bc * (1 - opacity) + cc * opacity)).ToString("X2")));
        }
        foreach (var palette in Catalog.Themes)
            foreach (var color in palette.Colors)
            {
                var tinted = Over(theme["BackgroundBrush"], color);
                foreach (var (token, minimum) in new[] { ("TextBrush", 4.5), ("MutedBrush", 4.5), ("LinkBrush", 4.5), ("FaintBrush", 3.0), ("FieldBorderBrush", 3.0) })
                    Assert.True(ThemeTokenTests.Contrast(theme[token], tinted) >= minimum, $"{variant} {token} on {palette.Name} {color} tint {tinted}");
            }
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
                var items = menu.Items.OfType<MenuItem>().Where(i => i.IsVisible).ToList();
                var result = (items.Select(i => i.Header as string).ToList(), items.Select(i => i.IsEnabled).ToList(),
                    AutomationProperties.GetName(Grip(window, atlas)), Grip(window, atlas).Focusable);
                menu.Close();
                return result;
            }
            finally { window.Close(); }
        });
        Assert.Equal(["Skapa underflik", "Flytta upp", "Flytta ned", "Starta om", "Fortsätt samtal", TabViewModel.OpenFolderName], headers);
        // The first row cannot move up; a never-started tab has nothing to restart or continue.
        Assert.Equal([true, false, true, false, false, true], enabled);
        Assert.Equal("Flytta Atlas", gripName);
        Assert.True(gripFocusable);
    }

    private static Button More(Window window, TabViewModel tab) =>
        Row(window, tab).GetVisualDescendants().OfType<Button>().Single(b => b.Content is Avalonia.Controls.Shapes.Path p
            && ReferenceEquals(p.Data, window.FindResource("MoreIcon")));

    private static Border Segment(Window window, TabViewModel tab) =>
        window.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("segment") && b.DataContext == tab);

    /// <summary>The group card (#196): no expander or indent, a card only around a project with subtabs, and a ⋯ on every
    /// row that opens the row menu below itself, by mouse or keyboard, then lets a right-click open at the pointer again.</summary>
    [Fact]
    public void EveryRowHasAMoreButtonThatOpensTheRowMenuAndOnlyGroupsGetACard()
    {
        var result = UiSession.Run(() =>
        {
            Loc.Use("sv");
            var vm = new MainViewModel(new AppConfig { Tabs = [
                new() { Name = "Atlas", Directory = "/srv/atlas", SubTabs = [new() { Name = "Atlas-api" }, new() { Name = "Atlas-test" }] },
                new() { Name = "Boreal", Directory = "/srv/boreal" }] });
            var window = new MainWindow(vm);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            try
            {
                var (atlas, api, test, boreal) = (vm.Tabs[0], vm.Tabs[1], vm.Tabs[2], vm.Tabs[3]);
                var names = vm.Tabs.Select(t => AutomationProperties.GetName(More(window, t))).ToList();
                var tips = vm.Tabs.Select(t => ToolTip.GetTip(More(window, t)) as string).Distinct().ToList();
                var classes = vm.Tabs.Select(t => string.Join(".", new[] { "grouped", "top", "bottom" }.Where(Segment(window, t).Classes.Contains))).ToList();
                var tint = window.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("segment-tint") && b.IsEffectivelyVisible)
                    .Select(b => b.DataContext).ToList();
                // Every name starts at the same x: no chevron or indent before the subtab's name.
                var nameX = vm.Tabs.Select(t =>
                {
                    var row = Row(window, t);
                    Visual name = t.IsSubTab
                        ? row.GetVisualDescendants().OfType<TextBox>().Single(b => b.Classes.Contains("name"))
                            .GetVisualDescendants().OfType<Avalonia.Controls.Presenters.TextPresenter>().Single()
                        : row.GetVisualDescendants().OfType<TextBlock>().First(b => b.Classes.Contains("name") && b.IsEffectivelyVisible);
                    return name.TranslatePoint(default, window)!.Value.X;
                }).ToList();
                var moreX = vm.Tabs.Select(t => Math.Round(More(window, t).TranslatePoint(default, window)!.Value.X)).Distinct().Count();

                // Keyboard: the focused ⋯ opens the menu with Enter, below the button.
                var more = More(window, boreal);
                Assert.True(more.Focus(NavigationMethod.Tab));
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                Dispatcher.UIThread.RunJobs();
                var menu = Assert.IsType<ContextMenu>(Row(window, boreal).ContextMenu);
                var opened = (menu.IsOpen, menu.PlacementTarget == more, menu.Placement);
                // The menu takes the focus: Down selects Create subtab and Enter runs it.
                window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                Dispatcher.UIThread.RunJobs();
                var created = vm.Tabs.Count(t => t.Parent == boreal);
                var afterClose = (menu.IsOpen, menu.PlacementTarget, menu.Placement);
                return (names, tips, classes, tint, nameX, moreX, opened, afterClose, created);
            }
            finally { window.Close(); }
        });
        Assert.Equal(["Fler val för Atlas", "Fler val för Atlas-api", "Fler val för Atlas-test", "Fler val för Boreal"], result.names);
        Assert.Equal(["Fler val"], result.tips);
        Assert.Equal(["grouped.top", "grouped", "grouped.bottom", ""], result.classes);
        Assert.Equal(3, result.tint.Count); // the group's three segments; Boreal has no card
        Assert.InRange(result.nameX.Max() - result.nameX.Min(), 0, 2); // the inline box's text sits on the project name's x
        Assert.Equal(1, result.moreX);
        Assert.Equal((true, true, Avalonia.Controls.PlacementMode.BottomEdgeAlignedRight), result.opened);
        Assert.Equal(1, result.created);
        Assert.Equal((false, null, Avalonia.Controls.PlacementMode.Pointer), result.afterClose);
    }

    [Fact]
    public void CreateSubtabFromTheMenuAddsASiblingAndFocusesItsSelectedName()
    {
        var result = UiSession.Run(() =>
        {
            Loc.Use("sv");
            var vm = new MainViewModel(new AppConfig { Tabs = [
                new() { Name = "Atlas", Directory = "/srv/atlas", SubTabs = [new() { Name = "Atlas-api" }] },
                new() { Name = "Boreal", Directory = "/srv/boreal" }] });
            var window = new MainWindow(vm);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            try
            {
                var (api, boreal) = (vm.Tabs[1], vm.Tabs[2]);
                MenuItem Create(TabViewModel tab)
                {
                    var menu = Row(window, tab).ContextMenu!;
                    menu.Open(Row(window, tab));
                    Dispatcher.UIThread.RunJobs();
                    return menu.Items.OfType<MenuItem>().First();
                }
                // From a subtab: a sibling in the same project, last in the group.
                Create(api).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                var sibling = vm.Tabs[2];
                var focused = window.FocusManager?.GetFocusedElement() as TextBox;
                var selection = (focused?.DataContext == sibling, focused?.SelectionStart, focused?.SelectionEnd, focused?.Text?.Length);
                // From a plain project: the first subtab turns the row into a card.
                Create(boreal).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                var borealSegment = Segment(window, boreal).Classes.Contains("grouped");
                return (vm.Tabs.Select(t => t.Parent?.Name ?? t.Name).ToList(), sibling.Name, selection, borealSegment);
            }
            finally { window.Close(); }
        });
        Assert.Equal(["Atlas", "Atlas", "Atlas", "Boreal", "Boreal"], result.Item1);
        Assert.Equal("Atlas · 2", result.Item2);
        Assert.True(result.selection.Item1);
        Assert.Equal(0, result.selection.SelectionStart);
        Assert.Equal(result.selection.Length, result.selection.SelectionEnd);
        Assert.True(result.borealSegment);
    }

    [Fact]
    public void RemovingASubtabIsInactiveWithItsReasonWhileTheTabIsOpen()
    {
        var result = UiSession.Run(() =>
        {
            Loc.Use("sv");
            var vm = new MainViewModel(new AppConfig { Tabs = [new() { Name = "Atlas", Directory = "/srv/atlas", SubTabs = [new() { Name = "Atlas-api" }] }] });
            var window = new MainWindow(vm);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            try
            {
                var api = vm.Tabs[1];
                var remove = Row(window, api).GetVisualDescendants().OfType<Button>().Single(b => b.Content is Avalonia.Controls.Shapes.Path p
                    && ReferenceEquals(p.Data, window.FindResource("CloseIcon")));
                var menu = Row(window, api).ContextMenu!;
                var item = menu.Items.OfType<MenuItem>().Last();
                (bool, string?, string?, bool, string?) State() => (remove.IsEnabled, ToolTip.GetTip(remove) as string, item.Header as string, item.IsEnabled, AutomationProperties.GetName(remove));
                menu.Open(Row(window, api));
                Dispatcher.UIThread.RunJobs();
                var closed = State();
                menu.Close();
                api.Track = new TabTrack("/tmp/none-lib", "/tmp/none", "track-1");
                api.SetState(TabState.Running, null);
                menu.Open(Row(window, api));
                Dispatcher.UIThread.RunJobs();
                var open = State();
                menu.Close();
                var projectMenu = Row(window, vm.Tabs[0]).ContextMenu!;
                projectMenu.Open(Row(window, vm.Tabs[0]));
                Dispatcher.UIThread.RunJobs();
                var projectHasRemove = projectMenu.Items.OfType<MenuItem>().Any(i => i.IsVisible && (i.Header as string)?.StartsWith("Ta bort") == true);
                projectMenu.Close();
                return (closed, open, ToolTip.GetShowOnDisabled(remove), ToolTip.GetShowOnDisabled(item), projectHasRemove);
            }
            finally { window.Close(); }
        });
        Assert.Equal((true, "Ta bort underfliken. Mappen påverkas inte.", "Ta bort underflik", true, "Ta bort underfliken Atlas-api"), result.closed);
        Assert.Equal((false, "Stäng terminalfliken först, sedan kan underfliken tas bort.", "Ta bort underflik (fliken är öppen)", false, "Ta bort underfliken Atlas-api"), result.open);
        Assert.True(result.Item3);
        Assert.True(result.Item4);
        Assert.False(result.projectHasRemove);
    }

    /// <summary>A dragged project lands before or after a whole group, so the drop line marks the card's edge; a subtab
    /// moves only within its own project (#196).</summary>
    [Fact]
    public void DropLineMarksTheGroupEdgeForAProjectAndStaysInsideTheCardForASubtab()
    {
        var result = UiSession.Run(() =>
        {
            var vm = new MainViewModel(new AppConfig { Tabs = [
                new() { Name = "Atlas", SubTabs = [new() { Name = "Atlas-api" }, new() { Name = "Atlas-test" }] },
                new() { Name = "Boreal" }] });
            var window = new MainWindow(vm);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            try
            {
                var (atlas, api, test, boreal) = (vm.Tabs[0], vm.Tabs[1], vm.Tabs[2], vm.Tabs[3]);
                string? Mark(TabViewModel? source, TabViewModel target, bool before) =>
                    window.DropMark(Row(window, target), source, target, before) is { } m
                        ? $"{(m.Border.Classes.Contains("segment") ? "segment" : "row")}:{((TabViewModel)m.Border.DataContext!).Name}:{(m.Before ? "before" : "after")}"
                        : null;
                return new[]
                {
                    Mark(boreal, api, before: false), Mark(boreal, api, before: true), Mark(atlas, boreal, before: false),
                    Mark(test, api, before: true), Mark(test, boreal, before: true), Mark(atlas, test, before: true), Mark(null, api, before: true),
                };
            }
            finally { window.Close(); }
        });
        Assert.Equal(new string?[]
        {
            "segment:Atlas-test:after", "segment:Atlas:before", "segment:Boreal:after",
            "row:Atlas-api:before", null, null, "row:Atlas-api:before",
        }, result);
    }
}
