using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BoosterAI;

/// <summary>A text in Swedish and English; <see cref="Pick"/> falls back to the other language, as the release page does.</summary>
public sealed record LocalText(string? Sv, string? En)
{
    public string Pick(string language) =>
        (language == "sv" ? Nonempty(Sv) ?? Nonempty(En) : Nonempty(En) ?? Nonempty(Sv)) ?? "";

    public bool IsEmpty => Nonempty(Sv) is null && Nonempty(En) is null;

    private static string? Nonempty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}

/// <summary>A notable feature of a release, as in docs/releases.json (#245, #250).</summary>
public sealed record ReleaseFeature(int? Issue, LocalText? Title, LocalText? Howto, LocalText? Image, string? Docs, string? Topic, LocalText? TopicTitle);

/// <summary>One release of docs/releases.json: summary, features and the commit subjects.</summary>
public sealed record ReleaseEntry(AppVersion Version, string? Date, string? Previous, LocalText? Summary,
    IReadOnlyList<ReleaseFeature> Features, IReadOnlyList<string> Changes)
{
    /// <summary>The features worth a card: those with a title or a how-to.</summary>
    public IEnumerable<ReleaseFeature> Shown => Features.Where(f => f.Title is { IsEmpty: false } || f.Howto is { IsEmpty: false });
}

/// <summary>An overview card: the features of one topic merged, or a feature of its own (#250).</summary>
public sealed record FeatureCard(string Id, IReadOnlyList<ReleaseFeature> Parts, LocalText? Title, LocalText? Howto, LocalText? Image,
    string? Docs, IReadOnlyList<int> Issues, IReadOnlyList<AppVersion> Versions)
{
    public bool Merged => Parts.Count > 1;
}

/// <summary>The from/to of what is new: the releases after From up to and including To; null means open.</summary>
public sealed record ReleaseRange(AppVersion? From, AppVersion? To)
{
    public static readonly ReleaseRange All = new(null, null);
}

public enum ReleaseNoticeKind { Unknown, Swapped, Same }

public sealed record ReleaseNotice(ReleaseNoticeKind Kind, AppVersion? Version = null);

/// <summary>What a range shows: the releases (newest first), the overview cards when several are shown, and notices.</summary>
public sealed record ReleaseSelection(IReadOnlyList<ReleaseEntry> Shown, AppVersion? From, AppVersion? To, bool Only,
    IReadOnlyList<FeatureCard>? Cards, IReadOnlyList<ReleaseNotice> Notices, ReleaseEntry? Newest);

/// <summary>
/// The release data behind the in-app "What's new" view (#252): the same docs/releases.json and the same selection
/// and grouping rules as docs/releases.html (described under "Release page" in docs/DEVELOPMENT.md).
/// </summary>
public static class ReleaseNotes
{
    public const string PagesBase = "https://solwation.github.io/booster-ai/";
    public const string DataUrl = PagesBase + "releases.json";
    public const string GitHub = "https://github.com/" + UpdateLogic.Repository;

