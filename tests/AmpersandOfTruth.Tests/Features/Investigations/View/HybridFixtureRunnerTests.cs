using AmpersandOfTruth.Web.Features.Evidence;
using AmpersandOfTruth.Web.Features.Investigations;
using AmpersandOfTruth.Web.Features.Investigations.Fixtures;
using AmpersandOfTruth.Web.Features.Investigations.Pipeline;
using AmpersandOfTruth.Web.Features.Investigations.View;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace AmpersandOfTruth.Tests.Features.Investigations.View;

public class HybridFixtureRunnerTests
{
    private readonly PipelineKit _kit = new();
    private readonly FakeTimeProvider _time = new();

    private static SampleIncidents TwoIncidentSample()
    {
        // Incident A (lower score): matches FakeInvestigationCorpus.WithWinterWar()'s revert 2000 (RevertedRevId = 1999 on page 1256).
        var first = new IncidentRecord
        {
            Id = "1256-1999",
            PageId = 1256,
            InsertingRevIds = [1999],
            RemovingRevIds = [2000],
            Tier = EvidenceTier.Reverted,
            Score = 12.0,
            ClaimEn = "Lower-scored incident matching a scanned revert.",
        };

        // Incident B (higher score): not in scan.Candidates → runner synthesises RemovedEdit from removing_rev_ids[0].
        var second = new IncidentRecord
        {
            Id = "9001-777",
            PageId = 9001,
            InsertingRevIds = [777],
            RemovingRevIds = [778],
            Tier = EvidenceTier.Contested,
            Score = 42.5,
            ClaimEn = "Higher-scored incident with synthetic candidate.",
            Articles = [new IncidentArticle(9001, "Mainilan laukaukset", "Shelling of Mainila")],
        };

        return SampleIncidents.From(new SampleTopic(
            Slug: "winter-war",
            Title: "Winter War",
            Aliases: ["talvisota"],
            Incidents: [first, second]));
    }

    private HybridFixtureRunner BuildRunner(SampleIncidents? samples = null, TimeSpan? delay = null)
    {
        var pipeOpts = Options.Create(_kit.Options);
        var fixOpts = Options.Create(new HybridFixtureOptions { Delay = delay ?? TimeSpan.Zero });
        return new HybridFixtureRunner(
            new VectorTopicSearch(_kit.Embedder, _kit.Corpus, pipeOpts),
            new RevertCandidateSource(_kit.Corpus, _kit.Revisions, pipeOpts),
            samples ?? TwoIncidentSample(),
            fixOpts,
            _time,
            NullLogger<HybridFixtureRunner>.Instance);
    }

    private static async Task<List<InvestigationEvent>> CollectAsync(
        IInvestigationRunner runner, string topic = "Talvisota", CancellationToken ct = default)
    {
        var list = new List<InvestigationEvent>();
        await foreach (var e in runner.RunAsync(topic, ct))
        {
            list.Add(e);
        }

        return list;
    }

    [Fact]
    public async Task Streams_articles_then_scan_then_ranked_sample_incidents_then_completed()
    {
        var events = await CollectAsync(BuildRunner(), "  Talvisota  ");

        Assert.Collection(
            events,
            e => Assert.Equal(["Talvisota", "Winter War", "Jatkosota"], Assert.IsType<ArticlesFound>(e).Items.Select(a => a.Title)),
            e =>
            {
                var scan = Assert.IsType<RevisionsScanned>(e);
                Assert.Equal(4, scan.Count);
                Assert.Equal([2000L, 1000L], scan.Candidates.Select(c => c.Revert.RevId));
            },
            e =>
            {
                var c1 = Assert.IsType<CandidateScored>(e);
                Assert.Equal((1, 2), (c1.Index, c1.Of));
                Assert.Equal("9001-777", c1.Incident!.Id); // ranked by score desc (42.5 before 12.0)
                Assert.Equal(("fi", 9001L, "Mainilan laukaukset", 1.0),
                    (c1.Candidate.Article.Lang, c1.Candidate.Article.PageId, c1.Candidate.Article.Title, c1.Candidate.Article.Distance));
                Assert.Equal(778, c1.Candidate.Revert.RevId);
                Assert.Equal(777, c1.Candidate.Revert.RevertedRevId);
            },
            e =>
            {
                var c2 = Assert.IsType<CandidateScored>(e);
                Assert.Equal((2, 2), (c2.Index, c2.Of));
                Assert.Equal("1256-1999", c2.Incident!.Id);
                // Reused the real matching candidate from scan.Candidates (Distance = 0.10, Revert.RevId = 2000).
                Assert.Equal(0.10, c2.Candidate.Article.Distance);
                Assert.Equal(2000, c2.Candidate.Revert.RevId);
            },
            e =>
            {
                var done = Assert.IsType<Completed>(e);
                Assert.False(done.FromCache);
                Assert.Equal(["9001-777", "1256-1999"], done.Incidents.Select(i => i.Id));
            });

        Assert.Empty(_kit.Model.Requests); // stages 3–4 come from fixtures, zero LLM calls
    }

    [Fact]
    public async Task Embedder_down_emits_single_StageFailed_for_FindArticles()
    {
        _kit.Embedder.Unavailable = true;

        var events = await CollectAsync(BuildRunner());

        var failed = Assert.IsType<StageFailed>(Assert.Single(events));
        Assert.Equal((InvestigationStage.FindArticles, StageFailureKind.EmbeddingUnavailable), (failed.Stage, failed.Kind));
    }

    [Fact]
    public async Task Corpus_down_emits_single_StageFailed_for_FindArticles_and_revision_store_down_fails_ScanHistory()
    {
        _kit.Corpus.Unavailable = true;
        var stageOneEvents = await CollectAsync(BuildRunner());
        var f1 = Assert.IsType<StageFailed>(Assert.Single(stageOneEvents));
        Assert.Equal((InvestigationStage.FindArticles, StageFailureKind.CorpusUnavailable), (f1.Stage, f1.Kind));

        _kit.Corpus.Unavailable = false;
        _kit.Revisions.Unavailable = true;
        var stageTwoEvents = await CollectAsync(BuildRunner());
        Assert.Equal(2, stageTwoEvents.Count);
        Assert.IsType<ArticlesFound>(stageTwoEvents[0]);
        var f2 = Assert.IsType<StageFailed>(stageTwoEvents[1]);
        Assert.Equal((InvestigationStage.ScanHistory, StageFailureKind.CorpusUnavailable), (f2.Stage, f2.Kind));
    }

    [Fact]
    public async Task Unknown_topic_emits_zero_CandidateScored_then_Completed()
    {
        var events = await CollectAsync(BuildRunner(), "Unknown Topic With No Fixture");

        Assert.Collection(
            events,
            e => Assert.IsType<ArticlesFound>(e),
            e => Assert.IsType<RevisionsScanned>(e),
            e => Assert.Empty(Assert.IsType<Completed>(e).Incidents));
    }

    [Fact]
    public async Task Cancellation_throws_OperationCanceledException()
    {
        var runner = BuildRunner(delay: TimeSpan.FromMilliseconds(350));
        using var cts = new CancellationTokenSource();
        var seen = new List<InvestigationEvent>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var e in runner.RunAsync("Talvisota", cts.Token))
            {
                seen.Add(e);
                if (e is RevisionsScanned)
                {
                    await cts.CancelAsync();
                }
            }
        });

        Assert.DoesNotContain(seen, e => e is Completed);
    }
}
