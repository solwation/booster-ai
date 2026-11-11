using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
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
            var autoClear = Tabs(x => Shown<CheckBox>(x).Any(c => Equals(c.Content, Loc.Get("settings.autoClear"))));
            var baseFolder = Tabs(x => Shown<TextBox>(x).Any(t => AutomationProperties.GetName(t) == Loc.Get("settings.findRoot.name")));
            var compact = Tabs(x => Shown<ComboBox>(x).Any(c => c.Name == "CompactGlobalMode"));
            return (autoClear, baseFolder, compact);
        });
        Assert.Equal("2.3", where.autoClear);   // Minne och kontext → Kontext
        Assert.Equal("2.3", where.compact);     // beside auto-compact
        Assert.Equal("0", where.baseFolder);    // Projekt, next to Hitta projekt
        Assert.Equal("Minne och kontext", Loc.Table("sv")["settings.tab.optimization"]);
        Assert.Equal("Memory and context", Loc.Table("en")["settings.tab.optimization"]);
        Assert.Equal("Kontext", Loc.Table("sv")["settings.optimization.nav.context"]);
    }

    [Fact]
    public void TheKontextControlsRoundTripToTheExistingSettings()
    {
        ConfigStore.Save(new AppConfig { Tabs = [new TabConfig { Name = "Alpha", Directory = "/srv/alpha" }] });
        var vm = new MainViewModel(ConfigStore.Load());
        WithWindow(vm, w =>
        {
            w.SelectedTabIndex = 2;
            w.SelectedOptimizationSectionIndex = 3;
            Settle(w);
            var clear = w.FindControl<StackPanel>("ContextClearSection")!;
            var check = clear.GetVisualDescendants().OfType<CheckBox>().Single();
            var minutes = clear.GetVisualDescendants().OfType<NumericUpDown>().Single();
            var compact = w.FindControl<ComboBox>("CompactGlobalMode")!;
            Assert.True(check.IsEffectivelyVisible && compact.IsEffectivelyVisible);

            // Off in both: the CLI defaults.
            Assert.False(check.IsChecked);
            Assert.Equal(AutoCompactPolicy.Off, ((CompactChoice)compact.SelectedItem!).Id);

            // Clear when idle and summarize can both be on: the saved combination the controls keep.
            check.IsChecked = true;
            minutes.Value = 12;
            compact.SelectedItem = vm.CompactGlobalModes.Single(c => c.Id == AutoCompactPolicy.Auto);
            Settle(w);
            return 0;
        });
        vm.Save();
        var saved = ConfigStore.Load();
        Assert.True(saved.AutoClearIdle);
        Assert.Equal(12, saved.AutoClearMinutes);
        Assert.Equal(AutoCompactPolicy.Auto, saved.AutoCompactMode);

        // And back: a fresh window over the saved file shows the same choices.
        var reread = WithWindow(new MainViewModel(ConfigStore.Load()), w =>
        {
            w.SelectedTabIndex = 2;
            w.SelectedOptimizationSectionIndex = 3;
            Settle(w);
            var clear = w.FindControl<StackPanel>("ContextClearSection")!;
            return (clear.GetVisualDescendants().OfType<CheckBox>().Single().IsChecked,
                clear.GetVisualDescendants().OfType<NumericUpDown>().Single().Value,
                ((CompactChoice)w.FindControl<ComboBox>("CompactGlobalMode")!.SelectedItem!).Id);
        });
        Assert.Equal((true, 12m, AutoCompactPolicy.Auto), reread);
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
