using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;

namespace BoosterAI;

/// <summary>
/// <c>--screenshot file.png [--window main|settings|install] [--theme light|dark] [--config file.json]</c>:
/// renders a window with Avalonia's headless Skia platform. Nothing is shown on the desktop and no
/// focus changes. The window frame (title bar) is not drawn; only the client area.
/// </summary>
internal sealed record ScreenshotOptions(string File, string Window);

internal static class Screenshot
{
    public static bool Active { get; private set; }

    public static ScreenshotOptions? Parse(string[] args)
    {
        var file = Value(args, "--screenshot");
        if (file is null) return null;
        if (Value(args, "--config") is { } cfg) ConfigStore.UseFile(cfg);
        Active = true;
        return new ScreenshotOptions(file, Value(args, "--window") ?? "main");
    }

    private static string? Value(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    public static int Run(ScreenshotOptions o)
    {
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();
        var app = (App)Application.Current!;
        // Sample tab statuses, as before (the real window shows the real ones).
        var vm = new MainViewModel(app.Config);
        vm.ShowSampleStatus();
        Window window = o.Window switch
        {
            "main" => new MainWindow(vm),
            "settings" => new SettingsWindow(vm),
            "install" => new ConfirmInstallWindow(vm.Tabs.First()),
            _ => throw new ArgumentException($"Unknown --window '{o.Window}' (main, settings or install)"),
        };
        window.Show();
        // The orchestra check runs in the background; give it a moment so the toggles show their real state.
        for (var i = 0; i < 40 && vm.Tabs.Any(t => t.HasOrkester is null && t.Directory.Trim() != ""); i++)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(50);
        }
        Dispatcher.UIThread.RunJobs();
        for (var i = 0; i < 3; i++)
        {
            Thread.Sleep(30);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
        var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No frame was rendered");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(o.File))!);
        frame.Save(o.File);
        return 0;
    }
}
