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

/// <summary>
/// A click on a live row shows its terminal tab (#253): a free part of the row (name, background) does what the removed
/// terminal icon did; the row's controls keep their own clicks; a drag or a right-click is no click; Enter on the focused
/// row and the row menu's Show in terminal reach the same path. A row without a terminal tab does nothing.
/// </summary>
[Collection(ConfigFileCollection.Name)]
public sealed class RowClickTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bs-rowclick-" + Guid.NewGuid().ToString("N"));
    private readonly string _language = Loc.Language;
    private readonly bool _supported = TerminalFocus.Supported;

    public RowClickTests()
    {
        Directory.CreateDirectory(_root);
        ConfigStore.UseFile(Path.Combine(_root, "config.json"));
        Loc.Use("sv");
        TerminalFocus.Supported = true;
    }

    public void Dispose()
    {
        Loc.Use(_language);
        TerminalFocus.Supported = _supported;
        Directory.Delete(_root, recursive: true);
    }

    /// <summary>Atlas runs (it has a terminal tab), Boreal was never started. No session: the real command is never built.</summary>
    private static MainViewModel Build()
    {
        var vm = new MainViewModel(new AppConfig { Tabs = [
            new() { Name = "Atlas", Directory = "/srv/atlas", Enabled = true, SubTabs = [new() { Name = "Atlas-api" }] },
            new() { Name = "Boreal", Directory = "/srv/boreal", Enabled = true }] });
        vm.Tabs[0].SetState(TabState.Running, null);
        return vm;
    }

    private static Border Row(Window window, TabViewModel tab) =>
        window.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("row") && b.DataContext == tab);

    private static Point Center(Visual v, Window window) => v.TranslatePoint(new Point(v.Bounds.Width / 2, v.Bounds.Height / 2), window)!.Value;

    private static void Click(Window window, Point at, MouseButton button = MouseButton.Left)
    {
        window.MouseMove(at);
        window.MouseDown(at, button);
        window.MouseUp(at, button);
        Dispatcher.UIThread.RunJobs();
    }

    private static T Run<T>(Func<MainWindow, MainViewModel, List<TabViewModel>, T> work) => UiSession.Run(() =>
    {
        Loc.Use("sv");
        var vm = Build();
        var window = new MainWindow(vm);
        var shown = new List<TabViewModel>();
        window.TerminalShown += shown.Add;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try { return work(window, vm, shown); }
        finally { window.Close(); }
    });

    [Fact]
    public void ClickOnTheNameOrBackgroundOfALiveRowShowsItsTerminalTab()
    {
        var (byName, byBackground, cursor, cursorOff) = Run((window, vm, shown) =>
        {
            var atlas = vm.Tabs[0];
            var row = Row(window, atlas);
            var name = row.GetVisualDescendants().OfType<TextBlock>().First(t => t.Classes.Contains("name") && t.IsEffectivelyVisible);
            Click(window, Center(name, window));
            var n = shown.Count(t => t == atlas);
            // The row's top padding, above the name: plain background.
            var top = row.TranslatePoint(new Point(name.TranslatePoint(default, row)!.Value.X + 4, 3), window)!.Value;
            Click(window, top);
            return (n, shown.Count(t => t == atlas), row.Cursor?.ToString(), Row(window, vm.Tabs[^1]).Classes.Contains("show-terminal"));
        });
        Assert.Equal(1, byName);
        Assert.Equal(2, byBackground);
        Assert.NotNull(cursor); // the hand: the live row says it can be clicked
        Assert.False(cursorOff); // a never-started row has no hand
    }

    [Fact]
    public void ControlsInTheRowKeepTheirClicks()
    {
        var (clicked, shown) = Run((window, vm, shown) =>
        {
            var row = Row(window, vm.Tabs[0]);
            var clicked = new List<string>();
            foreach (var c in row.GetVisualDescendants().OfType<Control>()
                .Where(c => c.IsEffectivelyVisible && c.Bounds.Width > 0 && c is CheckBox or StatusPeekButton or ComboBox
                    // Of the icon buttons only ⋯: folder, ↻ and Start would open a file manager or a terminal.
                    || c is Button b && b.IsEffectivelyVisible && b.Content is Avalonia.Controls.Shapes.Path p
                        && ReferenceEquals(p.Data, window.FindResource("MoreIcon"))).ToList())
            {
                Click(window, Center(c, window));
                clicked.Add(c.GetType().Name);
                // Close what the click opened (a list's dropdown, the status popup, the row menu) before the next one.
                foreach (var box in row.GetVisualDescendants().OfType<ComboBox>()) box.IsDropDownOpen = false;
                if (row.ContextMenu is { IsOpen: true } menu) menu.Close();
                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                Dispatcher.UIThread.RunJobs();
            }
            return (clicked, shown.Count);
        });
        Assert.Contains("CheckBox", clicked);
        Assert.Contains("StatusPeekButton", clicked);
        Assert.Contains("ComboBox", clicked);
        Assert.Contains("Button", clicked); // ⋯
        Assert.Equal(0, shown);
    }

    /// <summary>Every control kind in a row, visible or not, is outside the free area; the name and the row itself are in it.</summary>
    [Fact]
    public void FreeAreaExcludesEveryControlKindAndPopups()
    {
        var result = Run((window, vm, _) =>
        {
            var project = Row(window, vm.Tabs[0]);
            var sub = Row(window, vm.Tabs[1]);
            bool Free(Border row, Func<Control, bool> pick) => row.GetVisualDescendants().OfType<Control>().Where(pick).All(c => MainWindow.IsFreeRowArea(row, c));
            bool NoneFree(Border row, Func<Control, bool> pick)
            {
                var found = row.GetVisualDescendants().OfType<Control>().Where(pick).ToList();
                return found.Count > 0 && found.All(c => !MainWindow.IsFreeRowArea(row, c));
            }
            return new Dictionary<string, bool>
            {
                ["row"] = MainWindow.IsFreeRowArea(project, project),
                ["name"] = Free(project, c => c is TextBlock t && t.Classes.Contains("name")),
                ["grip"] = NoneFree(project, c => c.Classes.Contains("grip")),
                ["gripPath"] = NoneFree(project, c => c is Avalonia.Controls.Shapes.Path && c.GetVisualParent() is StyledElement p && p.Classes.Contains("grip")),
                ["badge"] = NoneFree(project, c => c.Classes.Contains("question-badge")),
                ["status"] = NoneFree(project, c => c is StatusPeekButton),
                ["statusDot"] = NoneFree(project, c => c is StatusDotView),
                ["agentStatus"] = NoneFree(project, c => c.Classes.Contains("agent-status")),
                ["meter"] = NoneFree(project, c => c is Panel && c.Classes.Contains("limit-meter")),
                ["combo"] = NoneFree(project, c => c is ComboBox),
                ["check"] = NoneFree(project, c => c is CheckBox),
                ["buttons"] = NoneFree(project, c => c is Button),
                ["subName"] = NoneFree(sub, c => c is TextBox t && t.Classes.Contains("name")),
                ["otherRow"] = !MainWindow.IsFreeRowArea(project, sub),
                ["popup"] = !MainWindow.IsFreeRowArea(project, new TextBlock()),
            };
        });
        Assert.All(result, kv => Assert.True(kv.Value, kv.Key));
    }

    [Fact]
    public void DragRightClickAndRowsWithoutATerminalTabDoNothing()
    {
        var (afterDrag, afterRight, onBoreal, menuOpen) = Run((window, vm, shown) =>
        {
            var row = Row(window, vm.Tabs[0]);
            var name = Center(row.GetVisualDescendants().OfType<TextBlock>().First(t => t.Classes.Contains("name") && t.IsEffectivelyVisible), window);
            window.MouseMove(name);
            window.MouseDown(name, MouseButton.Left);
            window.MouseMove(name + new Point(30, 12));
            window.MouseUp(name + new Point(30, 12), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            var drag = shown.Count;
            Click(window, name, MouseButton.Right);
            var open = row.ContextMenu!.IsOpen;
            row.ContextMenu.Close();
            Dispatcher.UIThread.RunJobs();
            var right = shown.Count;
            var boreal = Row(window, vm.Tabs[^1]);
            Click(window, Center(boreal.GetVisualDescendants().OfType<TextBlock>().First(t => t.Classes.Contains("name") && t.IsEffectivelyVisible), window));
            return (drag, right, shown.Count, open);
        });
        Assert.Equal(0, afterDrag);
        Assert.Equal(0, afterRight);
        Assert.True(menuOpen); // the right-click still opens the row menu
        Assert.Equal(0, onBoreal);
    }

    [Fact]
    public void EnterOnTheFocusedRowAndTheRowMenuShowTheTerminalTab()
    {
        var r = Run((window, vm, shown) =>
        {
            var atlas = vm.Tabs[0];
            Assert.True(window.FocusGrip(atlas));
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Dispatcher.UIThread.RunJobs();
            var byEnter = shown.Count(t => t == atlas);
            // Alt+Down still moves the row (#156).
            window.KeyPress(Key.Down, RawInputModifiers.Alt, PhysicalKey.ArrowDown, null);
            Dispatcher.UIThread.RunJobs();
            var moved = vm.Tabs.Where(t => t.IsProject).Select(t => t.Name).ToList();

            var help = AutomationProperties.GetHelpText(window.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("grip") && b.DataContext == atlas));
            var borealHelp = AutomationProperties.GetHelpText(window.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("grip") && b.DataContext == vm.Tabs.Single(t => t.Name == "Boreal")));

            MenuItem Show(TabViewModel tab)
            {
                var menu = Row(window, tab).ContextMenu!;
                menu.Open(Row(window, tab));
                Dispatcher.UIThread.RunJobs();
                return menu.Items.OfType<MenuItem>().Single(i => i.Header as string == "Visa i terminalen");
            }
            var item = Show(atlas);
            var enabled = (item.IsVisible, item.IsEnabled);
            item.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
            Row(window, atlas).ContextMenu!.Close();
            var other = Show(vm.Tabs.Single(t => t.Name == "Boreal"));
            var otherEnabled = other.IsEnabled;
            Row(window, vm.Tabs.Single(t => t.Name == "Boreal")).ContextMenu!.Close();
            Dispatcher.UIThread.RunJobs();
            return (byEnter, moved, help, borealHelp, enabled, byMenu: shown.Count(t => t == atlas), otherEnabled);
        });
        Assert.Equal(1, r.byEnter);
        Assert.Equal(["Boreal", "Atlas"], r.moved);
        Assert.Equal("Enter visar Atlas i terminalen", r.help);
        Assert.Null(r.borealHelp);
        Assert.Equal((true, true), r.enabled);
        Assert.Equal(2, r.byMenu);
        Assert.False(r.otherEnabled);
    }

    [Fact]
    public void TheTerminalIconIsGoneAndTextsExistInBothLanguages()
    {
        var axaml = File.ReadAllText(Path.Combine(RepoRoot(), "src", "BoosterAI", "MainWindow.axaml"));
        Assert.DoesNotContain("ShowTerminalIcon", axaml);
        Assert.Equal("Visa i terminalen", Loc.Table("sv")["row.menu.show"]);
        Assert.Equal("Show in terminal", Loc.Table("en")["row.menu.show"]);
        Assert.Equal("Enter shows {0} in the terminal", Loc.Table("en")["tab.show.help"]);
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "BoosterAI.slnx"))) return dir.FullName;
        throw new InvalidOperationException("Repository root not found.");
    }
}
