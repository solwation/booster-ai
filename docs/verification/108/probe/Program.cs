using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Avalonia.Threading;
using BoosterAI;
using System.Text.Json;

var language = args.FirstOrDefault() ?? "sv";
var theme = args.Skip(1).FirstOrDefault() ?? "light";
var output = Path.GetFullPath(args.Skip(2).FirstOrDefault() ?? "docs/verification/108");
var temporary = Path.Combine(Path.GetTempPath(), "booster-remove-project-probe-" + Guid.NewGuid() + ".json");
var cfg = new AppConfig { Language = language, AppTheme = theme, CheckForUpdates = false,
    Tabs = [new TabConfig { Name = "Atlas", Directory = "/sample/atlas", SubTabs = [new TabConfig { Name = "Review" }] }, new TabConfig { Name = "Harbor" }] };
ConfigStore.UseFile(temporary);
ConfigStore.Save(cfg);
Screenshot.Parse(["--screenshot", "/unused.png", "--config", temporary]);
AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
var vm = new MainViewModel(cfg);
var settings = new SettingsWindow(vm) { Width = 640, Height = 400 };
var result = new Dictionary<string, bool>();
try
{
    settings.Show(); Pump();
    var project = vm.Tabs[0];
    var child = vm.Tabs[1];
    Button Remove() => settings.GetVisualDescendants().OfType<Button>().Single(b => b.DataContext == project && b.Classes.Contains("remove"));
    void Open()
    {
        Require(Remove().IsEffectivelyEnabled, "Inactive remove control enabled");
        Remove().Focus();
        settings.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
        settings.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
        Pump();
    }
    ConfirmRemoveProjectWindow Dialog() => settings.OwnedWindows.OfType<ConfirmRemoveProjectWindow>().Single(w => w.IsVisible);
    void Intact() { Pump(); Require(vm.Tabs.Count == 3 && vm.Tabs[0] == project && vm.Tabs[1] == child, "Group preserved after cancel"); }

    child.SetState(TabState.Running, null);
    Pump();
    Require(!Remove().IsEffectivelyEnabled && !Remove().Focus(), "Active child disables removal and focus");
    Require(settings.GetVisualDescendants().OfType<TextBlock>().Any(t => t.IsEffectivelyVisible && t.Text == project.ProjectRemovalHint), "Blocked reason visible");
    Capture(settings, $"blocked-{language}-{theme}-minimum.png");
    child.SetState(TabState.CliExited, 0);
    child.Track = new TabTrack("/sample/tab-lib.sh", "/sample", "synthetic");
    Pump();
    Require(!Remove().IsEffectivelyEnabled, "Open idle shell remains blocked");
    child.SetState(TabState.Closed, 0);
    Pump();
    Require(Remove().IsEffectivelyEnabled, "Closed shell permits confirmation");

    Open();
    var dialog = Dialog();
    Require(dialog.DataContext is ProjectRemovalConfirmation { ProjectName: "Atlas", SubTabCount: 1 }, "Dialog receives exact group scope");
    Require(dialog.FindControl<Button>("CancelRemoval")!.IsDefault && dialog.FindControl<Button>("CancelRemoval")!.IsCancel, "Cancel is default and Escape action");
    Require(dialog.FindControl<Button>("CancelRemoval")!.IsFocused, "Initial focus stays on Cancel");
    Capture(dialog, $"confirm-{language}-{theme}.png");
    dialog.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None); settings.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None); Intact();
    Require(!dialog.IsVisible, "Escape closes confirmation");

    Open(); Dialog().Close(); Intact();
    Open();
    Dialog().FindControl<Button>("CancelRemoval")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Intact();
    Open();
    dialog = Dialog();
    dialog.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None); settings.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None); Intact();
    Open();
    dialog = Dialog();
    dialog.FindControl<Button>("ConfirmRemoval")!.Focus();
    dialog.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None); dialog.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None); Pump();
    Require(vm.Tabs.Count == 1 && vm.Tabs[0].Name == "Harbor", "Explicit keyboard confirmation removes only the target group");
    Require(ConfigStore.Load().Tabs.Count == 1, "Removal persisted");
    Console.WriteLine(JsonSerializer.Serialize(new { language, theme, synthetic = true, checks = result }, new JsonSerializerOptions { WriteIndented = true }));
}
finally
{
    foreach (var window in settings.OwnedWindows.ToArray()) window.Close();
    settings.Close();
    File.Delete(temporary);
}
void Require(bool condition, string label)
{
    result[label] = condition;
    if (!condition) throw new InvalidOperationException(label);
}
void Pump()
{
    for (var i = 0; i < 4; i++) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(15); }
}
void Capture(Window window, string name)
{
    window.UpdateLayout(); Pump(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    Directory.CreateDirectory(output);
    using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No headless frame");
    frame.Save(Path.Combine(output, name));
}
