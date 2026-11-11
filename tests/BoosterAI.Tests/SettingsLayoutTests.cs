using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BoosterAI;
using Xunit;

namespace BoosterAI.Tests;

/// <summary>
/// The settings page template and placement of content (#157): every tab starts with a title at the same y, Minne och
/// kontext and AI-agenter share one side navigation with their content at the same x, auto-clear lives in Kontext beside
/// auto-compact and the Find projects base folder on Projekt.
/// </summary>
[Collection(ConfigFileCollection.Name)]
public class SettingsLayoutTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bs-settings-" + Guid.NewGuid().ToString("N"));

    public SettingsLayoutTests()
    {
        Directory.CreateDirectory(_root);
        ConfigStore.UseFile(Path.Combine(_root, "config.json"));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static MainViewModel Build(int projects = 2) =>
        new(new AppConfig
        {
            Tabs = Enumerable.Range(0, projects).Select(i => new TabConfig { Name = "P" + i, Directory = "/srv/p" + i, Enabled = true }).ToList(),
        });

    private static T WithWindow<T>(MainViewModel vm, Func<SettingsWindow, T> work, double width = 920, double height = 700)
    {
        return UiSession.Run(() =>
        {
            Loc.Use("sv");
            var window = new SettingsWindow(vm) { Width = width, Height = height };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            try { return work(window); }
            finally { window.Close(); }
        });
    }

    private static void Settle(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    private static IEnumerable<T> Shown<T>(Window window) where T : Visual =>
        window.GetVisualDescendants().OfType<T>().Where(c => c.IsEffectivelyVisible && c.Bounds.Height > 0);

    private static Point At(Visual control, Window window) => control.TranslatePoint(default, window) ?? throw new InvalidOperationException();

    /// <summary>The page title of the selected tab (and side-navigation page).</summary>
    private static TextBlock Title(Window window) =>
        Shown<TextBlock>(window).Where(t => t.Classes.Contains("title")).OrderBy(t => At(t, window).Y).First();

    [Fact]
    public void AllFiveTabsStartWithATitleAtTheSameY()
    {
        var titles = WithWindow(Build(), w =>
        {
            var found = new List<(string Text, double Y)>();
            for (var tab = 0; tab < 5; tab++)
            {
                w.SelectedTabIndex = tab;
                Settle(w);
                var title = Title(w);
                found.Add((title.Text ?? "", At(title, w).Y));
            }
            return found;
        });
        Assert.Equal(["Projekt", "Allmänt", "RAM i WSL", "Utseende", "Dina AI-agenter"], titles.Select(t => t.Text));
        Assert.All(titles, t => Assert.Equal(titles[0].Y, t.Y, 0.5));
    }

    [Fact]
    public void MemoryAndAgentsShareOneSideNavigationAndTheirContentStartsAtTheSameX()
    {
        var (memory, agents) = WithWindow(Build(), w =>
        {
            (double ColumnX, double ColumnWidth, double ItemHeight, double TitleX, FontWeight Chosen, FontWeight Other) Measure(int tab)
            {
                w.SelectedTabIndex = tab;
                Settle(w);
                var items = Shown<ContentControl>(w).Where(c => c is ListBoxItem || c.Classes.Contains("sidenav")).ToList();
                var nav = items.Select(i => i.FindAncestorOfType<Grid>()!).First(g => g.ColumnDefinitions.Count == 2);
                var chosen = items.Single(i => i is ListBoxItem { IsSelected: true } || i.Classes.Contains("chosen"));
                Assert.All(items, i => Assert.Equal(36, i.Bounds.Height, 0.5));
                Assert.All(items, i => Assert.Equal(13, i.FontSize));
                return (At(nav, w).X, nav.ColumnDefinitions[0].ActualWidth, items[0].Bounds.Height, At(Title(w), w).X,
                    chosen.FontWeight, items.First(i => i != chosen).FontWeight);
            }
            return (Measure(2), Measure(4));
        });
        Assert.Equal(168, memory.ColumnWidth, 0.5);
        Assert.Equal(memory.ColumnWidth, agents.ColumnWidth, 0.5);
        Assert.Equal(memory.ColumnX, agents.ColumnX, 0.5);
        Assert.Equal(memory.TitleX, agents.TitleX, 0.5);
        Assert.Equal(FontWeight.SemiBold, memory.Chosen);
        Assert.Equal(FontWeight.SemiBold, agents.Chosen);
        Assert.Equal(FontWeight.Medium, memory.Other);
        Assert.Equal(FontWeight.Medium, agents.Other);
    }

    [Fact]
    public void BothSideNavigationsShowKeyboardFocusWithoutChangingLayout()
    {
        WithWindow(Build(), w =>
        {
            foreach (var tab in new[] { 2, 4 })
            {
                w.SelectedTabIndex = tab;
                Settle(w);
                var nav = Shown<ContentControl>(w).First(c => c is ListBoxItem || c is Button && c.Classes.Contains("sidenav"));
                var before = nav.Bounds;
                Assert.True(nav.Focus(NavigationMethod.Tab));
                Settle(w);
                Assert.True(nav.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Focus").IsVisible);
                Assert.Equal(before, nav.Bounds);
            }
            return 0;
        });
    }

    [Fact]
    public void AutoClearAndTheBaseFolderMovedOutOfGeneral()
    {
        var where = WithWindow(Build(), w =>
        {
            string Tabs(Func<Window, bool> present)
            {
                var hits = new List<string>();
                for (var tab = 0; tab < 5; tab++)
                {
                    w.SelectedTabIndex = tab;
                    for (var section = 0; section < (tab == 2 ? 4 : 1); section++)
                    {
                        if (tab == 2) w.SelectedOptimizationSectionIndex = section;
                        Settle(w);
                        if (present(w)) hits.Add(tab == 2 ? $"{tab}.{section}" : $"{tab}");
                    }
                }
                return string.Join(",", hits);
            }
            var context = Tabs(x => Shown<ComboBox>(x).Any(c => c.Name == "ContextMode"));
            var baseFolder = Tabs(x => Shown<TextBox>(x).Any(t => AutomationProperties.GetName(t) == Loc.Get("settings.findRoot.name")));
            return (context, baseFolder);
        });
        Assert.Equal("2.3", where.context);   // Memory and context → Context
        Assert.Equal("0", where.baseFolder);    // Projekt, next to Hitta projekt
        Assert.Equal("Minne och kontext", Loc.Table("sv")["settings.tab.optimization"]);
        Assert.Equal("Memory and context", Loc.Table("en")["settings.tab.optimization"]);
        Assert.Equal("Kontext", Loc.Table("sv")["settings.optimization.nav.context"]);
    }

    [Fact]
    public void ContextChoicesRoundTripAndKeepSavedManualBudgets()
    {
        var savedBudget = new AutoCompactSetting { Cli = "claude", Model = "example-model", Mode = "custom", CustomTokens = 123000 };
        ConfigStore.Save(new AppConfig { AutoCompactMode = AutoCompactPolicy.Manual, AutoCompactSettings = [savedBudget],
            Tabs = [new TabConfig { Name = "Alpha", Directory = "/srv/alpha" }] });
        var vm = new MainViewModel(ConfigStore.Load());
        WithWindow(vm, w =>
        {
            w.SelectedTabIndex = 2;
            w.SelectedOptimizationSectionIndex = 3;
            Settle(w);
            var mode = w.FindControl<ComboBox>("ContextMode")!;
            Assert.Equal(["Av", "Rensa vid inaktivitet", "Sammanfatta"], vm.ContextModes.Select(c => c.Label));
            Assert.Equal("summary", ((CompactChoice)mode.SelectedItem!).Id);
            mode.SelectedItem = vm.ContextModes.Single(c => c.Id == "clear");
            Settle(w);
            var clear = w.FindControl<StackPanel>("ContextClearSection")!;
            Assert.True(clear.IsEffectivelyVisible);
            clear.GetVisualDescendants().OfType<NumericUpDown>().Single().Value = 12;
            Assert.Equal(AutoCompactPolicy.Off, vm.SelectedCompactMode.Id);
            return 0;
        });
        var clearSaved = ConfigStore.Load();
        Assert.True(clearSaved.AutoClearIdle);
        Assert.Equal(12, clearSaved.AutoClearMinutes);
        Assert.Equal(AutoCompactPolicy.Off, clearSaved.AutoCompactMode);
        Assert.Equal(savedBudget, Assert.Single(clearSaved.AutoCompactSettings));

        // Toggling back to summary in this editor restores its previous Manual refinement.
        vm.SelectedContextMode = vm.ContextModes.Single(c => c.Id == "summary");
        vm.Save();
        Assert.False(ConfigStore.Load().AutoClearIdle);
        Assert.Equal(AutoCompactPolicy.Manual, ConfigStore.Load().AutoCompactMode);
        vm.SelectedContextMode = vm.ContextModes.Single(c => c.Id == "off");
        vm.Save();
        var off = ConfigStore.Load();
        Assert.False(off.AutoClearIdle);
        Assert.Equal(AutoCompactPolicy.Off, off.AutoCompactMode);
        Assert.Equal(savedBudget, Assert.Single(off.AutoCompactSettings));
    }

    [Fact]
    public void ExistingClearAndSummaryCombinationSurvivesOpeningAndSavingUntilAnExplicitChoice()
    {
        var config = new AppConfig { AutoClearIdle = true, AutoClearMinutes = 17, AutoCompactMode = AutoCompactPolicy.Manual };
        ConfigStore.Save(config);
        var vm = new MainViewModel(ConfigStore.Load());
        WithWindow(vm, w =>
        {
            w.SelectedTabIndex = 2;
            w.SelectedOptimizationSectionIndex = 3;
            Settle(w);
            Assert.True(vm.ContextHasLegacyCombination);
            Assert.Null(w.FindControl<ComboBox>("ContextMode")!.SelectedItem);
            Assert.Contains(Shown<TextBlock>(w), t => t.Text == Loc.Get("settings.context.legacy"));
            return 0;
        });
        Assert.True(ConfigStore.Load().AutoClearIdle);
        Assert.Equal(17, ConfigStore.Load().AutoClearMinutes);
        Assert.Equal(AutoCompactPolicy.Manual, ConfigStore.Load().AutoCompactMode);
        vm.SelectedContextMode = vm.ContextModes.Single(c => c.Id == "summary");
        vm.Save();
        Assert.False(vm.ContextHasLegacyCombination);
        Assert.False(ConfigStore.Load().AutoClearIdle);
        Assert.Equal(AutoCompactPolicy.Manual, ConfigStore.Load().AutoCompactMode);
    }

    [Fact]
    public void AtTheMinimumSizeThreeProjectRowsShowWithTheButtonsBelowInTheScrollArea()
    {
        var (visibleRows, buttonsInScroll) = WithWindow(Build(projects: 5), w =>
        {
            w.SelectedTabIndex = 0;
            Settle(w);
            var list = w.FindControl<Panel>("ProjectList")!;
            var scroll = list.FindAncestorOfType<ScrollViewer>()!;
            var top = At(scroll, w).Y;
            var bottom = top + scroll.Viewport.Height;
            var rows = list.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("row")).ToList();
            var visible = rows.Count(r => At(r, w).Y >= top - 0.5 && At(r, w).Y + r.Bounds.Height <= bottom + 0.5);
            var find = w.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, Loc.Get("import.button")));
            return (visible, find.FindAncestorOfType<ScrollViewer>() == scroll);
        }, width: 640, height: 400);
        Assert.True(visibleRows >= 3, $"{visibleRows} project rows fully visible at 640x400");
        Assert.True(buttonsInScroll);
    }
}
