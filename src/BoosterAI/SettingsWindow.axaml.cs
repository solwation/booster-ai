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
        TabsControl.SelectionChanged += (_, e) =>
        {
            if (ReferenceEquals(e.Source, TabsControl) && ReferenceEquals(TabsControl.SelectedItem, AgentsTab))
                _vm.SetupAgentsOpened();
        };
        Opened += (_, _) => { if (ReferenceEquals(TabsControl.SelectedItem, AgentsTab)) _vm.SetupAgentsOpened(); };
        Activated += (_, _) => { if (ReferenceEquals(TabsControl.SelectedItem, AgentsTab)) _vm.SetupAgentsOpened(); };
        ConfigureHardwareAdvice();
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
        get => _vm.Setup.SelectedAgent?.Cli;
        set
        {
            if (_vm.Setup.Agents.FirstOrDefault(a => a.Cli == value) is { } agent)
                _vm.Setup.SelectedAgent = agent;
        }
    }

    private LocalModelsWindow? _localModels;
    private async void OnLocalModels(object? sender, RoutedEventArgs e)
    {
        if (_localModels is not null) return;
        var model = new LocalModelsViewModel(_vm.Setup.HardwareAdvice);
        if (Screenshot.Active) model.ShowSample(null);
        _localModels = new LocalModelsWindow(model, liveReads: _vm.CanReadHardwareAdvice);
        try { await _localModels.ShowDialog(this); }
        finally { _localModels = null; }
    }

    private void OnAgentOverview(object? sender, RoutedEventArgs e) => _vm.Setup.SelectedAgent = null;
    private void OnOpenAgent(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is AgentInfo agent) { _vm.Setup.SelectedAgent = agent; agent.Section = 0; }
    }
    private void OnAgentSection(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: AgentInfo agent, Tag: string section } && int.TryParse(section, out var index)) agent.Section = Math.Clamp(index, 0, 2);
    }
    private void OnAgentCompactSettings(object? sender, RoutedEventArgs e) { SelectedTabIndex = 2; SelectedOptimizationSectionIndex = 3; }

    private void OnShowCompactAgents(object? sender, RoutedEventArgs e) => SelectedTabIndex = 4;

    private void OnShowEnvironmentAgents(object? sender, RoutedEventArgs e)
    {
        _vm.Setup.SelectedAgent = null;
        SelectedTabIndex = 4;
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
        if (await ConfirmSetupWindow.AgentMode(agent).ShowDialog<bool>(this))
            _vm.ApplyAgentMode(agent, yoloConfirmed: true, replaceCustomConfirmed: true);
    }

    private void OnAddTab(object? sender, RoutedEventArgs e) => _vm.AddTab();

    private async void OnFindProjects(object? sender, RoutedEventArgs e) => await ImportProjectsWindow.FindProjects(this, _vm);

    private async void OnRemoveTab(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is TabViewModel tab)
            await _vm.RemoveProjectAsync(tab, request => new ConfirmRemoveProjectWindow(request).ShowDialog<bool>(this));
    }

    private void OnNextColor(object? sender, RoutedEventArgs e) =>
        ((sender as Control)?.DataContext as TabViewModel)?.NextColor();

    private void OnOpenFolder(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is TabViewModel tab) _vm.OpenFolder(tab);
    }

    private void OnDone(object? sender, RoutedEventArgs e) => Close();

    private void OnUseRecommendedMemory(object? sender, RoutedEventArgs e) => _vm.Optimization.ApplyRecommendation();

    private void OnSaveOptimization(object? sender, RoutedEventArgs e) => _vm.Optimization.SaveConfig();

    private async void OnRestartWsl(object? sender, RoutedEventArgs e)
    {
        var confirmed = await ConfirmSetupWindow.ShutdownWsl().ShowDialog<bool>(this);
        _vm.Optimization.RestartWsl(confirmed);
    }

    private void OnResetSearchRoot(object? sender, RoutedEventArgs e) => _vm.ResetProjectSearchRoot();

    private async void OnBrowseSearchRoot(object? sender, RoutedEventArgs e)
    {
        try
        {
            var current = ProjectScanner.ResolveRoot(_vm.ProjectSearchRoot, out _);
            var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = Loc.Get("settings.findRoot.pick"),
                SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(current),
            });
            if (picked.Count > 0 && picked[0].TryGetLocalPath() is { } path) _vm.ProjectSearchRoot = path;
        }
        catch (Exception ex)
        {
            _vm.ShowError(ErrorKind.SearchRootPicker, Loc.Format("status.pickerFailed", ex.Message));
        }
    }

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
            _vm.ShowError(ErrorKind.ProjectPicker, Loc.Format("status.pickerFailed", ex.Message));
        }
    }
}
