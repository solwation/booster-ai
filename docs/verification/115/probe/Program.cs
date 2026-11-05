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
var output = Path.GetFullPath("docs/verification/115");
var temporary = Directory.CreateTempSubdirectory("booster-selector-").FullName;
ConfigStore.UseFile(Path.Combine(temporary, "config.json"));
var cfg = new AppConfig { Language = language, AppTheme = theme, CheckForUpdates = false, MinimizeToTray = false,
    Tabs = [new TabConfig { Name = "Atlas", Directory = "/sample/atlas", Llm = "claude", Model = "opus" },
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
    var combo = Selector(window, row);
    Capture("before");
    SelectMouse("codex");
    Require(row.Model == Catalog.StandardModel, "Changing agent resets an incompatible model");
    foreach (var cli in new[] { "agy", "aider", "interpreter", Catalog.ShellOnly, "claude" }) SelectMouse(cli);
    combo.Focus(); Key(PhysicalKey.ArrowDown, RawInputModifiers.Alt);
    Require(combo.IsDropDownOpen, "Alt Down opens agent choices");
    Key(PhysicalKey.ArrowDown); Key(PhysicalKey.Enter);
    AssertSelection("codex", "Keyboard");
    Capture("selected");
    Require(vm.Tabs[1].Llm == "agy", "Changing the first project leaves the second project unchanged");
    vm.Save();
    Require(ConfigStore.Load().Tabs[0].Llm == "codex", "The chosen agent is saved");
    window.Close(); Pump();
    var restored = new MainViewModel(ConfigStore.Load()); restored.ShowSampleAgents();
    window = new MainWindow(restored) { Width = 1100, Height = 440 };
    window.Show(); Pump();
    Require(restored.Tabs[0].Llm == "codex" && Equals(Selector(window, restored.Tabs[0]).SelectedItem, "codex"), "Reload restores selected agent in the actual control");
    window.Close(); Pump();
    Converters.IsCliMissing = cli => vm.Setup.Status.Checked && vm.Setup.Status.Cli(cli)?.Installed == false;
    window = new MainWindow(vm) { Width = 1100, Height = 440 }; window.Show(); Pump();
    combo = Selector(window, row);
    row.Llm = "openhands"; Pump();
    Require(row.Llms.Contains("openhands"), "A saved missing agent remains selectable");
    SelectMouse("codex");
    Require(!row.Llms.Contains("openhands"), "Choosing an installed agent removes the old missing choice after selection");
    vm.Setup.Apply(new SetupStatus(WslState.Ok, ["Ubuntu"], Prerequisites.Clis.Select(cli => new CliStatus(cli, "file", "/sample/bin/" + cli)).ToArray(), true, false), "Ubuntu"); Pump();
    foreach (var cli in Catalog.Llms) SelectMouse(cli);
    Require(launches == 0, "Selection and keyboard input never launch a CLI");
    Directory.CreateDirectory(output);
    File.WriteAllText(Path.Combine(output, $"interaction-{language}-{theme}.json"), JsonSerializer.Serialize(new { language, theme, synthetic = true, launches, checks }, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"PASS {checks.Count} checks ({language}/{theme})");

    void SelectMouse(string cli)
    {
        var point = combo.TranslatePoint(new Point(combo.Bounds.Width / 2, combo.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Pump();
        Require(combo.IsDropDownOpen, "Mouse opens agent choices");
        var index = combo.Items.Cast<string>().ToList().IndexOf(cli);
        Require(index >= 0, "Agent is available: " + cli);
        var item = (Control)combo.ContainerFromIndex(index)!;
        var root = TopLevel.GetTopLevel(item)!;
        var itemPoint = item.TranslatePoint(new Point(item.Bounds.Width / 2, item.Bounds.Height / 2), root)!.Value;
        root.MouseDown(itemPoint, MouseButton.Left); root.MouseUp(itemPoint, MouseButton.Left); Pump();
        AssertSelection(cli, "Mouse");
    }
    void AssertSelection(string cli, string input)
    {
        Require(!combo.IsDropDownOpen && Equals(combo.SelectedItem, cli) && row.Llm == cli, input + " commits agent: " + cli);
        Require(combo.GetVisualDescendants().OfType<AgentIcon>().Single().Cli == cli, input + " displays the chosen icon: " + cli);
        Require(combo.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == Loc.Display("llm", cli)), input + " displays the chosen name: " + cli);
    }
}
finally { window.Close(); Pump(); Directory.Delete(temporary, true); }
ComboBox Selector(Window w, TabViewModel row) => w.GetVisualDescendants().OfType<ComboBox>().First(c => c.DataContext == row);
void Require(bool value, string name) { checks[name] = value; if (!value) throw new InvalidOperationException(name); }
void Key(PhysicalKey key, RawInputModifiers mods = RawInputModifiers.None) { window.KeyPressQwerty(key, mods); window.KeyReleaseQwerty(key, mods); Pump(); }
void Pump() { var frame = new DispatcherFrame(); using var timer = DispatcherTimer.RunOnce(() => frame.Continue = false, TimeSpan.FromMilliseconds(80)); Dispatcher.UIThread.PushFrame(frame); }
void Capture(string name)
{
    window.UpdateLayout(); Pump(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    Directory.CreateDirectory(output);
    using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No headless frame");
    frame.Save(Path.Combine(output, $"{name}-{language}-{theme}.png"));
}
