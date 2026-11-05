using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BoosterAI;
using System.Reflection;
using System.Text.Json;

var file = Path.Combine(Path.GetTempPath(), "booster109-" + Guid.NewGuid() + ".json");
ConfigStore.UseFile(file);
AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
var memoryFile = Path.ChangeExtension(file, ".ini");
WslConfigFile.UseCustomPath(memoryFile);
var vm = new MainViewModel(new AppConfig { CheckForUpdates = false });
var calls = 0;
typeof(OptimizationViewModel).GetProperty("ShutdownWsl", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(vm.Optimization, (Action)(() => calls++));
vm.Optimization.SaveConfig();
if (calls != 0 || !File.Exists(memoryFile)) throw new Exception("Saving memory must not shut down WSL");
var owner = new SettingsWindow(vm) { SelectedTabIndex = 2, SelectedOptimizationSectionIndex = 1 };
owner.Show(); Dispatcher.UIThread.RunJobs();
var results = new List<object>();
foreach (var action in new[] { "escape", "cancel", "close", "enter-default", "confirm" })
{
    var button = owner.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, Loc.Get("settings.optimization.restartWsl")));
    var before = calls;
    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    Dispatcher.UIThread.RunJobs();
    var dialog = owner.OwnedWindows.OfType<ConfirmSetupWindow>().Single();
    if (calls != before) throw new Exception("Shutdown occurred before confirmation");
    var cancel = dialog.GetVisualDescendants().OfType<Button>().Single(b => b.IsCancel);
    if (!cancel.IsDefault) throw new Exception("Cancel is not the default");
    if (action == "escape") { dialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, "Escape"); owner.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, "Escape"); }
    else if (action == "enter-default") { cancel.Focus(); dialog.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "Enter"); owner.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "Enter"); }
    else if (action == "close") dialog.Close();
    else dialog.GetVisualDescendants().OfType<Button>().Single(b => action == "cancel" ? b.IsCancel : !b.IsCancel).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    Dispatcher.UIThread.RunJobs();
    if (dialog.IsVisible || calls - before != (action == "confirm" ? 1 : 0)) throw new Exception("Unexpected action: " + action);
    results.Add(new { action, shutdownCalls = calls - before, defaultCancel = cancel.IsDefault });
}
owner.Close(); Dispatcher.UIThread.RunJobs();
File.Delete(file); File.Delete(memoryFile); WslConfigFile.UseCustomPath(null);
Console.WriteLine(JsonSerializer.Serialize(new { synthetic = true, saveWithoutShutdown = true, totalShutdownCalls = calls, results }, new JsonSerializerOptions { WriteIndented = true }));