    /// <summary>Parses releases.json; unreadable entries are skipped, the rest sorted newest first. Null when the file is not readable at all.</summary>
    public static IReadOnlyList<ReleaseEntry>? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("releases", out var list) || list.ValueKind != JsonValueKind.Array) return null;
            var result = new List<ReleaseEntry>();
            foreach (var r in list.EnumerateArray())
            {
                if (r.ValueKind != JsonValueKind.Object || AppVersion.Parse(Str(r, "version")) is not { } v) continue;
                var features = new List<ReleaseFeature>();
                if (r.TryGetProperty("features", out var fs) && fs.ValueKind == JsonValueKind.Array)
                    foreach (var f in fs.EnumerateArray())
                    {
                        if (f.ValueKind != JsonValueKind.Object) continue;
                        int? issue = f.TryGetProperty("issue", out var i) && i.ValueKind == JsonValueKind.Number && i.TryGetInt32(out var n) ? n : null;
                        features.Add(new(issue, Text(f, "title"), Text(f, "howto"), Text(f, "image"), Str(f, "docs"),
                            Str(f, "topic") is { Length: > 0 } t ? t : null, Text(f, "topicTitle")));
                    }
                var changes = new List<string>();
                if (r.TryGetProperty("changes", out var cs) && cs.ValueKind == JsonValueKind.Array)
                    foreach (var c in cs.EnumerateArray())
                        if (c.ValueKind == JsonValueKind.String && c.GetString() is { Length: > 0 } s) changes.Add(s);
                result.Add(new(v, Str(r, "date"), Str(r, "previous"), Text(r, "summary"), features, changes));
            }
            return [.. result.DistinctBy(r => r.Version).OrderByDescending(r => r.Version)];
        }
        catch (JsonException) { return null; }
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static LocalText? Text(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var p)) return null;
        if (p.ValueKind == JsonValueKind.String) return new(p.GetString(), p.GetString());
        if (p.ValueKind != JsonValueKind.Object) return null;
        var t = new LocalText(Str(p, "sv"), Str(p, "en"));
        return t.IsEmpty ? null : t;
    }

    /// <summary>
    /// Adds the offered release (from the update check) when the data does not have it yet, with its one-line summary;
    /// the release page does the same from the GitHub releases API.
    /// </summary>
    public static IReadOnlyList<ReleaseEntry> WithOffered(IReadOnlyList<ReleaseEntry> releases, ReleaseInfo? offered)
    {
        if (offered is null || offered.PreRelease || releases.Any(r => r.Version.CompareTo(offered.Version) == 0)) return releases;
        var summary = offered.SummarySv is null && offered.SummaryEn is null ? null : new LocalText(offered.SummarySv, offered.SummaryEn);
        var previous = releases.FirstOrDefault(r => r.Version.CompareTo(offered.Version) < 0)?.Version.ToString();
        return [.. releases.Append(new ReleaseEntry(offered.Version, null, previous, summary, [], [])).OrderByDescending(r => r.Version)];
    }

    /// <summary>
    /// The releases after <paramref name="from"/> up to and including <paramref name="to"/>, newest first, as the
    /// release page selects them: a from newer than to is swapped, equal versions show only that release, and a
    /// version missing from the data is named in a notice (the page then shows the releases around it).
    /// </summary>
    public static ReleaseSelection Select(IReadOnlyList<ReleaseEntry> releases, AppVersion? from, AppVersion? to)
    {
        var notices = new List<ReleaseNotice>();
        if (from is not null && to is not null && from.CompareTo(to) > 0) { (from, to) = (to, from); notices.Add(new(ReleaseNoticeKind.Swapped)); }
        foreach (var v in new[] { from, to })
            if (v is not null && !releases.Any(r => r.Version.CompareTo(v) == 0)) notices.Add(new(ReleaseNoticeKind.Unknown, v));
        var only = false;
        if (from is not null && to is not null && from.CompareTo(to) == 0) { notices.Add(new(ReleaseNoticeKind.Same, to)); from = null; only = true; }
        var shown = releases.Where(r => only
            ? r.Version.CompareTo(to) == 0
            : (from is null || r.Version.CompareTo(from) > 0) && (to is null || r.Version.CompareTo(to) <= 0)).ToList();
        var cards = shown.Count > 1 ? Group(shown) : null;
        if (cards is { Count: 0 }) cards = null;
        return new(shown, from, to, only, cards, notices, releases.Count > 0 ? releases[0] : null);
    }

    /// <summary>
    /// The overview of several releases (#250): walk the releases newest first and their features in order; features
    /// with the same topic form one card, a feature without a topic is a card of its own. Per field the newest feature
    /// that has it wins; a card of two or more features takes the newest topicTitle as its title. The card links every
    /// issue (ascending) and names every version (ascending). Merged cards come first, then the rest, each in order of
    /// their newest version. The same rule as group() in docs/releases.html.
    /// </summary>
    public static IReadOnlyList<FeatureCard> Group(IEnumerable<ReleaseEntry> releases)
    {
        var cards = new List<(string Id, List<ReleaseFeature> Parts, List<AppVersion> Versions, List<int> Issues)>();
        var byTopic = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var r in releases.OrderByDescending(r => r.Version))
            foreach (var f in r.Shown)
            {
                int index;
                if (f.Topic is { } topic && byTopic.TryGetValue(topic, out var known)) index = known;
                else
                {
                    index = cards.Count;
                    cards.Add(("f-" + (f.Topic ?? r.Version.ToString().Replace('.', '-') + "-" + index), [], [], []));
                    if (f.Topic is { } t) byTopic[t] = index;
                }
                var c = cards[index];
                c.Parts.Add(f);
                if (!c.Versions.Any(v => v.CompareTo(r.Version) == 0)) c.Versions.Add(r.Version);
                if (f.Issue is { } issue && !c.Issues.Contains(issue)) c.Issues.Add(issue);
            }
        var all = cards.Select(c =>
        {
            T? Newest<T>(Func<ReleaseFeature, T?> field) where T : class => c.Parts.Select(field).FirstOrDefault(v => v is not null);
            LocalText? NewestText(Func<ReleaseFeature, LocalText?> field) => Newest(f => field(f) is { IsEmpty: false } t ? t : null);
            var title = (c.Parts.Count > 1 ? NewestText(f => f.TopicTitle) : null) ?? NewestText(f => f.Title);
            return new FeatureCard(c.Id, c.Parts, title, NewestText(f => f.Howto), NewestText(f => f.Image),
                Newest(f => string.IsNullOrWhiteSpace(f.Docs) ? null : f.Docs), [.. c.Issues.Order()], [.. c.Versions.Order()]);
        }).ToList();
        return [.. all.Where(c => c.Merged), .. all.Where(c => !c.Merged)];
    }

    private static readonly Regex SafePathPattern = new(@"^[\w./-]+(#[\w-]+)?$", RegexOptions.CultureInvariant);

    /// <summary>A relative path under docs/ as the page accepts it (letters, digits, ./-_ and an optional #anchor, no ".."); otherwise null.</summary>
    public static string? SafePath(string? path, bool anchor = true) =>
        path is not null && SafePathPattern.IsMatch(path) && !path.Contains("..") && (anchor || !path.Contains('#')) ? path : null;

    public static string? ImageUrl(string? path) => SafePath(path, anchor: false) is { } p ? PagesBase + p : null;
    public static string? DocsUrl(string? path) => SafePath(path) is { } p ? GitHub + "/blob/main/docs/" + p : null;
    public static string IssueUrl(int issue) => GitHub + "/issues/" + issue;
    public static string ReleaseUrl(AppVersion v) => GitHub + "/releases/tag/v" + v;
    public static string? CompareUrl(ReleaseEntry r) =>
        AppVersion.Parse(r.Previous) is { } p ? GitHub + "/compare/v" + p + "...v" + r.Version : null;

    private static readonly Regex IssueRef = new(@"#(\d+)\b", RegexOptions.CultureInvariant);

    /// <summary>The issue numbers a commit subject refers to (#123), in order and once each.</summary>
    public static IReadOnlyList<int> IssuesIn(string subject) =>
        [.. IssueRef.Matches(subject).Select(m => int.TryParse(m.Groups[1].Value, out var n) ? n : 0).Where(n => n > 0).Distinct()];

    private static readonly string[] MonthsSv = ["januari", "februari", "mars", "april", "maj", "juni", "juli", "augusti", "september", "oktober", "november", "december"];
    private static readonly string[] MonthsEn = ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];

    /// <summary>"27 november 2026" / "27 November 2026" (the page's sv-SE/en-GB long date); the app runs with invariant globalization.</summary>
    public static string? FormatDate(string? isoDate, string language)
    {
        if (!DateOnly.TryParseExact(isoDate, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d)) return null;
        return $"{d.Day} {(language == "sv" ? MonthsSv : MonthsEn)[d.Month - 1]} {d.Year}";
    }

    /// <summary>The copy of docs/releases.json built into the app, so the view works offline.</summary>
    public static string? EmbeddedJson()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("releases.json");
        if (stream is null) return null;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

