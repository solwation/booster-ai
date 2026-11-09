using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Avalonia.Threading;

namespace BoosterAI;

/// <summary>
/// <c>--screenshot file.png [--window main|settings|install|import|restart|setup|uninstall] [--theme light|dark] [--config file.json] [--fake-update [manual:]0.3.0] [--fake-status [error:]text] [--fake-denied] [--fake-changed] [--fake-restartable N] [--fake-output [N]] [--fake-setup [cli|wsl|alias]]</c>:
/// renders a window with Avalonia's headless Skia platform. Nothing is shown on the desktop and no
/// focus changes. The window frame (title bar) is not drawn; only the client area.
/// </summary>
internal sealed record ScreenshotOptions(string File, string Window, string? FakeUpdate, string? FakeStatus, bool FakeDenied, bool FakeChanged,
    int? FakeOutput = null, int FakeRestartable = 0, string? FakeSetup = null, string? Tab = null, bool FakeOptimization = false, int? FakeMemoryCap = null,
    string? Section = null, string? FakeMemoryState = null, int? Width = null, int? Height = null, bool FakeOrchestraIdle = false, int? FakeMemorySaved = null);

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
            args.Contains("--fake-changed") || Value(args, "--window") == "restart", FakeOutputIndex(args),
            int.TryParse(Value(args, "--fake-restartable"), out var r) ? r : 0,
            args.Contains("--fake-setup") ? (Value(args, "--fake-setup") is "wsl" or "alias" ? Value(args, "--fake-setup") : "cli") : null,
            Value(args, "--tab"), args.Contains("--fake-optimization"),
            int.TryParse(Value(args, "--fake-memory-cap"), out var cap) ? cap : null,
            Value(args, "--section"), Value(args, "--fake-memory-state"),
            int.TryParse(Value(args, "--width"), out var width) ? width : null,
            int.TryParse(Value(args, "--height"), out var height) ? height : null, args.Contains("--fake-orchestra-idle"),
            int.TryParse(Value(args, "--fake-memory-saved"), out var saved) ? saved : null);
    }

    /// <summary>--fake-output [N]: the popup of the Nth running sample tab (0: the first, interactive; 1: Watching).</summary>
    private static int? FakeOutputIndex(string[] args) =>
        !args.Contains("--fake-output") ? null : int.TryParse(Value(args, "--fake-output"), out var n) ? n : 0;

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

    private static AgentInfo SampleYolo()
    {
        var agent = new AgentInfo(new("codex", "file", "/sample/bin/codex")
        { Help = "--dangerously-bypass-approvals-and-sandbox" });
        agent.SelectedMode = agent.ModeChoices.Single(c => c.Mode == AgentMode.Yolo);
        return agent;
    }

    /// <summary>
    /// --window setup [--section welcome|settings|projects|install|progress|done|upgrade]: the setup wizard with a
    /// neutral sample plan (C:\Users\me\...) and, for projects and later, a fixed project list; no disk scan, nothing installed.
    /// </summary>
    private static InstallWizardWindow SampleWizard(App app, string? section)
    {
        var window = app.CreateWizard(Installer.SamplePlan(), installed: section == "upgrade");
        var vm = (InstallWizardViewModel)((Control)window.Content!).DataContext!;
        vm.SearchRootDefault = @"C:\Users\me\Documents";
        var step = section switch
        {
            null or "welcome" or "upgrade" => InstallWizardViewModel.Step.Welcome,
            "settings" => InstallWizardViewModel.Step.Settings,
            "projects" => InstallWizardViewModel.Step.Projects,
            "install" or "progress" => InstallWizardViewModel.Step.Install,
            "done" => InstallWizardViewModel.Step.Done,
            _ => throw new ArgumentException($"Unknown --section '{section}' for --window setup (welcome, settings, projects, install, progress, done or upgrade)"),
        };
        if (step >= InstallWizardViewModel.Step.Projects) vm.ShowSampleProjects(@"C:\Users\me\Documents");
        if (section == "progress") vm.ShowSampleProgress();
        vm.Current = step;
        return window;
    }

    private static LocalModelWindow SampleLocalModel(string? state)
    {
        var vm = new LocalModelViewModel("aider", "ollama/example-coder:7b-q4");
        if (state is not null) vm.ShowSample(state);
        return new LocalModelWindow(vm);
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
        // --fake-restartable N: the first N AI tabs have ended, so "Restart all" shows beside Start.
        if (o.FakeRestartable > 0) vm.ShowSampleRestartable(o.FakeRestartable);
        // --fake-setup [cli|wsl|alias]: the setup bar with codex and agy missing, WSL missing, or the full-auto alias offer.
        if (o.FakeSetup == "alias") vm.ShowSampleAlias();
        else if (o.FakeSetup is { } setup) vm.ShowSampleSetup(wslMissing: setup == "wsl");
        // --fake-optimization shows the WSL optimization banner on the main page.
        if (o.FakeOptimization) vm.Optimization.ShowFakeUnoptimized(selectedGb: o.FakeMemoryCap, savedGb: o.FakeMemorySaved);
        // --fake-status "text" shows a status line message as normal, error:text as an error (screenshots only).
        if (o.FakeStatus is { } st)
            vm.ShowStatus(st.StartsWith("error:") ? st[6..] : st, st.StartsWith("error:"));
        Window window = o.Window switch
        {
            "main" => new MainWindow(vm),
            "local-model" => SampleLocalModel(o.Section),
            "close" => new CloseAllWindow(vm.PrepareCloseAll(sample: true)),
            "settings" => new SettingsWindow(vm),
            "install" => new ConfirmInstallWindow(vm.Tabs.First()),
            "import" => new ImportProjectsWindow(SampleImport()),
            "restart" => new ConfirmRestartWindow(vm.Tabs.First(t => t.IsChanged)),
            "remove-project" => new ConfirmRemoveProjectWindow(new(vm.Projects.First().Name, vm.Tabs.Count(t => t.Parent == vm.Projects.First()))),
            "alias" => ConfirmSetupWindow.Aliases(["claude", "codex"]),
            "wsl-shutdown" => ConfirmSetupWindow.ShutdownWsl(),
            "agent-yolo" => ConfirmSetupWindow.AgentMode(SampleYolo()),
            "wsl" => ConfirmSetupWindow.Wsl(),
            "setup" => SampleWizard(app, o.Section),
            "uninstall" => ConfirmSetupWindow.Uninstall(Installer.SamplePlan()),
            _ => throw new ArgumentException($"Unknown --window '{o.Window}' (main, settings, install, import, restart, alias, wsl, setup or uninstall)"),
        };
        if (window is SettingsWindow sw)
        {
            if (o.Tab is "general" or "allmant") sw.SelectedTabIndex = 1;
            else if (o.Tab is "optimization" or "optimering")
            {
                sw.SelectedTabIndex = 2;
                vm.Optimization.ShowFakeUnoptimized(selectedGb: o.FakeMemoryCap, savedGb: o.FakeMemorySaved);
                sw.SelectedOptimizationSectionIndex = o.Section switch { "limit" => 1, "environment" => 2, "compact" => 3, _ => 0 };
                if (o.FakeMemoryState is { } state)
                {
                    const long gb = 1024L * 1024 * 1024;
                    vm.Optimization.SetMemorySnapshot(state switch
                    {
                        "empty" => new(WslMemoryState.NotRunning, null, null, null, null, TimeSpan.Zero, 0),
                        "partial" => new(WslMemoryState.Available, 12 * gb, 10 * gb, 8 * gb, 12 * gb, TimeSpan.FromMinutes(2), 5),
                        "stale" => new(WslMemoryState.Stale, null, 10 * gb, 8 * gb, 12 * gb, TimeSpan.FromMinutes(2), 5),
                        "legacy" => new(WslMemoryState.LegacyProcess, null, null, null, null, TimeSpan.Zero, 0),
                        "outside" => new(WslMemoryState.Available, 70 * gb, 55 * gb, 40 * gb, 70 * gb, TimeSpan.FromMinutes(30), 61),
                        _ => throw new ArgumentException("Unknown --fake-memory-state: " + state),
                    });
                }
            }
            else if (o.Tab is "agents" or "agenter")
            {
                vm.ShowSampleAgents(localClisMissing: o.Section == "missing" || o.Section?.Contains("-missing", StringComparison.Ordinal) == true);
                const long gib = LocalModelAdvice.GiB;
                vm.Setup.HardwareAdvice.SetSnapshot(new(16 * gib, 3 * gib, "Example CPU", 8, [new("Example GPU", 8 * gib, 6 * gib)]));
                sw.SelectedTabIndex = 4;
                if (o.Section is { } agentSection)
                {
                    sw.SelectedAgentCli = agentSection.Split('-')[0];
                    if (vm.Setup.SelectedAgent is { } agent)
                    {
                        agent.Section = agentSection.EndsWith("-compact") ? 2 : agentSection.EndsWith("-permissions") ? 1 : 0;
                        var profile = agentSection.EndsWith("-cpu") ? "cpu7b" : agentSection.EndsWith("-gpu") ? "gpu35b" : "none";
                        agent.Advice.Profile = agent.Advice.Profiles.FirstOrDefault(p => p.Id == profile) ?? agent.Advice.Profiles[0];
                    }
                }
            }
            else if (o.Tab is "appearance" or "utseende") sw.SelectedTabIndex = 3;
            else if (o.Tab is "projects" or "projekt") sw.SelectedTabIndex = 0;
            else if (o.FakeSetup is not null && o.Tab is null) sw.SelectedTabIndex = 1;
        }
        // Room below the first rows for the status popup.
        if (o.FakeOutput is not null) window.Height = 820;
        if (o.Width is { } shotWidth) window.Width = Math.Max(window.MinWidth, shotWidth);
        if (o.Height is { } shotHeight) window.Height = Math.Max(window.MinHeight, shotHeight);
        window.Show();
        // The orchestra check runs in the background; give it a moment so the toggles show their real state.
        for (var i = 0; i < 40 && vm.Tabs.Any(t => t.HasOrkester is null && t.Directory.Trim() != ""); i++)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(50);
        }
        if (o.FakeOrchestraIdle) vm.ShowSampleOrchestraIdle();
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
        // --fake-output [N]: a running tab's status popup open over the window, with sample output.
        if (o.FakeOutput is { } n && vm.ShowSampleOutput(n) is { } tab) WithStatusPopup(window, frame, tab).Save(o.File);
        else frame.Save(o.File);
        return 0;
    }

    /// <summary>
    /// Opens <paramref name="tab"/>'s status popup and draws it onto the window frame below the status, as a
    /// hover would show it (a headless popup is its own surface, not part of the window's frame).
    /// </summary>
    private static Bitmap WithStatusPopup(Window window, Bitmap frame, TabViewModel tab)
    {
        var target = window.GetVisualDescendants().OfType<Control>()
            .First(c => c.DataContext == tab && ToolTip.GetTip(c) is ToolTip);
        var tip = (ToolTip)ToolTip.GetTip(target)!;
        ToolTip.SetIsOpen(target, true);
        for (var i = 0; i < 3; i++)
        {
            Thread.Sleep(30);
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
        if (!tip.IsVisible || tip.Bounds.Width <= 0) throw new InvalidOperationException("The status popup did not open");
        // The headless popup surface is window-sized, so render the tip itself.
        var size = tip.Bounds.Size;
        var popupFrame = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height)), frame.Dpi);
        popupFrame.Render(tip);
        var cell = target.TranslatePoint(new Point(0, target.Bounds.Height), window) ?? default;
        var x = Math.Max(8, Math.Min(cell.X, window.ClientSize.Width - size.Width - 8));
        var y = Math.Max(8, Math.Min(cell.Y + 6, window.ClientSize.Height - size.Height - 8));
        var result = new RenderTargetBitmap(frame.PixelSize, frame.Dpi);
        using (var ctx = result.CreateDrawingContext())
        {
            ctx.DrawImage(frame, new Rect(frame.Size));
            ctx.DrawImage(popupFrame, new Rect(new Point(x, y), size));
        }
        return result;
    }
}
