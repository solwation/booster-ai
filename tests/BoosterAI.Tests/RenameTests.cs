using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BoosterAI;
using Xunit;

namespace BoosterAI.Tests;

/// <summary>Rename from the row menu or F2 (#258): saved like a Settings rename, validated, cancellable, usable by keyboard.</summary>
[Collection(ConfigFileCollection.Name)]
public sealed class RenameTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bs-rename-" + Guid.NewGuid().ToString("N"));
    private readonly string _language = Loc.Language;

    public RenameTests()
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

    private static MainViewModel Build() => new(new AppConfig { Tabs = [
        new() { Name = "Atlas", Directory = "/srv/atlas", SubTabs = [new() { Name = "Atlas-api" }, new() { Name = "Atlas-tests" }] },
        new() { Name = "Boreal", Directory = "/srv/boreal", SubTabs = [new() { Name = "Atlas-api" }] }] });

    private static List<string> SavedNames() => ConfigStore.TryRead()!.Tabs.SelectMany(t => t.SubTabs.Select(s => t.Name + "/" + s.Name).Prepend(t.Name)).ToList();

    [Fact]
    public void RenamingAProjectSavesItToTheConfigAndShowsItAtOnce()
    {
        var vm = Build();
        var atlas = vm.Tabs[0];
        atlas.BeginRename();
        Assert.True(atlas.IsRenaming);
        Assert.Equal("Atlas", atlas.RenameText);
        atlas.RenameText = "  Atlas 2  ";
        Assert.True(atlas.CommitRename());
        Assert.False(atlas.IsRenaming);
        Assert.Equal("Atlas 2", atlas.Name);
        Assert.Equal("Atlas 2", ConfigStore.TryRead()!.Tabs[0].Name);
        Assert.Equal("Fliken Atlas heter nu Atlas 2.", vm.Status);
        Assert.Equal("Underflik i Atlas 2", vm.Tabs[1].SubTabOfName);
    }

    [Fact]
    public void RenamingASubtabKeepsItsProjectAndOtherSubtabs()
    {
        var vm = Build();
        var api = vm.Tabs[1];
        api.BeginRename();
        api.RenameText = "Atlas-web";
        Assert.True(api.CommitRename());
        Assert.Equal(["Atlas", "Atlas/Atlas-web", "Atlas/Atlas-tests", "Boreal", "Boreal/Atlas-api"], SavedNames());
        Assert.Equal("Underfliken Atlas-api heter nu Atlas-web.", vm.Status);
    }

    [Theory]
    [InlineData("", "Namnet får inte vara tomt.")]
    [InlineData("   ", "Namnet får inte vara tomt.")]
    [InlineData("Boreal", "Det finns redan en flik som heter Boreal.")]
    [InlineData(" boreal ", "Det finns redan en flik som heter boreal.")]
    [InlineData("A;B", "A;B: fliknamnet får inte innehålla ;.")]
    public void InvalidProjectNamesKeepTheBoxOpenAndSaveNothing(string typed, string reason)
    {
        var vm = Build();
        var atlas = vm.Tabs[0];
        atlas.BeginRename();
        atlas.RenameText = typed;
        Assert.False(atlas.CommitRename());
        Assert.True(atlas.IsRenaming);
        Assert.Equal(reason, atlas.RenameError);
        Assert.Equal(reason, atlas.RenameHelp);
        Assert.Equal(reason, vm.Status);
        Assert.Equal("Atlas", atlas.Name);
        Assert.False(File.Exists(ConfigStore.PathOnDisk));
        atlas.RenameText = "Atlas 3"; // typing again clears the error
        Assert.False(atlas.HasRenameError);
    }

    [Fact]
    public void SubtabNamesOnlyClashWithinTheirProject()
    {
        var vm = Build();
        var tests = vm.Tabs[2];
        tests.BeginRename();
        tests.RenameText = "atlas-API";
        Assert.False(tests.CommitRename());
        Assert.Equal("Det finns redan en underflik som heter atlas-API i projektet.", tests.RenameError);
        // Boreal has its own Atlas-api, and a subtab may share a name with a project.
        tests.RenameText = "Boreal";
        Assert.True(tests.CommitRename());
    }

    [Fact]
    public void KeepingTheSameNameIsNotADuplicateAndSavesNothing()
    {
        var vm = Build();
        var atlas = vm.Tabs[0];
        atlas.BeginRename();
        Assert.True(atlas.CommitRename());
        Assert.Equal("Atlas", atlas.Name);
        Assert.False(File.Exists(ConfigStore.PathOnDisk));
        atlas.BeginRename();
        atlas.RenameText = "ATLAS"; // only the case changes: allowed
        Assert.True(atlas.CommitRename());
        Assert.Equal("ATLAS", atlas.Name);
    }

    [Fact]
    public void CancelLeavesTheNameAndTheConfigAlone()
    {
        var vm = Build();
        var atlas = vm.Tabs[0];
        atlas.BeginRename();
        atlas.RenameText = "Something else";
        atlas.EndRename();
        Assert.False(atlas.IsRenaming);
        Assert.Equal("Atlas", atlas.Name);
        Assert.False(File.Exists(ConfigStore.PathOnDisk));
    }

    [Fact]
    public void EnglishTextsAreUsed()
    {
        Loc.Use("en");
        var vm = Build();
        vm.Tabs[0].BeginRename();
        vm.Tabs[0].RenameText = "";
        Assert.False(vm.Tabs[0].CommitRename());
        Assert.Equal("The name must not be empty.", vm.Tabs[0].RenameError);
        Assert.Equal("New name for the tab Atlas", vm.Tabs[0].RenameBoxName);
        Assert.Equal("New name for the subtab Atlas-api", vm.Tabs[1].RenameBoxName);
    }

    [Fact]
    public void F2InATextBoxOnTheRowStartsNoRename()
    {
        var result = Run((window, vm) =>
        {
            var sub = vm.Tabs[1];
            var name = window.GetVisualDescendants().OfType<TextBox>().First(b => b.Classes.Contains("name") && !b.Classes.Contains("rename") && b.DataContext == sub);
            name.Focus(NavigationMethod.Tab);
            Press(window, Key.F2, PhysicalKey.F2);
            Dispatcher.UIThread.RunJobs();
            return (sub.IsRenaming, name.IsFocused);
        });
        Assert.Equal((false, true), result);
    }

    private static T Run<T>(Func<MainWindow, MainViewModel, T> work) => UiSession.Run(() =>
    {
        Loc.Use("sv");
        var vm = Build();
        var window = new MainWindow(vm);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try { return work(window, vm); }
        finally { window.Close(); }
    });

    private static void Press(MainWindow window, Key key, PhysicalKey physical)
    {
        window.KeyPress(key, RawInputModifiers.None, physical, null);
        Dispatcher.UIThread.RunJobs();
    }

    private static TextBox Box(MainWindow window, TabViewModel tab) =>
        window.GetVisualDescendants().OfType<TextBox>().Single(b => b.Classes.Contains("rename") && b.DataContext == tab);

    [Fact]
    public void F2OnARowOpensTheBoxFocusedAndEnterSavesEscCancels()
    {
        var result = Run((window, vm) =>
        {
            var atlas = vm.Tabs[0];
            var grip = window.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("grip") && b.DataContext == atlas);
            grip.Focus(NavigationMethod.Tab);
            Press(window, Key.F2, PhysicalKey.F2);
            Dispatcher.UIThread.RunJobs();
            var box = Box(window, atlas);
            var opened = (atlas.IsRenaming, box.IsEffectivelyVisible, box.IsFocused, box.SelectedText);
            Press(window, Key.Escape, PhysicalKey.Escape);
            var cancelled = (atlas.IsRenaming, atlas.Name);
            Press(window, Key.F2, PhysicalKey.F2);
            Dispatcher.UIThread.RunJobs();
            box = Box(window, atlas);
            box.Text = "Atlas Prime";
            Press(window, Key.Enter, PhysicalKey.Enter);
            return (opened, cancelled, atlas.IsRenaming, atlas.Name, ConfigStore.TryRead()?.Tabs[0].Name);
        });
        Assert.Equal((true, true, true, "Atlas"), result.opened);
        Assert.Equal((false, "Atlas"), result.cancelled);
        Assert.False(result.Item3);
        Assert.Equal("Atlas Prime", result.Item4);
        Assert.Equal("Atlas Prime", result.Item5);
    }

    [Fact]
    public void LeavingTheBoxSavesAValidNameAndDropsAnInvalidOne()
    {
        var result = Run((window, vm) =>
        {
            var atlas = vm.Tabs[0];
            var boreal = vm.Tabs[3];
            window.BeginRename(atlas);
            Dispatcher.UIThread.RunJobs();
            Box(window, atlas).Text = "Atlas Prime";
            window.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("grip") && b.DataContext == boreal).Focus(NavigationMethod.Tab);
            Dispatcher.UIThread.RunJobs();
            var saved = (atlas.IsRenaming, atlas.Name);
            window.BeginRename(boreal);
            Dispatcher.UIThread.RunJobs();
            Box(window, boreal).Text = "Atlas Prime"; // taken
            window.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("grip") && b.DataContext == atlas).Focus(NavigationMethod.Tab);
            Dispatcher.UIThread.RunJobs();
            return (saved, boreal.IsRenaming, boreal.Name);
        });
        Assert.Equal((false, "Atlas Prime"), result.saved);
        Assert.False(result.Item2);
        Assert.Equal("Boreal", result.Item3);
    }

    [Fact]
    public void TheRowMenuOffersRenameForProjectsAndSubtabsWithAnAccessibleBox()
    {
        var result = Run((window, vm) =>
        {
            var found = new List<(string?, string?)>();
            foreach (var tab in new[] { vm.Tabs[0], vm.Tabs[1] })
            {
                var row = window.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("row") && b.DataContext == tab);
                row.ContextMenu!.Open(row);
                Dispatcher.UIThread.RunJobs();
                var item = row.ContextMenu!.Items.OfType<MenuItem>().First(m => m.Header as string == "Byt namn …");
                item.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                found.Add((Avalonia.Automation.AutomationProperties.GetName(Box(window, tab)), Box(window, tab).Text));
                tab.EndRename();
                row.ContextMenu.Close();
            }
            return found;
        });
        Assert.Equal([("Nytt namn på fliken Atlas", "Atlas"), ("Nytt namn på underfliken Atlas-api", "Atlas-api")], result);
    }
}
