using Avalonia.Controls;
using Avalonia.Interactivity;

namespace BoosterAI;

/// <summary>Identical full status and live output for pointer preview and keyboard-opened details.</summary>
public partial class StatusPeekView : UserControl
{
    private bool _showsActions;

    public StatusPeekView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Watch();
    }

    /// <summary>The clickable popup (not the hover preview) offers ↻ after the CLI exited with an error.</summary>
    internal bool ShowsActions
    {
        get => _showsActions;
        set { _showsActions = value; UpdateRestart(); }
    }

    internal event Action? RestartRequested;

    private TabViewModel? _watched;

    private void Watch()
    {
        if (_watched is not null) _watched.PropertyChanged -= OnTabChanged;
        _watched = DataContext as TabViewModel;
        if (_watched is not null) _watched.PropertyChanged += OnTabChanged;
        UpdateRestart();
    }

    private void OnTabChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TabViewModel.IsExitError) or nameof(TabViewModel.ShowReload)) UpdateRestart();
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_watched is not null) _watched.PropertyChanged -= OnTabChanged;
        _watched = null;
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Watch();
    }

    private void UpdateRestart() =>
        RestartButton.IsVisible = _showsActions && _watched is { IsExitError: true, ShowReload: true };

    private void OnRestart(object? sender, RoutedEventArgs e) => RequestRestart();

    internal void RequestRestart()
    {
        if (RestartButton.IsVisible) RestartRequested?.Invoke();
    }

    internal void FocusContent() => OutputScroll.Focus(Avalonia.Input.NavigationMethod.Tab);
}
