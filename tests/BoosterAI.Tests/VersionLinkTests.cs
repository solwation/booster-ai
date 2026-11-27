using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BoosterAI;
using Xunit;

namespace BoosterAI.Tests;

/// <summary>#226: the version in the bottom-left corner is a link to the same page as the update banner's "Show more".</summary>
[Collection(ConfigFileCollection.Name)]
public sealed class VersionLinkTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bs-versionlink-" + Guid.NewGuid().ToString("N"));
    private readonly string _language = Loc.Language;

    public VersionLinkTests()
    {
        Directory.CreateDirectory(_root);
        ConfigStore.UseFile(Path.Combine(_root, "config.json"));
    }

    public void Dispose()
    {
        Loc.Use(_language);
        Directory.Delete(_root, recursive: true);
    }

    [Theory]
    [InlineData("sv", "Visa ändringar i BoosterAI v")]
    [InlineData("en", "Show changes in BoosterAI v")]
    public void TheVersionIsANamedFocusableLinkToTheChanges(string language, string namePrefix)
    {
        var (name, tip, text, focusable, more) = UiSession.Run(() =>
        {
            Loc.Use(language);
            var vm = new MainViewModel(new AppConfig { Language = language });
            var window = new MainWindow(vm);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            try
            {
                var link = window.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("version"));
                return (AutomationProperties.GetName(link), ToolTip.GetTip(link) as string,
                    (link.Content as TextBlock)?.Text, link.Focusable && link.IsEffectivelyVisible, (vm.Update.MoreUrl, vm.Update.MoreRange));
            }
            finally { window.Close(); }
        });
        Assert.StartsWith(namePrefix, name);
        Assert.Equal(Loc.Table(language)["version.tip"], tip);
        Assert.Equal("v" + UpdateLogic.Current, text);
        Assert.True(focusable);
        // Without a known newer release, "Show more" and the version both lead to what is new in the running version (#245).
        Assert.Equal(UpdateLogic.ReleaseNotesUrl(null, UpdateLogic.Current, language), more.MoreUrl);
        // #252: they open What's new inside BoosterAI for the same range.
        Assert.Equal(UpdateLogic.Range(null, UpdateLogic.Current), more.MoreRange);
    }

    [Fact]
    public void TheVersionLinkOpensWhatsNewInsideBoosterAIAndReusesIt()
    {
        var (opened, range, sameWindow, owner) = UiSession.Run(() =>
        {
            var before = MainWindow.NewsSource;
            MainWindow.NewsSource = () => new OfflineReleaseNotesSource(ReleaseNotes.EmbeddedJson, null);
            var vm = new MainViewModel(new AppConfig { UpdatedFromVersion = "0.0.1" });
            var window = new MainWindow(vm);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            try
            {
                var link = window.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("version"));
                link.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                var news = window.News;
                link.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                var result = (news is { IsVisible: true }, news?.ViewModel.Range, ReferenceEquals(news, window.News), news?.Owner == window);
                news?.Close();
                return result;
            }
            finally
            {
                window.Close();
                MainWindow.NewsSource = before;
            }
        });
        Assert.True(opened);
        Assert.True(sameWindow);
        Assert.True(owner);
        Assert.Equal(UpdateLogic.Range(AppVersion.Parse("0.0.1"), UpdateLogic.Current), range);
    }
}
