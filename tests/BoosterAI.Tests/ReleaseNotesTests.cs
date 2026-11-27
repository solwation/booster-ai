using System.Net;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BoosterAI;
using Xunit;

namespace BoosterAI.Tests;

/// <summary>#252: What's new inside BoosterAI, from docs/releases.json with the release page's selection and grouping (#245, #250).</summary>
[Collection(ConfigFileCollection.Name)] // switches Loc's global language; parallel tests read it (#278)
public sealed class ReleaseNotesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bs-releasenotes-" + Guid.NewGuid().ToString("N"));
    private readonly string _language = Loc.Language;

    public ReleaseNotesTests()
    {
        Directory.CreateDirectory(_root);
        Loc.Use("en"); // The tests expect English; an earlier test in the serial collection may have left Swedish on.
    }

    public void Dispose()
    {
        Loc.Use(_language);
        Directory.Delete(_root, recursive: true);
    }

    private static AppVersion V(string s) => AppVersion.Parse(s) ?? throw new ArgumentException(s);

    private static IReadOnlyList<ReleaseEntry> EmbeddedAll => ReleaseNotes.Parse(ReleaseNotes.EmbeddedJson())!;

    /// <summary>
    /// The embedded releases up to and including 0.54.0: past releases never change, so selection tests stay valid when a new
    /// release is added to docs/releases.json (#264). Tests about the source itself compare with <see cref="EmbeddedAll"/>.
    /// </summary>
    private static IReadOnlyList<ReleaseEntry> Embedded => EmbeddedAll.Where(r => r.Version.CompareTo(V("0.54.0")) <= 0).ToList();

    private static string[] Versions(IEnumerable<ReleaseEntry> list) => [.. list.Select(r => r.Version.ToString())];

    /// <summary>A releases.json with the given releases; each feature as (issue, topic, title, image, topicTitle).</summary>
    private static string Json(params (string Version, (int Issue, string? Topic, string? Title, string? Image, string? TopicTitle)[] Features)[] releases) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            releases = releases.Select(r => new
            {
                version = r.Version,
                date = "2026-11-27",
                summary = new { sv = "Sammanfattning " + r.Version, en = "Summary " + r.Version },
                features = r.Features.Select(f => new Dictionary<string, object?>
                {
                    ["issue"] = f.Issue,
                    ["title"] = f.Title is null ? null : new { sv = f.Title + " sv", en = f.Title },
                    ["howto"] = new { sv = "Gör " + f.Issue, en = "Do " + f.Issue },
                    ["image"] = f.Image is null ? null : new { sv = f.Image, en = f.Image },
                    ["docs"] = "DETAILS.md#x" + f.Issue,
                    ["topic"] = f.Topic,
                    ["topicTitle"] = f.TopicTitle is null ? null : new { sv = f.TopicTitle, en = f.TopicTitle },
                }.Where(p => p.Value is not null).ToDictionary()),
                changes = new[] { "Change in " + r.Version + " (#" + (r.Features.FirstOrDefault().Issue) + ")" },
            }),
        });

    [Fact]
    public void TheBuiltInCopyIsTheRepositoryReleaseData()
    {
        var all = Embedded;
        Assert.True(all.Count >= 95);
        Assert.Equal(all.OrderByDescending(r => r.Version).Select(r => r.Version), all.Select(r => r.Version));
        Assert.Contains(all, r => r.Version.CompareTo(V("0.54.0")) == 0 && r.Features.Any(f => f.Topic == "tab-splash"));
    }

    [Fact]
    public void FromToShowsTheReleasesAfterFromUpToAndIncludingToNewestFirst()
    {
        var sel = ReleaseNotes.Select(Embedded, V("0.52.0"), V("0.54.0"));
        Assert.Equal(["0.54.0", "0.53.1", "0.53.0"], Versions(sel.Shown));
        Assert.Empty(sel.Notices);
        Assert.False(sel.Only);
    }

    [Fact]
    public void SeveralVersionsMergeTheSameTopicIntoOneCardWithAllItsIssues()
    {
        // #250's acceptance: the logo is one card with #242–#244, the recaps (#241) a card of its own.
        var cards = ReleaseNotes.Select(Embedded, V("0.52.0"), V("0.54.0")).Cards!;
        Assert.Equal(2, cards.Count);
        Assert.True(cards[0].Merged);
        Assert.Equal([242, 243, 244], cards[0].Issues);
        Assert.Equal(["0.53.0", "0.53.1", "0.54.0"], cards[0].Versions.Select(v => v.ToString()));
        Assert.Equal("Animated BoosterAI splash in tabs", cards[0].Title!.Pick("en"));
        Assert.Equal("verification/244/splash-settled.png", cards[0].Image!.Pick("sv"));
        Assert.Equal([241], cards[1].Issues);
        // From 0.51.0 the sound effects of 0.52.0 (#240) join as a card of their own, after the newer ones.
        var wider = ReleaseNotes.Select(Embedded, V("0.51.0"), V("0.54.0")).Cards!;
        Assert.Equal([[242, 243, 244], [241], [240]], wider.Select(c => c.Issues.ToArray()));
        Assert.Equal("verification/240/settings-en.png", wider[2].Image!.Pick("en"));
        Assert.Equal("verification/240/settings-sv.png", wider[2].Image!.Pick("sv"));
    }

    [Fact]
    public void OneVersionShowsItsOwnFeaturesWithoutAnOverview()
    {
        var sel = ReleaseNotes.Select(Embedded, V("0.53.1"), V("0.54.0"));
        Assert.Equal(["0.54.0"], Versions(sel.Shown));
        Assert.Null(sel.Cards);
    }

    [Fact]
    public void AnUnknownVersionIsNamedAndTheReleasesAroundItAreShown()
    {
        var sel = ReleaseNotes.Select(Embedded, V("0.52.7"), V("0.54.0"));
        Assert.Equal(["0.54.0", "0.53.1", "0.53.0"], Versions(sel.Shown));
        Assert.Equal([new ReleaseNotice(ReleaseNoticeKind.Unknown, V("0.52.7"))], sel.Notices);
        // A version newer than the data (released after the build): nothing yet, with a notice.
        var newer = ReleaseNotes.Select(Embedded, V("0.54.0"), V("9.0.0"));
        Assert.Empty(newer.Shown);
        Assert.Contains(new ReleaseNotice(ReleaseNoticeKind.Unknown, V("9.0.0")), newer.Notices);
    }

    [Fact]
    public void SwappedAndEqualVersionsBehaveAsOnThePage()
    {
        var swapped = ReleaseNotes.Select(Embedded, V("0.54.0"), V("0.53.0"));
        Assert.Equal(["0.54.0", "0.53.1"], Versions(swapped.Shown));
        Assert.Contains(swapped.Notices, n => n.Kind == ReleaseNoticeKind.Swapped);
        var same = ReleaseNotes.Select(Embedded, V("0.53.1"), V("0.53.1"));
        Assert.True(same.Only);
        Assert.Equal(["0.53.1"], Versions(same.Shown));
        Assert.Contains(new ReleaseNotice(ReleaseNoticeKind.Same, V("0.53.1")), same.Notices);
        Assert.Equal(Embedded.Count, ReleaseNotes.Select(Embedded, null, null).Shown.Count);
        Assert.Equal(["0.54.0", "0.53.1"], Versions(ReleaseNotes.Select(Embedded, V("0.53.0"), null).Shown));
    }

    [Fact]
    public void GroupingTakesEachFieldFromTheNewestFeatureThatHasIt()
    {
        var data = ReleaseNotes.Parse(Json(
            ("1.3.0", [(30, "topic-a", "A three", null, "Topic A"), (31, null, "Single new", "img/31.png", null)]),
            ("1.2.0", [(20, "topic-b", "B two", "img/20.png", null)]),
            ("1.1.0", [(10, "topic-a", "A one", "img/10.png", "Old A"), (11, "topic-b", "B one", null, "Topic B"), (12, null, "Single old", null, null)])))!;
        var cards = ReleaseNotes.Group(data);
        // Merged first (newest first: A from 1.3.0, then B from 1.2.0), then the single cards.
        Assert.Equal(["Topic A", "Topic B", "Single new", "Single old"], cards.Select(c => c.Title!.Pick("en")));
        Assert.Equal("img/10.png", cards[0].Image!.Pick("en")); // 1.3.0 has no image, so 1.1.0's
        Assert.Equal("Do 30", cards[0].Howto!.Pick("en"));
        Assert.Equal("DETAILS.md#x30", cards[0].Docs);
        Assert.Equal([10, 30], cards[0].Issues);
        Assert.Equal(["1.1.0", "1.3.0"], cards[0].Versions.Select(v => v.ToString()));
        // A card of one feature keeps its own title even when it has a topic title.
        Assert.Equal("B two", ReleaseNotes.Group(data.Where(r => r.Version.CompareTo(V("1.2.0")) == 0)).Single().Title!.Pick("en"));
        Assert.Equal("f-topic-a", cards[0].Id);
        Assert.Equal("f-1-3-0-1", cards[2].Id); // numbered by creation order, as on the page
    }

    [Fact]
    public void ParseSkipsUnreadableEntriesAndRejectsAnythingElse()
    {
        Assert.Null(ReleaseNotes.Parse(null));
        Assert.Null(ReleaseNotes.Parse("not json"));
        Assert.Null(ReleaseNotes.Parse("[1,2]"));
        var list = ReleaseNotes.Parse("""{"releases":[{"version":"x"},{"version":"1.0.0","features":[3,{"title":"plain"}],"changes":["a",4]},{"version":"1.1.0"}]}""")!;
        Assert.Equal(["1.1.0", "1.0.0"], Versions(list));
        Assert.Equal("plain", list[1].Features.Single().Title!.Pick("sv"));
        Assert.Equal(["a"], list[1].Changes);
    }

    [Theory]
    [InlineData("releases/0.53.0/tab-logo.png", "https://solwation.github.io/booster-ai/releases/0.53.0/tab-logo.png")]
    [InlineData("../secret.png", null)]
    [InlineData("https://evil.example/x.png", null)]
    [InlineData("a b.png", null)]
    [InlineData("img.png#frag", null)]
    public void ImagesOnlyComeFromSafePathsUnderDocs(string path, string? url) => Assert.Equal(url, ReleaseNotes.ImageUrl(path));

    [Fact]
    public void LinksPointAtGitHub()
    {
        Assert.Equal("https://github.com/solwation/booster-ai/blob/main/docs/DETAILS.md#updates", ReleaseNotes.DocsUrl("DETAILS.md#updates"));
        Assert.Null(ReleaseNotes.DocsUrl("javascript:alert(1)"));
        Assert.Equal("https://github.com/solwation/booster-ai/issues/252", ReleaseNotes.IssueUrl(252));
        var r = Embedded.First(r => r.Version.CompareTo(V("0.54.0")) == 0);
        Assert.Equal("https://github.com/solwation/booster-ai/compare/v0.53.1...v0.54.0", ReleaseNotes.CompareUrl(r));
        Assert.Equal("https://github.com/solwation/booster-ai/releases/tag/v0.54.0", ReleaseNotes.ReleaseUrl(r.Version));
        Assert.Equal([244, 12], ReleaseNotes.IssuesIn("Fix (#244) and #12, again #244"));
        Assert.Equal("27 november 2026", ReleaseNotes.FormatDate("2026-11-27", "sv"));
        Assert.Equal("3 March 2026", ReleaseNotes.FormatDate("2026-03-03", "en"));
        Assert.Null(ReleaseNotes.FormatDate("soon", "en"));
    }

    [Fact]
    public void AnOfferedReleaseMissingFromTheDataIsAddedWithItsSummary()
    {
        var offered = new ReleaseInfo("v9.0.0", V("9.0.0"), false, UpdateLogic.ReleasesPage, [], "Nytt", "New");
        var data = ReleaseNotes.WithOffered(Embedded, offered);
        Assert.Equal("9.0.0", data[0].Version.ToString());
        Assert.Equal("New", data[0].Summary!.Pick("en"));
        Assert.Equal("0.54.0", data[0].Previous);
        Assert.Equal(Embedded.Count + 1, data.Count);
        Assert.Equal(["9.0.0"], Versions(ReleaseNotes.Select(data, V("0.54.0"), V("9.0.0")).Shown));
        // A release the data already has is not duplicated.
        var known = new ReleaseInfo("v0.54.0", V("0.54.0"), false, UpdateLogic.ReleasesPage, []);
        Assert.Equal(Embedded.Count, ReleaseNotes.WithOffered(Embedded, known).Count);
    }

    // --- the source: Pages with a timeout, a cache and the built-in copy -------------------------------------------

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handle) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            Requests.Add(request.RequestUri!.ToString());
            return handle(request, cancel);
        }
    }

    private static Handler Offline() => new((_, _) => throw new HttpRequestException("offline"));

    private static Handler Serving(string json, byte[]? image = null) => new((request, _) => Task.FromResult(
        request.RequestUri!.AbsolutePath.EndsWith("releases.json")
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) }
            : image is null ? new HttpResponseMessage(HttpStatusCode.NotFound) : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(image) }));

    private ReleaseNotesSource Source(Handler handler, string? embedded = null, TimeSpan? timeout = null) =>
        new(new HttpClient(handler), Path.Combine(_root, "cache"), () => embedded ?? ReleaseNotes.EmbeddedJson(), timeout ?? TimeSpan.FromSeconds(5));

    [Fact]
    public async Task OfflineTheViewUsesTheBuiltInCopy()
    {
        var source = Source(Offline());
        Assert.Null(await source.FetchAsync(CancellationToken.None));
        Assert.Equal(Versions(EmbeddedAll), Versions(source.Local()));
        Assert.Null(await source.ImageAsync("releases/0.53.0/tab-logo.png", CancellationToken.None));
        Assert.False(Directory.Exists(Path.Combine(_root, "cache")));
    }

    [Fact]
    public async Task ASlowAnswerGivesUpAfterTheTimeout()
    {
        var source = Source(new Handler(async (_, cancel) => { await Task.Delay(TimeSpan.FromSeconds(30), cancel); return new HttpResponseMessage(HttpStatusCode.OK); }),
            timeout: TimeSpan.FromMilliseconds(200));
        var watch = System.Diagnostics.Stopwatch.StartNew();
        Assert.Null(await source.FetchAsync(CancellationToken.None));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), watch.Elapsed.ToString());
    }

    [Fact]
    public async Task AFetchedListIsCachedAndUsedOfflineWhenItIsNewer()
    {
        var newer = Json(("9.1.0", [(900, null, "Future", null, null)]), ("0.54.0", []));
        var online = Source(Serving(newer));
        Assert.Equal(["9.1.0", "0.54.0"], Versions((await online.FetchAsync(CancellationToken.None))!));
        Assert.True(File.Exists(Path.Combine(_root, "cache", "releases.json")));
        // Later, offline: the cache beats the older built-in copy.
        Assert.Equal(["9.1.0", "0.54.0"], Versions(Source(Offline()).Local()));
    }

    [Fact]
    public void AnOlderOrBrokenCacheLosesToTheBuiltInCopy()
    {
        Directory.CreateDirectory(Path.Combine(_root, "cache"));
        File.WriteAllText(Path.Combine(_root, "cache", "releases.json"), Json(("0.1.0", [])));
        Assert.Equal(Versions(EmbeddedAll), Versions(Source(Offline()).Local()));
        File.WriteAllText(Path.Combine(_root, "cache", "releases.json"), "{broken");
        Assert.Equal(Versions(EmbeddedAll), Versions(Source(Offline()).Local()));
    }

    [Fact]
    public async Task ABadAnswerIsNotCached()
    {
        var source = Source(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>") })));
        Assert.Null(await source.FetchAsync(CancellationToken.None));
        Assert.False(File.Exists(Path.Combine(_root, "cache", "releases.json")));
        var error = Source(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError))));
        Assert.Null(await error.FetchAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ScreenshotsAreFetchedFromPagesOnceAndThenServedFromTheCache()
    {
        var handler = Serving("{}", [1, 2, 3]);
        var source = Source(handler);
        Assert.Equal([1, 2, 3], await source.ImageAsync("releases/0.53.0/tab-logo.png", CancellationToken.None));
        Assert.Equal(["https://solwation.github.io/booster-ai/releases/0.53.0/tab-logo.png"], handler.Requests);
        Assert.Equal([1, 2, 3], await Source(Offline()).ImageAsync("releases/0.53.0/tab-logo.png", CancellationToken.None));
        Assert.Null(await source.ImageAsync("../config.json", CancellationToken.None));
        Assert.Single(handler.Requests);
    }

    // --- the view model --------------------------------------------------------------------------------------------

    private sealed class FakeSource(IReadOnlyList<ReleaseEntry> local, IReadOnlyList<ReleaseEntry>? online) : IReleaseNotesSource
    {
        public IReadOnlyList<ReleaseEntry> Local() => local;
        public Task<IReadOnlyList<ReleaseEntry>?> FetchAsync(CancellationToken cancel) => Task.FromResult(online);
        public Task<byte[]?> ImageAsync(string path, CancellationToken cancel) => Task.FromResult<byte[]?>(null);
    }

    [Fact]
    public async Task TheViewShowsTheOverviewAndKeepsTheCommitListsCollapsed()
    {
        var vm = new ReleaseNotesViewModel(new FakeSource(Embedded, null), new ReleaseRange(V("0.52.0"), V("0.54.0")));
        Assert.Equal(2, vm.Overview.Count);
        Assert.Equal(["0.54.0", "0.53.1", "0.53.0"], vm.Releases.Select(r => r.Version.ToString()));
        Assert.All(vm.Releases, r => Assert.False(r.ChangesOpen));
        Assert.All(vm.Releases, r => Assert.Empty(r.Features));
        Assert.Same(vm.Overview[0], vm.Releases[0].Refs.Single().Card);
        Assert.Equal(2, vm.Releases[2].Refs.Count);
        Assert.Equal(["#242", "#243", "#244", "docs"], vm.Overview[0].Links.Select(l => l.Url.Contains("/issues/") ? "#" + l.Url.Split('/')[^1] : "docs"));
        Assert.True(vm.Releases[0].HasTag); // the version updated to
        Assert.False(vm.HasNotices);

        // Offline: the local copy stays, with a notice; the screenshots say they cannot be shown.
        await vm.RefreshAsync();
        Assert.Single(vm.Notices);
        await vm.LoadImagesAsync();
        Assert.All(vm.Overview.Where(c => c.HasImageArea), c => Assert.True(c.ShowImageStatus));
        Assert.False(vm.Overview[1].HasImageArea);
        Assert.Equal(0, vm.Overview[1].TextColumn);
        Assert.Equal(2, vm.Overview[1].TextSpan);
    }

    [Fact]
    public async Task OneVersionShowsItsCardsAndOpenCommitList()
    {
        var vm = new ReleaseNotesViewModel(new FakeSource(Embedded, null), new ReleaseRange(V("0.53.1"), V("0.54.0")));
        Assert.False(vm.HasOverview);
        var release = Assert.Single(vm.Releases);
        Assert.True(release.ChangesOpen);
        Assert.Equal([244], release.Features.Single().Links.Where(l => l.Url.Contains("/issues/")).Select(l => int.Parse(l.Url.Split('/')[^1])));
        Assert.Equal([244], release.Changes.Single().Issues.Select(l => int.Parse(l.Text[1..])));

        // Pages answers with a newer list: the view redraws from it.
        var fresh = ReleaseNotes.Parse(Json(("0.54.0", [(500, null, "Fresh", null, null)]), ("0.53.1", [])))!;
        var online = new ReleaseNotesViewModel(new FakeSource(Embedded, fresh), new ReleaseRange(V("0.53.1"), V("0.54.0")));
        await online.RefreshAsync();
        Assert.Equal("Fresh", online.Releases.Single().Features.Single().Title);
        Assert.False(online.HasNotices);
    }

    [Fact]
    public void AnEmptyRangeOffersAllReleases()
    {
        var vm = new ReleaseNotesViewModel(new FakeSource(Embedded, null), new ReleaseRange(V("0.54.0"), V("9.0.0")));
        Assert.True(vm.IsEmpty);
        vm.ShowAll();
        Assert.False(vm.IsEmpty);
        Assert.Equal(Embedded.Count, vm.Releases.Count);
        Assert.Equal(ReleaseRange.All, vm.Range);
    }

    [Fact]
    public void ByDefaultOnlyTheLatestReleaseShowsAndShowAllYieldsEveryRelease()
    {
        Loc.Use("sv");
        // Up to date (updated from an older version) and with a newer release on offer: the same rule.
        var upToDate = new ReleaseNotesViewModel(new FakeSource(Embedded, null), new ReleaseRange(V("0.50.0"), V("0.54.0")), latestOnly: true);
        Assert.Equal(["0.54.0"], Versions(upToDate.Releases.Select(r => Embedded.Single(e => e.Version.CompareTo(r.Version) == 0))));
        Assert.True(upToDate.LatestOnly);
        Assert.False(upToDate.HasOverview);

        var offered = new ReleaseInfo("v9.0.0", V("9.0.0"), false, UpdateLogic.ReleasesPage, [], "Nytt", "New");
        var newer = new ReleaseNotesViewModel(new FakeSource(Embedded, null), UpdateLogic.MoreRange(offered, V("0.50.0"), null), offered, latestOnly: true);
        Assert.Equal("9.0.0", newer.Releases.Single().Version.ToString());

        upToDate.ShowAll();
        Assert.False(upToDate.LatestOnly);
        Assert.Equal(Embedded.Count, upToDate.Releases.Count);
        Assert.Equal(ReleaseRange.All, upToDate.Range);
        newer.ShowAll();
        Assert.True(newer.Releases.Count > 1);
    }

    [Fact]
    public void TheOfferedReleaseShowsEvenBeforeTheDataHasIt()
    {
        var offered = new ReleaseInfo("v9.0.0", V("9.0.0"), false, UpdateLogic.ReleasesPage, [], "Nytt", "New");
        var vm = new ReleaseNotesViewModel(new FakeSource(Embedded, null), UpdateLogic.MoreRange(offered, V("0.54.0"), null), offered);
        Assert.Equal("9.0.0", vm.Releases.Single().Version.ToString());
        Assert.Single(vm.Notices);
    }

    // --- URLs and wiring ------------------------------------------------------------------------------------------------

    private const string Page = "https://solwation.github.io/booster-ai/releases.html";

    [Theory]
    [InlineData("sv", "light", "?from=0.52.0&to=0.54.0&lang=sv&theme=light")]
    [InlineData("sv", "dark", "?from=0.52.0&to=0.54.0&lang=sv&theme=dark")]
    [InlineData("en", "light", "?from=0.52.0&to=0.54.0&lang=en&theme=light")]
    [InlineData("en", "dark", "?from=0.52.0&to=0.54.0&lang=en&theme=dark")]
    [InlineData("en", null, "?from=0.52.0&to=0.54.0&lang=en")]
    [InlineData("en", "system", "?from=0.52.0&to=0.54.0&lang=en")]
    public void TheBrowserLinkCarriesLanguageAndTheme(string lang, string? theme, string query)
    {
        Assert.Equal(Page + query, UpdateLogic.ReleaseNotesUrl(V("0.52.0"), V("0.54.0"), lang, theme));
        Loc.Use(lang);
        var vm = new ReleaseNotesViewModel(new FakeSource(Embedded, null), new ReleaseRange(V("0.52.0"), V("0.54.0")));
        Assert.Equal(Page + query.Replace("lang=" + lang, "lang=" + Loc.Language), vm.BrowserUrl(theme));
    }

    [Fact]
    public void TheResolvedThemeIsLightOrDark()
    {
        Assert.Equal("dark", ReleaseNotesWindow.ResolvedTheme(ThemeVariant.Dark));
        Assert.Equal("light", ReleaseNotesWindow.ResolvedTheme(ThemeVariant.Light));
        Assert.Equal("light", ReleaseNotesWindow.ResolvedTheme(null));
    }

    [Fact]
    public void ShowMoreAndTheVersionLinkOpenTheSameRangeAsThePage()
    {
        var offered = new ReleaseInfo("v0.14.0", V("0.14.0"), false, UpdateLogic.ReleasesPage, []);
        Assert.Equal(new ReleaseRange(V("0.13.2"), V("0.14.0")), UpdateLogic.MoreRange(offered, V("0.13.2"), V("0.12.0")));
        Assert.Equal(new ReleaseRange(V("0.53.1"), V("0.54.0")), UpdateLogic.MoreRange(null, V("0.54.0"), V("0.53.1")));
        Assert.Equal(new ReleaseRange(null, V("0.54.0")), UpdateLogic.MoreRange(null, V("0.54.0"), null));
        Assert.Equal(ReleaseRange.All, UpdateLogic.MoreRange(null, V("0.0.0"), null));
        Assert.Equal(new ReleaseRange(null, V("0.14.0")), UpdateLogic.MoreRange(offered, V("0.0.0"), null));
        foreach (var (r, c, u) in new[] { (offered, V("0.13.2"), (AppVersion?)null), (null, V("0.54.0"), V("0.53.1")), (null, V("0.0.0"), null) })
            Assert.Equal(UpdateLogic.MoreUrl(r, c, u, "en"), UpdateLogic.ReleaseNotesUrl(UpdateLogic.MoreRange(r, c, u), "en"));
    }

    [Theory]
    [InlineData("sv", "dark")]
    [InlineData("en", "light")]
    public void TheWindowRendersHeadingsLinksAndTheBrowserButton(string language, string theme)
    {
        var (headings, browser, chips, expanders) = UiSession.Run(() =>
        {
            Loc.Use(language);
            var app = Avalonia.Application.Current!;
            var before = app.RequestedThemeVariant;
            app.RequestedThemeVariant = theme == "dark" ? ThemeVariant.Dark : ThemeVariant.Light;
            var vm = new ReleaseNotesViewModel(new FakeSource(Embedded, null), new ReleaseRange(V("0.52.0"), V("0.54.0")));
            var window = new ReleaseNotesWindow(vm);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            try
            {
                var texts = window.GetVisualDescendants().OfType<TextBlock>()
                    .Where(t => Avalonia.Automation.AutomationProperties.GetHeadingLevel(t) > 0 && t.IsEffectivelyVisible).Select(t => t.Text).ToList();
                var button = window.FindControl<Button>("BrowserButton")!;
                var links = window.GetVisualDescendants().OfType<Button>().Count(b => b.Classes.Contains("chip") && b.Focusable);
                var expanderCount = window.GetVisualDescendants().OfType<Expander>().Count(e => !e.IsExpanded);
                return (texts, (button.Content as string, ReleaseNotesWindow.ResolvedTheme(window.ActualThemeVariant)), links, expanderCount);
            }
            finally
            {
                window.Close();
                app.RequestedThemeVariant = before;
            }
        });
        Assert.Contains("BoosterAI 0.54.0", headings);
        Assert.Contains(Loc.Table(language)["news.overview"], headings);
        Assert.Contains(language == "sv" ? "Animerad BoosterAI-splash i flikarna" : "Animated BoosterAI splash in tabs", headings);
        Assert.Equal(Loc.Table(language)["news.browser"], browser.Item1);
        Assert.Equal(theme, browser.Item2);
        Assert.True(chips >= 6);
        Assert.Equal(3, expanders);
    }
}
