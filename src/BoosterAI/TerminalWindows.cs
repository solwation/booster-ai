namespace BoosterAI;

/// <summary>
/// Notices that a session's terminal window was closed (#283). Windows Terminal can keep a closed window's tabs (their PTYs,
/// and so the shells in WSL) alive until its next command, so the tab logs and /proc still show them running. The window
/// itself is gone, though: BoosterAI marks the windows it opens with a property (<see cref="TerminalPlacement"/>), and a
/// marked window seen earlier whose HWND no longer exists means every tab in it was closed. A window never seen marked, or
/// one only hidden or minimized, is never reported.
/// </summary>
internal sealed class TerminalWindows
{
    private readonly Dictionary<string, nint> _seen = new(StringComparer.Ordinal);

    /// <summary>The marked window of a session (0: none); tests replace it so no real window is looked at.</summary>
    internal Func<string, nint> Find { get; set; } = name => OperatingSystem.IsWindows() ? TerminalPlacement.FindOwnedWindow(name) : 0;

    /// <summary>Whether an HWND still exists, hidden or not.</summary>
    internal Func<nint, bool> Exists { get; set; } = hwnd => OperatingSystem.IsWindows() && TerminalPlacement.IsWindow(hwnd);

    /// <summary>
    /// The windows among <paramref name="windows"/> that were seen before and are destroyed now. Each closed window is
    /// reported once; a later launch into the same name opens a new window, which is tracked anew.
    /// </summary>
    internal IReadOnlyList<string> Closed(IEnumerable<string> windows)
    {
        var closed = new List<string>();
        foreach (var name in windows.Distinct(StringComparer.Ordinal))
        {
            var hwnd = Find(name);
            if (hwnd != 0) { _seen[name] = hwnd; continue; }
            if (!_seen.TryGetValue(name, out var last)) continue;
            // The property can only be missing from a live window for a moment (a watcher handing over); wait for its end.
            if (Exists(last)) continue;
            _seen.Remove(name);
            closed.Add(name);
        }
        return closed;
    }
}