/// <summary>Where the release data and screenshots come from: Pages with a timeout, a cache on disk, the built-in copy.</summary>
public interface IReleaseNotesSource
{
    /// <summary>The best copy at hand without the network: the cache when it is at least as new as the built-in copy.</summary>
    IReadOnlyList<ReleaseEntry> Local();

    /// <summary>The current data from GitHub Pages, cached on success; null when it cannot be had (offline, timeout, bad answer).</summary>
    Task<IReadOnlyList<ReleaseEntry>?> FetchAsync(CancellationToken cancel);

    /// <summary>A screenshot's bytes by its path under docs/: the cache, else Pages; null when it cannot be had.</summary>
    Task<byte[]?> ImageAsync(string path, CancellationToken cancel);
}

public sealed class ReleaseNotesSource(HttpClient http, string cacheDir, Func<string?> embedded, TimeSpan timeout) : IReleaseNotesSource
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(8);

    /// <summary>The app's source: the cache next to config.json, the built-in copy and the update check's HTTP client settings.</summary>
    public static ReleaseNotesSource ForApp() =>
        new(SharedHttp.Value, Path.Combine(Path.GetDirectoryName(ConfigStore.PathOnDisk)!, "releases-cache"), ReleaseNotes.EmbeddedJson, DefaultTimeout);

    private static readonly Lazy<HttpClient> SharedHttp = new(UpdateClient.CreateHttp);

    private string DataFile => Path.Combine(cacheDir, "releases.json");

    public IReadOnlyList<ReleaseEntry> Local()
    {
        var builtIn = ReleaseNotes.Parse(embedded()) ?? [];
        IReadOnlyList<ReleaseEntry>? cached = null;
        try { if (File.Exists(DataFile)) cached = ReleaseNotes.Parse(File.ReadAllText(DataFile)); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        if (cached is not { Count: > 0 }) return builtIn;
        if (builtIn.Count == 0) return cached;
        return cached[0].Version.CompareTo(builtIn[0].Version) >= 0 ? cached : builtIn;
    }

    public async Task<IReadOnlyList<ReleaseEntry>?> FetchAsync(CancellationToken cancel)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            cts.CancelAfter(timeout);
            using var response = await http.GetAsync(ReleaseNotes.DataUrl, cts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            var json = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            if (ReleaseNotes.Parse(json) is not { Count: > 0 } releases) return null;
            Save(DataFile, System.Text.Encoding.UTF8.GetBytes(json));
            return releases;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException) { return null; }
    }

    public async Task<byte[]?> ImageAsync(string path, CancellationToken cancel)
    {
        if (ReleaseNotes.ImageUrl(path) is not { } url) return null;
        var file = Path.Combine(cacheDir, "images", Path.Combine(path.Split('/')));
        try { if (File.Exists(file)) return await File.ReadAllBytesAsync(file, cancel).ConfigureAwait(false); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            cts.CancelAfter(timeout);
            using var response = await http.GetAsync(url, cts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            var bytes = await response.Content.ReadAsByteArrayAsync(cts.Token).ConfigureAwait(false);
            if (bytes.Length == 0) return null;
            Save(file, bytes);
            return bytes;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException) { return null; }
    }

    /// <summary>Writes through a temporary file, so a half-written cache is never read; a failure only loses the cache.</summary>
    private static void Save(string file, byte[] bytes)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            var tmp = file + ".tmp";
            File.WriteAllBytes(tmp, bytes);
            File.Move(tmp, file, overwrite: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

/// <summary>
/// For --screenshot (and tests): the built-in copy, never the network; screenshots come from a local docs folder
/// (the repository's, found from the working directory) so the view looks as it does online.
/// </summary>
public sealed class OfflineReleaseNotesSource(Func<string?> json, string? docsDir) : IReleaseNotesSource
{
    public static OfflineReleaseNotesSource ForScreenshot()
    {
        string? docs = null;
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
            for (var dir = new DirectoryInfo(start); dir is not null && docs is null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "docs", "releases.json"))) docs = Path.Combine(dir.FullName, "docs");
        return new(ReleaseNotes.EmbeddedJson, docs);
    }

    public IReadOnlyList<ReleaseEntry> Local() => ReleaseNotes.Parse(json()) ?? [];

    public Task<IReadOnlyList<ReleaseEntry>?> FetchAsync(CancellationToken cancel) => Task.FromResult<IReadOnlyList<ReleaseEntry>?>(null);

    public Task<byte[]?> ImageAsync(string path, CancellationToken cancel)
    {
        if (docsDir is null || ReleaseNotes.SafePath(path, anchor: false) is null) return Task.FromResult<byte[]?>(null);
        var file = Path.Combine(docsDir, Path.Combine(path.Split('/')));
        return Task.FromResult(File.Exists(file) ? File.ReadAllBytes(file) : null);
    }
}
