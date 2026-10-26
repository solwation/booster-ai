using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

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

    private async void OnBrowse(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not TabViewModel tab) return;
        try
        {
            var start = await StorageProvider.TryGetFolderFromPathAsync(PathMapping.StartFolder(tab.Directory, _vm.WslDistro));
            var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = $"Katalog för {tab.Name}",
                SuggestedStartLocation = start,
            });
            if (picked.Count > 0 && picked[0].TryGetLocalPath() is { } path)
                tab.Directory = PathMapping.ToTabPath(path);
        }
        catch (Exception ex)
        {
            _vm.Status = "Kunde inte öppna mappväljaren: " + ex.Message;
        }
    }

    private void OnLaunch(object? sender, RoutedEventArgs e)
    {
        if (_vm.Launch()) Close();
    }
}
