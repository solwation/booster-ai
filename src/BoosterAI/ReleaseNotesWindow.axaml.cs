using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace BoosterAI;

/// <summary>
/// What's new in BoosterAI (#252): opened by "Show more" in the update bar and the version link, with the same
/// from/to as the release page. Links (issues, docs, screenshots, "Open in browser") open in the browser.
/// </summary>
public partial class ReleaseNotesWindow : Window
{
    private readonly ReleaseNotesViewModel _vm;
    private readonly CancellationTokenSource _closing = new();

    public ReleaseNotesWindow() : this(new ReleaseNotesViewModel(new OfflineReleaseNotesSource(ReleaseNotes.EmbeddedJson, null), ReleaseRange.All)) { }

    public ReleaseNotesWindow(ReleaseNotesViewModel vm)
    {
        _vm = vm;
        DataContext = vm;
        InitializeComponent();
        Closed += (_, _) => _closing.Cancel();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        };
    }

    public ReleaseNotesViewModel ViewModel => _vm;

    /// <summary>Loads the screenshots from what is at hand, then asks Pages for newer data and loads any new screenshots.</summary>
    public async Task LoadAsync()
    {
        try
        {
            var refresh = _vm.RefreshAsync(_closing.Token);
            await _vm.LoadImagesAsync(_closing.Token);
            await refresh;
            await _vm.LoadImagesAsync(_closing.Token);
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>"light" or "dark": the theme BoosterAI actually shows, also when it follows the system (#251).</summary>
    public static string ResolvedTheme(ThemeVariant? actual) => actual == ThemeVariant.Dark ? "dark" : "light";

    private void Open(string? url)
    {
        if (url is null) return;
        LinkFailed.IsVisible = !Screenshot.Active && !ExternalLink.Open(url);
    }

    private void OnOpenBrowser(object? sender, RoutedEventArgs e) => Open(_vm.BrowserUrl(ResolvedTheme(ActualThemeVariant)));

    private void OnOpenLink(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is NewsLink link) Open(link.Url);
    }

    private void OnOpenImage(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is FeatureCardViewModel card) Open(card.ImageUrl);
    }

    private void OnShowAll(object? sender, RoutedEventArgs e)
    {
        _vm.ShowAll();
        _ = _vm.LoadImagesAsync(_closing.Token);
    }

    /// <summary>"New: …" in a release jumps to its card in the overview and moves the focus there.</summary>
    private void OnJumpToCard(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not NewsRef jump) return;
        var card = this.GetVisualDescendants().OfType<Border>()
            .FirstOrDefault(b => b.Classes.Contains("news-card") && ReferenceEquals(b.DataContext, jump.Card));
        if (card is null) return;
        card.BringIntoView(new Rect(card.Bounds.Size));
        card.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.IsEffectivelyVisible && b.Focusable)?.Focus(NavigationMethod.Tab);
    }
}
