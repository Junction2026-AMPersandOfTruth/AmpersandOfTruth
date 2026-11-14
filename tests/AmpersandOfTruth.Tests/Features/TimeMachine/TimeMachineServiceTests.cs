using AmpersandOfTruth.Web.Features.TimeMachine;
using AmpersandOfTruth.Web.Features.WikiDiff;

namespace AmpersandOfTruth.Tests.Features.TimeMachine;

public sealed class TimeMachineServiceTests
{
    private const long PageId = 1256;

    private static FakeTimeMachineCorpus BuildSampleCorpus()
    {
        var corpus = new FakeTimeMachineCorpus()
            .WithTitle(PageId, "Talvisota", "Winter War");

        corpus.AddRevision(
            pageId: PageId,
            revId: 100,
            parentId: null,
            tsUtc: new DateTime(2014, 1, 10, 12, 0, 0, DateTimeKind.Utc),
            user: "Ensimmainen",
            anonymous: false,
            wikitext: "'''Talvisota''' alkoi 30. marraskuuta 1939.");

        corpus.AddRevision(
            pageId: PageId,
            revId: 101,
            parentId: 100,
            tsUtc: new DateTime(2014, 3, 1, 14, 30, 0, DateTimeKind.Utc),
            user: "85.76.123.45",
            anonymous: true,
            wikitext: "'''Talvisota''' alkoi 30. marraskuuta 1939. Suomi aloitti sodan ampumalla Mainilan kylään.");

        corpus.AddRevision(
            pageId: PageId,
            revId: 102,
            parentId: 101,
            tsUtc: new DateTime(2014, 3, 5, 9, 15, 0, DateTimeKind.Utc),
            user: "Palauttaja",
            anonymous: false,
            wikitext: """
                '''Talvisota''' alkoi 30. marraskuuta 1939.

                == Tausta ==
                Neuvostoliitto hyökkäsi Suomeen.
                """);

        return corpus;
    }

    [Fact]
    public async Task Exact_timestamp_match_returns_that_revision()
    {
        var corpus = BuildSampleCorpus();
        var service = new TimeMachineService(corpus);

        var exactTs = new DateTime(2014, 3, 1, 14, 30, 0, DateTimeKind.Utc);
        var snapshot = await service.ReadAtAsync(PageId, atUtc: exactTs);

        Assert.NotNull(snapshot);
        Assert.False(snapshot.BeforeFirst);
        Assert.NotNull(snapshot.Revision);
        Assert.Equal(101, snapshot.Revision.RevId);
        Assert.Contains("Suomi aloitti sodan ampumalla Mainilan kylään.", snapshot.Prose);
    }

    [Fact]
    public async Task Timestamp_between_revisions_returns_the_earlier_revision()
    {
        var corpus = BuildSampleCorpus();
        var service = new TimeMachineService(corpus);

        var between = TimeMachineService.EndOfUtcDay(new DateOnly(2014, 3, 3));
        var snapshot = await service.ReadAtAsync(PageId, atUtc: between);

        Assert.NotNull(snapshot);
        Assert.False(snapshot.BeforeFirst);
        Assert.NotNull(snapshot.Revision);
        Assert.Equal(101, snapshot.Revision.RevId);
        Assert.Equal(new DateOnly(2014, 3, 3), snapshot.SelectedDate);
    }

    [Fact]
    public async Task Timestamp_before_first_revision_returns_before_first_null_revision_state()
    {
        var corpus = BuildSampleCorpus();
        var service = new TimeMachineService(corpus);

        var beforeFirst = TimeMachineService.EndOfUtcDay(new DateOnly(2013, 12, 31));
        var snapshot = await service.ReadAtAsync(PageId, atUtc: beforeFirst);

        Assert.NotNull(snapshot);
        Assert.True(snapshot.BeforeFirst);
        Assert.Null(snapshot.Revision);
        Assert.Empty(snapshot.Prose);
        Assert.Empty(snapshot.Blocks);
        Assert.Equal(3, snapshot.Range.Count);
    }

    [Fact]
    public async Task Unknown_article_returns_null()
    {
        var corpus = BuildSampleCorpus();
        var service = new TimeMachineService(corpus);

        var snapshot = await service.ReadAtAsync(999_999);
        Assert.Null(snapshot);
    }

    [Fact]
    public async Task Highlight_found_in_prose_marks_inserted_span_in_blocks()
    {
        var corpus = BuildSampleCorpus();
        var service = new TimeMachineService(corpus);

        var snapshot = await service.ReadAtAsync(
            pageId: PageId,
            atUtc: new DateTime(2014, 3, 1, 14, 30, 0, DateTimeKind.Utc),
            revId: 101,
            highlightRevId: 101,
            highlightParentRevId: 100);

        Assert.NotNull(snapshot);
        Assert.NotNull(snapshot.Highlight);
        Assert.True(snapshot.Highlight.FoundInProse);
        Assert.Null(snapshot.Highlight.FallbackText);
        Assert.NotEmpty(snapshot.Highlight.Ranges);

        var highlightedText = string.Concat(
            snapshot.Blocks
                .SelectMany(b => b.Pieces)
                .Where(p => p.Highlighted)
                .Select(p => p.Text));

        Assert.Equal("Suomi aloitti sodan ampumalla Mainilan kylään.", highlightedText);
    }

