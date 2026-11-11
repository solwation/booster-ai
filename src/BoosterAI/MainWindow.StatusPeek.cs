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
    private TabViewModel? _hoverOutputTab;
    private Control? _hoverOutputTarget;

    private void OnStatusTipOpening(object? sender, CancelRoutedEventArgs e)
    {
        // A keyboard-opened view remains stable until dismissed; do not open a second preview over it.
        if (_statusPeek?.IsOpen == true) { e.Cancel = true; return; }
        if (sender is Control { DataContext: TabViewModel tab } target)
        {
            if (ToolTip.GetTip(target) is ToolTip { Content: StatusPeekView view })
                view.MaxHeight = Math.Max(100, ClientSize.Height - 48);
            _hoverOutputTab = tab;
            _hoverOutputTarget = target;
            _vm.WatchOutput(tab, open: true);
        }
    }

    private void OnStatusTipIsOpenChanged(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs e)
    {
        // The closing routed event can precede IsOpen changing. Follow the actual property instead.
        if (e.Property != ToolTip.IsOpenProperty || sender is not Control { DataContext: TabViewModel tab } target
            || ToolTip.GetIsOpen(target)) return;
        if (_hoverOutputTab == tab) { _hoverOutputTab = null; _hoverOutputTarget = null; }
        if (_statusPeekTarget?.DataContext != tab) _vm.WatchOutput(tab, open: false);
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
        if (tab.Parent is { Expanded: false } parent) parent.Expanded = true;
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
        flyout.Opened += (_, _) =>
        {
            _vm.WatchOutput(tab, open: true);
            content.FocusContent();
        };
        flyout.Closed += (_, _) =>
        {
            if (_statusPeek != flyout) return;
            _statusPeek = null;
            _statusPeekTarget = null;
            if (_hoverOutputTab != tab) _vm.WatchOutput(tab, open: false);
        };
        flyout.ShowAt(target);
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
        var tab = _hoverOutputTab;
        var target = _hoverOutputTarget;
        _hoverOutputTab = null;
        _hoverOutputTarget = null;
        if (target is not null) ToolTip.SetIsOpen(target, false);
        if (tab is not null) _vm.WatchOutput(tab, open: false);
    }
}
