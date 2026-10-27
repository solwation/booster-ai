using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;

namespace BoosterAI;

/// <summary>
/// <c>--screenshot file.png [--window main|settings|install|import|restart] [--theme light|dark] [--config file.json] [--fake-update [manual:]0.3.0] [--fake-status [error:]text] [--fake-denied] [--fake-changed]</c>:
/// renders a window with Avalonia's headless Skia platform. Nothing is shown on the desktop and no
/// focus changes. The window frame (title bar) is not drawn; only the client area.
/// </summary>
internal sealed record ScreenshotOptions(string File, string Window, string? FakeUpdate, string? FakeStatus, bool FakeDenied, bool FakeChanged);

internal static class Screenshot
{
    public static bool Active { get; private set; }

    public static ScreenshotOptions? Parse(string[] args)
    {
        var file = Value(args, "--screenshot");
        if (file is null) return null;
        if (Value(args, "--config") is { } cfg) ConfigStore.UseFile(cfg);
        Active = true;
        return new ScreenshotOptions(file, Value(args, "--window") ?? "main", Value(args, "--fake-update"), Value(args, "--fake-status"), args.Contains("--fake-denied"),
            args.Contains("--fake-changed") || Value(args, "--window") == "restart");
    }

    private static string? Value(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static ImportViewModel SampleImport()
    {
        var vm = new ImportViewModel([]);
        vm.ShowSample(OperatingSystem.IsWindows() ? @"C:\Users\me\Documents" : "/home/me/Documents");
        return vm;
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
        // --fake-update 0.3.0 shows the update prompt as an installed copy would see it, manual:0.3.0 as a copy
        // that cannot update itself (no network, screenshots only).
        if (o.FakeUpdate is { } fake) vm.Update.ShowFake(fake);
        // --fake-denied shows the first tab with a denied agy command and the allow-rule offer.
        if (o.FakeDenied) vm.ShowSampleDenial();
        // --fake-changed: the first AI tab runs a turn with another model than the chosen one (--window restart implies it).
        if (o.FakeChanged) vm.ShowSampleChange();
        // --fake-status "text" shows a status line message as normal, error:text as an error (screenshots only).
        if (o.FakeStatus is { } st)
            vm.ShowStatus(st.StartsWith("error:") ? st[6..] : st, st.StartsWith("error:"));
        Window window = o.Window switch
        {
            "main" => new MainWindow(vm),
            "settings" => new SettingsWindow(vm),
            "install" => new ConfirmInstallWindow(vm.Tabs.First()),
            "import" => new ImportProjectsWindow(SampleImport()),
            "restart" => new ConfirmRestartWindow(vm.Tabs.First(t => t.IsChanged)),
            _ => throw new ArgumentException($"Unknown --window '{o.Window}' (main, settings, install, import or restart)"),
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
