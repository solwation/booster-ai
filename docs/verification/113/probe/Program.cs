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

var language = args.FirstOrDefault() ?? "sv";
var theme = args.Skip(1).FirstOrDefault() ?? "light";
var output = Path.GetFullPath("docs/verification/113");
var temporary = Directory.CreateTempSubdirectory("booster-alias-confirm-").FullName;
var config = new AppConfig { Language = language, AppTheme = theme, CheckForUpdates = false };
ConfigStore.UseFile(Path.Combine(temporary, "config.json")); ConfigStore.Save(config);
Screenshot.Parse(["--screenshot", "/unused.png", "--config", ConfigStore.PathOnDisk]);
AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
AvaloniaSynchronizationContext.InstallIfNeeded();
var vm = new MainViewModel(config);
vm.ShowSampleAgents();
var bashrc = Path.Combine(temporary, "bashrc");
typeof(MainViewModel).GetProperty("PermissionConfigPath", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(vm, bashrc);
const string original = "# neutral custom startup\nalias helper='printf helper'\nagy () { command agy --mode=auto \"$@\"; }\n";
var owner = new SettingsWindow(vm) { SelectedTabIndex = 4, SelectedAgentCli = "agy" };
var results = new List<object>();
try
{
    owner.Show(); Pump();
    foreach (var action in new[] { "escape", "cancel", "close", "enter-default", "confirm", "confirm-yolo", "confirm-no-alias" })
    {
        File.WriteAllText(bashrc, original);
        vm.ShowSampleAgents();
        var agent = vm.Setup.Agents.Single(a => a.Cli == "agy");
        vm.Setup.SelectedAgent = agent; agent.Section = 1;
        Pump();
        var picker = owner.GetVisualDescendants().OfType<ComboBox>().Single(c => ReferenceEquals(c.ItemsSource, agent.ModeChoices));
        picker.SelectedItem = agent.ModeChoices.Single(c => c.Mode == (action == "confirm-yolo" ? AgentMode.Yolo : action == "confirm-no-alias" ? AgentMode.Standard : AgentMode.AutoApprove));
        Pump();
        var apply = owner.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, Loc.Get("agents.mode.apply")));
        Require(apply.IsEnabled && agent.CanApply, "Preset cannot be applied over a custom function");
        Require(!owner.GetVisualDescendants().OfType<Expander>().Any(e => Equals(e.Header, Loc.Get("agents.alias.manual.title"))), "Manual shell instructions are still visible");
        if (action == "confirm") Capture(owner, "preview");
        apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
        var dialog = owner.OwnedWindows.OfType<ConfirmSetupWindow>().Single();
        Require(File.ReadAllText(bashrc) == original, "Write happened before confirmation");
        Require(dialog.Code.Contains(agent.CurrentAliasCommand) && dialog.Code.Contains(agent.ModePreview), "Confirmation omits current or selected definition");
        Require(!agent.IsYolo || dialog.Detail.Contains(Loc.Get("agents.yolo.warning")), "YOLO warning missing from combined confirmation");
        var cancel = dialog.GetVisualDescendants().OfType<Button>().Single(b => b.IsCancel);
        Require(cancel.IsDefault, "Cancel is not the default");
        if (action.StartsWith("confirm")) Capture(dialog, action);
        if (action == "escape") Key(dialog, PhysicalKey.Escape);
        else if (action == "enter-default") { cancel.Focus(); Key(dialog, PhysicalKey.Enter); }
        else if (action == "close") dialog.Close();
        else dialog.GetVisualDescendants().OfType<Button>().Single(b => action == "cancel" ? b.IsCancel : !b.IsCancel).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Pump();
        var saved = File.ReadAllText(bashrc);
        Require(!dialog.IsVisible && !owner.OwnedWindows.Any(), "Confirmation did not close or opened a second popup");
        if (action.StartsWith("confirm"))
        {
            Require(saved.StartsWith(original) && PermissionOverrides.TryRead(saved, "agy", out var selected) && selected == agent.SelectedMode, "Confirmed preset was not written or original definitions changed");
        }
        else Require(saved == original, "Cancelled confirmation wrote the startup file");
        results.Add(new { action, popupCount = 1, fileChanged = saved != original, originalPreserved = saved.StartsWith(original), defaultCancel = cancel.IsDefault });
    }
    Console.WriteLine(JsonSerializer.Serialize(new { language, theme, synthetic = true, results }, new JsonSerializerOptions { WriteIndented = true }));
}
finally
{
    owner.Close(); Pump(); Directory.Delete(temporary, recursive: true);
}
void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
void Key(TopLevel target, PhysicalKey key) { target.KeyPressQwerty(key, RawInputModifiers.None); owner.KeyReleaseQwerty(key, RawInputModifiers.None); Pump(); }
void Pump()
{
    var frame = new DispatcherFrame(); using var timer = DispatcherTimer.RunOnce(() => frame.Continue = false, TimeSpan.FromMilliseconds(100)); Dispatcher.UIThread.PushFrame(frame);
}
void Capture(Window window, string name)
{
    window.UpdateLayout(); Pump(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    Directory.CreateDirectory(output);
    using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No headless frame");
    frame.Save(Path.Combine(output, $"{name}-{language}-{theme}.png"));
}
