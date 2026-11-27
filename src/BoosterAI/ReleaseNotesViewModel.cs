using Avalonia.Media.Imaging;

namespace BoosterAI;

/// <summary>A link in the What's new view; it opens <see cref="Url"/> in the browser.</summary>
public sealed record NewsLink(string Text, string Url)
{
    public string Name => Text;
}

/// <summary>A commit subject with its issue numbers as links.</summary>
public sealed record ChangeLine(string Text, IReadOnlyList<NewsLink> Issues)
{
    public bool HasIssues => Issues.Count > 0;
}

/// <summary>A feature card: the features of one topic merged in the overview (#250), or one release's own feature.</summary>
public sealed class FeatureCardViewModel : Observable
{
    private Bitmap? _image;
    private string _imageStatus = "";

    public FeatureCardViewModel(string id, LocalText? title, LocalText? howto, LocalText? image, string? docs, IReadOnlyList<int> issues,
        IReadOnlyList<AppVersion>? versions)
    {
        var language = Loc.Language;
        Id = id;
        Title = title?.Pick(language) ?? "";
        Howto = howto?.Pick(language) ?? "";
        ImagePath = ReleaseNotes.SafePath(image?.Pick(language), anchor: false);
        ImageUrl = ReleaseNotes.ImageUrl(ImagePath);
        VersionsText = versions is { Count: > 0 }
            ? Loc.Format(versions.Count == 1 ? "news.versions.one" : "news.versions.other", string.Join(", ", versions))
            : null;
        var links = issues.Select(n => new NewsLink(Loc.Format("news.issue", n), ReleaseNotes.IssueUrl(n))).ToList();
        if (ReleaseNotes.DocsUrl(docs) is { } url) links.Add(new(Loc.Get("news.docs"), url));
        Links = links;
        _imageStatus = Loc.Get("news.image.loading");
    }

    public static FeatureCardViewModel From(FeatureCard c) => new(c.Id, c.Title, c.Howto, c.Image, c.Docs, c.Issues, c.Versions);

    public static FeatureCardViewModel From(ReleaseFeature f, string id) =>
        new(id, f.Title, f.Howto, f.Image, f.Docs, f.Issue is { } n ? [n] : [], null);

    public string Id { get; }
    public string Title { get; }
    public string Howto { get; }
    public bool HasHowto => Howto != "";
    public string? VersionsText { get; }
    public bool HasVersions => VersionsText is not null;
    public IReadOnlyList<NewsLink> Links { get; }
    public bool HasLinks => Links.Count > 0;

    /// <summary>The screenshot's path under docs/ (null: the card has none and its text takes the full width).</summary>
    public string? ImagePath { get; }
    public string? ImageUrl { get; }
    public bool HasImageArea => ImagePath is not null;
    public int TextColumn => HasImageArea ? 1 : 0;
    public int TextSpan => HasImageArea ? 1 : 2;
    public string ImageName => Loc.Format("news.image.name", Title);

    public Bitmap? Image
    {
        get => _image;
        set { if (Set(ref _image, value)) Raise(nameof(ShowImageStatus)); }
    }

    /// <summary>"Loading screenshot …", or that it cannot be shown without a connection.</summary>
    public string ImageStatus { get => _imageStatus; set => Set(ref _imageStatus, value); }
    public bool ShowImageStatus => Image is null;
}

/// <summary>A jump from a release to its card in the overview.</summary>
public sealed record NewsRef(string Text, FeatureCardViewModel Card);

/// <summary>One release in the view: heading, summary, its cards (or links to them in the overview) and the commit list.</summary>
public sealed class ReleaseSectionViewModel : Observable
{
    private bool _changesOpen;

    public ReleaseSectionViewModel(ReleaseEntry r, string? tag, IReadOnlyList<FeatureCardViewModel> features, IReadOnlyList<NewsRef> refs, bool changesOpen)
    {
        var language = Loc.Language;
        Version = r.Version;
        Heading = "BoosterAI " + r.Version;
        Date = ReleaseNotes.FormatDate(r.Date, language);
        Tag = tag;
        Summary = r.Summary?.Pick(language) is { Length: > 0 } s ? s : null;
        Features = features;
        Refs = refs;
        Changes = [.. r.Changes.Select(c => new ChangeLine(c,
            [.. ReleaseNotes.IssuesIn(c).Select(n => new NewsLink("#" + n, ReleaseNotes.IssueUrl(n)))]))];
        ChangesHeader = Loc.Format("news.changes", r.Changes.Count);
        _changesOpen = changesOpen;
        var links = new List<NewsLink> { new(Loc.Get("news.release"), ReleaseNotes.ReleaseUrl(r.Version)) };
        if (ReleaseNotes.CompareUrl(r) is { } compare) links.Add(new(Loc.Get("news.compare"), compare));
        Links = links;
    }

