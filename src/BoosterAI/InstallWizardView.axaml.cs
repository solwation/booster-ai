using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Interactivity;

namespace BoosterAI;

/// <summary>The wizard's content; the window rebuilds it when the language changes so every text follows.</summary>
public partial class InstallWizardView : UserControl
{
    // Required by the XAML previewer.
    public InstallWizardView() => InitializeComponent();

    public InstallWizardView(InstallWizardViewModel vm)
    {
        DataContext = vm;
        InitializeComponent();
    }

    private InstallWizardViewModel Vm => (InstallWizardViewModel)DataContext!;
    private InstallWizardWindow? Owner => TopLevel.GetTopLevel(this) as InstallWizardWindow;

    private void OnNext(object? sender, RoutedEventArgs e) => Vm.Next();
    private void OnBack(object? sender, RoutedEventArgs e) => Vm.Back();
    private void OnSkip(object? sender, RoutedEventArgs e) => Vm.Skip();
    private void OnClose(object? sender, RoutedEventArgs e) => Owner?.Close();
    private async void OnInstall(object? sender, RoutedEventArgs e) { if (Owner is { } w) await w.InstallAsync(); }
    private void OnOpen(object? sender, RoutedEventArgs e) => Owner?.OpenInstalled();
    private async void OnFindProjects(object? sender, RoutedEventArgs e) { if (Owner is { } w) await w.FindProjectsAsync(); }
    private async void OnBrowseSearchRoot(object? sender, RoutedEventArgs e) { if (Owner is { } w) await w.BrowseSearchRootAsync(); }
}

/// <summary>The setup wizard window (#131), shown instead of the main window in setup mode.</summary>
public sealed class InstallWizardWindow : Window
{
    private readonly InstallWizardViewModel _vm;

    // Required by the XAML previewer.
    public InstallWizardWindow() : this(new InstallWizardViewModel(Installer.SamplePlan(), null, false, _ => { })) { }

    public InstallWizardWindow(InstallWizardViewModel vm)
    {
        _vm = vm;
        Width = 700;
        Height = 670;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        using (var icon = Avalonia.Platform.AssetLoader.Open(new Uri("avares://BoosterAI/Assets/boosterai.ico")))
            Icon = new WindowIcon(icon);
        Rebuild();
        // The XAML texts are read once; a new language rebuilds the content (after the ComboBox commits).
        vm.LanguageChanged += () => Avalonia.Threading.Dispatcher.UIThread.Post(Rebuild);
        // Closing during the copy would leave a half-done install; wait for it.
        Closing += (_, e) => { if (_vm.IsWorking) e.Cancel = true; };
    }

    private void Rebuild()
    {
        Title = Loc.Get("setup.title");
        Content = new InstallWizardView(_vm);
    }

    public Task InstallAsync()
    {
        if (!OperatingSystem.IsWindows())
        {
            _vm.Error = Loc.Get("setup.error.windowsOnly");
            return Task.CompletedTask;
        }
        return _vm.InstallAsync(Installer.Install);
    }

    public void OpenInstalled()
    {
        try { if (OperatingSystem.IsWindows()) Installer.Open(_vm.Plan); }
        catch (Exception) { /* the Start menu shortcut still works */ }
        Close();
    }

    /// <summary>Picks the base folder "Hitta projekt" searches (saved as AppConfig.ProjectSearchRoot).</summary>
    public async Task BrowseSearchRootAsync()
    {
        try
        {
            var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = Loc.Get("settings.findRoot.pick"),
                SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(_vm.ScanRoot),
            });
            if (picked.Count > 0 && picked[0].TryGetLocalPath() is { } path) _vm.SearchRoot = path;
        }
        catch (Exception) { /* no picker available: the field can still be typed in */ }
    }

    /// <summary>The existing "Hitta projekt" dialog over the wizard; the picked folders join the list.</summary>
    public async Task FindProjectsAsync()
    {
        var import = new ImportViewModel(_vm.KnownPaths);
        var dialog = new ImportProjectsWindow(import);
        _ = import.ScanAsync(_vm.ScanRoot, includeRoot: !string.IsNullOrWhiteSpace(_vm.SearchRoot));
        if (await dialog.ShowDialog<IReadOnlyList<FoundProject>?>(this) is { } picked) _vm.AddProjects(picked);
    }
}
