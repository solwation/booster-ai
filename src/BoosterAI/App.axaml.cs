using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Markup.Xaml;

namespace BoosterAI;

public partial class App : Application
{
    private readonly AppConfig _config = ConfigStore.Load();

    public AppConfig Config => _config;

    public override void Initialize()
    {
        // The language must be set before any XAML (and its {local:T} texts) loads.
        Loc.Init(_config.Language);
        AvaloniaXamlLoader.Load(this);
        ApplyTheme();
    }

    /// <summary>Maps the config value to a theme variant: light, dark, anything else (auto, missing, unknown) follows the system.</summary>
    public static Avalonia.Styling.ThemeVariant ThemeFor(string? value) => value switch
    {
        "light" => Avalonia.Styling.ThemeVariant.Light,
        "dark" => Avalonia.Styling.ThemeVariant.Dark,
        _ => Avalonia.Styling.ThemeVariant.Default,
    };

    /// <summary>The value after --theme on the command line, or null when the flag is absent.</summary>
    public static string? ThemeArg(string[] args)
    {
        var i = Array.IndexOf(args, "--theme");
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    /// <summary>The effective theme: a --theme light|dark flag wins over the config's AppTheme.</summary>
    public static Avalonia.Styling.ThemeVariant ResolveTheme(string[] args, string? configValue) =>
        ThemeArg(args) is "light" or "dark" ? ThemeFor(ThemeArg(args)) : ThemeFor(configValue);

    /// <summary>Applies the effective theme to the whole application, so every open window follows at once.</summary>
    public void ApplyTheme() =>
        RequestedThemeVariant = ResolveTheme(Environment.GetCommandLineArgs(), _config.AppTheme);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime setup && !Screenshot.Active && Program.Setup != SetupMode.None)
        {
            setup.ShutdownMode = ShutdownMode.OnMainWindowClose;
            setup.MainWindow = Program.Setup == SetupMode.Install ? CreateWizard() : CreateUninstall();
            base.OnFrameworkInitializationCompleted();
            return;
        }
        // --screenshot builds its window itself (Screenshot.Run), headless and never shown.
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && !Screenshot.Active)
        {
            // A minimized or tray-hidden window keeps the app running; closing it (with Minimize to tray off), or Quit in the tray, ends it.
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            var vm = new MainViewModel(_config);
            var main = new MainWindow(vm, background: desktop.Args?.Contains("--background") == true);
            main.RememberSize();
            desktop.MainWindow = main;
            // Queue early requests until the window has opened, including the initial background hide.
            main.Opened += (_, _) => Program.Instance?.RegisterActivation(() =>
                Avalonia.Threading.Dispatcher.UIThread.Post(() => Restore(main)));
            Launcher.RestoreZoomOnStartup(_config.ZoomPercent);
            if (OperatingSystem.IsWindows()) Installer.RefreshUninstallEntry();
            vm.Update.Start();
            vm.FindRunningTabs();
            vm.StartSetupChecks();
            vm.Optimization.StartMemoryMonitoring();
            desktop.Exit += (_, _) => vm.Optimization.StopMemoryMonitoring();
        }
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>The setup wizard for this user (#131), prefilled from the config on disk; also used by --screenshot.</summary>
    public InstallWizardWindow CreateWizard(InstallPlan? plan = null, bool? installed = null)
    {
        plan ??= Installer.CurrentPlan();
        var vm = new InstallWizardViewModel(plan, ConfigStore.TryRead(), installed ?? File.Exists(plan.Exe),
            Screenshot.Active ? _ => { } : id => RequestedThemeVariant = ThemeFor(id));
        // The agents' installed state comes from the same shell check as the main window's; screenshots set a synthetic one.
        if (!Screenshot.Active)
        {
            var distro = ConfigStore.TryRead()?.WslDistro ?? new AppConfig().WslDistro;
            _ = vm.DetectAsync(() => Prerequisites.Detect(distro));
        }
        return new InstallWizardWindow(vm);
    }

    /// <summary>The --uninstall confirmation; confirming removes the install and ends the app.</summary>
    private static Window CreateUninstall()
    {
        var plan = Installer.CurrentPlan();
        var confirm = ConfirmSetupWindow.Uninstall(plan);
        confirm.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        confirm.Closed += (_, _) =>
        {
            if (!confirm.Confirmed || !OperatingSystem.IsWindows()) return;
            var problems = Installer.Uninstall(plan);
            if (problems.Count == 0) return;
            try { File.AppendAllText(Path.Combine(Path.GetDirectoryName(ConfigStore.PathOnDisk)!, "uninstall.log"),
                DateTime.Now.ToString("o") + " " + string.Join("; ", problems) + Environment.NewLine); }
            catch (Exception) { }
        };
        return confirm;
    }

    private TrayIcon? _tray;

    /// <summary>Shows the tray icon that brings <paramref name="window"/> back; false if there is none.</summary>
    public bool ShowTray(Window window)
    {
        try
        {
            if (_tray is null)
            {
                using var icon = AssetLoader.Open(new Uri("avares://BoosterAI/Assets/boosterai.ico"));
                var show = new NativeMenuItem(Loc.Get("tray.show"));
                var quit = new NativeMenuItem(Loc.Get("tray.quit"));
                _tray = new TrayIcon { Icon = new WindowIcon(icon), ToolTipText = Loc.Get("tray.tip"), Menu = [show, quit] };
                _tray.Clicked += (_, _) => Restore(window);
                show.Click += (_, _) => Restore(window);
                quit.Click += (_, _) => Quit();
                TrayIcon.SetIcons(this, [_tray]);
            }
            _tray.IsVisible = true;
            return true;
        }
        catch (Exception) { return false; }
    }

    /// <summary>Set by the tray's Quit so the close it causes is not turned into hide-to-tray.</summary>
    public static bool QuitRequested { get; private set; }

    private void Quit()
    {
        QuitRequested = true;
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.Shutdown();
    }

    private static void Restore(Window window)
    {
        window.ShowActivated = true;
        if (window.WindowState == WindowState.Minimized)
        {
            if (window is MainWindow main) main.RestoreSizeState();
            else window.WindowState = WindowState.Normal;
        }
        window.Show();
        window.Activate();
    }
}
