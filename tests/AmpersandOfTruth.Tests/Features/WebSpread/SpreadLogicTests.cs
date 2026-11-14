using AmpersandOfTruth.Web.Features.Investigations;
using AmpersandOfTruth.Web.Features.Evidence;
using AmpersandOfTruth.Web.Features.WebSpread;
using AmpersandOfTruth.Web.Features.WebSpread.Dating;
using AmpersandOfTruth.Web.Features.WebSpread.Matching;
using AmpersandOfTruth.Web.Features.WebSpread.Planning;
using AmpersandOfTruth.Web.Features.WebSpread.Spread;
using static AmpersandOfTruth.Tests.Features.WebSpread.WebSpreadKit;

namespace AmpersandOfTruth.Tests.Features.WebSpread;

/// <summary>P3.2 windows, P3.4 lexical matching, P3.5 dating, P3.6 buckets — the pure logic.</summary>
public sealed class SpreadLogicTests
{
    private static readonly DateOnly Today = new(2026, 11, 14);

    private static IncidentRecord Incident(params IncidentEdit[] edits) => new()
    {
        PageId = 1, InsertingRevIds = [1], RemovingRevIds = [2], Tier = EvidenceTier.Reverted, Score = 1, Edits = edits,
        FirstInsertedAt = Inserted, LastRemovedAt = Removed,
    };

    // ── exposure windows ──

