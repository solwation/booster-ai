using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace BoosterAI;

/// <summary>
/// Keeps BoosterAI's tabs in its session's Windows Terminal window in the order of their rows (#254). Windows Terminal
/// appends every <c>new-tab</c> at the end and its command line can neither open a tab at a position nor move one.
/// What it can do (since 1.11): <c>focus-tab -t i ; move-pane -t &lt;beyond the last tab&gt;</c> moves the single pane of
/// tab i into a new tab at the end, keeping its process, title and colour. A sequence of such "move to the end" steps
/// produces any order, so a new tab, or a reordered row, is put in place by moving the tabs that belong after it to
/// the end, in row order.
/// <para>
/// BoosterAI knows the window's tabs from a UI Automation snapshot of the window it owns (<see cref="TerminalPlacement"/>):
/// the tab titles left to right, so tabs the user dragged or opened by hand are seen. Without one (no owned window,
/// the snapshot failed) it uses the order it last arranged, kept per session in <c>window-order</c>, in which a tab
/// the user opened or dragged by hand is not known. Tabs that are not BoosterAI's are never moved: BoosterAI's own
/// tabs keep their order among them, and a tab moved to the end lands after them.
/// </para>
/// Every <c>wt -w</c> command brings the window forward, so a reorder briefly shows the terminal and BoosterAI then
/// takes the focus back. Linux terminals cannot address a tab from outside; there nothing is arranged.
/// </summary>
internal static class TerminalOrder
{
    /// <summary>A move-pane target past any real tab: the pane then becomes a new tab at the end.</summary>
    internal const int BeyondLast = 9999;
    /// <summary>A tab BoosterAI does not own, in the saved order (window-order).</summary>
    internal const string ForeignMark = "-";

    /// <summary>Whether this platform can arrange tabs; tests turn it on.</summary>
    internal static bool Supported { get; set; } = OperatingSystem.IsWindows();

    /// <summary>
    /// The steps that put <paramref name="desired"/> (own tab ids in row order) in that order in <paramref name="window"/>
    /// (its slots left to right, null for a tab BoosterAI does not own). <see cref="Plan.Moves"/> are the indices of the
    /// tabs to move to the end, each counted in the window as it is at that step; <see cref="Plan.Order"/> holds, for each
    /// resulting slot, the index it had in <paramref name="window"/>. The longest start of <paramref name="desired"/> that
    /// is already in order stays where it is; ids not in the window are ignored.
    /// </summary>
    internal static Plan Arrange(IReadOnlyList<string?> window, IReadOnlyList<string> desired)
    {
        var wanted = desired.Distinct().Where(id => window.Contains(id)).ToList();
        var kept = 0;
        foreach (var slot in window)
            if (kept < wanted.Count && slot == wanted[kept]) kept++;
        var order = Enumerable.Range(0, window.Count).ToList();
        var moves = new List<int>();
        for (var i = kept; i < wanted.Count; i++)
        {
            var at = order.FindIndex(o => window[o] == wanted[i]);
            moves.Add(at);
            var original = order[at];
            order.RemoveAt(at);
            order.Add(original);
        }
        return new(moves, order, order.Select(o => window[o]).ToList());
    }

    /// <summary>The result of <see cref="Arrange"/>; <see cref="Slots"/> is the window after the moves.</summary>
    internal sealed record Plan(IReadOnlyList<int> Moves, List<int> Order, List<string?> Slots)
    {
        /// <summary>Where the tab that was at <paramref name="before"/> ends up.</summary>
        public int? After(int? before) => before is { } b && Order.IndexOf(b) is var at and >= 0 ? at : null;
    }

    /// <summary>
    /// <c>wt.exe -w &lt;window&gt; focus-tab -t i ; move-pane -t 9999 ; ... ; focus-tab -t &lt;focus&gt;</c>; null when nothing
    /// has to move. <paramref name="focus"/> is the tab to show afterwards (each move leaves the moved tab focused).
    /// </summary>
    internal static TerminalFocus.Command? Build(string window, Plan plan, int? focus)
    {
        if (plan.Moves.Count == 0 || window.Length == 0) return null;
        var args = new List<string> { "-w", window };
        foreach (var at in plan.Moves)
        {
            if (args.Count > 2) args.Add(";");
            args.AddRange(["focus-tab", "-t", Num(at), ";", "move-pane", "-t", Num(BeyondLast)]);
        }
        if (focus is >= 0) args.AddRange([";", "focus-tab", "-t", Num(focus.Value)]);
        return new TerminalFocus.Command("wt.exe", args);
    }

