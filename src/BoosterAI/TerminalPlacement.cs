using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BoosterAI;

/// <summary>Visible physical-pixel bounds, independent of Win32's workspace/DPI virtualization.</summary>
internal sealed record TerminalBounds(int X, int Y, int Width, int Height)
{
    public bool Valid => Width > 0 && Height > 0 && Width <= 100000 && Height <= 100000
        && Math.Abs((long)X) <= 1000000 && Math.Abs((long)Y) <= 1000000;
}
internal sealed record TerminalDisplay(string Device, TerminalBounds WorkArea, bool Primary);
internal sealed record TerminalWindowState(TerminalBounds Bounds, string Device, TerminalBounds WorkArea, bool Maximized);
internal sealed record TerminalWatchRequest(string Token, string Window, string Directory, string StateFile, long[] ExistingWindows, int WatcherPid = 0);

[JsonSerializable(typeof(TerminalWindowState))]
[JsonSerializable(typeof(TerminalWatchRequest))]
internal partial class TerminalPlacementJson : JsonSerializerContext;

/// <summary>
/// Remembers only terminals launched by BoosterAI. A short bootstrap tab supplies an unguessable title for
/// first contact; after that a native window property binds the watcher to that exact HWND's lifetime.
/// No process-wide main-window guess, foreground window, global Terminal setting or keyboard input is used.
/// </summary>
internal static class TerminalPlacement
{
    internal const string WatchArgument = "--watch-terminal-placement";
    internal const string PropertyPrefix = "BoosterAI.TerminalPlacement.";
    internal static string StateFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BoosterAI", "terminal-placement.json");

