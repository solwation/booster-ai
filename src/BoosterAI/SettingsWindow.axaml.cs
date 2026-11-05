using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace BoosterAI;

/// <summary>Edits which projects open as tabs; changes apply live and are saved on close.</summary>
public partial class SettingsWindow : Window
{
    private readonly MainViewModel _vm;

    // Required by the XAML previewer.
    public SettingsWindow() : this(new MainViewModel(new AppConfig())) { }

    public SettingsWindow(MainViewModel vm)
    {
        _vm = vm;
        DataContext = vm;
        InitializeComponent();
        Closing += (_, _) => _vm.Save();
    }

    public int SelectedTabIndex
    {
        get => TabsControl.SelectedIndex;
        set => TabsControl.SelectedIndex = value;
    }

    public int SelectedOptimizationSectionIndex
    {
        get => OptimizationSections.SelectedIndex;
        set => OptimizationSections.SelectedIndex = value;
    }

    public string? SelectedAgentCli
    {
        get => (AgentSections.SelectedItem as AgentInfo)?.Cli;
        set
        {
            if (_vm.Setup.Agents.FirstOrDefault(a => a.Cli == value) is { } agent)
                AgentSections.SelectedItem = agent;
        }
    }

    private void OnShowMemoryLimit(object? sender, RoutedEventArgs e) => SelectedOptimizationSectionIndex = 1;
    private void OnShowMemoryUsage(object? sender, RoutedEventArgs e) => SelectedOptimizationSectionIndex = 0;

    private void OnInstallAgent(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is AgentInfo agent)
            _vm.InstallCli(agent.Cli);
    }

    private async void OnApplyAgentMode(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not AgentInfo agent || !agent.CanApply) return;
        var confirmed = agent.SelectedMode.Mode != AgentMode.Yolo
            || await ConfirmSetupWindow.AgentMode(agent).ShowDialog<bool>(this);
        if (confirmed) _vm.ApplyAgentMode(agent, yoloConfirmed: true);
    }

    private void OnAddTab(object? sender, RoutedEventArgs e) => _vm.AddTab();

    private async void OnFindProjects(object? sender, RoutedEventArgs e) => await ImportProjectsWindow.FindProjects(this, _vm);

    private void OnRemoveTab(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is TabViewModel tab) _vm.RemoveProject(tab);
    }

    private void OnNextColor(object? sender, RoutedEventArgs e) =>
        ((sender as Control)?.DataContext as TabViewModel)?.NextColor();

    private void OnOpenFolder(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is TabViewModel tab) _vm.OpenFolder(tab);
    }

    private void OnDone(object? sender, RoutedEventArgs e) => Close();

    private void OnCheckSetup(object? sender, RoutedEventArgs e) => _vm.CheckSetupNow();

    private void OnUseRecommendedMemory(object? sender, RoutedEventArgs e) => _vm.Optimization.ApplyRecommendation();

    private void OnSaveOptimization(object? sender, RoutedEventArgs e) => _vm.Optimization.SaveConfig();

    private void OnRestartWsl(object? sender, RoutedEventArgs e) => _vm.Optimization.RestartWsl();

    private async void OnBrowse(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not TabViewModel tab) return;
        try
        {
            var start = await StorageProvider.TryGetFolderFromPathAsync(PathMapping.StartFolder(tab.Directory, _vm.WslDistro));
            var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = Loc.Format("picker.title", tab.Name),
                SuggestedStartLocation = start,
            });
            if (picked.Count > 0 && picked[0].TryGetLocalPath() is { } path)
            {
                tab.Directory = PathMapping.ToTabPath(path);
                // A new tab named after its folder saves typing.
                if (tab.Name == "" || Loc.IsDefaultTabName(tab.Name)) tab.Name = picked[0].Name;
            }
        }
        catch (Exception ex)
        {
            _vm.ShowStatus(Loc.Format("status.pickerFailed", ex.Message), error: true);
        }
    }
}
