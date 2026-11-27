using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Threading;
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
        var columns = ProjectColumns(Bounds.Width, _vm.ShowOrchestraColumn);
        if (grid.Tag as string == columns) return;
        grid.Tag = columns;
        grid.ColumnDefinitions = new ColumnDefinitions(columns);
    }

    /// <summary>
    /// Stripe, handle, Start, project, status, agent, model, orchestra, ⋯. The project takes all the free room; the status is a
    /// narrow fixed column with a compact indicator and the row actions, its details in the popup (#209); agent, model and
    /// orchestra are fixed, and the orchestra column is 0 while no visible row can use it (#152). The row's ⋯ stays last (#196).
    /// </summary>
    internal static string ProjectColumns(double width, bool orchestra)
    {
        var compact = width is > 0 and < 1050;
        var fixedColumns = compact ? "150,140" : "200,195";
        return $"4,20,46,*,{StatusColumnWidth},{fixedColumns},{(orchestra ? 168 : 0)},{MoreColumnWidth}";
    }

    internal const int StatusColumnWidth = 160;

    internal const int MoreColumnWidth = 32;

    private void ApplyAllProjectColumns()
    {
        foreach (var grid in this.GetVisualDescendants().OfType<Grid>().Where(g => g.Classes.Contains("project-columns")))
            ApplyProjectColumns(grid);
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
        vm.InstallOrkesterRequested += ConfirmInstallOrkester;
        Closed += (_, _) => vm.InstallOrkesterRequested -= ConfirmInstallOrkester;
        ActualThemeVariantChanged += (_, _) => _vm.RaiseThemeBrushes();
        PropertyChanged += OnStatusWindowVisibilityChanged;
        Resized += (_, _) => ApplyAllProjectColumns();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.ShowOrchestraColumn)) ApplyAllProjectColumns();
        };
        if (background)
        {
            ShowActivated = false;
            WindowState = WindowState.Minimized;
            Opened += OnBackgroundOpened;
        }
        Closing += OnClosing;
        vm.LaunchFromWindow = () => OnLaunch(null, new RoutedEventArgs());
        PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty && WindowState == WindowState.Minimized
                && TrayPolicy.HideOnMinimize(_vm.MinimizeToTray, TrayAvailable())) Hide();
        };
        Activated += (_, _) => { _vm.Update.WindowActive(true); _vm.SetupWindowActive(); _vm.RefreshOrkester(); _vm.RefreshCommands(); _vm.ArmLimitAnimations(); _ = _vm.RefreshAccountLimitsAsync(); };
        // Pick up an orchestra installed or removed outside the app (#189); idle while hidden or minimized.
        var orchestraTimer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(45) };
        orchestraTimer.Tick += (_, _) => { if (IsVisible && WindowState != WindowState.Minimized) _vm.RefreshOrkester(); };
        orchestraTimer.Start();
        Closed += (_, _) => orchestraTimer.Stop();
        // Plan limits of the Agent boxes (#200, #205): local file read only, every 30 seconds while visible.
        var limitsTimer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        limitsTimer.Tick += (_, _) => { if (IsVisible && WindowState != WindowState.Minimized) _ = _vm.RefreshAccountLimitsAsync(force: true); };
        limitsTimer.Start();
        Closed += (_, _) => limitsTimer.Stop();
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

    private void OnInstallOrkester(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is TabViewModel tab) ConfirmInstallOrkester(tab);
    }

    /// <summary>The install link and the start command list's install entry (#255) ask the same question.</summary>
    private async void ConfirmInstallOrkester(TabViewModel tab)
    {
        if (await new ConfirmInstallWindow(tab).ShowDialog<bool>(this)) _vm.InstallOrkester(tab);
    }

    private void OnOpenFolder(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is TabViewModel tab) _vm.OpenFolder(tab);
    }

    /// <summary>The row menu's Create subtab (#196): a new subtab last in the row's project, its name focused and selected.</summary>
    private void OnAddSubTab(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not TabViewModel tab) return;
        var child = _vm.AddSubTab(tab);
        Dispatcher.UIThread.Post(() => FocusSubTabName(child), DispatcherPriority.Loaded);
    }

    /// <summary>The row menu's Rename… and F2 (#258): the name cell turns into a box with the name selected.</summary>
    private void OnRenameRow(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is TabViewModel tab) BeginRename(tab);
    }

    /// <summary>The row menu's Arguments… (#260): a small dialog; Save stores the trimmed line, Cancel keeps the old value.</summary>
    private async void OnEditArguments(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not TabViewModel tab) return;
        var command = tab.Orkester ? StartCommands.Orkester : tab.Command;
        if (await new CommandArgsWindow(command, tab.CommandArgs).ShowDialog<string?>(this) is { } args) tab.CommandArgs = args;
    }

    internal bool BeginRename(TabViewModel tab)
    {
        if (_vm.IsClosingAll) return false;
        tab.BeginRename();
        // Posted after the menu has closed and given its focus back, so the box keeps the focus.
        Dispatcher.UIThread.Post(() => FocusRenameBox(tab), DispatcherPriority.Input);
        return true;
    }

    private bool FocusRenameBox(TabViewModel tab)
    {
        if (!tab.IsRenaming || RenameBox(tab) is not { } box) return false;
        box.BringIntoView();
        if (!box.Focus(NavigationMethod.Tab)) return false;
        box.SelectAll();
        return true;
    }

    private TextBox? RenameBox(TabViewModel tab) =>
        this.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(b => b.Classes.Contains("rename") && b.DataContext == tab);

    /// <summary>Enter saves (an invalid name keeps the box open with the reason), Esc cancels; focus then returns to the row.</summary>
    private void OnRenameKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: TabViewModel tab } box || e.KeyModifiers != KeyModifiers.None) return;
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            if (tab.CommitRename()) FocusRowGrip(box);
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            tab.EndRename();
            FocusRowGrip(box);
        }
    }

    /// <summary>Leaving the box saves a valid name; an invalid one is dropped (the reason goes to the status line), so a
    /// half-typed name never stays open or reaches the config.</summary>
    private void OnRenameLostFocus(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not TabViewModel { IsRenaming: true } tab) return;
        if (!tab.CommitRename()) tab.EndRename();
    }

    private static void FocusRowGrip(Control from)
    {
        var row = from.GetVisualAncestors().OfType<Border>().FirstOrDefault(b => b.Classes.Contains("row"));
        row?.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Classes.Contains("grip"))?.Focus(NavigationMethod.Tab);
    }

    internal bool FocusSubTabName(TabViewModel tab)
    {
        if (this.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(b => b.Classes.Contains("name") && b.DataContext == tab) is not { } box) return false;
        box.BringIntoView();
        if (!box.Focus(NavigationMethod.Tab)) return false;
        box.SelectAll();
        return true;
    }

    /// <summary>
    /// The row's ⋯ (#196) opens the same menu as a right-click, below the button. The menu is shared, so its placement goes
    /// back to the pointer when it closes, and a later right-click opens at the pointer again.
    /// </summary>
    private void OnRowMore(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.GetVisualAncestors().OfType<Border>().FirstOrDefault(b => b.Classes.Contains("row")) is not { ContextMenu: { } menu } row) return;
        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.BottomEdgeAlignedRight;
        menu.Open(row);
        e.Handled = true;
    }

    private void OnRowMenuClosed(object? sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu) return;
        menu.PlacementTarget = null;
        menu.Placement = PlacementMode.Pointer;
    }

    private void OnRemoveSubTab(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is TabViewModel tab) _vm.RemoveSubTab(tab);
    }

    /// <summary>Alt+Up/Down moves the row holding the focus one step and saves the order (#156). A ComboBox keeps its own
    /// Alt+Down (open the list): it handles the key first, so a handled event is left alone. Enter on a live row (its grip
    /// or status line has the focus) shows its terminal tab (#253); buttons and lists handle their own Enter first, and a
    /// subtab's name box keeps Enter as before.</summary>
    private void OnRowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || (sender as Control)?.DataContext is not TabViewModel tab) return;
        // F2 typed into a text box on the row (a subtab's name, the start command's arguments) stays there (#263).
        if (e.Key == Key.F2 && e.KeyModifiers == KeyModifiers.None && !tab.IsRenaming && e.Source is not TextBox)
        {
            e.Handled = true;
            BeginRename(tab);
            return;
        }
        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None && e.Source is not TextBox && tab.CanShowTerminal)
        {
            e.Handled = true;
            ShowTerminal(tab);
            return;
        }
        if (e.KeyModifiers != KeyModifiers.Alt || e.Key is not (Key.Up or Key.Down)) return;
        e.Handled = true;
        MoveRow(tab, e.Key == Key.Up ? -1 : 1);
    }

    /// <summary>A left press on a free part of a live row (#253): the click completes on release, unless the pointer moved.</summary>
    private (Border Row, Point At)? _rowPress;
    /// <summary>How far the pointer may move between press and release and still count as a click, not a drag.</summary>
    private const double RowClickSlop = 4;

    private void OnRowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _rowPress = null;
        if (sender is Border { DataContext: TabViewModel { CanShowTerminal: true } } row && e.GetCurrentPoint(row).Properties.IsLeftButtonPressed
            && IsFreeRowArea(row, e.Source as Visual))
            _rowPress = (row, e.GetPosition(row));
    }

    private void OnRowPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_rowPress is not { } press || !ReferenceEquals(sender, press.Row)) return;
        _rowPress = null;
        var moved = e.GetPosition(press.Row) - press.At;
        if (e.InitialPressMouseButton != MouseButton.Left || Math.Abs(moved.X) > RowClickSlop || Math.Abs(moved.Y) > RowClickSlop
            || !IsFreeRowArea(press.Row, e.Source as Visual) || press.Row.DataContext is not TabViewModel { CanShowTerminal: true } tab) return;
        e.Handled = true;
        ShowTerminal(tab);
    }

    /// <summary>
    /// Whether <paramref name="source"/> is a free part of <paramref name="row"/> (#253): the row's background, name or other
    /// plain text. Its controls keep their own clicks: buttons (status, Start, ↻, Continue, folder, ×, the question badge, ⋯),
    /// the tick, the lists, the subtab's name box, the grip, the usage meters and the agent's status line. Anything in a popup
    /// (a list's dropdown, the status or question panel) is outside the row's visual tree.
    /// </summary>
    internal static bool IsFreeRowArea(Border row, Visual? source)
    {
        if (source is null || !(ReferenceEquals(source, row) || row.IsVisualAncestorOf(source))) return false;
        for (var v = source; v is not null && !ReferenceEquals(v, row); v = v.GetVisualParent())
            if (v is Button or ToggleButton or ComboBox or TextBox or MenuItem
                || v is StyledElement el && (el.Classes.Contains("grip") || el.Classes.Contains("limit-meter") || el.Classes.Contains("agent-status")))
                return false;
        return true;
    }

    /// <summary>Raised before a row's terminal tab is shown (#253), so tests can see which clicks and keys reach it.</summary>
    internal event Action<TabViewModel>? TerminalShown;

    /// <summary>The row click, Enter and the row menu all use the same path as the old terminal icon (#213).</summary>
    private void ShowTerminal(TabViewModel tab)
    {
        if (_vm.IsClosingAll) return;
        TerminalShown?.Invoke(tab);
        _vm.ShowTerminal(tab);
    }

    private void OnRowMoveUp(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is TabViewModel tab) MoveRow(tab, -1);
    }

    private void OnRowMoveDown(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is TabViewModel tab) MoveRow(tab, 1);
    }

    /// <summary>The list rebuilds its rows after a move, so the moved row's grip takes the focus back (Alt+Down again moves it on).</summary>
    private void MoveRow(TabViewModel tab, int delta)
    {
        if (!_vm.MoveStep(tab, delta)) return;
        Dispatcher.UIThread.Post(() => FocusGrip(tab), DispatcherPriority.Loaded);
    }

    internal bool FocusGrip(TabViewModel tab) =>
        this.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Classes.Contains("grip") && b.DataContext == tab)
            ?.Focus(NavigationMethod.Tab) == true;

    private async void OnTabDragStart(object? sender, PointerPressedEventArgs e)
    {
        // Only the left button drags; the right one opens the row menu (#156).
        if ((sender as Control)?.DataContext is not TabViewModel tab || !e.GetCurrentPoint(sender as Visual).Properties.IsLeftButtonPressed) return;
        var index = _vm.Tabs.IndexOf(tab);
        if (index < 0) return;
        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(TabDragFormat, index.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        e.Handled = true;
        _dragSource = tab;
        try { await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Move); }
        finally
        {
            _dragSource = null;
            ClearDropTarget();
        }
    }

    /// <summary>The row being dragged from this window, so the drop line can show where the drop really lands (#196).</summary>
    private TabViewModel? _dragSource;
    private Border? _dropMarked;

    private void OnTabDragOver(object? sender, DragEventArgs e)
    {
        if (sender is not Border { DataContext: TabViewModel target } row || !e.DataTransfer.Contains(TabDragFormat)
            || DropMark(row, _dragSource, target, e.GetPosition(row).Y < row.Bounds.Height / 2) is not { } mark)
        {
            ClearDropTarget();
            e.DragEffects = DragDropEffects.None;
            return;
        }
        e.DragEffects = DragDropEffects.Move;
        SetDropTarget(mark.Border, mark.Before);
        e.Handled = true;
    }

    /// <summary>
    /// Where the drop line goes: a subtab moves within its own project, so the line sits on the row inside the card; a
    /// project lands before or after a whole group (MoveTabRelative), so the line marks the card's top or bottom edge.
    /// Null where the drop would do nothing (a subtab over another project, a project over its own group).
    /// </summary>
    internal (Border Border, bool Before)? DropMark(Border row, TabViewModel? source, TabViewModel target, bool before)
    {
        if (source is null) return (row, before); // a drag from elsewhere: as before
        if (source.Parent is { } parent) return target.Parent == parent && target != source ? (row, before) : null;
        var project = target.Parent ?? target;
        if (project == source) return null;
        var edge = before ? project : _vm.Tabs.LastOrDefault(t => t == project || t.Parent == project) ?? project;
        var segment = this.GetVisualDescendants().OfType<Border>()
            .FirstOrDefault(b => b.Classes.Contains("segment") && b.DataContext == edge);
        return segment is null ? (row, before) : (segment, before);
    }

    private void OnTabDragLeave(object? sender, DragEventArgs e) => ClearDropTarget();

    private void OnTabDrop(object? sender, DragEventArgs e)
    {
        if (sender is not Border target || target.DataContext is not TabViewModel tab
            || !int.TryParse(e.DataTransfer.TryGetValue(TabDragFormat), out var sourceIndex)) return;

        var targetIndex = _vm.Tabs.IndexOf(tab);
        if (targetIndex < 0) return;
        var after = e.GetPosition(target).Y >= target.Bounds.Height / 2;
        if (sourceIndex < 0 || sourceIndex >= _vm.Tabs.Count) return;
        _vm.MoveTabRelative(_vm.Tabs[sourceIndex], tab, after);
        ClearDropTarget();
        e.DragEffects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void SetDropTarget(Border target, bool before)
    {
        var name = before ? "drop-before" : "drop-after";
        if (_dropMarked == target && target.Classes.Contains(name)) return;
        ClearDropTarget();
        target.Classes.Add(name);
        _dropMarked = target;
    }

    private void ClearDropTarget()
    {
        if (_dropMarked is not { } target) return;
        target.Classes.Remove("drop-before");
        target.Classes.Remove("drop-after");
        _dropMarked = null;
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
        catch (Exception ex) { _vm.ShowError(ErrorKind.Link, Loc.Format("status.openFailed", ex.Message), () => OnUpdatePage(sender, e)); }
    }

    private ReleaseNotesWindow? _news;

    /// <summary>Where What's new gets its data; tests replace it so no request leaves the machine.</summary>
    internal static Func<IReleaseNotesSource> NewsSource { get; set; } = ReleaseNotesSource.ForApp;

    /// <summary>The open What's new view, if any.</summary>
    internal ReleaseNotesWindow? News => _news;

    /// <summary>
    /// "Show more" in the update bar and the version link (#252): What's new in BoosterAI, from the running version to
    /// the offered release, or what is new since the version this one was updated from. An open view is reused.
    /// </summary>
    private async void OnUpdateMore(object? sender, RoutedEventArgs e)
    {
        var range = _vm.Update.MoreRange;
        if (_news is { } open && open.ViewModel.Range == range)
        {
            if (open.WindowState == WindowState.Minimized) open.WindowState = WindowState.Normal;
            open.Activate();
            return;
        }
        _news?.Close();
        var news = new ReleaseNotesWindow(new ReleaseNotesViewModel(NewsSource(), range, _vm.Update.OfferedRelease));
        _news = news;
        news.Closed += (_, _) => { if (_news == news) _news = null; };
        news.Show(this);
        await news.LoadAsync();
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

    private void OnStartTab(object? sender, RoutedEventArgs e)
    {
        if (!_vm.IsClosingAll && (sender as Control)?.DataContext is TabViewModel tab) _vm.StartTab(tab);
    }

    /// <summary>The usage meter lies over the Agent box's bottom edge (#202); a press on it opens the box, as one beside it would.</summary>
    private void OnLimitMeterPressed(object? sender, PointerPressedEventArgs e)
    {
        if ((sender as Control)?.Parent is Panel panel && panel.Children.OfType<ComboBox>().FirstOrDefault() is { IsEnabled: true } box
            && e.GetCurrentPoint(box).Properties.IsLeftButtonPressed)
        {
            box.Focus();
            box.IsDropDownOpen = true;
            e.Handled = true;
        }
    }

    private void OnContinue(object? sender, RoutedEventArgs e)
    {
        if (!_vm.IsClosingAll && (sender as Control)?.DataContext is TabViewModel tab) _vm.Restart(tab, resume: true);
    }

    private async void OnCloseAll(object? sender, RoutedEventArgs e)
    {
        if (_vm.CanCloseAll) await new CloseAllWindow(_vm.PrepareCloseAll()).ShowDialog(this);
    }

    private void OnShowTerminal(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is TabViewModel { CanShowTerminal: true } tab) ShowTerminal(tab);
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

    private void OnErrorFirst(object? sender, RoutedEventArgs e) => RunErrorAction(0);

    private void OnErrorSecond(object? sender, RoutedEventArgs e) => RunErrorAction(1);

    private void OnErrorClose(object? sender, RoutedEventArgs e) => _vm.Error.Hide();

    private void OnQuotaClose(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is QuotaNotice notice) _vm.Quota.Close(notice);
    }

    private async void RunErrorAction(int index)
    {
        var error = _vm.Error;
        if (!error.IsVisible || index >= error.Actions.Count) return;
        var action = error.Actions[index];
        switch (action)
        {
            case ErrorAction.Retry:
                error.Retry();
                break;
            case ErrorAction.Details:
                if (error.Tab is { } tab) OpenStatusPeek(tab);
                break;
            default:
                // The banner stays until closed: the fix in Settings does not by itself show that the cause is gone.
                var settings = new SettingsWindow(_vm);
                switch (action)
                {
                    case ErrorAction.TerminalSettings when OperatingSystem.IsWindows():
                        settings.SelectedTabIndex = 2;
                        settings.SelectedOptimizationSectionIndex = 2;
                        break;
                    case ErrorAction.TerminalSettings: settings.SelectedTabIndex = 1; break;
                    case ErrorAction.AgentSettings: settings.SelectedTabIndex = 4; break;
                    default: settings.SelectedTabIndex = 0; break;
                }
                await settings.ShowDialog(this);
                break;
        }
    }
}
