using AmpersandOfTruth.Web.Features.Evidence;
using AmpersandOfTruth.Web.Features.Investigations;
using AmpersandOfTruth.Web.Features.Investigations.Pipeline;
using AmpersandOfTruth.Web.Features.Investigations.View;

namespace AmpersandOfTruth.Tests.Features.Investigations.View;

public class IncidentMapperTests
{
    private readonly FakeIncidentCorpus _corpus = new();
    private readonly FakeRevisionStore _revisions = new();
    private readonly IncidentMapper _mapper;

    public IncidentMapperTests()
    {
        _corpus.Articles[1256] = new ArticleRef(1256, "Talvisota", "Winter War");
        _mapper = new IncidentMapper(_corpus, _revisions);
    }

    [Fact]
    public async Task Insertion_wikitext_maps_passage_and_Insertion_kind()
    {
        SeedEdit(
            parentRevId: 100,
            insertRevId: 101,
            removeRevId: 102,
            before: "Talvisota alkoi marraskuussa 1939.",
            after: "Talvisota alkoi marraskuussa 1939. Suomi hyökkäsi ensin Neuvostoliittoon.");

        var view = await _mapper.MapAsync(SampleRecord(101, 102), rank: 1);

        Assert.NotNull(view.Passage);
        Assert.Equal(101, view.Passage.RevId);
        Assert.Equal(100, view.Passage.ParentRevId);
        Assert.Equal(IncidentKind.Insertion, view.Kind);
        Assert.Contains("Suomi hyökkäsi ensin", view.Passage.Diff.AddedProse);
        Assert.Equal(new ArticleRef(1256, "Talvisota", "Winter War"), view.PrimaryArticle);
    }

    [Fact]
    public async Task Deletion_wikitext_maps_Deletion_kind()
    {
        SeedEdit(
            parentRevId: 200,
            insertRevId: 201,
            removeRevId: 202,
            before: "Talvisota alkoi marraskuussa 1939. Neuvostoliitto pommitti Helsinkiä sodan ensimmäisenä päivänä.",
            after: "Talvisota alkoi marraskuussa 1939.");

        var view = await _mapper.MapAsync(SampleRecord(201, 202), rank: 1);

        Assert.NotNull(view.Passage);
        Assert.Equal(IncidentKind.Deletion, view.Kind);
        Assert.Contains("Neuvostoliitto pommitti Helsinkiä", view.Passage.Diff.RemovedProse);
    }

    [Fact]
    public async Task Citation_swap_wikitext_maps_CitationSwap_kind()
    {
        SeedEdit(
            parentRevId: 300,
            insertRevId: 301,
            removeRevId: 302,
            before: "Talvisota alkoi marraskuussa 1939.<ref>{{Kirjaviite | Tekijä = Tanner | Vuosi = 1950}}</ref>",
            after: "Talvisota alkoi marraskuussa 1939.<ref>https://example.org/blog/totuus</ref>");

        var view = await _mapper.MapAsync(SampleRecord(301, 302), rank: 1);

        Assert.NotNull(view.Passage);
        Assert.Equal(IncidentKind.CitationSwap, view.Kind);
        Assert.Equal(1, view.Passage.Diff.Stats.AddedRefs);
        Assert.Equal(1, view.Passage.Diff.Stats.RemovedRefs);
    }

    [Fact]
    public async Task Number_change_wikitext_maps_NumberChange_kind()
    {
        SeedEdit(
            parentRevId: 400,
            insertRevId: 401,
            removeRevId: 402,
            before: "Talvisodassa kaatui 25 904 suomalaista sotilasta.",
            after: "Talvisodassa kaatui 125 904 suomalaista sotilasta.");

        var view = await _mapper.MapAsync(SampleRecord(401, 402), rank: 1);

        Assert.NotNull(view.Passage);
        Assert.Equal(IncidentKind.NumberChange, view.Kind);
    }

