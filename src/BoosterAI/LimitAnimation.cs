using System.Text.Json;
using System.Text.Json.Serialization;

namespace BoosterAI;

/// <summary>
/// One animated change of a plan-limit level (#234): the shown number counts from <see cref="From"/> to <see cref="To"/> over
/// <see cref="CountSeconds"/>, the "(-5%)" beside it stays fully visible until <see cref="HoldSeconds"/> and fades out by
/// <see cref="TotalSeconds"/>. Pure: every value is a function of the time since <see cref="Start"/>.
/// </summary>
public sealed record LimitTransition(double From, double To, DateTimeOffset Start)
{
    public const double CountSeconds = 2.0;
    public const double HoldSeconds = 2.5;
    public const double TotalSeconds = 3.0;

    /// <summary>The change to show, or null when there is nothing to animate: no previous or no new value, or an unchanged one.</summary>
    public static LimitTransition? Begin(double? previous, double? next, DateTimeOffset now) =>
        previous is { } p && next is { } n && Math.Abs(p - n) > 1e-9 ? new(p, n, now) : null;

    private double Elapsed(DateTimeOffset now) => Math.Max(0, (now - Start).TotalSeconds);

    public bool IsActive(DateTimeOffset now) => Elapsed(now) < TotalSeconds;

    /// <summary>The level at <paramref name="now"/>: eased (fast first, slow last) from the old to the new value, then the new one.</summary>
    public double ValueAt(DateTimeOffset now)
    {
        var t = Math.Min(1, Elapsed(now) / CountSeconds);
        var eased = 1 - (1 - t) * (1 - t);
        return From + (To - From) * eased;
    }

    /// <summary>The signed change, new minus old.</summary>
    public double Diff => To - From;

    /// <summary>0..1 opacity of the diff text: 1 while it is held, then a linear fade, 0 once the transition is over.</summary>
    public double DiffOpacityAt(DateTimeOffset now)
    {
        var e = Elapsed(now);
        return e <= HoldSeconds ? 1 : e >= TotalSeconds ? 0 : (TotalSeconds - e) / (TotalSeconds - HoldSeconds);
    }

    /// <summary>"(-5%)" / "(+5%)" , without a space before the percent sign so it fits beside two row icons, one decimal only when the change has one; empty for no change.</summary>
    public static string DiffText(double diff)
    {
        var shown = Math.Round(diff, 1);
        if (Math.Abs(shown) < 1e-9) return "";
        return Loc.Format("account.limit.diff", (shown < 0 ? "-" : "+") + LimitPercent.Text(Math.Abs(shown)));
    }
}

/// <summary>
/// Decides when a plan-limit level change is animated (#234) and keeps the last known levels, also on disk so the first reading after
/// a restart is compared with the level shown when the app was closed. A change animates only while <see cref="Arm"/>ed (just after the
/// window returned or the app started) and only with a known level before and after; anything else just stores the new level.
/// Thread-safe: readings arrive off the UI thread.
/// </summary>
public sealed class LimitAnimations
{
    private readonly object _lock = new();
    private readonly Dictionary<string, double> _last;
    private readonly Dictionary<string, LimitTransition> _active = [];
    private readonly string? _file;
    private DateTimeOffset _armedUntil = DateTimeOffset.MinValue;

    /// <summary>How long after <see cref="Arm"/> a changed reading still counts as the one that came back with the window.</summary>
    public static readonly TimeSpan ArmedFor = TimeSpan.FromSeconds(40);

    /// <param name="file">Where the last levels are kept between runs; null keeps them in memory only.</param>
    public LimitAnimations(string? file = null)
    {
        _file = file;
        _last = Load(file);
    }

    public void Arm(DateTimeOffset now)
    {
        lock (_lock) _armedUntil = now + ArmedFor;
    }

    /// <summary>Records a reading of <paramref name="key"/> (null while unknown: nothing changes). Returns true when an animation started.</summary>
    public bool Observe(string key, double? value, DateTimeOffset now)
    {
        if (value is not { } v) return false;
        bool started = false, save = false;
        lock (_lock)
        {
            var previous = _last.TryGetValue(key, out var p) ? p : (double?)null;
            if (previous is { } pv && Math.Abs(pv - v) <= 1e-9) return false;
            // A change while an animation runs continues from the level currently shown.
            var from = _active.TryGetValue(key, out var running) && running.IsActive(now) ? running.ValueAt(now) : previous;
            _last[key] = v;
            save = true;
            if (now <= _armedUntil && LimitTransition.Begin(from, v, now) is { } t)
            {
                _active[key] = t;
                started = true;
            }
            else _active.Remove(key);
        }
        if (save) Save();
        return started;
    }

    /// <summary>The level to show: the counting value while <paramref name="key"/> animates, else <paramref name="level"/>.</summary>
    public double Value(string key, double level, DateTimeOffset now)
    {
        lock (_lock)
            return _active.TryGetValue(key, out var t) && t.IsActive(now) && Math.Abs(t.To - level) < 1e-9 ? t.ValueAt(now) : level;
    }

    public bool IsCounting(string key, DateTimeOffset now)
    {
        lock (_lock) return _active.TryGetValue(key, out var t) && t.IsActive(now) && Math.Abs(t.ValueAt(now) - t.To) > 1e-9;
    }

    /// <summary>The diff text and its opacity for <paramref name="key"/>; empty and 0 when it is not animating or <paramref name="level"/> is no longer its target.</summary>
    public (string Text, double Opacity) Diff(string key, double level, DateTimeOffset now)
    {
        lock (_lock)
        {
            if (!_active.TryGetValue(key, out var t) || !t.IsActive(now) || Math.Abs(t.To - level) > 1e-9) return ("", 0);
            var text = LimitTransition.DiffText(t.Diff);
            return text.Length == 0 ? ("", 0) : (text, t.DiffOpacityAt(now));
        }
    }

    /// <summary>Whether any animation still runs, so the caller keeps its frame timer going.</summary>
    public bool AnyActive(DateTimeOffset now)
    {
        lock (_lock)
        {
            foreach (var k in _active.Where(a => !a.Value.IsActive(now)).Select(a => a.Key).ToList()) _active.Remove(k);
            return _active.Count > 0;
        }
    }

    private void Save()
    {
        if (_file is null) return;
        try
        {
            string json;
            lock (_lock) json = JsonSerializer.Serialize(_last, LimitLevelsJson.Default.DictionaryStringDouble);
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            File.WriteAllText(_file, json);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private static Dictionary<string, double> Load(string? file)
    {
        try
        {
            if (file is not null && File.Exists(file))
                return JsonSerializer.Deserialize(File.ReadAllText(file), LimitLevelsJson.Default.DictionaryStringDouble) ?? [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }
        return [];
    }
}

[JsonSerializable(typeof(Dictionary<string, double>))]
internal partial class LimitLevelsJson : JsonSerializerContext;
