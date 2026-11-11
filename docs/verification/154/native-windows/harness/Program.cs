using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BoosterAI;
using SkiaSharp;

internal static class Program
{
    static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    [STAThread]
    static int Main(string[] args)
    {
        var root = args[^1];
        Directory.CreateDirectory(root);
        try { return args[0] == "--child" ? Child(root, args.Contains("--software")) : Parent(root); }
        catch (Exception e) { File.WriteAllText(Path.Combine(root, "error.txt"), e.ToString()); return 1; }
    }

    static int Parent(string root)
    {
        var desktopName = "BoosterAI-Peek-" + Guid.NewGuid().ToString("N");
        var foreground = GetForegroundWindow();
        var desktop = CreateDesktop(desktopName, null, 0, 0, 0x1FF, 0);
        if (desktop == 0) throw new System.ComponentModel.Win32Exception();
        var results = new List<object>();
        var childrenPassed = true;
        try
        {
            foreach (var mode in new[] { "default", "software" })
            {
                var target = Path.Combine(root, mode);
                Directory.CreateDirectory(target);
                var startup = new Startup { cb = Marshal.SizeOf<Startup>(), desktop = "winsta0\\" + desktopName };
                var command = new StringBuilder('"' + Environment.ProcessPath! + "\" --child " + (mode == "software" ? "--software " : "") + '"' + target + '"');
                if (!CreateProcess(null, command, 0, 0, false, 0x08000000, 0, null, ref startup, out var info))
                    throw new System.ComponentModel.Win32Exception();
                CloseHandle(info.thread);
                using var process = Process.GetProcessById(info.pid);
                try
                {
                    var watch = Stopwatch.StartNew();
                    var foregroundStayed = true;
                    while (!process.WaitForExit(25))
                    {
                        foregroundStayed &= foreground == GetForegroundWindow();
                        if (watch.ElapsedMilliseconds > 45000) throw new TimeoutException("Native child timed out");
                    }
                    if (!GetExitCodeProcess(info.process, out var exit)) throw new System.ComponentModel.Win32Exception();
                    results.Add(new { mode, exit, foregroundUnchanged = foregroundStayed && foreground == GetForegroundWindow() });
                    childrenPassed &= exit == 0 && foregroundStayed;
                }
                finally
                {
                    if (!process.HasExited) { process.Kill(entireProcessTree: true); process.WaitForExit(); }
                    CloseHandle(info.process);
                }
            }
            var unchanged = foreground == GetForegroundWindow();
            File.WriteAllText(Path.Combine(root, "controller.json"), JsonSerializer.Serialize(new { privateDesktop = true, switchedDesktop = false, foregroundUnchanged = unchanged, results }, new JsonSerializerOptions { WriteIndented = true }));
            return unchanged && results.Count == 2 && childrenPassed ? 0 : 1;
        }
        finally { CloseDesktop(desktop); }
    }

