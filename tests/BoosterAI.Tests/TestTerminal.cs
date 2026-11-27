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

    /// <summary>A start with a tab named like this fails as if the terminal did not open.</summary>
    public const string FailPrefix = "fail-terminal-";
    public static readonly ConcurrentQueue<(int Percent, bool Restore)> ZoomRequests = new();

    /// <summary>Every notification of the run (#269): none reaches the real toast or the HKCU protocol registration.</summary>
    public static readonly ConcurrentQueue<(string Title, string Body, bool OpenQuestion)> Notifications = new();

    public static readonly string SessionRoot =
        Path.Combine(Path.GetTempPath(), "boosterai-test-sessions-" + Environment.ProcessId);

    [ModuleInitializer]
    internal static void Install()
    {
        Launcher.OpenTerminal = r =>
        {
            if (r.Tabs.Any(t => t.Name.StartsWith(FailPrefix, StringComparison.Ordinal)))
                throw new InvalidOperationException("synthetic terminal failure");
            Requests.Enqueue(r);
        };
        TabTitles.Restore = (_, _) => { };
        Notifier.Post = (title, body, openQuestion) => Notifications.Enqueue((title, body, openQuestion));
        TerminalFocus.Run = _ => { };
        // Arranging tabs (#254) never reads or moves the developer's real Windows Terminal tabs.
        TerminalOrder.Run = _ => { };
        TerminalOrder.Read = _ => null;
        TerminalOrder.Appends = () => true;
        Launcher.RequestZoom = (percent, restore) => ZoomRequests.Enqueue((percent, restore));
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