    private static string Num(int n) => n.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// The window as BoosterAI believes it to be: the saved slots (own ids and foreign marks) without own tabs that are no
    /// longer live, then live ids it has no position for, by start order (tab1, tab2, ...), as they were appended.
    /// </summary>
    internal static List<string?> Believed(IReadOnlyList<string?> saved, IEnumerable<string> live)
    {
        var alive = live.ToList();
        var slots = saved.Where(s => s is null || alive.Contains(s)).ToList();
        foreach (var id in alive.Where(id => !slots.Contains(id)).Distinct().OrderBy(TrackNumber).ThenBy(id => id, StringComparer.Ordinal))
            slots.Add(id);
        return slots;
    }

    internal static int TrackNumber(string id) =>
        int.TryParse(id.Length > 3 ? id[3..] : "", NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : int.MaxValue;

    /// <summary>
    /// The window's slots from its tab <paramref name="titles"/> (left to right): each title takes the first own tab with that
    /// title that is not taken yet, in <paramref name="own"/>'s (believed) order; other titles are foreign (null).
    /// </summary>
    internal static List<string?> Match(IReadOnlyList<string> titles, IReadOnlyList<(string Id, string Title)> own)
    {
        var free = own.ToList();
        var slots = new List<string?>();
        foreach (var title in titles)
        {
            var at = free.FindIndex(o => o.Title == title);
            if (at < 0) { slots.Add(null); continue; }
            slots.Add(free[at].Id);
            free.RemoveAt(at);
        }
        return slots;
    }

    /// <summary>The title Windows Terminal shows for a tab BoosterAI opened (see Launcher.WindowsTerminalArgs).</summary>
    internal static string Title(string name) => name.Replace('"', '”');

    /// <summary>The saved order, one slot per line (<see cref="ForeignMark"/> for a tab BoosterAI does not own).</summary>
    internal static string Format(IEnumerable<string?> slots) => string.Concat(slots.Select(s => (s ?? ForeignMark) + "\n"));

    internal static List<string?> Parse(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(l => l == ForeignMark ? null : l).ToList();

    /// <summary>A window's tabs as UI Automation sees them: titles left to right and the selected one (or null).</summary>
    internal sealed record Snapshot(IReadOnlyList<string> Titles, int? Selected);

    /// <summary>
    /// PowerShell that prints one line per tab item of window <paramref name="hwnd"/>: "1" or "0" (selected), a space and
    /// the title as base64 UTF-8 (console encodings cannot mangle it). It only reads; nothing is shown or focused.
    /// </summary>
    internal static string SnapshotScript(long hwnd) => """
        $ErrorActionPreference = 'Stop'
        Add-Type -AssemblyName UIAutomationClient
        Add-Type -AssemblyName UIAutomationTypes
        $w = [Windows.Automation.AutomationElement]::FromHandle([IntPtr]
        """ + hwnd.ToString(CultureInfo.InvariantCulture) + """
        )
        $c = New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::ControlTypeProperty, [Windows.Automation.ControlType]::TabItem)
        foreach ($t in $w.FindAll([Windows.Automation.TreeScope]::Descendants, $c)) {
          $s = '0'
          $p = $null
          if ($t.TryGetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern, [ref]$p) -and $p.Current.IsSelected) { $s = '1' }
          [Console]::Out.WriteLine($s + ' ' + [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($t.Current.Name)))
        }
        """;

    /// <summary>Parses <see cref="SnapshotScript"/>'s output; null when it is not that output.</summary>
    internal static Snapshot? ParseSnapshot(string output)
    {
        var titles = new List<string>();
        int? selected = null;
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (line.Length < 2 || line[1] != ' ' || line[0] is not ('0' or '1')) return null;
            try { titles.Add(Encoding.UTF8.GetString(Convert.FromBase64String(line[2..]))); }
            catch (FormatException) { return null; }
            if (line[0] == '1') selected ??= titles.Count - 1;
        }
        return new(titles, selected);
    }

    /// <summary>Takes the snapshot of the session window; tests replace it so no terminal or PowerShell is touched.</summary>
    internal static Func<string, Snapshot?> Read { get; set; } = ReadReal;

    private static Snapshot? ReadReal(string window)
    {
        if (!OperatingSystem.IsWindows()) return null;
        var hwnd = TerminalPlacement.FindOwnedWindow(window);
        if (hwnd == 0) return null;
        var psi = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-WindowStyle", "Hidden", "-EncodedCommand",
                     Convert.ToBase64String(Encoding.Unicode.GetBytes(SnapshotScript(hwnd))) })
            psi.ArgumentList.Add(arg);
        Process? process = null;
        try
        {
            process = Process.Start(psi);
            if (process is null) return null;
            var output = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(10000)) return null;
            return process.ExitCode == 0 ? ParseSnapshot(output.Result) : null;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException) { return null; }
        finally
        {
            try { if (process is { HasExited: false }) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            process?.Dispose();
        }
    }

    /// <summary>
    /// False when Windows Terminal opens new tabs next to the focused one (<c>"newTabPosition": "afterCurrentTab"</c>):
    /// then neither <c>new-tab</c> nor a pane moved to a new tab lands at the end, and BoosterAI leaves the order alone.
    /// </summary>
    internal static bool NewTabsAppend(IEnumerable<string> settingsJson)
    {
        foreach (var json in settingsJson)
        {
            try
            {
                using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("newTabPosition", out var p)
                    && p.ValueKind == JsonValueKind.String && p.GetString() == "afterCurrentTab")
                    return false;
            }
            catch (JsonException) { }
        }
        return true;
    }

    /// <summary>Windows Terminal's settings files (store, preview, canary and unpackaged), those that exist.</summary>
    internal static IEnumerable<string> SettingsFiles()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        foreach (var package in new[] { "Microsoft.WindowsTerminal_8wekyb3d8bbwe", "Microsoft.WindowsTerminalPreview_8wekyb3d8bbwe", "Microsoft.WindowsTerminalCanary_8wekyb3d8bbwe" })
            yield return Path.Combine(local, "Packages", package, "LocalState", "settings.json");
        yield return Path.Combine(local, "Microsoft", "Windows Terminal", "settings.json");
    }

    /// <summary>Whether the user's Windows Terminal appends new tabs; tests replace it.</summary>
    internal static Func<bool> Appends { get; set; } = () => NewTabsAppend(SettingsFiles().Select(f =>
    {
        try { return File.Exists(f) ? File.ReadAllText(f) : ""; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return ""; }
    }));

    /// <summary>What one arrange did: the window afterwards (null: unknown, keep the saved order) and whether wt.exe ran.</summary>
    internal sealed record Outcome(IReadOnlyList<string?>? Slots, bool Ran);

    /// <summary>
    /// Arranges one session window: reads it (<see cref="Read"/>) or, without a snapshot, takes <paramref name="believed"/>,
    /// plans the moves for <paramref name="desired"/> and runs them. <paramref name="mustSee"/> are tabs just launched:
    /// the snapshot is retried until they show, and nothing moves if they never do. Afterwards the tab
    /// <paramref name="focusId"/> is shown, otherwise the tab that was selected before. Runs off the UI thread.
    /// </summary>
    internal static Outcome ArrangeWindow(string window, IReadOnlyList<string?> believed, IReadOnlyList<string> desired,
        IReadOnlyDictionary<string, string> titles, IReadOnlyCollection<string> mustSee, string? focusId, Action<TimeSpan> sleep)
    {
        if (!Appends()) return new(null, false);
        IReadOnlyList<string?> slots = believed;
        int? selected = null;
        var own = believed.OfType<string>().Concat(desired).Distinct().Where(titles.ContainsKey).Select(id => (id, titles[id])).ToList();
        for (var attempt = 0; ; attempt++)
        {
            if (Read(window) is not { } snapshot) break;
            var seen = Match(snapshot.Titles, own);
            if (mustSee.All(seen.Contains)) { slots = seen; selected = snapshot.Selected; break; }
            if (attempt == 3) return new(null, false);
            sleep(TimeSpan.FromMilliseconds(1500));
        }
        var plan = Arrange(slots, desired);
        var focus = focusId is not null && plan.Slots.IndexOf(focusId) is var at and >= 0 ? at : plan.After(selected);
        var command = Build(window, plan, focus);
        if (command is not null) Run(command);
        return new(plan.Slots, command is not null);
    }

    /// <summary>Runs the command and waits briefly for wt.exe to hand it over; tests replace it so no terminal is touched.</summary>
    internal static Action<TerminalFocus.Command> Run { get; set; } = RunReal;

    private static void RunReal(TerminalFocus.Command command)
    {
        var psi = new ProcessStartInfo(command.Exe) { UseShellExecute = false };
        foreach (var arg in command.Args) psi.ArgumentList.Add(arg);
        try
        {
            using var process = Process.Start(psi);
            process?.WaitForExit(5000);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { /* no wt.exe */ }
    }
}
