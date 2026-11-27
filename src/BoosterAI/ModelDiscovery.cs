using System.Text;
using System.Text.Json;

namespace BoosterAI;

/// <summary>
/// Finds the models the agent CLIs offer (#265): <c>agy models</c> and the Codex model cache, fetched in the background
/// at most once per 24 h and cached on disk. Any failure falls back to the last cache and then to the built-in list.
/// </summary>
public static class ModelDiscovery
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(25);

    /// <summary>Lines "id&lt;TAB&gt;Display name"; the "Fetching..." line and anything with an unsafe id is skipped.</summary>
    public static string[] ParseAgy(string? output)
    {
        var ids = new List<string>();
        foreach (var line in (output ?? "").Split('\n'))
        {
            var tab = line.IndexOf('\t');
            if (tab <= 0) continue;
            var id = line[..tab].Trim();
            if (Catalog.IsSafeModel(id) && !ids.Contains(id)) ids.Add(id);
        }
        return [.. ids];
    }

    /// <summary>The "slug" of every model in models_cache.json whose visibility is "list", in priority order.</summary>
    public static string[] ParseCodex(string? json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json ?? "");
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("models", out var models)
                || models.ValueKind != JsonValueKind.Array) return [];
            var found = new List<(int Priority, string Slug)>();
            foreach (var m in models.EnumerateArray())
            {
                if (m.ValueKind != JsonValueKind.Object) continue;
                if (!m.TryGetProperty("slug", out var slug) || slug.ValueKind != JsonValueKind.String) continue;
                if (!m.TryGetProperty("visibility", out var vis) || vis.ValueKind != JsonValueKind.String || vis.GetString() != "list") continue;
                var priority = m.TryGetProperty("priority", out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var n) ? n : int.MaxValue;
                var id = slug.GetString()!;
                if (Catalog.IsSafeModel(id)) found.Add((priority, id));
            }
            return [.. found.OrderBy(f => f.Priority).Select(f => f.Slug).Distinct()];
        }
        catch (JsonException) { return []; }
    }

    private static string CachePath => ConfigStore.FileNextToConfig("models-cache.json");

    /// <summary>Writes the cache; the time is passed in for tests.</summary>
    public static void SaveCache(string path, DateTime fetchedUtc, IReadOnlyDictionary<string, string[]> models)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteString("fetched", fetchedUtc.ToString("o"));
            w.WriteStartObject("models");
            foreach (var (llm, ids) in models)
            {
                w.WriteStartArray(llm);
                foreach (var id in ids) w.WriteStringValue(id);
                w.WriteEndArray();
            }
            w.WriteEndObject();
            w.WriteEndObject();
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, Encoding.UTF8.GetString(ms.ToArray()));
    }

    /// <summary>Reads the cache; null when missing or broken. Unsafe ids are dropped.</summary>
    public static (DateTime Fetched, Dictionary<string, string[]> Models)? LoadCache(string path)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var fetched = DateTime.Parse(doc.RootElement.GetProperty("fetched").GetString()!, null, System.Globalization.DateTimeStyles.RoundtripKind);
            var result = new Dictionary<string, string[]>();
            foreach (var p in doc.RootElement.GetProperty("models").EnumerateObject())
                result[p.Name] = [.. p.Value.EnumerateArray().Select(e => e.GetString() ?? "").Where(Catalog.IsSafeModel)];
            return (fetched, result);
        }
        catch (Exception e) when (e is IOException or JsonException or InvalidOperationException or FormatException or UnauthorizedAccessException or KeyNotFoundException)
        {
            return null;
        }
    }

    /// <summary>
    /// Loads the cache into the catalog at once and, if it is missing or older than 24 h, fetches new lists on a worker thread.
    /// <paramref name="changed"/> runs (on that thread) when the catalog got new ids.
    /// </summary>
    public static Task RefreshAsync(string distro, Action? changed = null)
    {
        // A fetch that is still running is not started twice.
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0) return Task.CompletedTask;
        try { return Start(distro, changed); }
        catch { Volatile.Write(ref _running, 0); throw; }
    }

    private static int _running;

    private static Task Start(string distro, Action? changed)
    {
        var path = CachePath;
        var cache = LoadCache(path);
        if (cache is { } c && c.Models.Count > 0) { Catalog.SetDiscovered(c.Models); changed?.Invoke(); }
        if (cache is { } fresh && DateTime.UtcNow - fresh.Fetched < MaxAge && DateTime.UtcNow >= fresh.Fetched)
        {
            Volatile.Write(ref _running, 0);
            return Task.CompletedTask;
        }
        return Task.Run(() =>
        {
            try
            {
                var found = new Dictionary<string, string[]>(cache?.Models ?? []);
                var (agyOk, agyCode, agyOut) = Prerequisites.Run(Command(distro, "timeout -k 1 20 agy models </dev/null"), Limit);
                if (agyOk && agyCode == 0 && ParseAgy(agyOut) is { Length: > 0 } agy) found["agy"] = agy;
                var (cxOk, cxCode, cxOut) = Prerequisites.Run(Command(distro, "cat ~/.codex/models_cache.json </dev/null"), Limit);
                if (cxOk && cxCode == 0 && ParseCodex(cxOut) is { Length: > 0 } codex) found["codex"] = codex;
                if (found.Count == 0) return;
                SaveCache(path, DateTime.UtcNow, found);
                Catalog.SetDiscovered(found);
                changed?.Invoke();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // Silent: the last cache or the built-in list stays.
            }
            finally { Volatile.Write(ref _running, 0); }
        });
    }

    private static System.Diagnostics.ProcessStartInfo Command(string distro, string script) =>
        OperatingSystem.IsWindows()
            ? Prerequisites.Hidden("wsl.exe", ["-d", distro, "--exec", "bash", "-lic", script])
            : Prerequisites.Hidden("bash", ["-lic", script]);
}
