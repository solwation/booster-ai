using Avalonia.Controls;
using Avalonia.Interactivity;

namespace BoosterAI;

public enum RestartChoice { Cancel, Now, WhenIdle }

/// <summary>
/// Asks before a changed tab is restarted while its CLI works on a turn (or when that cannot be told):
/// now (the turn is interrupted), when it is idle (only when idle can be detected), or not at all.
/// </summary>
public partial class ConfirmRestartWindow : Window
{
    // Required by the XAML previewer.
    public ConfirmRestartWindow() : this(new TabViewModel(new TabConfig(), new MainViewModel(new AppConfig()), 0)) { }

    public ConfirmRestartWindow(TabViewModel tab)
    {
        var name = tab.Name.Trim();
        // A one-shot run is busy until it exits, so waiting for idle would never restart it.
        CanWait = tab.Activity == Activity.Busy && tab.LaunchedAs is not { NonInteractive: true, Orkester: true };
        Heading = Loc.Format("restartConfirm.heading", name);
        Body = Loc.Get(CanWait ? "restartConfirm.busy" : "restartConfirm.unknown");
        Detail = tab.RestartTip;
        DataContext = this;
        InitializeComponent();
    }

    public string Heading { get; }
    public string Body { get; }
    public string Detail { get; }
    public bool CanWait { get; }

    private void OnNow(object? sender, RoutedEventArgs e) => Close(RestartChoice.Now);
    private void OnWhenIdle(object? sender, RoutedEventArgs e) => Close(RestartChoice.WhenIdle);
    private void OnCancel(object? sender, RoutedEventArgs e) => Close(RestartChoice.Cancel);
}
