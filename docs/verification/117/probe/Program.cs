using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BoosterAI;
using System.Reflection;
using System.Text.Json;
var output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
var file = Path.Combine(Path.GetTempPath(), "booster117-" + Guid.NewGuid() + ".json");
ConfigStore.UseFile(file);
// Never write to the user's terminal profile from an evidence probe.
var zoomRequests = new List<int>();
typeof(Launcher).GetProperty("RequestZoom", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, (Action<int, bool>)((p, _) => zoomRequests.Add(p)));
AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
var results = new List<object>();
try
{
 foreach (var lang in new[] { "sv", "en" })
 {
  Loc.Use(lang);
  Application.Current!.RequestedThemeVariant = lang == "sv" ? Avalonia.Styling.ThemeVariant.Light : Avalonia.Styling.ThemeVariant.Dark;
  var vm = new MainViewModel(new AppConfig { CheckForUpdates = false });
  var window = new SettingsWindow(vm) { Width = 1000, Height = 760, SelectedTabIndex = 3 };
  window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
  var combo = window.GetVisualDescendants().OfType<ComboBox>().Single(c => c.ItemsSource == vm.ZoomChoices);
  foreach (var percent in new[] { 50, 60, 70 })
  {
   combo.SelectedItem = vm.ZoomChoices.Single(c => c.Percent == percent); Dispatcher.UIThread.RunJobs();
   if (vm.SelectedZoom.Percent != percent || ConfigStore.Load().ZoomPercent != percent || new MainViewModel(ConfigStore.Load()).SelectedZoom.Percent != percent || zoomRequests.Last() != percent) throw new Exception("Zoom selection/persistence failed");
  }
  combo.IsDropDownOpen = true;
  for (var i = 0; i < 3; i++) { Thread.Sleep(30); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
  window.CaptureRenderedFrame()!.Save(Path.Combine(output, $"font-sizes-{lang}.png"));
  combo.IsDropDownOpen = false;
  results.Add(new { lang, sizes = new[] { 50, 60, 70 }, uiSelection = true, persistedAndReloaded = true, terminalRequestsRecorded = true });
  window.Close(); Dispatcher.UIThread.RunJobs();
 }
 Console.WriteLine(JsonSerializer.Serialize(new { synthetic = true, results }, new JsonSerializerOptions { WriteIndented = true }));
}
finally { File.Delete(file); }
