using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace BoosterStart;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    // Required by the XAML previewer.
    public MainWindow() : this(new MainViewModel(new AppConfig())) { }

    public MainWindow(MainViewModel vm)
    {
        _vm = vm;
        DataContext = vm;
        InitializeComponent();
        Closing += (_, _) => _vm.Save();
    }

    private async void OnSettings(object? sender, RoutedEventArgs e)
    {
        if (!_vm.HasTabs) _vm.AddTab();
        await new SettingsWindow(_vm).ShowDialog(this);
    }

    private async void OnInstallOrkester(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not TabViewModel tab) return;
        if (await new ConfirmInstallWindow(tab).ShowDialog<bool>(this)) _vm.InstallOrkester(tab);
    }

    private void OnOpenFolder(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is TabViewModel tab) _vm.OpenFolder(tab);
    }

    private void OnLaunch(object? sender, RoutedEventArgs e)
    {
        if (!_vm.Launch()) return;
        if (_vm.CloseAfterLaunch) Close();
        // Windows: keep watching from the tray. Linux trays need a StatusNotifierItem host
        // (missing on plain GNOME), so the window only minimizes there.
        else if (OperatingSystem.IsWindows() && Application.Current is App app && app.ShowTray(this)) Hide();
        else WindowState = WindowState.Minimized;
    }

    private void OnRestart(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is TabViewModel tab) _vm.Restart(tab);
    }
}
