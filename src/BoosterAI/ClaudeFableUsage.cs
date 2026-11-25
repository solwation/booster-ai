using System.Diagnostics;
using System.Text.Json;

namespace BoosterAI;

/// <summary>Reads the Fable window from Claude Code's structured /usage output, at most once per account every five minutes.</summary>
internal sealed class ClaudeFableUsage(Func<string, string?, LimitWindow?> read)
{
    internal static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(17);
    private readonly Dictionary<(string Distro, string? Directory), (DateTimeOffset At, Task<LimitWindow?> Read, LimitWindow? Previous)> _cache = [];
    private readonly object _lock = new();

    internal ClaudeFableUsage() : this(Read) { }

    internal Task<LimitWindow?> ReadAsync(string distro, string? directory, DateTimeOffset now)
    {
        lock (_lock)
        {
            var key = (distro, directory);
            if (_cache.TryGetValue(key, out var cached) &&
                (!cached.Read.IsCompleted || now - cached.At < Interval))
                return cached.Read;

            var previous = cached.Read is { IsCompletedSuccessfully: true } ? cached.Read.Result : null;
            var task = Task.Run(() => read(distro, directory));
            _cache[key] = (now, task, previous);
            return task;
        }
    }

    internal LimitWindow? LastRead(string distro, string? directory)
    {
        lock (_lock)
        {
            if (!_cache.TryGetValue((distro, directory), out var cached)) return null;
            if (!cached.Read.IsCompleted) return cached.Previous;
            return cached.Read.IsCompletedSuccessfully ? cached.Read.Result : null;
        }
    }

    internal static string Script(string? directory)
    {
        if (directory is not null && !Accounts.IsValidDirectory(directory))
            throw new ArgumentException("Invalid Claude account directory.", nameof(directory));

        var home = directory is null ? "" : $"export CLAUDE_CONFIG_DIR={Accounts.ShellPath(directory)} && test -d \"$CLAUDE_CONFIG_DIR\" && ";
        return $"cd /tmp && {home}exec timeout -k 2 12 claude --safe-mode --no-session-persistence --tools '' -p --output-format stream-json --verbose /usage";
    }

    internal static ProcessStartInfo Command(string distro, string? directory) =>
        OperatingSystem.IsWindows()
            ? Prerequisites.Hidden("wsl.exe", ["-d", distro, "--exec", "bash", "-lic", Script(directory)])
            : Prerequisites.Hidden("bash", ["-lic", Script(directory)]);

    private static LimitWindow? Read(string distro, string? directory)
    {
        var (started, code, output) = Prerequisites.Run(Command(distro, directory), Timeout);
        return started && code == 0 ? Parse(output) : null;
    }

    internal static LimitWindow? Parse(string? output)
    {
        if (output is null || output.Length > 256 * 1024) return null;
        LimitWindow? fable = null;
        foreach (var line in output.Split('\n', StringSplitOptions.TrimEntries))
        {
            if (!line.StartsWith('{')) continue;
            try
            {
                using var json = JsonDocument.Parse(line);
                var root = json.RootElement;
                if (String(root, "type") == "result" && String(root, "subtype") == "success" &&
                    Property(root, "is_error").ValueKind == JsonValueKind.False &&
                    Property(root, "num_turns") is { ValueKind: JsonValueKind.Number } turns &&
                    turns.TryGetInt32(out var count) && count == 0)
                    return fable;

                var limits = Property(Property(Property(root, "usage_report"), "rate_limits"), "limits");
                if (String(root, "type") != "assistant" || limits.ValueKind != JsonValueKind.Array) continue;
                foreach (var row in limits.EnumerateArray())
                {
                    if (String(row, "kind") != "weekly_scoped") continue;
                    var model = String(Property(Property(row, "scope"), "model"), "display_name")?.Trim();
                    if (model is null || !IsFableModel(model)) continue;
                    var percent = Property(row, "percent");
                    if (percent.ValueKind != JsonValueKind.Number || !percent.TryGetDouble(out var used) ||
                        !double.IsFinite(used) || used is < 0 or > 100) continue;

                    var reset = Property(row, "resets_at");
                    DateTimeOffset? resetsAt = reset.ValueKind == JsonValueKind.String && reset.TryGetDateTimeOffset(out var parsed)
                        ? parsed : null;

                    // If several Fable windows are reported, display the tightest one with its own reset time.
                    if (fable is null || used > fable.UsedPercent) fable = new(used, resetsAt);
                }
            }
            catch (JsonException) { /* Login-shell banners may include text that starts with a brace. */ }
        }
        return null;
    }

    internal static bool IsFableModel(string model)
    {
        model = model.Trim();
        if (model.StartsWith("claude-", StringComparison.OrdinalIgnoreCase) ||
            model.StartsWith("claude ", StringComparison.OrdinalIgnoreCase))
            model = model[7..];
        const string family = "fable";
        return model.StartsWith(family, StringComparison.OrdinalIgnoreCase)
            && (model.Length == family.Length || model[family.Length] is '-' or ' ' or '[');
    }

    private static JsonElement Property(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value : default;

    private static string? String(JsonElement element, string name) =>
        Property(element, name) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;
}
