namespace BoosterAI;

/// <summary>
/// Auto-clear on inactivity (#69, AppConfig.AutoClearIdle): once an interactive CLI has been idle for the configured
/// time, BoosterAI types its clear command into the tab (tab-lib.sh's bs_pty, TabSession.RequestInput), once per idle
/// period. Only claude is supported: its hooks report when a turn ends, and /clear starts a new session with empty
/// context without starting a turn. codex and agy are skipped: without hooks BoosterAI only knows that their output
/// has been quiet, which a long tool call also is, so typing into them could land in the middle of a turn.
/// </summary>
public static class AutoClear
{
    public const int DefaultMinutes = 5;
    public const int MinMinutes = 1;
    public const int MaxMinutes = 240;

    /// <summary>What is typed: the command, then Enter (bs_pty sends the CR on its own a moment later).</summary>
    public const string Command = "/clear\r";

    public static bool Supports(string llm) => llm == "claude";

    /// <summary>The configured minutes within the allowed range (a hand-edited config may hold anything).</summary>
    public static int Minutes(int configured) => Math.Clamp(configured, MinMinutes, MaxMinutes);

    /// <summary>An idle period to clear: the "idle" it began with and the "busy" before it (epoch-ms; input after it skips the clear).</summary>
    public readonly record struct Request(long IdleAt, long Since);

    /// <summary>
    /// Whether to type the clear command into a tab now, and for which idle period; null: not now. Never for a tab that
    /// works on a turn, has an exited or closed CLI, runs once (bs_stream, claude -p), waits for a restart at its next idle,
    /// is being closed, or whose CLI does not run under bs_pty (started with the setting off, an older BoosterAI, or no
    /// python3). <paramref name="requestedFor"/> is the idle period BoosterAI already asked for; together with the tab's
    /// own input-sent/input-skipped it makes this once per idle period, until a new turn (busy) starts the next one.
    /// </summary>
    public static Request? Due(bool enabled, int minutes, TabConfig? launchedAs, TabState state, Activity activity, bool pendingRestart,
        bool closing, TabLog? log, long? requestedFor, DateTime nowUtc)
    {
        if (!enabled || closing || pendingRestart || launchedAs is null || !Supports(launchedAs.Llm)) return null;
        if (launchedAs is { NonInteractive: true, Orkester: true }) return null;
        if (state != TabState.Running || activity != Activity.Idle) return null;
        if (log is not { InputReady: true, CliExit: null, Exited: false, Activity: Activity.Idle, IdleAt: { } idleAt }) return null;
        if (requestedFor == idleAt || log.InputAt >= idleAt) return null;
        var idleFor = nowUtc - DateTimeOffset.FromUnixTimeMilliseconds(idleAt).UtcDateTime;
        if (idleFor < TimeSpan.FromMinutes(Minutes(minutes))) return null;
        return new Request(idleAt, log.BusyAt is { } busy && busy <= idleAt ? busy : idleAt);
    }

    /// <summary>When the clear command was typed in the current idle period (epoch-ms), for the tab's status; null: not cleared.</summary>
    public static long? ClearedAt(TabLog? log) =>
        log is { Activity: Activity.Idle, InputSent: true, InputAt: { } at, IdleAt: { } idle } && at >= idle ? at : null;
}
