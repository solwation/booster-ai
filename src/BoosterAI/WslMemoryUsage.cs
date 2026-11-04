using System.Diagnostics;

namespace BoosterAI;

public enum WslMemoryState { Available, NotRunning, Unavailable, LegacyProcess, Stale }

public readonly record struct WslMemoryReading(WslMemoryState State, long Bytes = 0);
public readonly record struct WslMemorySample(DateTimeOffset Time, long Bytes);

/// <summary>Observed resident memory, never the configured WSL cap. Missing samples are not zeroes.</summary>
public sealed record WslMemorySnapshot(WslMemoryState State, long? CurrentBytes, double? AverageBytes,
    long? MinimumBytes, long? MaximumBytes, TimeSpan CollectedTime, int SampleCount);

/// <summary>Samples the explicitly named WSL process without starting WSL or a helper process.</summary>
public static class WslMemorySource
{
    public static WslMemoryReading Read()
    {
        if (!OperatingSystem.IsWindows()) return new(WslMemoryState.Unavailable);
        try
        {
            var processes = Process.GetProcessesByName("vmmemWSL");
            try
            {
                if (processes.Length > 0)
                {
                    long bytes = 0;
                    foreach (var process in processes)
                        bytes = checked(bytes + process.WorkingSet64);
                    return bytes > 0 ? new(WslMemoryState.Available, bytes) : new(WslMemoryState.Unavailable);
                }
            }
            finally { foreach (var process in processes) process.Dispose(); }

            // Older vmmem can include unrelated Hyper-V workloads. Never guess its WSL share.
            var legacy = Process.GetProcessesByName("vmmem");
            try { return new(legacy.Length > 0 ? WslMemoryState.LegacyProcess : WslMemoryState.NotRunning); }
            finally { foreach (var process in legacy) process.Dispose(); }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException
            or NotSupportedException or OverflowException or System.Security.SecurityException)
        {
            return new(WslMemoryState.Unavailable);
        }
    }
}

/// <summary>A bounded, rolling sample history. Gaps and failed reads do not count as observed time.</summary>
public sealed class WslMemoryHistory
{
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(90);
    private readonly List<WslMemorySample> _samples = [];
    private DateTimeOffset? _lastRead;
    private WslMemoryReading _reading = new(WslMemoryState.Unavailable);

    public void Add(DateTimeOffset now, WslMemoryReading reading)
    {
        if (_lastRead.HasValue && now < _lastRead.Value) return;
        _lastRead = now;
        _reading = reading;
        if (reading.State == WslMemoryState.Available && reading.Bytes > 0)
        {
            // Only one sample per instant; repeated UI refreshes cannot bias the average.
            if (_samples.Count > 0 && _samples[^1].Time == now) _samples.RemoveAt(_samples.Count - 1);
            _samples.Add(new(now, reading.Bytes));
        }
        else if (reading.State == WslMemoryState.Available) _reading = new(WslMemoryState.Unavailable);
        Prune(now);
    }

    public WslMemorySnapshot Snapshot(DateTimeOffset now)
    {
        Prune(now);
        var state = _lastRead.HasValue && now - _lastRead.Value > StaleAfter
            ? WslMemoryState.Stale : _reading.State;
        var current = state == WslMemoryState.Available ? (long?)_reading.Bytes : null;
        if (_samples.Count == 0) return new(state, current, null, null, null, TimeSpan.Zero, 0);
        var observed = TimeSpan.Zero;
        for (var i = 1; i < _samples.Count; i++)
        {
            var gap = _samples[i].Time - _samples[i - 1].Time;
            if (gap <= Interval * 1.5) observed += gap;
        }
        return new(state, current, _samples.Average(s => (double)s.Bytes), _samples.Min(s => s.Bytes),
            _samples.Max(s => s.Bytes), observed, _samples.Count);
    }

    private void Prune(DateTimeOffset now) => _samples.RemoveAll(s => s.Time < now - Window);
}

/// <summary>Owns a single periodic reader for the application lifetime, including tray-hidden time.</summary>
public sealed class WslMemoryMonitor : IDisposable
{
    private readonly Func<WslMemoryReading> _read;
    private readonly TimeProvider _clock;
    private readonly WslMemoryHistory _history = new();
    private readonly object _gate = new();
    private Timer? _timer;
    private bool _reading;
    private bool _disposed;
    public event Action<WslMemorySnapshot>? Updated;

    public WslMemoryMonitor(Func<WslMemoryReading>? read = null, TimeProvider? clock = null)
    {
        _read = read ?? WslMemorySource.Read;
        _clock = clock ?? TimeProvider.System;
    }

    public void Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _timer ??= new Timer(_ => Sample(), null, TimeSpan.Zero, WslMemoryHistory.Interval);
        }
    }

    internal void Sample()
    {
        lock (_gate)
        {
            if (_disposed || _reading) return;
            _reading = true;
        }
        WslMemoryReading reading;
        try { reading = _read(); }
        catch (Exception) { reading = new(WslMemoryState.Unavailable); }
        lock (_gate)
        {
            _reading = false;
            if (_disposed) return;
            var now = _clock.GetUtcNow();
            _history.Add(now, reading);
            Updated?.Invoke(_history.Snapshot(now));
        }
    }

    public WslMemorySnapshot Snapshot()
    {
        lock (_gate) return _history.Snapshot(_clock.GetUtcNow());
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _timer?.Dispose();
            _timer = null;
            Updated = null;
        }
    }
}
