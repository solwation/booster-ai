using BoosterAI;
using Xunit;

public sealed class StatusPeekTests
{
    [Fact]
    public void AccessibleStatusActionKeepsFullExplanationAndUpdatesWithRenameAndRuntime()
    {
        var vm = new MainViewModel(new AppConfig { Tabs = [new() { Name = "Atlas", Directory = "/sample/atlas" }] });
        var tab = vm.Tabs[0];
        var changed = new List<string?>();
        tab.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        tab.SetState(TabState.Running, null, activity: Activity.Watching, watchingSince: DateTimeOffset.UtcNow.AddMinutes(-12).ToUnixTimeMilliseconds());
        Assert.Contains("Atlas", tab.StatusActionName);
        Assert.Contains(tab.StatusTip, tab.StatusActionName);
        Assert.NotEqual(tab.StatusText, tab.StatusTip);
        Assert.Contains(nameof(TabViewModel.StatusActionName), changed);

        changed.Clear();
        tab.Name = "Harbor";
        Assert.Contains("Harbor", tab.StatusActionName);
        Assert.DoesNotContain("Atlas", tab.StatusActionName);
        Assert.Contains(nameof(TabViewModel.StatusActionName), changed);

        changed.Clear();
        tab.SetState(TabState.Closed, null);
        Assert.Contains(tab.StatusTip, tab.StatusActionName);
        Assert.Contains(nameof(TabViewModel.StatusActionName), changed);
    }

    [Fact]
    public void CloseProgressIsReflectedInAccessibleStatusAction()
    {
        var vm = new MainViewModel(new AppConfig { Tabs = [new() { Name = "Atlas" }] });
        var tab = vm.Tabs[0];
        tab.SetState(TabState.Running, null);
        tab.SetCloseProgress("Saving synthetic work");
        Assert.Contains("Saving synthetic work", tab.StatusActionName);
        tab.SetCloseProgress(null);
        Assert.DoesNotContain("Saving synthetic work", tab.StatusActionName);
    }
}