    public AppVersion Version { get; }
    public string Heading { get; }
    public string? Date { get; }
    public bool HasDate => Date is not null;
    public string? Tag { get; }
    public bool HasTag => Tag is not null;
    public string? Summary { get; }
    public bool HasSummary => Summary is not null;
    public IReadOnlyList<FeatureCardViewModel> Features { get; }
    public bool HasFeatures => Features.Count > 0;
    public IReadOnlyList<NewsRef> Refs { get; }
    public bool HasRefs => Refs.Count > 0;
    public IReadOnlyList<ChangeLine> Changes { get; }
    public bool HasChanges => Changes.Count > 0;
    public bool NoChanges => Changes.Count == 0;
    public string ChangesHeader { get; }
    public bool ChangesOpen { get => _changesOpen; set => Set(ref _changesOpen, value); }
    public IReadOnlyList<NewsLink> Links { get; }
}

/// <summary>
/// The What's new view (#252): the releases of a from/to range from docs/releases.json, with the overview cards of
/// #250, rendered natively so language, theme and zoom follow the app. The data comes from <see cref="IReleaseNotesSource"/>:
/// the local copy at once, then GitHub Pages when it answers in time.
/// </summary>
public sealed class ReleaseNotesViewModel : Observable
{
    private readonly IReleaseNotesSource _source;
    private readonly ReleaseInfo? _offered;
    private IReadOnlyList<ReleaseEntry> _data = [];
    private ReleaseRange _range;
    private bool _stale;
    private bool _latestOnly;
    private readonly Dictionary<string, Bitmap?> _images = new(StringComparer.Ordinal);

    public ReleaseNotesViewModel(IReleaseNotesSource source, ReleaseRange range, ReleaseInfo? offered = null, bool latestOnly = false)
    {
        _source = source;
        _latestOnly = latestOnly;
        _range = range;
        _offered = offered;
        _data = source.Local();
        Build();
    }

    public ReleaseRange Range => _range;

    /// <summary>True while only the newest release of the range is shown (#266); "Show all releases" lifts it.</summary>
    public bool LatestOnly => _latestOnly;
    public string Title { get; private set; } = "";
    public string Subtitle { get; private set; } = "";
    public IReadOnlyList<string> Notices { get; private set; } = [];
    public bool HasNotices => Notices.Count > 0;
    public IReadOnlyList<FeatureCardViewModel> Overview { get; private set; } = [];
    public bool HasOverview => Overview.Count > 0;
    public string OverviewIntro { get; private set; } = "";
    public IReadOnlyList<ReleaseSectionViewModel> Releases { get; private set; } = [];
    public bool IsEmpty { get; private set; }
    public string EmptyText { get; private set; } = "";
    public ReleaseSelection? Selection { get; private set; }

    /// <summary>Every card on the view, overview and per release, for the screenshots to load.</summary>
    public IEnumerable<FeatureCardViewModel> Cards => Overview.Concat(Releases.SelectMany(r => r.Features));

    /// <summary>The same range on the release page, in the app language and the app's resolved theme (#251).</summary>
    public string BrowserUrl(string? theme) => UpdateLogic.ReleaseNotesUrl(_range, Loc.Language, theme);

    /// <summary>Asks Pages for newer data (with the source's timeout) and redraws when it differs; offline keeps the local copy with a notice.</summary>
    public async Task RefreshAsync(CancellationToken cancel = default)
    {
        var fresh = await _source.FetchAsync(cancel);
        if (fresh is null) { _stale = true; Build(); return; }
        var changed = !SameData(fresh, _data);
        _stale = false;
        _data = fresh;
        if (changed) Build();
        else Raise(nameof(Notices));
    }

    private static bool SameData(IReadOnlyList<ReleaseEntry> a, IReadOnlyList<ReleaseEntry> b) =>
        a.Count == b.Count && a.Zip(b).All(p => p.First.Version.CompareTo(p.Second.Version) == 0
            && p.First.Features.SequenceEqual(p.Second.Features)
            && p.First.Changes.SequenceEqual(p.Second.Changes) && Equals(p.First.Summary, p.Second.Summary) && p.First.Date == p.Second.Date);

    /// <summary>"Show all releases" from an empty range.</summary>
    public void ShowAll()
    {
        _range = ReleaseRange.All;
        _latestOnly = false;
        Build();
    }

