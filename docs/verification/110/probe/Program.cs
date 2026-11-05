using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Avalonia.Threading;
using BoosterAI;
using System.Text.Json;
var file = Path.Combine(Path.GetTempPath(), "booster110-" + Guid.NewGuid() + ".json");
ConfigStore.UseFile(file);
AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
var results = new List<object>();
foreach (var lang in new[] { "sv", "en" })
{
 Loc.Use(lang);
 for (var instance = 0; instance < 2; instance++)
 {
  var vm = new MainViewModel(new AppConfig { CheckForUpdates = false });
  vm.ShowSampleAlias();
  var main = new MainWindow(vm); main.Show(); Dispatcher.UIThread.RunJobs();
  var offered = main.GetVisualDescendants().OfType<Button>().Any(b => Equals(b.Content, Loc.Get("setup.alias.add")));
  if (offered || vm.Setup.MissingVisible || vm.Setup.WslVisible) throw new Exception("Optional aliases shown as unfinished setup");
  var plain = vm.Setup.Agents.Where(a => a.Cli is "claude" or "codex").ToArray();
  if (plain.Length != 2 || plain.Any(a => a.CurrentMode != AgentMode.Standard || !a.CanSelectMode)) throw new Exception("Agent settings unavailable");
  results.Add(new { lang, instance, aliasOffer = offered, installationComplete = !vm.Setup.MissingVisible, explicitAgentChoices = plain.Length });
  main.Close(); Dispatcher.UIThread.RunJobs();
 }
}
File.Delete(file);
Console.WriteLine(JsonSerializer.Serialize(new { synthetic = true, results }, new JsonSerializerOptions { WriteIndented = true }));
