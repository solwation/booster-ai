using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BoosterAI;
using System.Reflection;
using System.Text.Json;

var language = args.FirstOrDefault() ?? "sv";
var theme = args.Skip(1).FirstOrDefault() ?? "light";
var output = Path.GetFullPath("docs/verification/111");
var temporary = Directory.CreateTempSubdirectory("booster-status-peek-").FullName;
var cfg = new AppConfig { Language = language, AppTheme = theme, CheckForUpdates = false, MinimizeToTray = false,
    Tabs = [new TabConfig { Name = "Atlas", Directory = "/sample/atlas" }, new TabConfig { Name = "Harbor", Directory = "/sample/harbor" }] };
ConfigStore.UseFile(Path.Combine(temporary, "config.json")); ConfigStore.Save(cfg);
Screenshot.Parse(["--screenshot", "/unused.png", "--config", ConfigStore.PathOnDisk]);
AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
AvaloniaSynchronizationContext.InstallIfNeeded();
var launches = 0;
Launcher.OpenTerminal = _ => launches++;
var vm = new MainViewModel(cfg);
var row = vm.Tabs[0];
var session = new TabSession(Path.Combine(temporary, "sessions"), DateTime.UtcNow);
row.Session = session; row.Track = session.NewTrack();
row.SetState(TabState.Running, null, activity: Activity.Watching, watchingSince: DateTimeOffset.UtcNow.AddMinutes(-12).ToUnixTimeMilliseconds());
vm.Tabs[1].SetState(TabState.Closed, null);
var path = Path.Combine(session.Directory, row.Track!.Id + ".out");
Write("phase-one");
var window = new MainWindow(vm) { Width = 900, Height = 380 };
var checks = new Dictionary<string, bool>();
try
{
    window.Show(); Pump();
    var button = window.GetVisualDescendants().OfType<StatusPeekButton>().Single(b => b.DataContext == row);
    Require((AutomationProperties.GetName(button) ?? "").Contains("Atlas") && (AutomationProperties.GetName(button) ?? "").Contains(row.StatusTip), "Named with project and complete status");
    for (var i = 0; i < 25 && !button.IsFocused; i++) Key(window, PhysicalKey.Tab);
    Require(button.IsFocused, "Tab reaches status control");
    Capture("focus", null);
    Require(!Watching(), "No watch before opening");

    Key(window, PhysicalKey.Enter);
    var flyout = Flyout();
    Require(flyout.IsOpen, "Enter opens details");
    Require(Watching(), "Keyboard opening starts output timer");
    Key(TopLevel.GetTopLevel(Content())!, PhysicalKey.Enter);
    Key(TopLevel.GetTopLevel(Content())!, PhysicalKey.Space);
    Require(launches == 0 && Flyout().IsOpen, "Enter and Space inside details cannot reach Start");
    Wait(() => row.OutputText.Contains("phase-one"));
    Require(Content().DataContext == row && Content().GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == row.StatusTip), "Keyboard details contain full status");
    Capture("keyboard", Content().GetVisualAncestors().OfType<FlyoutPresenter>().Single());
    var scroll = Content().GetVisualDescendants().OfType<ScrollViewer>().Single();
    Key(TopLevel.GetTopLevel(Content())!, PhysicalKey.PageDown);
    Require(scroll.Offset.Y > 0, "Keyboard can scroll all output at minimum size");
    Capture("keyboard-bottom", Content().GetVisualAncestors().OfType<FlyoutPresenter>().Single());
    Write("phase-two"); Wait(() => row.OutputText.Contains("phase-two"));
    Require(row.OutputText.Contains("phase-two"), "Open details refresh from synthetic capture");
    Key(TopLevel.GetTopLevel(Content())!, PhysicalKey.Escape, window);
    Require(!flyout.IsOpen && button.IsFocused && !Watching(), "Escape closes stops watch and restores focus");
    var stopped = row.OutputText;
    Write("phase-three"); Pump(1250);
    Require(row.OutputText == stopped && !Watching(), "Closed details do not poll output");

    Key(window, PhysicalKey.Space);
    Require(Flyout().IsOpen, "Space opens details");
    Wait(() => row.OutputText.Contains("phase-three"));
    Key(TopLevel.GetTopLevel(Content())!, PhysicalKey.Escape, window);
    Require(button.IsFocused && !Watching(), "Space details close cleanly");

    ToolTip.SetShowDelay(button, 0);
    var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
    window.MouseMove(point); Pump(150);
    Require(ToolTip.GetIsOpen(button), "Pointer hover retains quick preview");
    Require(Watching(), "Hover starts the same watch");
    var tooltip = (ToolTip)ToolTip.GetTip(button)!;
    Require(tooltip.Content is StatusPeekView hover && hover.DataContext == row, "Hover and keyboard use the same bound content type");
    Capture("hover", tooltip);
    window.MouseMove(new Point(899, 379));
    Pump(150);
    Require(!ToolTip.GetIsOpen(button) && !Watching(), "Pointer leave stops hover watch");
    window.MouseMove(point); Pump(150);
    Require(Watching(), "Hover reopens after keyboard use");
    window.Hide(); Pump();
    Require(!Watching(), "Hiding main window stops output watch");
    window.Show(); Pump();
    window.MouseMove(new Point(899, 379)); Pump(); window.MouseMove(point); Pump(150);
    Require(Watching(), "Hover resumes after showing main window");
    window.Close(); Pump();
    Require(!Watching(), "Closing main window stops output watch");
    Require(launches == 0, "Enter and Space never launch a CLI");
    Console.WriteLine(JsonSerializer.Serialize(new { language, theme, synthetic = true, launches, checks }, new JsonSerializerOptions { WriteIndented = true }));
}
finally
{
    window.Close(); Pump();
    Directory.Delete(temporary, recursive: true);
}
Flyout Flyout() => (Flyout)typeof(MainWindow).GetField("_statusPeek", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
StatusPeekView Content() => (StatusPeekView)Flyout().Content!;
bool Watching() => typeof(MainViewModel).GetField("_outputTab", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(vm) == row
    && (bool)(typeof(MainViewModel).GetField("_outputTimer", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(vm)?.GetType().GetProperty("IsEnabled")?.GetValue(typeof(MainViewModel).GetField("_outputTimer", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(vm)) ?? false);
void Write(string marker) => File.WriteAllText(path, string.Join("\n", Enumerable.Range(1, 20).Select(i => $"{marker}: neutral output line {i:00}")) + "\n");
void Require(bool value, string name) { checks[name] = value; if (!value) throw new InvalidOperationException(name); }
void Key(TopLevel target, PhysicalKey key, TopLevel? release = null)
{
    target.KeyPressQwerty(key, RawInputModifiers.None); (release ?? target).KeyReleaseQwerty(key, RawInputModifiers.None); Pump();
}
void Pump(int ms = 80)
{
    var frame = new DispatcherFrame();
    using var timer = DispatcherTimer.RunOnce(() => frame.Continue = false, TimeSpan.FromMilliseconds(ms));
    Dispatcher.UIThread.PushFrame(frame);
}
void Wait(Func<bool> done)
{
    var until = DateTime.UtcNow.AddSeconds(5);
    while (!done() && DateTime.UtcNow < until) Pump();
    if (!done()) throw new InvalidOperationException("Timed out waiting for output");
}
void Capture(string name, Control? popup)
{
    window.UpdateLayout(); Pump(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    Directory.CreateDirectory(output);
    using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No headless frame");
    if (popup is null) { frame.Save(Path.Combine(output, $"{name}-{language}-{theme}-minimum.png")); return; }
    popup.UpdateLayout();
    var size = popup.Bounds.Size;
    Require(size.Width <= window.ClientSize.Width - 16 && size.Height <= window.ClientSize.Height - 16, "Popup fits minimum frame");
    if (TopLevel.GetTopLevel(popup) == window) { frame.Save(Path.Combine(output, $"{name}-{language}-{theme}-minimum.png")); return; }
    using var popupFrame = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height)), frame.Dpi);
    popupFrame.Render(popup);
    using var composite = new RenderTargetBitmap(frame.PixelSize, frame.Dpi);
    using (var ctx = composite.CreateDrawingContext())
    {
        ctx.DrawImage(frame, new Rect(frame.Size));
        ctx.DrawImage(popupFrame, new Rect(new Point((frame.Size.Width - size.Width) / 2, 8), size));
    }
    composite.Save(Path.Combine(output, $"{name}-{language}-{theme}-minimum.png"));
}
