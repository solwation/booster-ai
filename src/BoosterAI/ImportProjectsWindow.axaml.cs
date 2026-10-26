using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace BoosterAI;

/// <summary>"Hitta projekt": lists git/AI project folders under Documents; closes with the ones to import.</summary>
public partial class ImportProjectsWindow : Window
{
    private readonly ImportViewModel _vm;

    // Required by the XAML previewer.
    public ImportProjectsWindow() : this(new ImportViewModel([])) { }

    public ImportProjectsWindow(ImportViewModel vm)
    {
        _vm = vm;
        DataContext = vm;
        InitializeComponent();
        Closed += (_, _) => _vm.Cancel();
    }

    /// <summary>Shows the dialog over <paramref name="owner"/>, scanning Documents, and imports what is picked.</summary>
    public static async Task FindProjects(Window owner, MainViewModel main)
    {
        var vm = new ImportViewModel(main.ProjectPaths);
        var dialog = new ImportProjectsWindow(vm);
        _ = vm.ScanAsync(ProjectScanner.DefaultRoot());
        if (await dialog.ShowDialog<IReadOnlyList<FoundProject>?>(owner) is { } picked) main.Import(picked);
    }

    private void OnImport(object? sender, RoutedEventArgs e) => Close(_vm.Selected);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);

    private async void OnPickRoot(object? sender, RoutedEventArgs e)
    {
        try
        {
            var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = Loc.Get("import.root.tip"),
                SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(_vm.Root),
            });
            if (picked.Count > 0 && picked[0].TryGetLocalPath() is { } path) await _vm.ScanAsync(path);
        }
        catch (Exception) { /* No dialog available: keep the current list. */ }
    }
}
