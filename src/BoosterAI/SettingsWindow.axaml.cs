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
        Activated += (_, _) =>
        {
            if (!ReferenceEquals(TabsControl.SelectedItem, AgentsTab)) return;
            _vm.SetupAgentsOpened();
            // A login in the tab BoosterAI opened shows when the user comes back (#192).
            if (_vm.Setup.SelectedAgent is { IsAccounts: true, Accounts: { } accounts }) _ = accounts.RefreshStatusAsync();
        };
        OptimizationSections.SelectionChanged += (_, _) => ShowOptimizationPage();
        ShowOptimizationPage();
        ConfigureHardwareAdvice();
        Closing += OnClosing;
    }

    /// <summary>The experimental automatic switch (#279): turning it on asks first; Avbryt leaves it off.</summary>
    private async void OnAutoModelSwitchClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox box) return;
        var on = await _vm.RequestAutoModelSwitchAsync(box.IsChecked == true,
            () => new ConfirmChoiceWindow(ChoiceQuestion.EnableAutoSwitch()).ShowDialog<bool>(this));
        box.IsChecked = on;
    }

    private bool _closeApproved;

    /// <summary>
    /// Unsaved compact or WSL-memory edits are never lost silently (#188): the first close attempt asks, then closes again
    /// once the answer is Save or Discard; Cancel keeps the window open. Without such edits nothing is asked.
    /// </summary>
    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        // App or OS shutdown is never held up by the question; it closes as before (#188).
        if (_closeApproved || e.CloseReason is WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown) { _vm.Save(); return; }
        if (_vm.UnsavedChanges().Count == 0) { _vm.Save(); return; }
        e.Cancel = true;
        if (_asking) return;
        _asking = true;
        try
        {
            if (!await _vm.ResolveUnsavedAsync(items => UnsavedChangesWindow.AskAsync(this, items))) return;
            _closeApproved = true;
        }
        finally { _asking = false; }
        Close();
    }
    private bool _asking;

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

    /// <summary>Minne och kontext's side navigation picks one of its pages; the others stay mounted but hidden.</summary>
    private void ShowOptimizationPage()
    {
        var index = Math.Max(0, OptimizationSections.SelectedIndex);
        for (var i = 0; i < OptimizationPages.Children.Count; i++) OptimizationPages.Children[i].IsVisible = i == index;
        // The memory limit page reads autoMemoryReclaim again and checks the WSL version (hidden wsl --version, #183);
        // screenshots and other hosts keep their sample state and run no process.
        if (index == 1 && OperatingSystem.IsWindows() && !Screenshot.Active)
        {
            _vm.Optimization.Reclaim.Refresh();
            _ = _vm.Optimization.Reclaim.CheckVersionAsync();
        }
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
        if (sender is Button { DataContext: AgentInfo agent, Tag: string section } && int.TryParse(section, out var index)) agent.Section = Math.Clamp(index, 0, 3);
    }

    private void OnAccountAdd(object? sender, RoutedEventArgs e) => ((sender as Control)?.DataContext as AgentInfo)?.Accounts?.Add();

    private void OnAccountNameKeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key != Avalonia.Input.Key.Enter || (sender as Control)?.DataContext is not AgentInfo { Accounts: { } accounts }) return;
        // Enter adds the account instead of pressing the window's default button (Klar).
        e.Handled = accounts.Add() || true;
    }

    private void OnAccountLogin(object? sender, RoutedEventArgs e) => ((sender as Control)?.DataContext as AccountRow)?.Login();

    private void OnAccountRemove(object? sender, RoutedEventArgs e) => ((sender as Control)?.DataContext as AccountRow)?.Remove();

    /// <summary>"Använd reset" (#274): the question, then exactly one redemption on yes.</summary>
    private async void OnAccountUseReset(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not AccountRow row) return;
        try { await row.UseResetAsync((name, n) => ConfirmSetupWindow.UseReset(name, n).ShowDialog<bool>(this)); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { /* the row and the banner already say what failed */ }
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

    /// <summary>A mode picked in the list (#187): confirm, then write; cancel puts the list back.</summary>
    private async void OnAgentModeSelected(object? sender, SelectionChangedEventArgs e)
    {
        if ((sender as Control)?.DataContext is AgentInfo agent && e.AddedItems.Count > 0)
            await _vm.ChooseAgentModeAsync(agent, a => ConfirmSetupWindow.AgentMode(a).ShowDialog<bool>(this));
    }

    private async void OnRepairAgentConfig(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is AgentInfo agent)
            await _vm.ChooseAgentModeAsync(agent, a => ConfirmSetupWindow.AgentMode(a).ShowDialog<bool>(this), repair: true);
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

    /// <summary>Tag "gradual", "dropCache" or "remove": confirm the exact lines first, then write; WSL is not restarted.</summary>
    private async void OnSetMemoryReclaim(object? sender, RoutedEventArgs e)
    {
        var reclaim = _vm.Optimization.Reclaim;
        var value = MemoryReclaimViewModel.ValueFor((sender as Control)?.Tag as string);
        if (!reclaim.CanWrite(value)) return;
        var path = Screenshot.Active || WslConfigFile.PathOnDisk == "" ? @"C:\Users\me\.wslconfig" : WslConfigFile.PathOnDisk;
        var confirmed = await ConfirmSetupWindow.MemoryReclaim(value, reclaim.Preview(value), path).ShowDialog<bool>(this);
        reclaim.Write(value, confirmed);
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
