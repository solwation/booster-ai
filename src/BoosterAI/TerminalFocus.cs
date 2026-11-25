using System.Diagnostics;
using System.Globalization;

namespace BoosterAI;

/// <summary>
/// "Show the tab in the terminal" (#213): switches Windows Terminal to the tab a BoosterAI row opened.
/// Every tab of a session opens in the session's named window (<see cref="TabSession.WindowName"/>) in start order, so the
/// tab's position is its rank among the session's tabs that still have a terminal tab. Windows Terminal's CLI can only
/// address a tab by index (<c>focus-tab -t</c>), never by title, so a tab the user dragged to another position, or a
/// non-BoosterAI tab the user opened in that window, makes the index wrong; the focus then lands on a neighbour.
/// Linux terminals have no way to select one tab from outside (windows are only addressable by their active tab's title), so
/// the action is not offered there.
/// </summary>
internal static class TerminalFocus
{
    /// <summary>One process to start.</summary>
    internal sealed record Command(string Exe, IReadOnlyList<string> Args);

    /// <summary>Whether this platform can focus a single tab; the row hides the action otherwise. Screenshots may force it on.</summary>
    internal static bool Supported { get; set; } = OperatingSystem.IsWindows();

    /// <summary>The tab's index in its window: its rank by start order (tab1, tab2, ...) among <paramref name="liveIds"/>, or null when it is not among them.</summary>
    internal static int? Index(string trackId, IEnumerable<string> liveIds)
    {
        var ordered = liveIds.Distinct().OrderBy(Number).ThenBy(i => i, StringComparer.Ordinal).ToList();
        var at = ordered.IndexOf(trackId);
        return at < 0 ? null : at;

        static int Number(string id) => int.TryParse(id.Length > 3 ? id[3..] : "", NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : int.MaxValue;
    }

    /// <summary>
    /// <c>wt.exe -w &lt;window&gt; focus-tab -t &lt;index&gt;</c>; null off Windows or for an unknown position. A window name that
    /// no longer exists would make wt.exe open a new window, which is why the row only offers this while the tab is alive.
    /// </summary>
    internal static Command? Build(string window, int? index, bool windows) =>
        windows && index is >= 0 && window.Length > 0
            ? new Command("wt.exe", ["-w", window, "focus-tab", "-t", index.Value.ToString(CultureInfo.InvariantCulture)])
            : null;

    internal static string Describe(Command command) =>
        command.Exe + " " + string.Join(' ', command.Args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));

    /// <summary>Starts the command; tests replace it so no terminal is touched.</summary>
    internal static Action<Command> Run { get; set; } = RunReal;

    private static void RunReal(Command command)
    {
        var psi = new ProcessStartInfo(command.Exe) { UseShellExecute = false };
        foreach (var arg in command.Args) psi.ArgumentList.Add(arg);
        try { using var process = Process.Start(psi); }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { /* no wt.exe: nothing to focus */ }
    }
}
