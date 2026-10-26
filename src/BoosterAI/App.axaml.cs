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
        // --screenshot builds its window itself (Screenshot.Run), headless and never shown.
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && !Screenshot.Active)
        {
            // A minimized window keeps the app running; closing it (or Quit in the tray) ends it.
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            var vm = new MainViewModel(_config);
            desktop.MainWindow = new MainWindow(vm);
            vm.Update.Start();
        }
        base.OnFrameworkInitializationCompleted();
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
                quit.Click += (_, _) => window.Close();
                TrayIcon.SetIcons(this, [_tray]);
            }
            _tray.IsVisible = true;
            return true;
        }
        catch (Exception) { return false; }
    }

    private static void Restore(Window window)
    {
        window.Show();
        window.WindowState = WindowState.Normal;
        window.Activate();
    }
}
