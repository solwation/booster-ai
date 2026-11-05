namespace BoosterAI;

/// <summary>Sending a new orchestra prompt requires explicit idle hooks, never output silence alone.</summary>
public static class OrchestraReload
{
    public static bool Supports(string llm) => llm == "claude";
    public static readonly TimeSpan QuietTime = TimeSpan.FromSeconds(2);

    public const string Command = "/orkester\r";

    public static AutoClear.Request? Due(TabConfig? launched, TabState state, Activity activity, bool pendingRestart,
        bool closing, TabLog? log, DateTime? lastOutput, long? requestedFor, DateTime now)
    {
        if (launched is not { Orkester: true, NonInteractive: false } || !Supports(launched.Llm)
            || closing || pendingRestart || state != TabState.Running || activity != Activity.Idle) return null;
        if (log is not { InputReady: true, InputProtocol: >= 2, InputReadyAt: { }, CliExit: null, Exited: false,
            Activity: Activity.Idle, IdleAt: { } idle }) return null;
        if (requestedFor == idle || log.OrchestraIdleAt == idle || lastOutput is not { } output) return null;
        var idleTime = DateTimeOffset.FromUnixTimeMilliseconds(idle).UtcDateTime;
        if (now - (output > idleTime ? output : idleTime) < QuietTime) return null;
        var since = log.BusyAt is { } busy && busy <= idle ? busy : idle;
        return log.InputTypedAt > since ? null : new AutoClear.Request(idle, since);
    }
}
