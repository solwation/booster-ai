using System.Globalization;
using System.Text;
using System.Text.Json;

namespace BoosterAI;

/// <summary>What an agent says it is doing, from a "status" event of the tab status channel (#247).</summary>
public enum AgentState { Idle, Working, Blocked, Done }

/// <summary>The latest "status" event since the CLI started; <paramref name="At"/> is its epoch-ms.</summary>
public sealed record AgentStatus(AgentState State, string Text, long At);

/// <summary>
/// A "question" event: <paramref name="Free"/> allows a free-text answer besides <paramref name="Options"/>;
/// <paramref name="ExpiresAt"/> (optional "expires_at", epoch-ms) is when the asker stops waiting for the answer (#249).
/// </summary>
public sealed record TabQuestion(string Id, string Text, IReadOnlyList<string> Options, bool Free, long At, long? ExpiresAt = null);

/// <summary>An answer file in tabN.answers/: the question (by id and its event's epoch-ms) it answers, and the answer.</summary>
public sealed record TabAnswer(string Id, long QuestionAt, string Text, long At);

/// <summary>A question together with its answer (null while it is open).</summary>
public sealed record AnsweredQuestion(TabQuestion Question, TabAnswer Answer);

public enum AnswerResult { Answered, AlreadyAnswered, UnknownQuestion, Invalid, Failed }

/// <summary>What tabN.events says since the CLI's latest cli-start: the latest status, open and answered questions.</summary>
public sealed record TabChannelState(AgentStatus? Status, IReadOnlyList<TabQuestion> Open, IReadOnlyList<AnsweredQuestion> Answered)
{
    public static readonly TabChannelState Empty = new(null, [], []);
}

/// <summary>
/// The per-tab status channel (#246, #247): plain files in the session directory, no network port. The agent's side (an
/// MCP server or shell command, #248) appends JSON lines to tabN.events and waits for tabN.answers/&lt;id&gt;; BoosterAI
/// reads the events and writes the answers. The format is documented in docs/DETAILS.md ("Tab status channel").
/// </summary>
public static class TabChannel
{
    /// <summary>The format version: every event and answer carries it as "v"; lines without it or with a higher version are ignored.</summary>
    public const int Version = 1;

    /// <summary>Only the last part of a large events file is read, so a runaway writer cannot stall the monitor.</summary>
    public const int MaxReadBytes = 1 << 20;
    public const int MaxTextLength = 2000;
    public const int MaxOptions = 20;
    public const int MaxOptionLength = 200;
    public const int MaxAnswerLength = 4000;

    /// <summary>
    /// Environment variables for the agent's side (#248 exports them when <see cref="TabTrack.StatusChannel"/> is on): the
    /// events file, the answers directory and the format version, as the tab's shell sees the paths.
    /// </summary>
    public const string EventsVariable = "BOOSTERAI_TAB_EVENTS";
    public const string AnswersVariable = "BOOSTERAI_TAB_ANSWERS";
    public const string VersionVariable = "BOOSTERAI_TAB_PROTOCOL";

    public static string EventsFile(string id) => id + ".events";
    public static string AnswersDirectory(string id) => id + ".answers";

    /// <summary>The channel's variables for a tab; empty when the channel is off, so the tab starts as without it.</summary>
    public static IReadOnlyDictionary<string, string> Environment(TabTrack track) => track.StatusChannel
        ? new Dictionary<string, string>
        {
            [EventsVariable] = $"{track.Dir}/{EventsFile(track.Id)}",
            [AnswersVariable] = $"{track.Dir}/{AnswersDirectory(track.Id)}",
            [VersionVariable] = Version.ToString(CultureInfo.InvariantCulture),
        }
        : new Dictionary<string, string>();

    /// <summary>A question id is a file name in tabN.answers/: 1–64 letters, digits, '-', '_' or '.', not starting with '.'.</summary>
    public static bool IsValidId(string? id) =>
        id is { Length: > 0 and <= 64 } && id[0] != '.' && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.');

