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

    private void OnLaunch(object? sender, RoutedEventArgs e)
    {
        if (_vm.Launch()) Close();
    }
}
