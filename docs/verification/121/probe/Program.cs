using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BoosterAI;
using System.Text.Json;

var output = Path.GetFullPath(args.FirstOrDefault() ?? "docs/verification/121");
Directory.CreateDirectory(output);
var temporary = Directory.CreateTempSubdirectory("booster-missing-agent-").FullName;
ConfigStore.UseFile(Path.Combine(temporary, "config.json"));
AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
AvaloniaSynchronizationContext.InstallIfNeeded();
var results = new List<object>();
var recorded = new List<Launcher.TerminalRequest>();
Launcher.OpenTerminal = request => { recorded.Add(request); throw new InvalidOperationException("Synthetic recorder: no installer or timer is started"); };
try
{
 foreach (var language in new[] { "sv", "en" })
 foreach (var minimum in new[] { false, true })
 {
  Loc.Use(language);
  var theme = language == "sv" ? "light" : "dark";
  Application.Current!.RequestedThemeVariant = language == "sv" ? Avalonia.Styling.ThemeVariant.Light : Avalonia.Styling.ThemeVariant.Dark;
  var vm = new MainViewModel(new AppConfig { Language = language, CheckForUpdates = false });
  var owner = new SettingsWindow(vm) { Width = minimum ? 640 : 920, Height = minimum ? 400 : 680, SelectedTabIndex = 4 };
  try
  {
   owner.Show(); Pump();
   foreach (var cli in Prerequisites.Clis)
   foreach (var section in new[] { 0, 1, 2 })
   {
    Apply(installed: false);
    vm.Setup.SelectedAgent = vm.Setup.Agents.Single(a => a.Cli == cli);
    vm.Setup.SelectedAgent.Section = section; Pump();
    var install = MissingOnly();
    if (cli == "aider" && section == 1) Capture("missing");
    if (section == 0)
    {
     Require(install.Focus(NavigationMethod.Tab), "Install cannot receive keyboard focus");
     var before = recorded.Count;
     install.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
     Require(recorded.Count == before + 1 && recorded[^1].Tabs.Single().Script == Prerequisites.InstallScript(cli), "Install routed to the wrong CLI");
     vm.ShowStatus("", error: false);
    }
    Apply(installed: true);
    var agent = vm.Setup.SelectedAgent!;
    Require(agent.Cli == cli && agent.Section == section && agent.IsInstalled, "Refresh lost selected agent or section");
    var page = Page();
    Require(page.GetVisualDescendants().OfType<Button>().Count(b => b.IsEffectivelyVisible && b.Classes.Contains("agent-nav")) == 3, "Installed navigation missing");
    Require(!page.GetVisualDescendants().OfType<Button>().Any(b => b.IsEffectivelyVisible && Equals(b.Content, Loc.Format("setup.cli.install", cli))), "Missing-install view remains on installed page");
    if (section == 0) Require(page.GetVisualDescendants().OfType<Button>().Any(b => b.IsEffectivelyVisible && Equals(b.Content, Loc.Format("agents.update", cli))), "Installed update action missing");
    if (section == 1) Require(page.GetVisualDescendants().OfType<ComboBox>().Any(c => c.IsEffectivelyVisible), "Installed permission editor missing");
    if (section == 2) Require(page.GetVisualDescendants().OfType<AutoCompactView>().Any(c => c.IsEffectivelyVisible), "Installed context editor missing");
    if (cli == "aider" && section == 0 && !minimum) Capture("installed");
    Apply(installed: false); MissingOnly();
    results.Add(new { language, theme, minimum, cli, section, onlyInstallWhenMissing = true, installedEditorsRestored = true, removedEditorsHiddenAgain = true });
   }
  }
  finally { owner.Close(); Pump(); }
  void Apply(bool installed)
  {
   vm.Setup.Apply(new SetupStatus(WslState.Ok, ["Ubuntu"], Prerequisites.Clis.Select(c =>
    new CliStatus(c, installed ? "file" : c == "aider" ? "alias" : "function", installed ? "/sample/bin/" + c : null)
    { Version = "1.2.3", Help = "--yes-always --oss --approve-for-me --permission-mode acceptEdits --dangerously-skip-permissions --dangerously-bypass-approvals-and-sandbox --mode auto --ask-for-approval --sandbox --llm-approve --always-approve", AliasDefinition = "alias aider='aider --yes-always'", FunctionDefinition = "neutral () { :; }" }).ToArray(), true, false), "Ubuntu");
   Pump();
  }
  ScrollViewer Page() => owner.GetVisualDescendants().OfType<ScrollViewer>().First(s => ReferenceEquals(s.DataContext, vm.Setup.SelectedAgent) && s.IsEffectivelyVisible);
  Button MissingOnly()
  {
   var agent = vm.Setup.SelectedAgent!; var page = Page();
   var buttons = page.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible).ToArray();
   Require(buttons.Length == 1 && Equals(buttons[0].Content, Loc.Format("setup.cli.install", agent.Cli)), "Missing page shows more than Install");
   Require(!page.GetVisualDescendants().OfType<ComboBox>().Any(c => c.IsEffectivelyVisible) && !page.GetVisualDescendants().OfType<TextBox>().Any(c => c.IsEffectivelyVisible), "Missing editor remains visible");
   var text = page.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text).Where(t => !string.IsNullOrWhiteSpace(t));
   Require(text.All(t => t == agent.DisplayName || t == buttons[0].Content?.ToString()), "Missing page shows extra status/version/settings text");
   var point = buttons[0].TranslatePoint(new Point(0, buttons[0].Bounds.Height), owner);
   Require(point is { } p && p.Y > 0 && p.Y < owner.ClientSize.Height - 60, "Install is outside the visible page");
   return buttons[0];
  }
  void Capture(string state)
  {
   for (var i = 0; i < 3; i++) { owner.UpdateLayout(); Pump(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
   using var frame = owner.CaptureRenderedFrame() ?? throw new InvalidOperationException("No frame");
   frame.Save(Path.Combine(output, $"{state}-{language}-{theme}{(minimum ? "-minimum" : "")}.png"));
  }
 }
 File.WriteAllText(Path.Combine(output, "interaction.json"), JsonSerializer.Serialize(new { synthetic = true, realInstallerProcesses = 0, recordedInstallActions = recorded.Count, cases = results.Count, results }, new JsonSerializerOptions { WriteIndented = true }));
 Console.WriteLine($"Passed {results.Count} UI cases; {recorded.Count} recorded Install actions; 0 real installers.");
}
finally { Directory.Delete(temporary, recursive: true); }
void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
void Pump()
{
 var frame = new DispatcherFrame(); using var timer = DispatcherTimer.RunOnce(() => frame.Continue = false, TimeSpan.FromMilliseconds(25)); Dispatcher.UIThread.PushFrame(frame);
}
