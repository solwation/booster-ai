using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BoosterAI;
using System.Text.Json;
var file = Path.Combine(Path.GetTempPath(), "booster112-" + Guid.NewGuid() + ".json");
ConfigStore.UseFile(file);
AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
var results = new List<object>();
foreach (var lang in new[] { "sv", "en" })
{
 Loc.Use(lang);
 var vm = new MainViewModel(new AppConfig { CheckForUpdates = false });
 vm.ShowSampleAgents();
 vm.Optimization.ShowFakeUnoptimized();
 var window = new SettingsWindow(vm) { Width = 640, Height = 400, SelectedTabIndex = 2, SelectedOptimizationSectionIndex = 2 };
 window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
 var check = window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, Loc.Get("setup.environment.check")));
 var text = string.Join("\n", window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
 if (!text.Contains(Loc.Get("setup.environment.windows")) || text.Contains(Loc.Format("setup.summary.alias", "claude"))) throw new Exception("Environment copy includes alias detail or omits scope");
 var summary = vm.Setup.Summary;
 vm.Setup.IsChecking = true; Dispatcher.UIThread.RunJobs();
 if (check.IsEnabled || vm.Setup.Summary != Loc.Get("setup.summary.checking")) throw new Exception("Running check state is unclear");
 vm.Setup.IsChecking = false; Dispatcher.UIThread.RunJobs();
 if (!check.IsEnabled || vm.Setup.Summary != summary) throw new Exception("Completed result was not restored");
 vm.Setup.SelectedAgent = vm.Setup.Agents.Single(a => a.Cli == "codex");
 var link = window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, Loc.Get("setup.environment.agents")));
 link.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
 if (window.SelectedTabIndex != 4 || !vm.Setup.IsAgentOverview) throw new Exception("Link did not open AI agent overview");
 results.Add(new { lang, readOnlyScopeExplained = true, aliasDetailsAbsent = true, runningDisablesButton = true, resultsRetained = true, agentOverviewLink = true });
 window.Close(); Dispatcher.UIThread.RunJobs();
}
File.Delete(file);
Console.WriteLine(JsonSerializer.Serialize(new { synthetic = true, results }, new JsonSerializerOptions { WriteIndented = true }));