    /// <summary>
    /// Parses tabN.events. Lines are JSON objects ending in '\n'; an unterminated last line (still being written), broken JSON,
    /// unknown kinds, newer versions and invalid fields are skipped. Events before <paramref name="sinceMs"/> (the latest
    /// cli-start, i.e. from a CLI that an in-place restart replaced) do not count. A question asked again under the same id
    /// replaces the earlier one. <paramref name="answer"/> looks up the answer file of a question id (null: none).
    /// </summary>
    public static TabChannelState Parse(string text, long? sinceMs, Func<string, TabAnswer?>? answer = null)
    {
        AgentStatus? status = null;
        var questions = new Dictionary<string, TabQuestion>(StringComparer.Ordinal);
        var end = text.LastIndexOf('\n');
        if (end < 0) return TabChannelState.Empty;
        foreach (var raw in text[..end].Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] != '{') continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var e = doc.RootElement;
                if (e.ValueKind != JsonValueKind.Object) continue;
                if (Long(e, "v") is not { } v || v < 1 || v > Version) continue;
                if (Long(e, "at") is not { } at || (sinceMs is { } since && at < since)) continue;
                switch (Str(e, "kind"))
                {
                    case "status" when ParseState(Str(e, "state")) is { } state:
                        status = new AgentStatus(state, Cap(Str(e, "text") ?? "", MaxTextLength), at);
                        break;
                    case "question" when Str(e, "id") is { } id && IsValidId(id) && Str(e, "text") is { Length: > 0 } q:
                        var options = new List<string>();
                        if (e.TryGetProperty("options", out var o) && o.ValueKind == JsonValueKind.Array)
                            foreach (var item in o.EnumerateArray())
                                if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } s && options.Count < MaxOptions)
                                    options.Add(Cap(s, MaxOptionLength));
                        var free = e.TryGetProperty("free", out var f) ? f.ValueKind == JsonValueKind.True : options.Count == 0;
                        // A question nobody can answer is not a question.
                        if (options.Count == 0 && !free) continue;
                        questions.Remove(id);
                        questions[id] = new TabQuestion(id, Cap(q, MaxTextLength), options, free, at, Long(e, "expires_at") is { } exp && exp > at ? exp : null);
                        break;
                }
            }
            catch (JsonException) { }
        }
        var open = new List<TabQuestion>();
        var answered = new List<AnsweredQuestion>();
        foreach (var q in questions.Values)
        {
            if (answer?.Invoke(q.Id) is { } a && a.QuestionAt == q.At) answered.Add(new AnsweredQuestion(q, a));
            else open.Add(q);
        }
        return new TabChannelState(status, open, answered);
    }

    /// <summary>Reads at most the last <see cref="MaxReadBytes"/> of a file being appended to, from the first whole line.</summary>
    public static string ReadTail(string path, int maxBytes = MaxReadBytes)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var length = stream.Length;
        var skipPartial = length > maxBytes;
        if (skipPartial) stream.Seek(length - maxBytes, SeekOrigin.Begin);
        var buffer = new byte[Math.Min(length, maxBytes)];
        var read = 0;
        while (read < buffer.Length && stream.Read(buffer, read, buffer.Length - read) is var n and > 0) read += n;
        var start = 0;
        if (skipPartial)
        {
            var newline = Array.IndexOf(buffer, (byte)'\n', 0, read);
            start = newline < 0 ? read : newline + 1;
        }
        return Encoding.UTF8.GetString(buffer, start, read - start);
    }

    /// <summary>Parses an answer file; null when it is not a valid answer.</summary>
    public static TabAnswer? ParseAnswer(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var e = doc.RootElement;
            if (e.ValueKind != JsonValueKind.Object || Long(e, "v") is not { } v || v < 1 || v > Version) return null;
            return Str(e, "id") is { } id && Long(e, "question_at") is { } qAt && Str(e, "text") is { } text && Long(e, "at") is { } at
                ? new TabAnswer(id, qAt, text, at) : null;
        }
        catch (JsonException) { return null; }
    }

    public static string AnswerJson(TabAnswer answer)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            w.WriteNumber("v", Version);
            w.WriteString("id", answer.Id);
            w.WriteNumber("question_at", answer.QuestionAt);
            w.WriteString("text", answer.Text);
            w.WriteNumber("at", answer.At);
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
    }

    /// <summary>A lock left by a writer that died is taken over after this long.</summary>
    public static readonly TimeSpan StaleLock = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Writes the answer to <paramref name="question"/> into <paramref name="answersDir"/>, once. Writers first create
    /// .&lt;id&gt;.lock exclusively (CreateNew, atomic on Linux and Windows; .NET's File.Move without overwrite is not), so
    /// only one checks and writes at a time; a second answer to the same question is rejected. The answer goes to a temporary
    /// file that is renamed to the question's id, so the agent never reads half an answer. An answer left from an earlier
    /// question with the same id (another event time, e.g. before an in-place restart) is replaced.
    /// </summary>
    public static AnswerResult WriteAnswer(string answersDir, TabQuestion question, string text, long nowMs)
    {
        text = text.Trim();
        if (text.Length == 0 || text.Length > MaxAnswerLength || !IsValidId(question.Id)) return AnswerResult.Invalid;
        if (!question.Free && !question.Options.Contains(text, StringComparer.Ordinal)) return AnswerResult.Invalid;
        var path = Path.Combine(answersDir, question.Id);
        var lockPath = Path.Combine(answersDir, "." + question.Id + ".lock");
        var temporary = Path.Combine(answersDir, "." + question.Id + "." + Guid.NewGuid().ToString("N") + ".tmp");
        FileStream? held = null;
        try
        {
            System.IO.Directory.CreateDirectory(answersDir);
            held = TryLock(lockPath);
            if (held is null)
            {
                // Another answer is being written, or a writer died holding the lock.
                var info = new FileInfo(lockPath);
                if (!info.Exists || DateTime.UtcNow - info.LastWriteTimeUtc < StaleLock) return AnswerResult.AlreadyAnswered;
                File.Delete(lockPath);
                if ((held = TryLock(lockPath)) is null) return AnswerResult.AlreadyAnswered;
            }
            if (ReadAnswer(path) is { } old && old.QuestionAt == question.At) return AnswerResult.AlreadyAnswered;
            File.WriteAllText(temporary, AnswerJson(new TabAnswer(question.Id, question.At, text, nowMs)));
            File.Move(temporary, path, overwrite: true);
            return AnswerResult.Answered;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return AnswerResult.Failed; }
        finally
        {
            try { File.Delete(temporary); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            if (held is not null)
            {
                held.Dispose();
                try { File.Delete(lockPath); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
        }
    }

    private static FileStream? TryLock(string path)
    {
        try { return new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None); }
        // Held by another writer (or just released by one, which then wrote the answer).
        catch (IOException) { return null; }
    }

    /// <summary>The answer file at <paramref name="path"/>; null when missing, unreadable or invalid.</summary>
    public static TabAnswer? ReadAnswer(string path)
    {
        try { return File.Exists(path) ? ParseAnswer(File.ReadAllText(path)) : null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    private static AgentState? ParseState(string? s) => s switch
    {
        "idle" => AgentState.Idle,
        "working" => AgentState.Working,
        "blocked" => AgentState.Blocked,
        "done" => AgentState.Done,
        _ => null,
    };

    private static string Cap(string s, int max) => s.Length <= max ? s : s[..max];

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static long? Long(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt64(out var v) ? v : null;
}