    [Fact]
    public async Task Short_digit_segments_with_range_to_approximate_qualifier_map_NumberChange_before_Deletion()
    {
        SeedEdit(
            parentRevId: 410,
            insertRevId: 411,
            removeRevId: 412,
            before: "Tappiot olivat 1 000–1 500 miestä.",
            after: "Tappiot olivat n. 2000 miestä.");

        var proseView = await _mapper.MapAsync(SampleRecord(411, 412), rank: 1);
        Assert.NotNull(proseView.Passage);
        Assert.Equal(IncidentKind.NumberChange, proseView.Kind);

        SeedEdit(
            parentRevId: 420,
            insertRevId: 421,
            removeRevId: 422,
            before: "{{Sota\n| tappiot2 = 1 000–1 500\n}}",
            after: "{{Sota\n| tappiot2 = n. 2000\n}}");

        var infoboxView = await _mapper.MapAsync(SampleRecord(421, 422), rank: 2);
        Assert.NotNull(infoboxView.Passage);
        Assert.Equal(IncidentKind.NumberChange, infoboxView.Kind);
    }

    [Fact]
    public async Task Masks_ip_editor_and_edit_comments_and_builds_fallback_tier_reason()
    {
        var t0 = new DateTime(2024, 3, 3, 14, 0, 0, DateTimeKind.Utc);
        _corpus.Revisions.AddRange([
            new RevisionRow(1256, 501, 500, t0, "85.76.123.45", "Lähde 192.0.2.99 mukaan", 120),
            new RevisionRow(1256, 502, 501, t0.AddMinutes(15), "Matti Meikäläinen", "Kumottu muokkaus [[Special:Contributions/85.76.123.45|85.76.123.45]]", 100),
        ]);
        _revisions.Texts[(1256, 500)] = "Alkuperäinen teksti.";
        _revisions.Texts[(1256, 501)] = "Alkuperäinen teksti. Lisätty väite ilman lähdettä.";

        var record = SampleRecord(501, 502) with
        {
            Tier = EvidenceTier.Contested,
            TierReason = null,
            Signals =
            [
                new IncidentSignal("removal_reason", "removed as inaccurate", "väärin"),
                new IncidentSignal("reinserted", null, null),
            ],
            Assessment = new IncidentAssessment(
                ContentCategory.FalseFactualClaim,
                Confidence: 4,
                WhatChangedEn: null,
                Quotes: ["Lisätty väite"],
                HasVerifiedQuote: true,
                UncertaintyNote: "Context short",
                HistoricalNoteEn: "Historical fallback note."),
        };

        var view = await _mapper.MapAsync(record, rank: 2);

        Assert.Equal(2, view.Rank);
        Assert.Equal(
            "Disputed and removed \u2014 because: removal reason given, put back after removal.",
            view.TierReason);

        Assert.Equal(2, view.Edits.Count);
        var ins = view.Edits[0];
        Assert.Equal("85.76.x.x (anonymous)", ins.Editor.PlainText);
        Assert.True(ins.Editor.IsAnonymous);
        Assert.Null(ins.Editor.ProfileUrl);
        Assert.Equal("Lähde 192.0.x.x mukaan", ins.Summary);
        Assert.Equal(TimeSpan.FromMinutes(15), ins.Live);

        var rem = view.Edits[1];
        Assert.Equal("Matti Meikäläinen", rem.Editor.PlainText);
        Assert.NotNull(rem.Editor.ProfileUrl);
        Assert.DoesNotContain("85.76.123.45", rem.Summary);
        Assert.Null(rem.Live);

        Assert.Equal(TimeSpan.FromMinutes(15), view.TotalLive);
        Assert.NotNull(view.Assessment);
        Assert.Equal("Historical fallback note.", view.Assessment.Reasoning);
        Assert.Equal("Context short", view.Assessment.Uncertainty);

        // No-signals fallback uses "taken out by another editor".
        var noSignals = await _mapper.MapAsync(SampleRecord(501, 502) with { Id = "no-sig", Signals = [], TierReason = null }, rank: 1);
        Assert.Equal("Removed by Wikipedia editors \u2014 because: taken out by another editor.", noSignals.TierReason);
    }

