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
                    (link.Content as TextBlock)?.Text, link.Focusable && link.IsEffectivelyVisible, vm.Update.MoreUrl);
            }
            finally { window.Close(); }
        });
        Assert.StartsWith(namePrefix, name);
        Assert.Equal(Loc.Table(language)["version.tip"], tip);
        Assert.Equal("v" + UpdateLogic.Current, text);
        Assert.True(focusable);
        // Without a known newer release, "Show more" and the version both lead to what is new in the running version (#245).
        Assert.Equal(UpdateLogic.ReleaseNotesUrl(null, UpdateLogic.Current, language), more);
    }
}