    [Fact]
    public void Insert_remove_reinsert_remove_gives_two_windows()
    {
        var reinserted = new DateTimeOffset(2015, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var removedAgain = new DateTimeOffset(2015, 1, 3, 0, 0, 0, TimeSpan.Zero);
        var windows = ExposureWindows.From(Incident(
            Edit(1, Inserted, IncidentEditRole.Inserted), Edit(2, Removed, IncidentEditRole.Removed),
            Edit(3, reinserted, IncidentEditRole.Reinserted), Edit(4, removedAgain, IncidentEditRole.Removed)));

        Assert.Equal([new ExposureWindow(Inserted, Removed), new ExposureWindow(reinserted, removedAgain)], windows);
    }

    [Fact]
    public void Never_removed_claim_has_an_open_window_and_no_after_search()
    {
        var windows = ExposureWindows.From(Incident(Edit(1, Inserted, IncidentEditRole.Inserted)));
        Assert.Equal([new ExposureWindow(Inserted, null)], windows);

        var search = ExposureWindows.SearchWindows(windows, Today);
        Assert.Equal([SearchWindowKind.Before, SearchWindowKind.During], search.Select(s => s.Kind));
        Assert.Equal(new DateWindow(null, new DateOnly(2014, 2, 28)), search[0].Window);
        Assert.Equal(new DateWindow(new DateOnly(2014, 3, 1), Today), search[1].Window);
    }

    [Fact]
    public void Edits_without_timestamps_fall_back_to_first_and_last()
    {
        var windows = ExposureWindows.From(Incident(Edit(1, null, IncidentEditRole.Inserted)));
        Assert.Equal([new ExposureWindow(Inserted, Removed)], windows);

        var search = ExposureWindows.SearchWindows(windows, Today);
        Assert.Equal(new DateWindow(new DateOnly(2014, 3, 21), Today), search[2].Window);
    }

    // ── buckets ──

    [Theory]
    [InlineData("2013-12-31", SpreadBucket.Before, false)]
    [InlineData("2014-02-28", SpreadBucket.Before, true)]
    [InlineData("2014-03-01", SpreadBucket.During, true)]
    [InlineData("2014-03-10", SpreadBucket.During, false)]
    [InlineData("2014-03-20", SpreadBucket.During, true)]
    [InlineData("2014-03-21", SpreadBucket.AfterRemoval, true)]
    [InlineData("2014-12-30", SpreadBucket.AfterRemoval, false)]
    [InlineData("2014-12-31", SpreadBucket.AfterRemoval, true)]    // a day before the re-insertion
    [InlineData("2015-01-02", SpreadBucket.During, true)]    // inside the (short) second window
    [InlineData("2015-06-01", SpreadBucket.AfterRemoval, false)]
    public void Bucket_by_day_across_several_windows(string date, SpreadBucket expected, bool near)
    {
        IReadOnlyList<ExposureWindow> windows =
        [
            new(Inserted, Removed),
            new(new DateTimeOffset(2015, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2015, 1, 3, 0, 0, 0, TimeSpan.Zero)),
        ];

        var (bucket, nearBoundary) = SpreadClassifier.Bucket(DateOnly.Parse(date), windows, Today);

        Assert.Equal(expected, bucket);
        Assert.Equal(near, nearBoundary);
    }

    [Fact]
    public void Undated_is_never_forced_into_a_bucket()
    {
        Assert.Equal(SpreadBucket.Undated, SpreadClassifier.Bucket(null, [new(Inserted, Removed)], Today).Bucket);
    }

    [Fact]
    public void Timing_alone_never_upgrades_to_likely_from_wikipedia()
    {
        var independent = new ClaimMatch(Stance.Asserts, MatchKind.SameClaimDifferentWording, "q", false, SourceType.News, 0.1, null);
        var copy = independent with { MatchType = MatchKind.VerbatimCopy };
        var credited = independent with { CitesWikipedia = true };

        Assert.False(SpreadClassifier.LikelyFromWikipedia(SpreadBucket.AfterRemoval, independent));
        Assert.True(SpreadClassifier.LikelyFromWikipedia(SpreadBucket.AfterRemoval, copy));
        Assert.True(SpreadClassifier.LikelyFromWikipedia(SpreadBucket.During, credited));
        Assert.False(SpreadClassifier.LikelyFromWikipedia(SpreadBucket.Before, copy));
        Assert.False(SpreadClassifier.LikelyFromWikipedia(SpreadBucket.During, copy with { Stance = Stance.Refutes }));
    }

    [Fact]
    public void Headline_counts_before_after_reuse_and_refutations()
    {
        WebSpreadSource S(SpreadBucket b, Stance st = Stance.Asserts, bool reuse = false) => new(
            "https://x.example", "x.example", null, SourceType.News, st, MatchKind.None, null, false, b, false, reuse,
            PageDating.Undated, []);

        var headline = SpreadClassifier.Headline(
            [S(SpreadBucket.Before), S(SpreadBucket.During, reuse: true), S(SpreadBucket.AfterRemoval), S(SpreadBucket.During, Stance.Refutes)],
            12, ["fi", "en"]);

        Assert.Equal("Found on 1 site before the Wikipedia edit and 2 sites after it; 1 of the later ones reuse Wikipedia's wording or credit it. One site argues against it.", headline);
        Assert.Equal("No page carrying this claim was found (12 searches in Finnish and English).", SpreadClassifier.Headline([], 12, ["fi", "en"]));
        Assert.Equal("Found on 1 site after the Wikipedia edit. One site argues against it.",
            SpreadClassifier.Headline([S(SpreadBucket.AfterRemoval), S(SpreadBucket.During, Stance.Refutes)], 6, ["fi", "en"]));
    }

    // ── lexical matching ──

    [Fact]
    public void Verbatim_copy_is_found_and_quoted_from_the_page()
    {
        var page = "Otsikko\n\nMonet uskovat, että Suomi aloitti talvisodan ampumalla Mainilan kylään, minkä vuoksi Neuvostoliiton oli pakko puolustautua. Muuta tekstiä.";

        var m = LexicalMatcher.Match(InsertedText, page);

        Assert.Equal(MatchKind.VerbatimCopy, m.Type);
        Assert.Equal(1.0, m.Containment);
        Assert.StartsWith("Suomi aloitti talvisodan", m.Passage);
        Assert.Contains(m.Passage!, page);
    }

    [Fact]
    public void Partial_reuse_is_a_close_paraphrase_and_a_topic_page_is_no_match()
    {
        var reworded = "Lehden mukaan Suomi aloitti talvisodan ampumalla Mainilan kylään, minkä vuoksi Neuvostoliitto joutui puolustautumaan.";
        var topic = "Talvisota alkoi 30. marraskuuta 1939, kun Neuvostoliitto hyökkäsi Suomeen.";

        Assert.Equal(MatchKind.CloseParaphrase, LexicalMatcher.Match(InsertedText, reworded).Type);
        Assert.Equal(MatchKind.None, LexicalMatcher.Match(InsertedText, topic).Type);
    }

    [Fact]
    public void Best_passage_finds_the_part_of_a_long_page_about_the_claim()
    {
        var page = string.Join(' ', Enumerable.Repeat("Kalastus on suosittu harrastus järvillä.", 80))
                   + " Mainilan laukaukset ammuttiin 26. marraskuuta 1939. "
                   + string.Join(' ', Enumerable.Repeat("Sienestys on syksyn iloja.", 80));

        var passage = LexicalMatcher.BestPassage(page, ["Mainilan laukaukset ammuttiin"], chars: 200);

        Assert.Contains("Mainilan laukaukset", passage);
    }

    // ── distinctive fragment ──

    [Fact]
    public void Fragment_prefers_rare_content_and_never_starts_on_a_function_word()
    {
        var fragment = DistinctiveFragment.Pick(InsertedText)!;

        var words = fragment.Split(' ');
        Assert.InRange(words.Length, 5, 8);
        Assert.Contains(fragment, InsertedText.Replace(",", ""));
        Assert.DoesNotContain(words[0].ToLowerInvariant(), new[] { "ja", "että", "minkä" });
        Assert.Null(DistinctiveFragment.Pick("Liian lyhyt."));
        Assert.Equal("Mannerheim oli Venäjän armeijan kenraali", DistinctiveFragment.Pick("Mannerheim oli Venäjän armeijan kenraali."));
    }

    // ── dating ──

    [Fact]
    public void Json_ld_published_wins_and_modified_is_kept_only_as_a_candidate()
    {
        var html = Html("x", """<script type="application/ld+json">{"@graph":[{"@type":"NewsArticle","datePublished":"2014-03-05T08:00:00+02:00","dateModified":"2019-01-01"}]}</script>""");

        var d = PageDateExtractor.Extract("https://news.example/a", html);

        Assert.Equal(new DateOnly(2014, 3, 5), d.PublishedAt);
        Assert.Equal(DateMethod.JsonLdPublished, d.Method);
        Assert.Equal(DateConfidence.Medium, d.Confidence);
        Assert.Contains(d.Candidates, c => c.Method == DateMethod.Modified && c.Date == new DateOnly(2019, 1, 1));
        Assert.Empty(d.Conflicts);
    }

    [Fact]
    public void Meta_tags_in_either_attribute_order_and_url_dates_are_read()
    {
        var html = Html("x", """<meta content="2016-05-02T10:00:00Z" property="article:published_time"><meta name="DC.date.issued" content="2016-05-02">""");

        var d = PageDateExtractor.Extract("https://blog.example/2016/05/02/kirjoitus", html);

        Assert.Equal(new DateOnly(2016, 5, 2), d.PublishedAt);
        Assert.Equal(DateMethod.MetaPublished, d.Method);
        Assert.Contains(d.Candidates, c => c.Method == DateMethod.UrlPath);
    }

    [Fact]
    public void Disagreeing_dates_are_reported_and_lower_confidence()
    {
        var html = Html("x", """<meta property="article:published_time" content="2016-05-02">""");

        var d = PageDateExtractor.Extract("https://site.example/2014/03/old-story", html);

        Assert.Equal(new DateOnly(2016, 5, 2), d.PublishedAt);
        Assert.Equal(DateConfidence.Low, d.Confidence);
        Assert.Equal(["the URL says 2014-03-01, page metadata says 2016-05-02"], d.Conflicts);
    }

    [Fact]
    public void Archive_capture_is_an_upper_bound_on_publication()
    {
        var html = Html("x", """<meta property="article:published_time" content="2020-01-01">""");

        var d = PageDateExtractor.Extract("https://site.example/a", html, archiveFirstCapture: new DateOnly(2014, 4, 1));

        Assert.Equal(new DateOnly(2014, 4, 1), d.PublishedAt);
        Assert.Equal(DateMethod.ArchiveFirstCapture, d.Method);
    }

    [Fact]
    public void Only_a_serp_date_is_low_confidence_and_nothing_is_undated()
    {
        Assert.Equal(DateConfidence.Low, PageDateExtractor.Extract("https://a.example/x", "<p>x</p>", new DateOnly(2014, 1, 1)).Confidence);
        Assert.Null(PageDateExtractor.Extract("https://a.example/x", "<p>x</p>").PublishedAt);
    }

    // ── url policy ──

    [Theory]
    [InlineData("https://fi.wikipedia.org/wiki/Talvisota", UrlKind.Wikipedia)]
    [InlineData("https://www.wikiwand.com/fi/Talvisota", UrlKind.Mirror)]
    [InlineData("https://yle.fi/a/3-123", UrlKind.Page)]
    [InlineData("https://example.com/doc.PDF", UrlKind.File)]
    [InlineData("mailto:x@example.com", UrlKind.Invalid)]
    public void Url_kinds(string url, UrlKind kind) => Assert.Equal(kind, UrlPolicy.Classify(url));

    [Fact]
    public void Normalize_drops_www_trailing_slash_fragment_and_tracking()
    {
        Assert.Equal("https://yle.fi/a/3-123?id=5", UrlPolicy.Normalize("https://www.yle.fi/a/3-123/?utm_source=x&id=5#top"));
    }
}
