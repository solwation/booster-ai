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

    private void OnAddTab(object? sender, RoutedEventArgs e) => _vm.AddTab();

    private void OnRemoveTab(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is TabViewModel tab) _vm.Tabs.Remove(tab);
    }

    private void OnNextColor(object? sender, RoutedEventArgs e) =>
        ((sender as Control)?.DataContext as TabViewModel)?.NextColor();

    private void OnLaunch(object? sender, RoutedEventArgs e)
    {
        if (_vm.Launch()) Close();
    }
}
