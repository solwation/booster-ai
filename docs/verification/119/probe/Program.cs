using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BoosterAI;
using System.Text.Json;

var language = args.FirstOrDefault() ?? "sv";
var theme = args.Skip(1).FirstOrDefault() ?? "light";
var output = Path.GetFullPath("docs/verification/119");
var suffix = args.Length > 2 ? "-" + args[2] : "";
var temporary = Directory.CreateTempSubdirectory("booster-model-popup-").FullName;
ConfigStore.UseFile(Path.Combine(temporary, "config.json"));
var cfg = new AppConfig { Language = language, AppTheme = theme, CheckForUpdates = false, MinimizeToTray = false,
    Tabs = [new TabConfig { Name = "Atlas", Directory = "/sample/atlas", Llm = "claude", Model = "claude-opus-5-5" },
        new TabConfig { Name = "Harbor", Directory = "/sample/harbor", Llm = "agy" }] };
ConfigStore.Save(cfg);
Screenshot.Parse(["--screenshot", "/unused.png", "--config", ConfigStore.PathOnDisk]);
AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
AvaloniaSynchronizationContext.InstallIfNeeded();
var launches = 0;
Launcher.OpenTerminal = _ => launches++;
var vm = new MainViewModel(cfg);
vm.ShowSampleAgents();
var window = new MainWindow(vm) { Width = 1100, Height = 440 };
var checks = new Dictionary<string, bool>();
try
{
    window.Show(); Pump();
    var row = vm.Tabs[0];
    var combo = ModelSelector(row);
    var otherModel = vm.Tabs[1].Model;
    var otherCombo = ModelSelector(vm.Tabs[1]);
    var arrow = combo.TranslatePoint(new Point(combo.Bounds.Width - 12, combo.Bounds.Height / 2), window)!.Value;
    window.MouseDown(arrow, MouseButton.Left); window.MouseUp(arrow, MouseButton.Left); Pump();
    Require(combo.IsDropDownOpen, "Mouse opens model list");
    for (var i = 0; i < 3; i++)
    {
        // The polling completion refreshes aggregate state through this same public method.
        row.SetState(TabState.Running, null, activity: i % 2 == 0 ? Activity.Busy : Activity.Idle);
        vm.TabsChanged(); Pump();
        Require(ReferenceEquals(combo, ModelSelector(row)), $"Status refresh {i} preserves actual model control");
        Require(combo.IsDropDownOpen, $"Status refresh {i} leaves model popup open");
    }
    Capture("open-after-refresh");
    var index = combo.Items.Cast<string>().ToList().IndexOf("claude-sonnet-5-5");
    var item = (Control)combo.ContainerFromIndex(index)!;
    var root = TopLevel.GetTopLevel(item)!;
    var point = item.TranslatePoint(new Point(item.Bounds.Width / 2, item.Bounds.Height / 2), root)!.Value;
    root.MouseDown(point, MouseButton.Left); root.MouseUp(point, MouseButton.Left); Pump();
    Require(!combo.IsDropDownOpen && row.Model == "claude-sonnet-5-5", "Mouse commits model after background refreshes");
    Require(vm.Tabs[1].Model == otherModel && ReferenceEquals(otherCombo, ModelSelector(vm.Tabs[1])), "Other project model and control remain unchanged");
    vm.Save();
    Require(ConfigStore.Load().Tabs[0].Model == "claude-sonnet-5-5", "Model choice persists");
    row.Llm = "codex"; Pump();
    Require(row.Model == Catalog.StandardModel && combo.Items.Cast<string>().SequenceEqual(Catalog.ModelsFor("codex")), "Agent switch updates models and resets incompatible choice");
    var child = vm.AddSubTab(row); Pump();
    Require(vm.VisibleTabs.Contains(child) && ModelSelector(child).IsVisible, "Adding a subtab updates rendered rows");
    row.Expanded = false; Pump();
    Require(!vm.VisibleTabs.Contains(child) && !window.GetVisualDescendants().OfType<ComboBox>().Any(c => c.DataContext == child), "Collapsing hides the child row");
    row.Expanded = true; Pump();
    Require(ModelSelector(child).IsVisible, "Expanding restores child row");
    Require(launches == 0, "No CLI launches");
    Directory.CreateDirectory(output);
    File.WriteAllText(Path.Combine(output, $"interaction-{language}-{theme}{suffix}.json"), JsonSerializer.Serialize(new { language, theme, synthetic = true, launches, checks }, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"PASS {checks.Count} checks ({language}/{theme})");
}
finally { window.Close(); Pump(); Directory.Delete(temporary, true); }
ComboBox ModelSelector(TabViewModel row) => window.GetVisualDescendants().OfType<ComboBox>().Single(c => c.DataContext == row && c.IsEditable);
void Require(bool value, string name) { checks[name] = value; if (!value) throw new InvalidOperationException(name); }
void Pump() { var frame = new DispatcherFrame(); using var timer = DispatcherTimer.RunOnce(() => frame.Continue = false, TimeSpan.FromMilliseconds(80)); Dispatcher.UIThread.PushFrame(frame); }
void Capture(string name)
{
    window.UpdateLayout(); Pump(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    Directory.CreateDirectory(output);
    using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No headless frame");
    frame.Save(Path.Combine(output, $"{name}-{language}-{theme}{suffix}.png"));
}
