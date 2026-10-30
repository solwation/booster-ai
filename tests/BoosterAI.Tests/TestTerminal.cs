using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using BoosterAI;

/// <summary>
/// Keeps the test run off the developer's desktop: every Launch is recorded instead of opening a real terminal
/// (a view-model test that reached Restart once opened two Ptyxis windows running claude), and sessions go to a
/// temporary folder instead of the real one that BoosterAI watches.
/// </summary>
internal static class TestTerminal
{
    public static readonly ConcurrentQueue<Launcher.TerminalRequest> Requests = new();

    public static readonly string SessionRoot =
        Path.Combine(Path.GetTempPath(), "boosterai-test-sessions-" + Environment.ProcessId);

    [ModuleInitializer]
    internal static void Install()
    {
        Launcher.OpenTerminal = Requests.Enqueue;
        TabSession.DefaultRoot = SessionRoot;
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { Directory.Delete(SessionRoot, recursive: true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        };
    }

    /// <summary>The recorded starts that contain a tab with this name (tests run in parallel, so names keep them apart).</summary>
    public static List<Launcher.TerminalRequest> For(string tabName) =>
        Requests.Where(r => r.Tabs.Any(t => t.Name == tabName)).ToList();
}
