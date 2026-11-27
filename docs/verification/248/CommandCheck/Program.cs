using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using BoosterAI;

if (!OperatingSystem.IsWindows()) return 0;
var output = args[0];
var terminal = args[1];
var token = "BoosterAI248-" + Guid.NewGuid().ToString("N");
var marker = "/tmp/" + token;
var foreground = Native.GetForegroundWindow();
var desktop = Native.CreateDesktop(token, null, 0, 0, 0x1FF, 0);
if (desktop == 0) throw new System.ComponentModel.Win32Exception();
Process? process = null;
nint hwnd = 0;
var focusUnchanged = true;
string? markerText = null;
var titles = new List<string>();
var sessionRoot = Path.Combine(Path.GetTempPath(), token);
// A real session under %TEMP% (WSL sees it through /mnt/c), with the status channel on (#248).
var session = new TabSession(sessionRoot, DateTime.Now);
var track = session.NewTrack(logo: false, statusChannel: true) ?? throw new Exception("No track");
var cli = Launcher.TabScript(new TabConfig { Name = token, Directory = "/tmp", Llm = "claude", Model = "sonnet" }, track: track);
if (!cli.Contains("BOOSTERAI_TAB_EVENTS") || !cli.Contains("--mcp-config")) throw new Exception("No channel in the tracked script");
// A neutral fake claude first on PATH: it records what the tab gave it, uses boosterai-tab from PATH and does an MCP
// initialize with the server from its --mcp-config. No real agent or network.
var bin = "/tmp/" + token + "-bin";
var fake = string.Join("\n",
    "#!/bin/bash",
    "while [ $# -gt 0 ] && [ \"$1\" != --mcp-config ]; do shift; done",
    "cfg=$2",
    "srv=$(python3 -c 'import json,sys; c=json.load(open(sys.argv[1]))[\"mcpServers\"][\"boosterai\"]; print(\" \".join([c[\"command\"]]+c[\"args\"]))' \"$cfg\")",
    "init=$(printf '%s\\n' '{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-06-18\"}}' | $srv | python3 -c 'import json,sys; print(json.loads(sys.stdin.readline())[\"result\"][\"serverInfo\"][\"name\"])')",
    "boosterai-tab status done 'Windows Terminal check' >/dev/null",
    "rc=$?",
    "printf '%s|%s|%s|%s|%s|%s|%s' \"$rc\" \"$init\" \"$(command -v boosterai-tab)\" \"$BOOSTERAI_TAB_EVENTS\" \"$BOOSTERAI_TAB_PROTOCOL\" $$ $(cut -d' ' -f22 /proc/$$/stat) > " + ShellQuote(marker),
    "sleep 20", "");
