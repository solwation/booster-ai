using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace BoosterAI;

public partial class MainWindow : Window
{
    private void OnProjectGridLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is Grid grid) ApplyProjectColumns(grid);
    }

    private void ApplyProjectColumns(Grid grid)
    {
        var compact = Bounds.Width is > 0 and < 1050;
        if (grid.Tag is bool previous && previous == compact) return;
        grid.Tag = compact;
        grid.ColumnDefinitions = new ColumnDefinitions(compact
            ? "4,20,36,*,115,166,120,75,105" : "4,20,36,*,200,166,195,110,130");
    }

    private static readonly DataFormat<string> TabDragFormat = DataFormat.CreateStringApplicationFormat("boosterai-tab-index");
    private readonly MainViewModel _vm;
    private MainWindowSize? _rememberedSize;

    /// <summary>Enabled only for the real GUI; screenshots and preview windows never touch the user's size file.</summary>
    internal void RememberSize(string? file = null)
    {
        file ??= MainWindowSize.StateFile;
        _rememberedSize = MainWindowSize.Read(file);
        var work = Screens.Primary?.WorkingArea;
        var scaling = Screens.Primary?.Scaling ?? 1;
        Width = Math.Clamp(_rememberedSize.Width, MinWidth, Math.Max(MinWidth, work?.Width / scaling ?? 100000));
        Height = Math.Clamp(_rememberedSize.Height, MinHeight, Math.Max(MinHeight, work?.Height / scaling ?? 100000));
        if (WindowState != WindowState.Minimized && _rememberedSize.Maximized) WindowState = WindowState.Maximized;
        var opened = false;
        var save = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        void Persist()
        {
            save.Stop();
            _rememberedSize.Save(file);
        }
        save.Tick += (_, _) => Persist();
        void Changed()
        {
            if (!opened || !IsVisible || WindowState == WindowState.Minimized) return;
            _rememberedSize = _rememberedSize.Observe(ClientSize.Width, ClientSize.Height,
                WindowState == WindowState.Normal, WindowState == WindowState.Maximized);
            save.Stop();
            save.Start();
        }
        Opened += (_, _) => opened = true;
        Resized += (_, _) => Changed();
        PropertyChanged += (_, e) =>
        {
            if (e.Property != WindowStateProperty) return;
            if (WindowState == WindowState.Minimized) Persist();
            else Changed();
        };
        Closing += (_, _) => Persist();
        Closed += (_, _) => save.Stop();
    }

    internal void RestoreSizeState() => WindowState = _rememberedSize?.Maximized == true ? WindowState.Maximized : WindowState.Normal;

    // Required by the XAML previewer.
    public MainWindow() : this(new MainViewModel(new AppConfig())) { }

    public MainWindow(MainViewModel vm, bool background = false)
    {
        _vm = vm;
        DataContext = vm;
        InitializeComponent();
        Closed += (_, _) => StopStatusPeek();
        PropertyChanged += OnStatusWindowVisibilityChanged;
        Resized += (_, _) =>
        {
            foreach (var grid in this.GetVisualDescendants().OfType<Grid>().Where(g => g.Classes.Contains("project-columns")))
                ApplyProjectColumns(grid);
        };
        if (background)
        {
            ShowActivated = false;
            WindowState = WindowState.Minimized;
            Opened += OnBackgroundOpened;
        }
        Closing += OnClosing;
        PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty && WindowState == WindowState.Minimized
                && TrayPolicy.HideOnMinimize(_vm.MinimizeToTray, TrayAvailable())) Hide();
        };
        Activated += (_, _) => { _vm.Update.WindowActive(true); _vm.SetupWindowActive(); };
        Deactivated += (_, _) => _vm.Update.WindowActive(false);
    }

    private void OnBackgroundOpened(object? sender, EventArgs e)
    {
        // Opened fires again when a tray-hidden window is shown. Only startup may hide it automatically.
        Opened -= OnBackgroundOpened;
        if (_vm.MinimizeToTray && TrayAvailable()) Hide();
    }

    /// <summary>Windows only (Linux trays need a StatusNotifierItem host, missing on plain GNOME); shows the tray icon as a side effect.</summary>
    private bool TrayAvailable() => OperatingSystem.IsWindows() && Application.Current is App app && app.ShowTray(this);

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        _vm.Save();
        // Only the user's own close can hide; probe the tray only then, so no icon appears for a real exit.
        if (_vm.MinimizeToTray && !e.IsProgrammatic && e.CloseReason == WindowCloseReason.WindowClosing
            && TrayPolicy.HideOnClose(_vm.MinimizeToTray, TrayAvailable(), e.CloseReason, e.IsProgrammatic, App.QuitRequested))
        {
            e.Cancel = true;
            Hide();
        }
    }

    private async void OnSettings(object? sender, RoutedEventArgs e)
    {
        await new SettingsWindow(_vm).ShowDialog(this);
    }

    private async void OnAddProject(object? sender, RoutedEventArgs e)
    {
        _vm.AddTab();
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

    private void OnExpandProject(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is TabViewModel tab) tab.Expanded = !tab.Expanded;
    }

    private void OnAddSubTab(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is TabViewModel tab) _vm.AddSubTab(tab);
    }

    private void OnRemoveSubTab(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is TabViewModel tab) _vm.RemoveSubTab(tab);
    }

    private async void OnTabDragStart(object? sender, PointerPressedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not TabViewModel tab) return;
        var index = _vm.Tabs.IndexOf(tab);
        if (index < 0) return;
        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(TabDragFormat, index.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        e.Handled = true;
        await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Move);
    }

    private static void OnTabDragOver(object? sender, DragEventArgs e)
    {
        if (sender is not Border target || !e.DataTransfer.Contains(TabDragFormat))
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }
        e.DragEffects = DragDropEffects.Move;
        SetDropTarget(target, e.GetPosition(target).Y < target.Bounds.Height / 2);
        e.Handled = true;
    }

    private static void OnTabDragLeave(object? sender, DragEventArgs e)
    {
        if (sender is Border target) ClearDropTarget(target);
    }

    private void OnTabDrop(object? sender, DragEventArgs e)
    {
        if (sender is not Border target || target.DataContext is not TabViewModel tab
            || !int.TryParse(e.DataTransfer.TryGetValue(TabDragFormat), out var sourceIndex)) return;

        var targetIndex = _vm.Tabs.IndexOf(tab);
        if (targetIndex < 0) return;
        var after = e.GetPosition(target).Y >= target.Bounds.Height / 2;
        if (sourceIndex < 0 || sourceIndex >= _vm.Tabs.Count) return;
        _vm.MoveTabRelative(_vm.Tabs[sourceIndex], tab, after);
        ClearDropTarget(target);
        e.DragEffects = DragDropEffects.Move;
        e.Handled = true;
    }

    private static void SetDropTarget(Border target, bool before)
    {
        ClearDropTarget(target);
        target.Classes.Add(before ? "drop-before" : "drop-after");
    }

    private static void ClearDropTarget(Border target)
    {
        target.Classes.Remove("drop-before");
        target.Classes.Remove("drop-after");
    }

    private void OnLaunch(object? sender, RoutedEventArgs e)
    {
        if (!_vm.Launch()) return;
        var (close, showTray, minimize) = AfterLaunchPolicy.Decide(_vm.AfterLaunch, OperatingSystem.IsWindows());
        if (close) { Close(); return; }
        // Windows gets the tray icon whether the window stays open or is minimized. With the tray setting on (and a
        // tray), minimizing hides the window to the tray icon; otherwise the taskbar button stays.
        if (showTray && Application.Current is App app) app.ShowTray(this);
        if (minimize) WindowState = WindowState.Minimized;
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

    private async void OnUpdateMore(object? sender, RoutedEventArgs e)
    {
        try { if (TopLevel.GetTopLevel(this)?.Launcher is { } l) await l.LaunchUriAsync(new Uri(_vm.Update.MoreUrl)); }
        catch (Exception ex) { _vm.ShowStatus(Loc.Format("status.openFailed", ex.Message), error: true); }
    }

    private void OnUpdateLater(object? sender, RoutedEventArgs e) => _vm.Update.Later();

    private void OnUpdateSkip(object? sender, RoutedEventArgs e) => _vm.Update.Skip();

    private void OnAllowDenied(object? sender, RoutedEventArgs e) => _vm.AllowDenied();

    private void OnDismissDenied(object? sender, RoutedEventArgs e) => _vm.DismissDenied();

    private void OnInstallCli(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is CliOffer offer) _vm.InstallCli(offer.Cli);
    }

    private void OnDismissMissing(object? sender, RoutedEventArgs e) => _vm.Setup.DismissMissing();

    private async void OnInstallWsl(object? sender, RoutedEventArgs e)
    {
        if (await ConfirmSetupWindow.Wsl().ShowDialog<bool>(this)) _vm.InstallWsl();
    }

    private void OnContinue(object? sender, RoutedEventArgs e)
    {
        if (!_vm.IsClosingAll && (sender as Control)?.DataContext is TabViewModel tab) _vm.Restart(tab, resume: true);
    }

    private async void OnCloseAll(object? sender, RoutedEventArgs e)
    {
        if (_vm.CanCloseAll) await new CloseAllWindow(_vm.PrepareCloseAll()).ShowDialog(this);
    }

    private async void OnRestart(object? sender, RoutedEventArgs e)
    {
        if (_vm.IsClosingAll || (sender as Control)?.DataContext is not TabViewModel tab) return;
        if (tab.IsAlive && !tab.IsChanged) { _vm.ReloadOrchestra(tab); return; }
        if (!tab.IsChanged) { _vm.Restart(tab); return; }
        // A changed running tab: a second click cancels the wait; an idle CLI restarts at once; otherwise ask.
        if (tab.PendingRestart) { tab.PendingRestart = false; return; }
        if (tab.Activity == Activity.Idle) { _vm.RestartChanged(tab); return; }
        switch (await new ConfirmRestartWindow(tab).ShowDialog<RestartChoice>(this))
        {
            case RestartChoice.Now: _vm.RestartChanged(tab); break;
            case RestartChoice.WhenIdle: tab.PendingRestart = true; break;
        }
    }

    private async void OnRestartAll(object? sender, RoutedEventArgs e)
    {
        if (!_vm.CanRestartAll) return;
        var plan = _vm.PlanRestartAll();
        var choice = RestartChoice.Now;
        // Busy changed tabs are never interrupted silently: one question for all of them.
        if (plan.Busy.Count > 0) choice = await new ConfirmRestartWindow(plan.Busy).ShowDialog<RestartChoice>(this);
        _vm.RestartAll(plan, choice);
    }

    private async void OnOptimizeWsl(object? sender, RoutedEventArgs e)
    {
        var sw = new SettingsWindow(_vm) { SelectedTabIndex = 2, SelectedOptimizationSectionIndex = 1 };
        await sw.ShowDialog(this);
    }

    private void OnDismissOptimization(object? sender, RoutedEventArgs e) => _vm.Optimization.DismissBanner();
}