    [Fact]
    public async Task Highlight_not_found_in_prose_falls_back_to_extracted_passage()
    {
        var corpus = BuildSampleCorpus();
        // Add an infobox-only insertion whose value is in WikitextDiff.AddedProse, not in WikitextProse.Of().Prose
        corpus.AddRevision(
            pageId: PageId,
            revId: 103,
            parentId: 102,
            tsUtc: new DateTime(2014, 4, 1, 10, 0, 0, DateTimeKind.Utc),
            user: "InfoboxEditor",
            anonymous: false,
            wikitext: """
                {{Sota
                | nimi = Talvisota
                | tulos = Kiistanalainen väite infolaatikossa
                }}
                '''Talvisota''' alkoi 30. marraskuuta 1939.
                """);

        var service = new TimeMachineService(corpus);

        // Case 1: Inserted text lives inside multi-line infobox template (not in running prose)
        var infoboxSnap = await service.ReadAtAsync(
            pageId: PageId,
            atUtc: new DateTime(2014, 4, 1, 10, 0, 0, DateTimeKind.Utc),
            revId: 103,
            highlightRevId: 103,
            highlightParentRevId: 102);

        Assert.NotNull(infoboxSnap?.Highlight);
        Assert.False(infoboxSnap.Highlight.FoundInProse);
        Assert.Empty(infoboxSnap.Highlight.Ranges);
        Assert.Contains("Kiistanalainen väite infolaatikossa", infoboxSnap.Highlight.FallbackText);

        // Case 2: Viewing rev 102 (after rev 101's claim was removed) while highlighting rev 101
        var removedSnap = await service.ReadAtAsync(
            pageId: PageId,
            atUtc: new DateTime(2014, 3, 5, 9, 15, 0, DateTimeKind.Utc),
            revId: 102,
            highlightRevId: 101,
            highlightParentRevId: 100);

        Assert.NotNull(removedSnap?.Highlight);
        Assert.False(removedSnap.Highlight.FoundInProse);
        Assert.Equal("Suomi aloitti sodan ampumalla Mainilan kylään.", removedSnap.Highlight.FallbackText);
    }

    [Fact]
    public async Task Prose_is_capped_at_max_chars_and_compare_builds_diff_between_dates()
    {
        var corpus = new FakeTimeMachineCorpus();
        var longParagraph = string.Join(' ', Enumerable.Repeat("Talvisodan historiaa käsittelevä lause.", 1_200));
        Assert.True(longParagraph.Length > TimeMachineService.MaxProseChars);

        corpus.AddRevision(
            pageId: 77,
            revId: 1,
            parentId: null,
            tsUtc: new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            user: "EditorA",
            anonymous: false,
            wikitext: "Ensimmäinen versio artikkelista.");

        corpus.AddRevision(
            pageId: 77,
            revId: 2,
            parentId: 1,
            tsUtc: new DateTime(2020, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            user: "EditorB",
            anonymous: false,
            wikitext: longParagraph);

        var service = new TimeMachineService(corpus);
        var snap = await service.ReadAtAsync(
            pageId: 77,
            atUtc: TimeMachineService.EndOfUtcDay(new DateOnly(2020, 6, 1)),
            compareDate: new DateOnly(2020, 1, 1));

        Assert.NotNull(snap);
        Assert.True(snap.IsTruncated);
        Assert.Equal(TimeMachineService.MaxProseChars, snap.Prose.Length);
        Assert.NotNull(snap.Comparison);
        Assert.Equal(2, snap.Comparison.RevisionA?.RevId);
        Assert.Equal(1, snap.Comparison.RevisionB?.RevId);
        Assert.NotEmpty(snap.Comparison.Passage.Diff.Segments);
    }

    [Fact]
    public async Task Minimal_three_method_ITimeMachineCorpus_implementation_works()
    {
        var minimal = new MinimalThreeMethodCorpus();
        var service = new TimeMachineService(minimal);

        var snap = await service.ReadAtAsync(
            pageId: 5,
            atUtc: new DateTime(2018, 5, 1, 12, 0, 0, DateTimeKind.Utc),
            highlightRevId: 20,
            highlightParentRevId: 10);

        Assert.NotNull(snap);
        Assert.Equal(20, snap.Revision?.RevId);
        Assert.NotNull(snap.Highlight);
        Assert.True(snap.Highlight.FoundInProse);
        Assert.Equal("Lisätty kappale.", snap.Highlight.Ranges.Select(r => snap.Prose.Substring(r.Start, r.Length)).Single());
    }

    private sealed class MinimalThreeMethodCorpus : ITimeMachineCorpus
    {
        public Task<RevisionAt?> RevisionAtAsync(long pageId, DateTime atUtc, CancellationToken ct = default) =>
            Task.FromResult<RevisionAt?>(
                pageId == 5 && atUtc >= new DateTime(2018, 5, 1, 0, 0, 0, DateTimeKind.Utc)
                    ? new RevisionAt(5, 20, 10, new DateTime(2018, 5, 1, 10, 0, 0, DateTimeKind.Utc), "UserX", false, 40)
                    : null);

        public Task<(DateTime First, DateTime Last, int Count)?> RangeAsync(long pageId, CancellationToken ct = default) =>
            Task.FromResult<(DateTime, DateTime, int)?>(
                pageId == 5
                    ? (new DateTime(2018, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2018, 5, 1, 10, 0, 0, DateTimeKind.Utc), 2)
                    : null);

        public Task<string?> TextAsync(long pageId, long revId, CancellationToken ct = default) =>
            Task.FromResult<string?>((pageId, revId) switch
            {
                (5, 10) => "Alkuperäinen teksti.",
                (5, 20) => "Alkuperäinen teksti. Lisätty kappale.",
                _ => null,
            });
    }
}