    [Fact]
    public async Task Corpus_down_degrades_to_record_fields_without_throwing_and_caches_only_once_passage_is_found()
    {
        _corpus.Unavailable = true;
        _revisions.Unavailable = true;

        var tIns = new DateTimeOffset(2024, 5, 1, 10, 0, 0, TimeSpan.Zero);
        var tRem = tIns.AddMinutes(30);

        var record = new IncidentRecord
        {
            PageId = 1256,
            InsertingRevIds = [601],
            RemovingRevIds = [602],
            Tier = EvidenceTier.Reverted,
            Score = 9.5,
            OtherPageIds = [9999],
            ClaimEn = "Claim headline",
            ClaimFi = "Väite",
            Articles = [new IncidentArticle(1256, "Talvisota (fallback)", "Winter War (fallback)")],
            Edits =
            [
                new IncidentEdit(602, 601, 0, "Reviewer", false, tRem, IncidentEditRole.Removed, "Poistettu", null, []),
                new IncidentEdit(601, 600, 0, "192.0.x.x (anonymous)", true, tIns, IncidentEditRole.Inserted, "Lisäys", null, []),
            ],
        };

        var degraded = await _mapper.MapAsync(record, rank: 1);

        Assert.Equal("1256-601", degraded.Id);
        Assert.Null(degraded.Passage);
        Assert.Equal(IncidentKind.Insertion, degraded.Kind);
        Assert.Equal(
            [
                new ArticleRef(1256, "Talvisota (fallback)", "Winter War (fallback)"),
                new ArticleRef(9999, "Page 9999", null),
            ],
            degraded.Articles);
        Assert.Equal([601L, 602L], degraded.Edits.Select(e => e.RevId)); // sorted by timestamp
        Assert.Equal("192.0.x.x (anonymous)", degraded.Edits[0].Editor.PlainText);
        Assert.Null(degraded.Edits[0].Editor.ProfileUrl);
        Assert.Equal("ip4:192.0", degraded.Edits[0].Editor.GroupingKey);
        Assert.Equal(TimeSpan.FromMinutes(30), degraded.Edits[0].Live);
        Assert.Equal(tIns, degraded.FirstSeen);
        Assert.Equal(tRem, degraded.LastRemoved);
        Assert.Equal(TimeSpan.FromMinutes(30), degraded.TotalLive);

        // Now bring corpus up: because Passage was null, the degraded view was NOT cached.
        _corpus.Unavailable = false;
        _revisions.Unavailable = false;
        SeedEdit(600, 601, 602, "Ennen.", "Ennen. Uusi lause tähän kohtaan.");

        var hydrated = await _mapper.MapAsync(record, rank: 3);
        Assert.NotNull(hydrated.Passage);
        Assert.Equal(3, hydrated.Rank);
        Assert.Equal("Talvisota", hydrated.PrimaryArticle.TitleFi);

        // Subsequent calls hit the cache (even with corpus down again) and update Rank.
        var callsBefore = _corpus.RevisionCalls;
        _corpus.Unavailable = true;
        _revisions.Unavailable = true;

        var fromCache = await _mapper.MapAsync(record, rank: 5);
        Assert.NotNull(fromCache.Passage);
        Assert.Equal(5, fromCache.Rank);
        Assert.Equal(callsBefore, _corpus.RevisionCalls);
    }

    [Fact]
    public async Task MapAsync_fetches_articles_and_revisions_concurrently()
    {
        SeedEdit(700, 701, 702, "Ennen.", "Ennen. Jälkeen.");

        // Both lookups are held open, so the second is in flight only if it was started before the first returned.
        // One after the other would stop at 1 and fail the wait.
        _corpus.Calls.Hold();
        var mapping = _mapper.MapAsync(SampleRecord(701, 702), rank: 1);
        await _corpus.Calls.WaitForAsync(2);
        _corpus.Calls.Release();
        var view = await mapping;

        Assert.NotNull(view.Passage);
        Assert.Equal(1, _corpus.ArticleCalls);
        Assert.Equal(1, _corpus.RevisionCalls);
        Assert.Equal(2, _corpus.Calls.Max);
    }

    private void SeedEdit(long parentRevId, long insertRevId, long removeRevId, string before, string after)
    {
        var ts = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        _corpus.Revisions.AddRange([
            new RevisionRow(1256, insertRevId, parentRevId, ts, "192.0.2.1", "muokkaus", after.Length),
            new RevisionRow(1256, removeRevId, insertRevId, ts.AddMinutes(5), "Rollbacker", "kumottu", before.Length),
        ]);
        _revisions.Texts[(1256, parentRevId)] = before;
        _revisions.Texts[(1256, insertRevId)] = after;
    }

    private static IncidentRecord SampleRecord(long insertRevId, long removeRevId) =>
        new()
        {
            PageId = 1256,
            InsertingRevIds = [insertRevId],
            RemovingRevIds = [removeRevId],
            Tier = EvidenceTier.Reverted,
            Score = 10.0,
        };
}
