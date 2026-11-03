using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text;
using BoosterAI;
using Xunit;
using SkiaSharp;

/// <summary>Opt-in conductor check. No real window or shell is started by the ordinary test suite.</summary>
public class TerminalPlacementWindowsTests
{
    [Fact]
    public async Task RealTerminalRemembersItsWindowAcrossTwoSessions()
    {
        var evidence = Environment.GetEnvironmentVariable("BOOSTERAI_PLACEMENT_E2E");
        if (!OperatingSystem.IsWindows() || string.IsNullOrEmpty(evidence)) return;
        Directory.CreateDirectory(evidence);
        var state = Path.Combine(evidence, "placement.json");
        File.Delete(state);
        var requests = new List<(TerminalWatchRequest Request, string Marker)>();
        var processes = new List<Process>();
        var watchers = new List<Process>();
        var shells = new List<(int Pid, long Start)>();
        var observations = new List<object>();
        SetThreadDpiAwarenessContext(new nint(-4));
        try
        {
            TerminalWindowState? expected = null;
            for (var round = 0; round < 2; round++)
            {
                var window = "BoosterAI-placement-test-" + Guid.NewGuid().ToString("N");
                var request = TerminalPlacement.Prepare(window, state, Path.Combine(evidence, "watch-" + round + ".log"));
                Assert.NotNull(request);
                var marker = Path.Combine(evidence, "shell-" + round);
                File.Delete(marker);
                requests.Add((request, marker));
                watchers.Add(Process.GetProcessById(request.WatcherPid));
                var script = "printf '%s' $$ > " + Launcher.Quote(PathMapping.ToWslPath(marker)) + "\nread -r -t 120 ignored || true";
                var args = TerminalPlacement.WithBootstrap(Launcher.WindowsTerminalArgs(
                    [new TabConfig { Name = request.Token + " check", Script = script }], "Ubuntu", window: window), request);
                // Run this opt-in check on the conductor's isolated Windows desktop. Hidden startup alone does not hide WT.
                var terminal = Environment.GetEnvironmentVariable("BOOSTERAI_PLACEMENT_TERMINAL") ?? "wt.exe";
                var psi = new ProcessStartInfo(terminal)
                    { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
                foreach (var arg in args) psi.ArgumentList.Add(arg);
                var process = Process.Start(psi)!;
                processes.Add(process);
                nint hwnd = 0;
                await Until(() => (hwnd = TerminalPlacement.FindOwnedWindow(window)) != 0 && File.Exists(marker), 20000);
                var pid = int.Parse(File.ReadAllText(marker));
                if (TabSession.Liveness("Ubuntu")?.Invoke(pid) is { } start) shells.Add((pid, start));
                await Until(() => File.Exists(Path.Combine(request.Directory, "ready")), 10000);
                // The bootstrap writes its final marker and disappears before closing the single remaining test tab.
                await Until(() => File.Exists(Path.Combine(request.Directory, "bootstrap-done"))
                    && WindowTitle(hwnd).Contains(request.Token + " check", StringComparison.Ordinal), 15000);
                if (round == 0)
                {
                    var display = TerminalPlacement.Displays().First(d => d.Primary);
                    var work = display.WorkArea;
                    var wanted = new TerminalWindowState(new(work.X + 40, work.Y + 50,
                        Math.Min(800, work.Width - 80), Math.Min(500, work.Height - 100)), display.Device, work, false);
                    var foreground = GetForegroundWindow();
                    TerminalPlacement.Restore(hwnd, wanted);
                    await Until(() => (expected = TerminalPlacement.Read(state)) is { Bounds.Width: > 0 }, 5000);
                    expected = TerminalPlacement.Capture(hwnd);
                    Assert.NotNull(expected);
                    await Until(() => TerminalPlacement.Read(state) == expected, 5000);
                    Assert.Equal(foreground, GetForegroundWindow());
                    var screenshot = CaptureWindow(hwnd, Path.Combine(evidence, "before.png"));
                    observations.Add(new { round, expected, screenshot });
                    PostMessage(hwnd, 0x0010, 0, 0); // WM_CLOSE, only our proven HWND.
                    await Until(() => TerminalPlacement.FindOwnedWindow(window) == 0, 10000);
                    await Until(() => watchers[^1].HasExited, 5000);
                }
                else
                {
                    var actual = TerminalPlacement.Capture(hwnd);
                    var screenshot = CaptureWindow(hwnd, Path.Combine(evidence, "after.png"));
                    observations.Add(new { round, expected, actual, screenshot });
                    Assert.NotNull(actual);
                    Assert.InRange(Math.Abs(actual.Bounds.X - expected!.Bounds.X), 0, 2);
                    Assert.InRange(Math.Abs(actual.Bounds.Y - expected.Bounds.Y), 0, 2);
                    Assert.InRange(Math.Abs(actual.Bounds.Width - expected.Bounds.Width), 0, 2);
                    Assert.InRange(Math.Abs(actual.Bounds.Height - expected.Bounds.Height), 0, 2);
                    Assert.Equal(expected.Device, actual.Device);
                }
            }
        }
        finally
        {
            // A failure before the normal handle/PID registration must also close everything this test started.
            foreach (var (request, marker) in requests)
            {
                // Release the bootstrap before closing a test window, including early failures.
                try
                {
                    if (Directory.Exists(request.Directory))
                        File.WriteAllText(Path.Combine(request.Directory, "ready"), "cleanup");
                }
                catch (IOException) { } // The exiting watcher may have removed its temporary directory already.
                if (File.Exists(marker) && int.TryParse(File.ReadAllText(marker), out var pid)
                    && TabSession.Liveness("Ubuntu")?.Invoke(pid) is { } start) shells.Add((pid, start));
            }
            // End the test's own shells first; closing a multi-tab window directly can leave a WT confirmation dialog.
            foreach (var shell in shells.Distinct()) await StopShell(shell);
            foreach (var (request, _) in requests)
            {
                var wait = Stopwatch.StartNew();
                while (wait.ElapsedMilliseconds < 15000)
                {
                    var owned = TerminalPlacement.FindOwnedWindow(request.Window);
                    var candidates = TerminalPlacement.Windows().Where(h => TerminalPlacement.Candidate(h, request));
                    var handles = candidates.Append(owned).Where(h => h != 0).Distinct().ToArray();
                    if (handles.Length == 0) break;
                    foreach (var hwnd in handles)
                        if (File.Exists(Path.Combine(request.Directory, "bootstrap-done"))
                            && WindowTitle(hwnd).Contains(request.Token + " check", StringComparison.Ordinal))
                            PostMessage(hwnd, 0x0010, 0, 0);
                    await Task.Delay(50);
                }
            }
            foreach (var process in processes.Concat(watchers))
            {
                try
                {
                    if (!process.HasExited && !await Wait(process, 10000)) process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                }
                finally
                {
                    if (process.HasExited) observations.Add(new { processId = process.Id, exitCode = process.ExitCode });
                    process.Dispose();
                }
            }
            File.WriteAllText(Path.Combine(evidence, "observations.json"), JsonSerializer.Serialize(observations, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    private static async Task StopShell((int Pid, long Start) shell)
    {
        // Only stop the exact recorded WSL process, never a shared terminal host or reused PID.
        if (TabSession.Liveness("Ubuntu")?.Invoke(shell.Pid) != shell.Start) return;
        using var kill = Process.Start(new ProcessStartInfo("wsl.exe")
        {
            UseShellExecute = false, CreateNoWindow = true,
            ArgumentList = { "-d", "Ubuntu", "--exec", "bash", "-c", "kill -KILL " + shell.Pid }
        });
        if (kill is null) return;
        try { if (!await Wait(kill, 10000)) kill.Kill(entireProcessTree: true); }
        finally { await kill.WaitForExitAsync(); }
    }

    private static string WindowTitle(nint hwnd)
    {
        var title = new StringBuilder(512);
        GetWindowText(hwnd, title, title.Capacity);
        return title.ToString();
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint hwnd, StringBuilder title, int size);

    // PrintWindow captures only the dedicated test terminal; never capture the user's desktop.
    private static bool CaptureWindow(nint hwnd, string file)
    {
        if (!GetWindowRect(hwnd, out var rect)) return false;
        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0) return false;
        var screen = GetDC(0);
        var dc = CreateCompatibleDC(screen);
        var info = new BitmapInfo { Size = 40, Width = width, Height = -height, Planes = 1, BitCount = 32 };
        var bitmap = CreateDIBSection(screen, ref info, 0, out var bits, 0, 0);
        var previous = SelectObject(dc, bitmap);
        try
        {
            if (bitmap == 0 || !PrintWindow(hwnd, dc, 2)) return false;
            using var image = new SKBitmap();
            if (!image.InstallPixels(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque), bits, width * 4)) return false;
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var output = File.Create(file);
            data.SaveTo(output);
            return true; // A successful capture can still be blank with hardware rendering; inspect it.
        }
        finally { SelectObject(dc, previous); DeleteObject(bitmap); DeleteDC(dc); ReleaseDC(0, screen); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo
    {
        public uint Size;
        public int Width, Height;
        public ushort Planes, BitCount;
        public uint Compression, ImageSize;
        public int XPixelsPerMeter, YPixelsPerMeter;
        public uint ColorsUsed, ColorsImportant;
    }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out TerminalPlacement.Rect rect);
    [DllImport("user32.dll")] private static extern nint GetDC(nint hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint hwnd, nint dc);
    [DllImport("user32.dll")] private static extern bool PrintWindow(nint hwnd, nint dc, uint flags);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] private static extern nint CreateDIBSection(nint dc, ref BitmapInfo info, uint usage, out nint bits, nint section, uint offset);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc, nint item);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint item);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint dc);

    private static async Task Until(Func<bool> ready, int milliseconds)
    {
        var timer = Stopwatch.StartNew();
        while (!ready() && timer.ElapsedMilliseconds < milliseconds) await Task.Delay(50);
        Assert.True(ready(), "Timed out waiting for terminal placement evidence");
    }
    private static async Task<bool> Wait(Process process, int milliseconds)
    {
        using var timeout = new CancellationTokenSource(milliseconds);
        try { await process.WaitForExitAsync(timeout.Token); return true; }
        catch (OperationCanceledException) { return false; }
    }
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern nint SetThreadDpiAwarenessContext(nint context);
    [DllImport("user32.dll")] private static extern bool PostMessage(nint hwnd, uint message, nint wparam, nint lparam);
}