    /// <summary>Loads each card's screenshot (cache, then Pages); one that cannot be had shows a short text instead.</summary>
    public async Task LoadImagesAsync(CancellationToken cancel = default)
    {
        foreach (var card in Cards.ToList())
        {
            if (card.ImagePath is not { } path) continue;
            if (!_images.TryGetValue(path, out var bitmap))
            {
                var bytes = await _source.ImageAsync(path, cancel);
                bitmap = Decode(bytes);
                // A failed load is not remembered, so the next refresh or opening tries again.
                if (bitmap is not null) _images[path] = bitmap;
            }
            card.Image = bitmap;
            if (bitmap is null) card.ImageStatus = Loc.Get("news.image.offline");
        }
    }

    private static Bitmap? Decode(byte[]? bytes)
    {
        if (bytes is null) return null;
        try
        {
            using var stream = new MemoryStream(bytes);
            return new Bitmap(stream);
        }
        catch (Exception) { return null; }
    }

    private void Build()
    {
        var data = ReleaseNotes.WithOffered(_data, _offered);
        var fromOffer = data.Count != _data.Count;
        var sel = ReleaseNotes.Select(data, _range.From, _range.To);
        if (_latestOnly && sel.Shown.Count > 0) sel = sel with { Shown = [sel.Shown.MaxBy(r => r.Version)!], Cards = null };
        Selection = sel;
        var notices = sel.Notices.Select(n => n.Kind switch
        {
            ReleaseNoticeKind.Unknown => Loc.Format("news.unknown", n.Version!),
            ReleaseNoticeKind.Swapped => Loc.Get("news.swapped"),
            _ => Loc.Format("news.same", n.Version!),
        }).ToList();
        if (fromOffer && _offered is not null && sel.Shown.Any(r => r.Version.CompareTo(_offered.Version) == 0))
            notices.Add(Loc.Format("news.fallback", _offered.Version));
        if (_stale) notices.Add(Loc.Get("news.offline"));
        Notices = notices;

        Title = _latestOnly ? Loc.Get("news.h1Latest") : sel.From is { } f ? Loc.Format("news.h1Range", f) : Loc.Get("news.h1");
        var count = Loc.FormatCount("news.count", sel.Shown.Count);
        Subtitle = _latestOnly ? Loc.Get("news.latestOnly") : sel.From is not null || sel.To is not null
            ? (sel.From is { } from && sel.To is { } to ? Loc.Format("news.range", from, to)
                : sel.From is { } a ? Loc.Format("news.rangeFrom", a) : Loc.Format("news.rangeTo", sel.To!)) + " · " + count
            : Loc.FormatCount("news.allIntro", sel.Shown.Count);
        IsEmpty = sel.Shown.Count == 0;
        EmptyText = Loc.Get("news.empty");

        var cards = sel.Cards?.Select(FeatureCardViewModel.From).ToList() ?? [];
        Overview = cards;
        OverviewIntro = Loc.FormatCount("news.overviewIntro", sel.Shown.Count);
        var sections = new List<ReleaseSectionViewModel>();
        for (var i = 0; i < sel.Shown.Count; i++)
        {
            var r = sel.Shown[i];
            var tag = sel.To is { } last && r.Version.CompareTo(last) == 0 && sel.From is not null ? Loc.Get("news.yours")
                : ReferenceEquals(r, sel.Newest) || (sel.Newest is { } newest && r.Version.CompareTo(newest.Version) == 0) ? Loc.Get("news.latest") : null;
            var shown = r.Shown.ToList();
            if (sel.Cards is { } groups)
            {
                var refs = groups.Select((c, k) => (c, k)).Where(x => x.c.Parts.Any(p => shown.Any(s => ReferenceEquals(s, p))))
                    .Select(x => new NewsRef(cards[x.k].Title, cards[x.k])).ToList();
                sections.Add(new(r, tag, [], refs, changesOpen: false));
            }
            else
            {
                var features = shown.Select((f, k) => FeatureCardViewModel.From(f, "f-" + r.Version.ToString().Replace('.', '-') + "-" + k)).ToList();
                sections.Add(new(r, tag, features, [], changesOpen: sel.Shown.Count <= 3 || (shown.Count == 0 && i < 10)));
            }
        }
        Releases = sections;
        foreach (var name in new[] { nameof(Range), nameof(Title), nameof(Subtitle), nameof(Notices), nameof(HasNotices), nameof(Overview), nameof(HasOverview),
                     nameof(OverviewIntro), nameof(Releases), nameof(IsEmpty), nameof(EmptyText), nameof(Selection), nameof(LatestOnly) })
            Raise(name);
    }
}