File.WriteAllText(Path.Combine(sessionRoot, token + "-claude"), fake);
var fakeWsl = PathMapping.ToWslPath(Path.Combine(sessionRoot, token + "-claude"));
RunWsl("mkdir -p " + ShellQuote(bin) + " && cp " + ShellQuote(fakeWsl) + " " + ShellQuote(bin + "/claude") + " && chmod +x " + ShellQuote(bin + "/claude"));
var script = "export PATH=" + ShellQuote(bin) + ":$PATH\n" + cli;
if (script.Contains(';')) throw new Exception("WT splits semicolons");
var wt = Launcher.WindowsTerminalArgs([new TabConfig { Name = token, Script = script }], "Ubuntu", window: token);
try
{
    var startup = new Native.Startup { cb = Marshal.SizeOf<Native.Startup>(), desktop = "winsta0\\" + token };
    var command = new StringBuilder(Quote(terminal) + " " + string.Join(' ', wt.Select(Quote)));
    if (!Native.CreateProcess(terminal, command, 0, 0, false, 0x08000000, 0, null, ref startup, out var info))
        throw new System.ComponentModel.Win32Exception();
    Native.CloseHandle(info.thread);
    Native.CloseHandle(info.process);
    process = Process.GetProcessById(info.pid);
    var timer = Stopwatch.StartNew();
    while (timer.Elapsed < TimeSpan.FromSeconds(40))
    {
        focusUnchanged &= Native.GetForegroundWindow() == foreground;
        titles.Clear();
        Native.EnumDesktopWindows(desktop, (window, _) =>
        {
            var title = new StringBuilder(512);
            Native.GetWindowText(window, title, title.Capacity);
            titles.Add(title.ToString());
            if (title.ToString().Contains(token, StringComparison.Ordinal)) hwnd = window;
            return true;
        }, 0);
        markerText = RunWsl("cat " + ShellQuote(marker) + " 2>/dev/null || true").Trim();
        if (hwnd != 0 && markerText.Length > 0) break;
        Thread.Sleep(200);
    }
    var parts = markerText?.Split('|') ?? [];
    var status = session.ReadChannel(track.Id).Status;
    var passed = hwnd != 0 && parts.Length == 7 && parts[0] == "0" && parts[1] == "boosterai"
        && parts[2] == TabChannelAgent.CommandPath(track.Dir) && parts[3] == $"{track.Dir}/{track.Id}.events" && parts[4] == "1"
        && status is { State: AgentState.Done, Text: "Windows Terminal check" };
    if (!passed)
        { File.WriteAllText(output + ".failure.json", JsonSerializer.Serialize(new { hwndFound = hwnd != 0, markerText, status = status?.ToString(), titles, processExited = process.HasExited })); throw new Exception("Channel check failed"); }
    Native.PostMessage(hwnd, 0x0010, 0, 0);
    if (!process.WaitForExit(10000)) throw new TimeoutException("WT did not close");
    focusUnchanged &= Native.GetForegroundWindow() == foreground;
    if (!focusUnchanged) throw new Exception("Foreground changed");
    File.WriteAllText(output, JsonSerializer.Serialize(new
    {
        passed = true, privateDesktop = true, foregroundUnchanged = focusUnchanged, scriptHasSemicolon = false,
        commandExit = parts[0], mcpServerName = parts[1], commandOnPath = parts[2].Replace(track.Dir, "<session>"),
        eventsVariable = parts[3].Replace(track.Dir, "<session>"), protocol = parts[4],
        statusReadByBoosterAI = new { state = status!.State.ToString(), text = status.Text }, terminalClosed = process.HasExited,
    }, new JsonSerializerOptions { WriteIndented = true }));
    return 0;
}
finally
{
    if (hwnd != 0) Native.PostMessage(hwnd, 0x0010, 0, 0);
    if (process is { HasExited: false }) { process.Kill(entireProcessTree: true); process.WaitForExit(); }
    process?.Dispose();
    if (markerText?.Split('|') is [_, _, _, _, _, var pidText, var startText]
        && int.TryParse(pidText, out var shellPid) && long.TryParse(startText, out var shellStart))
        RunWsl("test $(cut -d' ' -f22 /proc/" + shellPid + "/stat 2>/dev/null) = " + shellStart
            + " && kill -HUP " + shellPid + " 2>/dev/null || true");
    RunWsl("rm -rf " + ShellQuote(marker) + " " + ShellQuote(bin));
    try { Directory.Delete(sessionRoot, recursive: true); } catch (IOException) { }
    Native.CloseDesktop(desktop);
}

static string RunWsl(string script)
{
    var psi = new ProcessStartInfo("wsl.exe") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
    foreach (var arg in new[] { "-d", "Ubuntu", "-e", "bash", "-c", script }) psi.ArgumentList.Add(arg);
    using var process = Process.Start(psi)!;
    var stdout = process.StandardOutput.ReadToEndAsync();
    var stderr = process.StandardError.ReadToEndAsync();
    try
    {
        if (!process.WaitForExit(10000)) throw new TimeoutException("WSL probe timed out");
        if (process.ExitCode != 0) throw new Exception(stderr.GetAwaiter().GetResult());
        return stdout.GetAwaiter().GetResult();
    }
    finally { if (!process.HasExited) process.Kill(entireProcessTree: true); process.WaitForExit(); }
}
static string ShellQuote(string s) => "'" + s.Replace("'", "'\\''") + "'";
static string Quote(string s)
{
    if (s.Length > 0 && !s.Any(c => char.IsWhiteSpace(c) || c == '"')) return s;
    var b = new StringBuilder("\"");
    var slashes = 0;
    foreach (var c in s)
    {
        if (c == '\\') { slashes++; continue; }
        b.Append('\\', c == '"' ? slashes * 2 + 1 : slashes).Append(c);
        slashes = 0;
    }
    return b.Append('\\', slashes * 2).Append('"').ToString();
}
static class Native
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] public struct Startup { public int cb; public string? reserved, desktop, title; public int x,y,width,height,charX,charY,fill,flags; public short show,reservedSize; public nint reservedPtr,input,output,error; }
    [StructLayout(LayoutKind.Sequential)] public struct ProcessInfo { public nint process, thread; public int pid, tid; }
    public delegate bool EnumProc(nint hwnd, nint param);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern nint CreateDesktop(string name, string? device, nint mode, uint flags, uint access, nint security);
    [DllImport("user32.dll")] public static extern bool CloseDesktop(nint desktop);
    [DllImport("user32.dll")] public static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool EnumDesktopWindows(nint desktop, EnumProc callback, nint param);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(nint window, StringBuilder title, int count);
    [DllImport("user32.dll")] public static extern bool PostMessage(nint hwnd, uint message, nint wparam, nint lparam);
    [DllImport("kernel32.dll")] public static extern bool CloseHandle(nint handle);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern bool CreateProcess(string? application, StringBuilder command, nint processSecurity, nint threadSecurity, bool inherit, uint flags, nint environment, string? directory, ref Startup startup, out ProcessInfo info);
}