    internal static TerminalWindowState? Read(string file)
    {
        try
        {
            var state = JsonSerializer.Deserialize(File.ReadAllText(file), TerminalPlacementJson.Default.TerminalWindowState);
            return state is { Bounds.Valid: true, WorkArea.Valid: true } && !string.IsNullOrEmpty(state.Device) ? state : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    /// <summary>Keep the same work-area proportions if a display moved, resized, or was disconnected.</summary>
    internal static TerminalBounds? RestoreBounds(TerminalWindowState? state, IReadOnlyList<TerminalDisplay> displays)
    {
        if (state is not { Bounds.Valid: true, WorkArea.Valid: true } || displays.Count == 0) return null;
        var display = displays.FirstOrDefault(d => d.Device == state.Device)
            ?? displays.FirstOrDefault(d => d.Primary) ?? displays[0];
        var work = display.WorkArea;
        if (!work.Valid) return null;
        var old = state.WorkArea;
        var width = Math.Clamp((int)Math.Round((double)state.Bounds.Width / old.Width * work.Width), Math.Min(240, work.Width), work.Width);
        var height = Math.Clamp((int)Math.Round((double)state.Bounds.Height / old.Height * work.Height), Math.Min(160, work.Height), work.Height);
        var x = work.X + (int)Math.Round(((double)state.Bounds.X - old.X) / old.Width * work.Width);
        var y = work.Y + (int)Math.Round(((double)state.Bounds.Y - old.Y) / old.Height * work.Height);
        return new(Math.Clamp(x, work.X, work.X + work.Width - width), Math.Clamp(y, work.Y, work.Y + work.Height - height), width, height);
    }

    internal static void Write(string file, TerminalWindowState state)
    {
        // Different windows can update the remembered placement. A unique temporary file keeps each write atomic.
        var temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(temp, JsonSerializer.Serialize(state, TerminalPlacementJson.Default.TerminalWindowState));
            File.Move(temp, file, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    /// <summary>Prepare the optional handshake before opening Terminal. Failure must never prevent the actual tabs.</summary>
    internal static TerminalWatchRequest? Prepare(string window, string? stateFile = null)
    {
        if (!OperatingSystem.IsWindows() || FindOwnedWindow(window) != 0) return null;
        TerminalWatchRequest? request = null;
        try
        {
            var token = "BoosterAI-placement-" + Guid.NewGuid().ToString("N");
            var dir = Path.Combine(Path.GetTempPath(), token);
            if (dir.Contains(';')) return null; // wt splits semicolons even inside quoted arguments.
            Directory.CreateDirectory(dir);
            request = new(token, window, dir, stateFile ?? StateFile, Windows().Select(h => (long)h).ToArray());
            var ready = Path.Combine(dir, "ready");
            // No semicolons: wt treats them as command delimiters, including inside quoted arguments.
            File.WriteAllText(Path.Combine(dir, "bootstrap.cmd"),
                "@echo off\r\nfor /l %%i in (1,1,12) do (\r\n if exist \"" + ready + "\" exit /b 0\r\n timeout /t 1 /nobreak >nul 2>nul\r\n)\r\nexit /b 0\r\n");
            var file = Path.Combine(dir, "request.json");
            File.WriteAllText(file, JsonSerializer.Serialize(request, TerminalPlacementJson.Default.TerminalWatchRequest));
            using var process = Process.Start(WatcherStart(file));
            return process is null ? null : request with { WatcherPid = process.Id };
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            if (request is not null) Clean(request.Directory);
            return null;
        }
    }

    internal static ProcessStartInfo WatcherStart(string requestFile)
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("No executable path");
        var assembly = Assembly.GetExecutingAssembly().Location;
        // A unit/integration harness may call this through testhost. Never restart testhost as the watcher.
        var ownHost = Path.GetFileNameWithoutExtension(exe).Equals("BoosterAI", StringComparison.OrdinalIgnoreCase);
        // A packaged watcher must not lock the installed exe: updates replace that file while terminals stay open.
        // Framework-dependent development/test builds use dotnet + DLL; single-file releases share a cache copy.
        var singleFile = ownHost && string.IsNullOrEmpty(assembly);
        var psi = new ProcessStartInfo(singleFile ? CachedWatcher(exe) : "dotnet")
            { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        if (!singleFile) psi.ArgumentList.Add(assembly);
        psi.ArgumentList.Add(WatchArgument);
        psi.ArgumentList.Add(requestFile);
        return psi;
    }

    internal static string CachedWatcher(string exe, string? cacheRoot = null)
    {
        var source = new FileInfo(exe);
        var root = cacheRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BoosterAI", "placement-watchers");
        var generation = source.Length.ToString("x") + "-" + source.LastWriteTimeUtc.Ticks.ToString("x");
        var directory = Path.Combine(root, generation);
        Directory.CreateDirectory(directory);
        // Running old watchers hold their own exe open; leave those generations until a later launch can remove them.
        foreach (var old in Directory.GetDirectories(root))
            if (old != directory) Clean(old);
        var cached = Path.Combine(directory, "BoosterAI.exe");
        if (!File.Exists(cached))
        {
            var temp = cached + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.Copy(exe, temp);
                try { File.Move(temp, cached); }
                catch (IOException) when (File.Exists(cached)) { } // Another session populated this generation.
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        return cached;
    }

    internal static List<string> WithBootstrap(IReadOnlyList<string> terminalArgs, TerminalWatchRequest request)
    {
        // First tab is active long enough to identify the new HWND; it then exits, leaving the real first tab active.
        var args = terminalArgs.Take(2).ToList();
        args.AddRange(["new-tab", "-p", Launcher.ZoomProfileName, "--title", request.Token, "--suppressApplicationTitle",
            "cmd.exe", "/d", "/c", Path.Combine(request.Directory, "bootstrap.cmd"), ";"]);
        args.AddRange(terminalArgs.Skip(2));
        // A real tab proves it exists before the bootstrap is allowed to close. This also covers slow WSL startup.
        for (var i = 0; i + 6 < args.Count; i++)
        {
            if (args[i] != "wsl.exe" || args[i + 1] != "-d" || args[i + 3] != "--exec"
                || args[i + 4] != "bash" || args[i + 5] != "-lic") continue;
            args[i + 6] = "printf ready > " + Launcher.Quote(PathMapping.ToWslPath(Path.Combine(request.Directory, "real-ready")))
                + "\n" + args[i + 6];
            break;
        }
        return args;
    }

    internal static bool IsCandidate(long handle, string title, string windowClass, string processName,
        TerminalWatchRequest request) => !request.ExistingWindows.Contains(handle)
        && title.Contains(request.Token, StringComparison.Ordinal) && windowClass == "CASCADIA_HOSTING_WINDOW"
        && processName.Equals("WindowsTerminal", StringComparison.OrdinalIgnoreCase);

    internal static nint FindOwnedWindow(string window) => Windows().FirstOrDefault(h => GetProp(h, PropertyPrefix + window) != 0);

    /// <summary>Headless process; survives AfterLaunch=close and exits when its exact terminal window disappears.</summary>
    internal static void Watch(string requestFile)
    {
        if (!OperatingSystem.IsWindows()) return;
        // GetWindowRect, monitor work areas and DWM bounds must all use physical pixels, including mixed-DPI displays.
        SetThreadDpiAwarenessContext(new nint(-4));
        TerminalWatchRequest? request = null;
        nint hwnd = 0;
        string? property = null;
        try
        {
            request = JsonSerializer.Deserialize(File.ReadAllText(requestFile), TerminalPlacementJson.Default.TerminalWatchRequest);
            if (request is null) return;
            var until = Stopwatch.StartNew();
            while (until.Elapsed < TimeSpan.FromSeconds(15))
            {
                var candidates = Windows().Where(h => Candidate(h, request)).ToArray();
                if (candidates.Length == 1) { hwnd = candidates[0]; break; }
                Thread.Sleep(50);
            }
            if (hwnd == 0) return;
            property = PropertyPrefix + request.Window;
            if (GetProp(hwnd, property) != 0 || !SetProp(hwnd, property, Environment.ProcessId)) return;
            // Never close the last bootstrap tab before at least one real tab has actually started.
            while (!File.Exists(Path.Combine(request.Directory, "real-ready")) && until.Elapsed < TimeSpan.FromSeconds(15)
                && GetProp(hwnd, property) == Environment.ProcessId) Thread.Sleep(50);
            if (!File.Exists(Path.Combine(request.Directory, "real-ready"))) return;
            var saved = Read(request.StateFile);
            Restore(hwnd, saved);
            // Unblock the bootstrap even if there was no saved state. It is bounded independently if this process dies.
            File.WriteAllText(Path.Combine(request.Directory, "ready"), "ready");
            TerminalWindowState? previous = null;
            while (GetProp(hwnd, property) == Environment.ProcessId)
            {
                var current = Capture(hwnd);
                if (current is not null && current != previous)
                {
                    try { Write(request.StateFile, current); previous = current; }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
                }
                Thread.Sleep(500);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or System.ComponentModel.Win32Exception) { }
        finally
        {
            if (hwnd != 0 && property is not null && GetProp(hwnd, property) == Environment.ProcessId) RemoveProp(hwnd, property);
            if (request is not null) Clean(request.Directory);
        }
    }

    private static void Clean(string directory)
    {
        try { Directory.Delete(directory, recursive: true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    internal static bool Candidate(nint hwnd, TerminalWatchRequest request)
    {
        var title = new StringBuilder(256); GetWindowText(hwnd, title, title.Capacity);
        if (!title.ToString().Contains(request.Token, StringComparison.Ordinal) || request.ExistingWindows.Contains((long)hwnd)) return false;
        var windowClass = new StringBuilder(256); GetClassName(hwnd, windowClass, windowClass.Capacity);
        GetWindowThreadProcessId(hwnd, out var pid);
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return IsCandidate((long)hwnd, title.ToString(), windowClass.ToString(), process.ProcessName, request);
        }
        catch (ArgumentException) { return false; }
    }

    internal static IReadOnlyList<nint> Windows()
    {
        var handles = new List<nint>();
        EnumWindows((h, _) => { handles.Add(h); return true; }, 0);
        return handles;
    }

    internal static List<TerminalDisplay> Displays()
    {
        var displays = new List<TerminalDisplay>();
        EnumDisplayMonitors(0, 0, (nint h, nint _, ref Rect r, nint data) =>
        {
            var info = MonitorInfo.Create();
            if (GetMonitorInfo(h, ref info)) displays.Add(new(info.Device, info.Work.Bounds, (info.Flags & 1) != 0));
            return true;
        }, 0);
        return displays;
    }

    internal static TerminalWindowState? Capture(nint hwnd)
    {
        // Minimized windows report an off-screen icon rectangle; retain the last real placement instead.
        if (IsIconic(hwnd) || !GetWindowRect(hwnd, out var outer)) return null;
        var info = MonitorInfo.Create();
        if (!GetMonitorInfo(MonitorFromWindow(hwnd, 2), ref info)) return null;
        var bounds = VisibleBounds(hwnd, outer);
        return bounds.Valid ? new(bounds, info.Device, info.Work.Bounds, IsZoomed(hwnd)) : null;
    }

    internal static void Restore(nint hwnd, TerminalWindowState? saved)
    {
        if (RestoreBounds(saved, Displays()) is not { } bounds) return;
        if (IsZoomed(hwnd) && !saved!.Maximized)
        {
            ShowWindowAsync(hwnd, 4); // SW_SHOWNOACTIVATE restores without taking keyboard focus.
            var wait = Stopwatch.StartNew();
            while (IsZoomed(hwnd) && wait.ElapsedMilliseconds < 500) Thread.Sleep(10);
        }
        if (!GetWindowRect(hwnd, out var outer)) return;
        var visible = VisibleBounds(hwnd, outer);
        // The invisible resize borders are excluded from saved geometry, and reintroduced for this new window.
        SetWindowPos(hwnd, 0, bounds.X + outer.Left - visible.X, bounds.Y + outer.Top - visible.Y,
            bounds.Width + outer.Bounds.Width - visible.Width, bounds.Height + outer.Bounds.Height - visible.Height,
            0x0004 | 0x0010 | 0x0200); // NOZORDER | NOACTIVATE | NOOWNERZORDER
        if (saved!.Maximized)
        {
            var placement = new WindowPlacement { Length = Marshal.SizeOf<WindowPlacement>() };
            if (GetWindowPlacement(hwnd, ref placement))
            {
                placement.ShowCmd = 3; // SW_SHOWMAXIMIZED; do not use foreground activation or keyboard simulation.
                placement.Flags = 4; // WPF_ASYNCWINDOWPLACEMENT
                SetWindowPlacement(hwnd, ref placement);
            }
        }
    }

    private static TerminalBounds VisibleBounds(nint hwnd, Rect fallback) =>
        DwmGetWindowAttribute(hwnd, 9, out var frame, Marshal.SizeOf<Rect>()) == 0 && frame.Bounds.Valid ? frame.Bounds : fallback.Bounds;

    [StructLayout(LayoutKind.Sequential)] internal struct Rect
    {
        public int Left, Top, Right, Bottom;
        public readonly TerminalBounds Bounds => new(Left, Top, Right - Left, Bottom - Top);
    }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct WindowPlacement
    {
        public int Length, Flags, ShowCmd;
        public Point MinPosition, MaxPosition;
        public Rect NormalPosition;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor, Work;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
        public static MonitorInfo Create() => new() { Size = Marshal.SizeOf<MonitorInfo>(), Device = "" };
    }
    private delegate bool EnumWindowsProc(nint hwnd, nint param);
    private delegate bool MonitorEnumProc(nint monitor, nint hdc, ref Rect rect, nint data);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, nint param);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint hwnd, StringBuilder text, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint hwnd, StringBuilder text, int max);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool SetProp(nint hwnd, string name, nint value);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern nint GetProp(nint hwnd, string name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint RemoveProp(nint hwnd, string name);
    [DllImport("user32.dll")] private static extern nint SetThreadDpiAwarenessContext(nint context);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsZoomed(nint hwnd);
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(nint hdc, nint clip, MonitorEnumProc callback, nint data);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(nint hwnd, int command);
    [DllImport("user32.dll")] private static extern bool GetWindowPlacement(nint hwnd, ref WindowPlacement placement);
    [DllImport("user32.dll")] private static extern bool SetWindowPlacement(nint hwnd, ref WindowPlacement placement);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out Rect rect, int size);
}
