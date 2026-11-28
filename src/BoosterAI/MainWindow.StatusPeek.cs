using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace BoosterAI;

public partial class MainWindow
{
    private Flyout? _statusPeek;
    private StatusPeekButton? _statusPeekTarget;
    private Control? _hoverOutputTarget;

    private void OnStatusTipOpening(object? sender, CancelRoutedEventArgs e)
    {
        // A keyboard-opened view remains stable until dismissed; do not open a second preview over it.
        if (_statusPeek?.IsOpen == true) { e.Cancel = true; return; }
        // The agent box's preview (#221) waits while its list is open: the entries carry their own tips.
        if (sender is Panel panel && panel.Children.OfType<ComboBox>().Any(box => box.IsDropDownOpen)) { e.Cancel = true; return; }
        if (sender is Control { DataContext: TabViewModel } target)
        {
            if (ToolTip.GetTip(target) is ToolTip { Content: StatusPeekView view })
                view.MaxHeight = Math.Max(100, ClientSize.Height - 48);
            _hoverOutputTarget = target;
        }
    }

    private void OnStatusPeek(object? sender, RoutedEventArgs e)
    {
        if (sender is not StatusPeekButton { DataContext: TabViewModel tab } target) return;
        if (_statusPeekTarget == target && _statusPeek?.IsOpen == true) { CloseStatusPeek(restoreFocus: true); return; }
        ShowStatusPeek(target, tab);
    }

    /// <summary>The error banner's "Visa detaljer": the tab's status popup, as a click on its status opens it.</summary>
    private void OpenStatusPeek(TabViewModel tab)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var target = this.GetVisualDescendants().OfType<StatusPeekButton>().FirstOrDefault(b => b.DataContext == tab);
            if (target is null) return;
            target.BringIntoView();
            ShowStatusPeek(target, tab);
        }, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    private void ShowStatusPeek(StatusPeekButton target, TabViewModel tab)
    {
        CloseStatusPeek(restoreFocus: false);
        ToolTip.SetIsOpen(target, false);
        var content = new StatusPeekView { DataContext = tab, MaxHeight = Math.Max(100, ClientSize.Height - 48), ShowsActions = true };
        content.RestartRequested += () =>
        {
            CloseStatusPeek(restoreFocus: true);
            OnRestart(target, new RoutedEventArgs());
        };
        content.UseResetRequested += () =>
        {
            CloseStatusPeek(restoreFocus: true);
            _ = UseResetFromPeekAsync(tab);
        };
        var flyout = new Flyout { Content = content, Placement = PlacementMode.Bottom };
        flyout.FlyoutPresenterClasses.Add("status-output");
        _statusPeek = flyout;
        _statusPeekTarget = target;
        content.AddHandler(KeyDownEvent, (_, key) =>
        {
            if (key.Key is Key.Enter or Key.Space)
            {
                // Swallowed so it never reaches the status button again; the popup's own ↻ still answers it.
                key.Handled = true;
                if (key.Source is Button { Name: "RestartButton" }) content.RequestRestart();
                else if (key.Source is Button { Name: "UseResetButton" }) content.RequestUseReset();
                return;
            }
            if (key.Key != Key.Escape) return;
            key.Handled = true;
            CloseStatusPeek(restoreFocus: true);
        }, RoutingStrategies.Tunnel);
        content.AddHandler(KeyUpEvent, (_, key) =>
        {
            if (key.Key is Key.Enter or Key.Space) key.Handled = true;
        }, RoutingStrategies.Tunnel);
        flyout.Opened += (_, _) => content.FocusContent();
        flyout.Closed += (_, _) =>
        {
            if (_statusPeek != flyout) return;
            _statusPeek = null;
            _statusPeekTarget = null;
        };
        flyout.ShowAt(target);
    }

    /// <summary>"Använd reset" from the status popup (#293): the Accounts page's question, then exactly one redemption on yes.</summary>
    private async Task UseResetFromPeekAsync(TabViewModel tab)
    {
        try { await tab.UseResetAsync((name, n) => ConfirmSetupWindow.UseReset(name, n).ShowDialog<bool>(this)); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { /* the status line and the banner already say what failed */ }
    }

    private void OnStatusPeekKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        CloseStatusPeek(restoreFocus: true);
        if (sender is Control target) ToolTip.SetIsOpen(target, false);
    }

    private void CloseStatusPeek(bool restoreFocus)
    {
        var target = _statusPeekTarget;
        _statusPeek?.Hide();
        if (restoreFocus) target?.Focus(NavigationMethod.Tab);
    }

    private void OnStatusWindowVisibilityChanged(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == IsVisibleProperty && !IsVisible
            || e.Property == WindowStateProperty && WindowState == WindowState.Minimized) StopStatusPeek();
    }

    private void StopStatusPeek()
    {
        CloseStatusPeek(restoreFocus: false);
        var target = _hoverOutputTarget;
        _hoverOutputTarget = null;
        if (target is not null) ToolTip.SetIsOpen(target, false);
    }
}
