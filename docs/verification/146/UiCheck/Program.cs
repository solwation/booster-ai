using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BoosterAI;

var root = Path.Combine(Path.GetTempPath(), "booster-146-ui-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
ConfigStore.UseFile(Path.Combine(root, "config.json"));
ConfigStore.Save(new AppConfig { Language = "sv", Tabs = [] });
AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
var checks = 0;
void Require(bool condition, string what) { if (!condition) throw new Exception(what); checks++; }
void Jobs() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
void Click(Button button) { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Jobs(); }
try
{
    foreach (var language in new[] { "sv", "en" })
    {
        Loc.Use(language);
        var main = new MainViewModel(new AppConfig { Tabs = [] });
        main.ShowSampleAgents(localClisMissing: true);
        var settings = new SettingsWindow(main);
        settings.SelectedTabIndex = 4;
        settings.Show(); Jobs();
        var link = settings.FindControl<Button>("LocalModelsButton")!;
        Require(link.IsVisible && link.IsEnabled, "Models link with missing agent programs");
        Click(link);
        var manager = settings.OwnedWindows.OfType<LocalModelsWindow>().Single();
        Require(manager.Title == Loc.Get("models.title"), "Separate model manager title");
        Require(main.Setup.SelectedAgent is null, "Opening manager does not select or install an agent");
        manager.Close(); Jobs(); Require(!manager.ViewModel.CanEdit, "Closing manager disposes hardware subscription"); settings.Close(); Jobs();
        foreach (var minimum in new[] { false, true })
        {
            var hardware = new SettingsHardwareAdviceState(_ => Task.FromResult(new HardwareSnapshot(64 * LocalModelAdvice.GiB, 40 * LocalModelAdvice.GiB, "Example CPU", 16, [])));
            var calls = 0; var inventory = 0;
            var model = new LocalModelsViewModel(hardware,
                (_, _) => Task.FromResult(new OllamaSnapshot(++inventory == 1 ? [] : [new(DownloadableModels.All[1].Id, "sample", 3_300_000_000, false)], [], true)),
                (_, _, progress, _) => { calls++; progress(new("pulling", 42, 100)); return Task.CompletedTask; });
            var window = new LocalModelsWindow(model) { Width = minimum ? 640 : 860, Height = minimum ? 400 : 790 };
            window.Show(); Jobs(); Jobs(); Jobs();
            try
            {
                Require(window.FindControl<ScrollViewer>("ModelScroll")!.Offset.Y == 0, "Initial view keeps title visible");
                Require(calls == 0 && inventory == 0 && !hardware.HasReading, "Window opening does not probe or pull");
                Click(window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, Loc.Get("models.refresh"))));
                Require(inventory == 1 && hardware.HasReading, "Check button updates hardware and inventory");
                Require(model.Recommendation.Contains("qwen3.5:4b-q4_K_M"), "High RAM without measured GPU suggests small CPU model");
                var consent = window.GetVisualDescendants().OfType<CheckBox>().Single(); consent.IsChecked = true; Jobs();
                var download = window.FindControl<Button>("DownloadButton")!;
                Require(download.IsEnabled, "Explicit confirmation enables download");
                Click(download);
                Require(calls == 1 && inventory == 2 && model.Selected.Installed && !download.IsEnabled, "Download button refreshes exact installed inventory");
                Require(model.DownloadStatus.Contains(model.Selected.Id), "Successful result names exact downloaded model");
                var scroll = window.FindControl<ScrollViewer>("ModelScroll")!;
                scroll.Offset = new Vector(0, scroll.Extent.Height); Jobs();
                Require(scroll.Offset.Y > 0, "All content scrolls at minimum and normal sizes");
            }
            finally { window.Close(); Jobs(); }
        }
    }
    var shots = args.Length > 0 ? args[0] : root;
    Directory.CreateDirectory(shots);
    Loc.Use("sv");
    var step = 0;
    var sampleHardware = new SettingsHardwareAdviceState(_ => Task.FromResult(new HardwareSnapshot(64 * LocalModelAdvice.GiB, 40 * LocalModelAdvice.GiB, "Example CPU", 16, [])));
    var progressModel = new LocalModelsViewModel(sampleHardware,
        (_, _) => ++step == 1 ? throw new HttpRequestException("sample offline") : Task.FromResult(new OllamaSnapshot([], [], true)),
        async (_, _, progress, cancellation) => { progress(new("pulling", 1_386_000_000, 3_300_000_000)); await Task.Delay(Timeout.Infinite, cancellation); });
    var progressWindow = new LocalModelsWindow(progressModel);
    progressWindow.Show(); Jobs();
    try
    {
        var check = progressWindow.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, Loc.Get("models.refresh")));
        Click(check);
        Require(progressModel.Status == Loc.Get("models.unreachable"), "Actual UI reports missing server");
        var setup = progressWindow.GetVisualDescendants().OfType<Expander>().Single(x => Equals(x.Header, Loc.Get("models.setupAndInstalled")));
        setup.IsExpanded = true; Jobs();
        var scroll = progressWindow.FindControl<ScrollViewer>("ModelScroll")!;
        scroll.Offset = new Vector(0, scroll.Extent.Height); Jobs(); Jobs();
        progressWindow.CaptureRenderedFrame()!.Save(Path.Combine(shots, "models-missing.png"));
        Click(check);
        progressWindow.GetVisualDescendants().OfType<CheckBox>().Single().IsChecked = true;
        Click(progressWindow.FindControl<Button>("DownloadButton")!);
        Require(progressModel.Busy && progressModel.Percent == 42, "Actual UI reports in-progress layer");
        progressWindow.CaptureRenderedFrame()!.Save(Path.Combine(shots, "models-download.png"));
        Click(progressWindow.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, Loc.Get("models.cancel"))));
        for (var i = 0; i < 100 && progressModel.Busy; i++) { Jobs(); Thread.Sleep(5); }
        Require(!progressModel.Busy && progressModel.DownloadStatus == Loc.Get("models.downloadCancelled"), "Actual Cancel button stops client operation");
        Jobs(); progressWindow.CaptureRenderedFrame()!.Save(Path.Combine(shots, "models-cancelled.png"));
    }
    finally { progressWindow.Close(); Jobs(); }
    Console.WriteLine($"PASS {checks} headless controls/route assertions, sv/en and normal/minimum sizes; injected metadata/pulls only, no live model downloads or hardware probes.");
}
finally { Directory.Delete(root, true); }
