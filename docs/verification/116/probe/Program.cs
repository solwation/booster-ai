using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BoosterAI;
using System.Text.Json;
var output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
var file = Path.Combine(Path.GetTempPath(), "booster116-" + Guid.NewGuid() + ".json");
ConfigStore.UseFile(file);
AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
var results = new List<object>();
try
{
 foreach (var lang in new[] { "sv", "en" })
 foreach (var minimum in new[] { false, true })
 {
  Loc.Use(lang);
  Application.Current!.RequestedThemeVariant = lang == "sv" ? Avalonia.Styling.ThemeVariant.Light : Avalonia.Styling.ThemeVariant.Dark;
  var vm = new MainViewModel(new AppConfig { CheckForUpdates = false });
  vm.ShowSampleAgents(); vm.Optimization.ShowFakeUnoptimized();
  vm.Setup.Apply(new SetupStatus(WslState.Ok, ["Ubuntu", "Debian"], [], false, false), "Ubuntu");
  var window = new SettingsWindow(vm) { Width = minimum ? 640 : 1000, Height = minimum ? 400 : 760, SelectedTabIndex = 2, SelectedOptimizationSectionIndex = 2 };
  window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
  var texts = string.Join("\n", window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text));
  if (!texts.Contains(Loc.Get("wsl.info.status")) || !texts.Contains("Ubuntu\nDebian") || !texts.Contains(Loc.Get("setup.environment.windows"))) throw new Exception("Missing installation facts or automatic refresh explanation");
  if (window.GetVisualDescendants().OfType<Button>().Any(b => Equals(b.Content, "Check environment") || Equals(b.Content, "Kontrollera miljön"))) throw new Exception("Duplicate check remains");
  foreach (var state in new[] { WslState.Unknown, WslState.Missing, WslState.DistroMissing, WslState.Ok })
  { vm.Setup.Apply(new SetupStatus(state, state == WslState.Missing ? [] : ["Ubuntu", "Debian"], [], false, false), "Ubuntu"); Dispatcher.UIThread.RunJobs(); }
  for (var i = 0; i < 3; i++) { Thread.Sleep(30); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
  window.CaptureRenderedFrame()!.Save(Path.Combine(output, $"wsl-{lang}{(minimum ? "-minimum" : "")}.png"));
  var link = window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, Loc.Get("setup.environment.agents")));
  link.BringIntoView(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
  var viewport = link.GetVisualAncestors().OfType<ScrollViewer>().First();
  if (minimum && viewport.Offset.Y <= 0) throw new Exception("Page did not scroll to agent link");
  link.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
  if (window.SelectedTabIndex != 4 || !vm.Setup.IsAgentOverview) throw new Exception("Agent link failed");
  results.Add(new { lang, minimum, installationFacts = true, noDuplicateCheck = true, states = 4, scrollAndAgentLink = true });
  window.Close(); Dispatcher.UIThread.RunJobs();
 }
 Console.WriteLine(JsonSerializer.Serialize(new { synthetic = true, results }, new JsonSerializerOptions { WriteIndented = true }));
}
finally { File.Delete(file); }
