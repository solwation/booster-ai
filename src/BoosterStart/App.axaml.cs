using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace BoosterStart;

public partial class App : Application
{
    private readonly AppConfig _config = ConfigStore.Load();

    public override void Initialize()
    {
        // The language must be set before any XAML (and its {local:T} texts) loads.
        Loc.Init(_config.Language);
        AvaloniaXamlLoader.Load(this);
        // --theme light|dark overrides the system theme (also used for screenshots).
        var args = Environment.GetCommandLineArgs();
        var i = Array.IndexOf(args, "--theme");
        if (i >= 0 && i + 1 < args.Length)
            RequestedThemeVariant = args[i + 1] switch
            {
                "light" => Avalonia.Styling.ThemeVariant.Light,
                "dark" => Avalonia.Styling.ThemeVariant.Dark,
                _ => RequestedThemeVariant,
            };
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow(new MainViewModel(_config));
        base.OnFrameworkInitializationCompleted();
    }
}
