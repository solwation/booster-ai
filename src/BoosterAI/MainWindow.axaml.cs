using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace BoosterAI;

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

    private async void OnFindProjects(object? sender, RoutedEventArgs e) => await ImportProjectsWindow.FindProjects(this, _vm);

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
        else
        {
            // Minimize rather than hide: the taskbar button stays, so the status is one click away.
            // Windows also gets a tray icon; Linux trays need a StatusNotifierItem host (missing on plain GNOME).
            if (OperatingSystem.IsWindows() && Application.Current is App app) app.ShowTray(this);
            WindowState = WindowState.Minimized;
        }
    }

    private async void OnUpdateInstall(object? sender, RoutedEventArgs e)
    {
        // The prompt is the confirmation: open tabs keep running but are no longer watched.
        if (await _vm.Update.InstallAsync()
            && Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime life)
            life.Shutdown();
    }

    private async void OnUpdatePage(object? sender, RoutedEventArgs e)
    {
        try { if (TopLevel.GetTopLevel(this)?.Launcher is { } l) await l.LaunchUriAsync(new Uri(_vm.Update.PageUrl)); }
        catch (Exception ex) { _vm.ShowStatus(Loc.Format("status.openFailed", ex.Message), error: true); }
    }

    private void OnUpdateLater(object? sender, RoutedEventArgs e) => _vm.Update.Later();

    private void OnUpdateSkip(object? sender, RoutedEventArgs e) => _vm.Update.Skip();

    private void OnAllowDenied(object? sender, RoutedEventArgs e) => _vm.AllowDenied();

    private void OnDismissDenied(object? sender, RoutedEventArgs e) => _vm.DismissDenied();

    private void OnRestart(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is TabViewModel tab) _vm.Restart(tab);
    }
}
