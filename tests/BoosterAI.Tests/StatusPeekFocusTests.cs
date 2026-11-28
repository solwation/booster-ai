using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BoosterAI;
using Xunit;

namespace BoosterAI.Tests;

/// <summary>The pinned status popup (#290) must not frame its whole content with the focus adorner (a white/black box).</summary>
[Collection(ConfigFileCollection.Name)]
public sealed class StatusPeekFocusTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bs-peekfocus-" + Guid.NewGuid().ToString("N"));
    private readonly string _language = Loc.Language;

    public StatusPeekFocusTests()
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

    [Fact]
    public void PinnedPopupHasNoFocusFrameAroundItsContentAndEscapeRestoresFocus()
    {
        var (adorner, focusedType, restored, open) = UiSession.Run(() =>
        {
            var vm = new MainViewModel(new AppConfig { Tabs = [new() { Name = "Atlas", Directory = "/srv/atlas", Enabled = true }] });
            vm.Tabs[0].SetState(TabState.Running, null);
            var window = new MainWindow(vm);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            try
            {
                var target = window.GetVisualDescendants().OfType<StatusPeekButton>().First(b => b.DataContext == vm.Tabs[0]);
                window.OpenStatusPeekForScreenshot(target, vm.Tabs[0]);
                Dispatcher.UIThread.RunJobs();
                var view = window.GetVisualDescendants().OfType<StatusPeekView>().First();
                var scroll = view.GetVisualDescendants().OfType<ScrollViewer>().First(s => s.Name == "OutputScroll");
                var focused = TopLevel.GetTopLevel(view)!.FocusManager!.GetFocusedElement();
                view.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
                Dispatcher.UIThread.RunJobs();
                return (scroll.FocusAdorner, focused?.GetType().Name, TopLevel.GetTopLevel(target)!.FocusManager!.GetFocusedElement() == target,
                    window.GetVisualDescendants().OfType<StatusPeekView>().Any());
            }
            finally { window.Close(); }
        });
        Assert.Null(adorner);
        Assert.Equal("ScrollViewer", focusedType);
        Assert.True(restored);
        Assert.False(open);
    }
}