    static int Child(string root, bool software)
    {
        ConfigStore.UseFile(Path.Combine(root, "config.json"));
        var cfg = new AppConfig { Language = "sv", AppTheme = "dark", MinimizeToTray = false, AutoUpdateClis = false,
            Tabs = [new TabConfig { Name = "Atlas", Directory = "/srv/atlas", Llm = "claude" }, new TabConfig { Name = "Boreal", Directory = "/srv/boreal", Llm = "codex" }] };
        ConfigStore.Save(cfg);
        var assembly = typeof(App).Assembly;
        assembly.GetType("BoosterAI.Screenshot")!.GetProperty("Active", Flags)!.SetValue(null, true);
        var builder = AppBuilder.Configure<App>().UsePlatformDetect();
        if (software) builder.With(new Win32PlatformOptions { RenderingMode = [Win32RenderingMode.Software], CompositionMode = [Win32CompositionMode.RedirectionSurface] });
        builder.SetupWithoutStarting();
        var vm = new MainViewModel(cfg);
        vm.ShowSampleStatus();
        var tab = vm.ShowSampleOutput()!;
        var main = new MainWindow(vm) { Width = 1100, Height = 720, ShowActivated = false, Position = new PixelPoint(40, 40), Title = "BoosterAI native Peek verification" };
        var stop = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var failed = false;
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                main.Show();
                await Task.Delay(1000);
                var target = main.GetVisualDescendants().OfType<StatusPeekButton>().First(c => c.DataContext == tab);
                typeof(MainWindow).GetMethod("ShowStatusPeek", Flags)!.Invoke(main, [target, tab]);
                await Task.Delay(1000);
                var flyout = (Flyout)typeof(MainWindow).GetField("_statusPeek", Flags)!.GetValue(main)!;
                var content = (StatusPeekView)flyout.Content!;
                var popup = TopLevel.GetTopLevel(content)!;
                var mainHandle = main.TryGetPlatformHandle()!.Handle;
                var popupHandle = popup.TryGetPlatformHandle()!.Handle;
                if (mainHandle == popupHandle || popupHandle == 0) throw new InvalidOperationException("No separate native popup HWND");
                var surface = content.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("peek-surface"));
                var ancestors = surface.GetVisualAncestors().Prepend(surface).ToArray();
                var capture = Capture(popupHandle, Path.Combine(root, "peek-native.png"));
                Capture(mainHandle, Path.Combine(root, "main-native.png"));
                var report = new { software, nativePopup = true, separateHwnd = true, rootType = popup.GetType().FullName,
                    opacity = ancestors.Select(v => v.Opacity), surfaceColor = ((ISolidColorBrush)surface.Background!).Color.ToString(),
                    extendedStyle = GetWindowLongPtr(popupHandle, -20).ToInt64(), captured = capture,
                    assemblyVersion = assembly.GetName().Version!.ToString(), framework = RuntimeInformation.FrameworkDescription,
                    platform = NativeFramebuffer.Describe(popup) };
                File.WriteAllText(Path.Combine(root, "native.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
                await NativeBuffer.Verify(popup, surface, root, software);
                flyout.Hide();
                await Task.Delay(100);
                ToolTip.SetIsOpen(target, true);
                await Task.Delay(1000);
                if (ToolTip.GetTip(target) is ToolTip tip && tip.Content is StatusPeekView hover)
                {
                    var hoverRoot = TopLevel.GetTopLevel(hover)!;
                    Capture(hoverRoot.TryGetPlatformHandle()!.Handle, Path.Combine(root, "hover-native.png"));
                }
                ToolTip.SetIsOpen(target, false);
                vm.WatchOutput(tab, false);
                var watching = vm.ShowSampleOutput(1)!;
                var watchingTarget = main.GetVisualDescendants().OfType<StatusPeekButton>().First(c => c.DataContext == watching);
                typeof(MainWindow).GetMethod("ShowStatusPeek", Flags)!.Invoke(main, [watchingTarget, watching]);
                await Task.Delay(1000);
                var watchingFlyout = (Flyout)typeof(MainWindow).GetField("_statusPeek", Flags)!.GetValue(main)!;
                var watchingContent = (Control)watchingFlyout.Content!;
                Capture(TopLevel.GetTopLevel(watchingContent)!.TryGetPlatformHandle()!.Handle, Path.Combine(root, "watching-native.png"));
                watchingFlyout.Hide();
                vm.WatchOutput(watching, false);
            }
            catch (Exception e) { failed = true; File.WriteAllText(Path.Combine(root, "error.txt"), e.ToString()); }
            finally { main.Close(); stop.Cancel(); }
        });
        try { Dispatcher.UIThread.MainLoop(stop.Token); } catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        return failed ? 1 : 0;
    }

    internal static bool Capture(nint hwnd, string path)
    {
        if (!GetWindowRect(hwnd, out var r)) return false;
        var screen = GetDC(0); var dc = CreateCompatibleDC(screen);
        var info = new BitmapInfo { Size = 40, Width = r.Right-r.Left, Height = -(r.Bottom-r.Top), Planes = 1, BitCount = 32 };
        var bitmap = CreateDIBSection(screen, ref info, 0, out var bits, 0, 0); var old = SelectObject(dc, bitmap);
        try
        {
            if (!PrintWindow(hwnd, dc, 2)) return false;
            using var image = new SKBitmap();
            image.InstallPixels(new SKImageInfo(info.Width, -info.Height, SKColorType.Bgra8888, SKAlphaType.Opaque), bits, info.Width*4);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100); using var file = File.Create(path); data.SaveTo(file); return true;
        }
        finally { SelectObject(dc, old); DeleteObject(bitmap); DeleteDC(dc); ReleaseDC(0, screen); }
    }
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] struct Startup { public int cb; public string? reserved, desktop, title; public int x,y,width,height,charX,charY,fill,flags; public short show,reservedSize; public nint reservedPtr,input,output,error; }
    [StructLayout(LayoutKind.Sequential)] struct ProcessInfo { public nint process, thread; public int pid, tid; }
    [StructLayout(LayoutKind.Sequential)] struct Rect { public int Left,Top,Right,Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct BitmapInfo { public uint Size; public int Width,Height; public ushort Planes,BitCount; public uint Compression,ImageSize; public int XPixelsPerMeter,YPixelsPerMeter; public uint ColorsUsed,ColorsImportant; }
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern nint CreateDesktop(string name,string? device,nint devMode,uint flags,uint access,nint security);
    [DllImport("user32.dll")] static extern bool CloseDesktop(nint desktop);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool CreateProcess(string? application,StringBuilder command,nint processSecurity,nint threadSecurity,bool inherit,uint flags,nint environment,string? directory,ref Startup startup,out ProcessInfo info);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(nint handle);
    [DllImport("kernel32.dll")] static extern bool GetExitCodeProcess(nint process,out uint exit);
    [DllImport("user32.dll")] static extern nint GetForegroundWindow();
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")] static extern nint GetWindowLongPtr(nint hwnd,int index);
    [DllImport("user32.dll")] static extern bool GetWindowRect(nint hwnd,out Rect rect);
    [DllImport("user32.dll")] static extern nint GetDC(nint hwnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(nint hwnd,nint dc);
    [DllImport("user32.dll")] static extern bool PrintWindow(nint hwnd,nint dc,uint flags);
    [DllImport("gdi32.dll")] static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] static extern nint CreateDIBSection(nint dc,ref BitmapInfo info,uint usage,out nint bits,nint section,uint offset);
    [DllImport("gdi32.dll")] static extern nint SelectObject(nint dc,nint item);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(nint item);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(nint dc);
}
