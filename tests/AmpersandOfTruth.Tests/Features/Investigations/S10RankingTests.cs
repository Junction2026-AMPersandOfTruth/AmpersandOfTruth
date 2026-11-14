using AmpersandOfTruth.Tests.Infrastructure;
using AmpersandOfTruth.Web.Features.Analysis.Prompts;
using AmpersandOfTruth.Web.Features.Embedding;
using AmpersandOfTruth.Web.Features.Evidence;
using AmpersandOfTruth.Web.Features.Investigations;
using AmpersandOfTruth.Web.Features.Investigations.Pipeline;
using AmpersandOfTruth.Web.Features.WikiDiff;
using Microsoft.Extensions.Logging.Abstractions;

namespace AmpersandOfTruth.Tests.Features.Investigations;

/// <summary>P1.S10 (#37) mode B+rel in the app: tier first, then score_A × topic relevance.</summary>
public sealed class S10RankingTests
{
    // ── score_A: parity with research/37-s10-assembly/export_classify.py score_variant_a (values printed by the Python) ──

    public static TheoryData<string, IncidentEvidence, int, long, double> ScoreACases => new()
    {
        { "bare", new IncidentEvidence { IsSubstantive = true, RemovedByOtherAccount = true }, 0, 0, 2.0 },
        { "claim", new IncidentEvidence
            {
                IsSubstantive = true, RemovedByOtherAccount = true,
                Assessment = new ContentAssessment(ContentCategory.FalseFactualClaim, 4, true), RemovalReason = RemovalReason.Accuracy,
            }, 180, 3600, 12.046816684802822 },
        { "war", new IncidentEvidence
            {
                IsSubstantive = true, RemovedByOtherAccount = true,
                Assessment = new ContentAssessment(ContentCategory.PovPush, 3, false), ReinsertionCount = 2, InEditWar = true,
                DistinctInsertingEditors = 3, InsertionSpan = TimeSpan.FromDays(9), SourceManipulation = true,
            }, 5000, 86400L * 800, 22.790826260363847 },
        { "graffiti", new IncidentEvidence
            {
                IsSubstantive = true, RemovedByOtherAccount = true,
                Assessment = new ContentAssessment(ContentCategory.Vandalism, 2, false), RemovalReason = RemovalReason.Vandalism,
            }, 10, 30, 1.040103440706063 },
        { "gfe", new IncidentEvidence
            {
                IsSubstantive = true, RemovedByOtherAccount = true,
                Assessment = new ContentAssessment(ContentCategory.GoodFaithError, 5, true), RemovalReason = RemovalReason.Other,
                DisputeTemplateAdded = true, DistinctArticles = 2,
            }, 400, 120, 9.371029928550968 },
    };

    [Theory]
    [MemberData(nameof(ScoreACases))]
    public void ScoreA_matches_the_research_variant(string name, IncidentEvidence evidence, int proseChars, long exposure, double expected)
    {
        Assert.True(Math.Abs(IncidentAssembler.ScoreA(evidence, proseChars, exposure) - expected) < 1e-9, name);
    }

    [Fact]
    public void Rank_puts_the_tier_first_and_scales_only_the_within_tier_score()
    {
        Assert.True(IncidentAssembler.Rank(EvidenceTier.Contested, 40, 1) < IncidentAssembler.Rank(EvidenceTier.Campaign, 0.1, 0));
        Assert.Equal(200 + 6.0, IncidentAssembler.Rank(EvidenceTier.Contested, 12, 0.5));
        Assert.Equal(200, IncidentAssembler.Rank(EvidenceTier.Contested, 12, -0.3));   // negative cosine counts as 0
    }

    [Fact]
    public void Relevance_text_is_the_claim_else_the_added_prose_else_the_wikitext_cut_at_500()
    {
        var reading = Reading("'''Talvisota''' alkoi, kun [[Suomi]] hyökkäsi ensin.");
        Assert.Equal("Finland attacked first.", IncidentAssembler.RelevanceText(reading, Classification("  Finland attacked first. ")));
        Assert.Equal("Talvisota alkoi, kun Suomi hyökkäsi ensin.", IncidentAssembler.RelevanceText(reading, Classification(null)));
        Assert.Equal(500, IncidentAssembler.RelevanceText(Reading(new string('x', 900)), null).Length);
    }

    // ── TopicRelevance ──

    [Fact]
    public async Task Relevance_is_the_clamped_cosine_and_null_when_the_embedder_is_down()
    {
        var embedder = new ScriptedEmbedder { ["Talvisota"] = [1, 0], ["same"] = [2, 0], ["opposite"] = [-1, 0], ["diagonal"] = [1, 1] };
        var relevance = new TopicRelevance(embedder, NullLogger<TopicRelevance>.Instance);

        Assert.Equal(1.0, (await relevance.OfAsync("Talvisota", "same"))!.Value, 6);
        Assert.Equal(0.0, await relevance.OfAsync("Talvisota", "opposite"));
        Assert.Equal(Math.Sqrt(0.5), (await relevance.OfAsync(" Talvisota ", "diagonal"))!.Value, 6);
        Assert.Equal(1.0, await relevance.OfAsync("Talvisota", "  "));          // nothing to compare: as the research variant

        embedder.Unavailable = true;
        Assert.Null(await relevance.OfAsync("Talvisota", "same"));
    }

    // ── in the pipeline ──

    private readonly PipelineKit _kit = new();

    private async Task<IncidentRecord> TheIncident() =>
        Assert.Single(Assert.IsType<Completed>((await _kit.RunAsync())[^1]).Incidents);

    [Fact]
    public async Task The_incident_score_is_tier_plus_score_a_times_relevance()
    {
        var unscaled = await TheIncident();   // no relevance service: relevance 1
        var tierBase = 100 * (int)unscaled.Tier;
        var scoreA = unscaled.Score - tierBase;
        Assert.True(scoreA > 0.1);

        _kit.Relevance = new TopicRelevance(
            new ScriptedEmbedder { ["Talvisota"] = [1, 0], [PipelineKit.ClaimEn] = [1, 1] }, NullLogger<TopicRelevance>.Instance);
        var scaled = await TheIncident();

        Assert.Equal(unscaled.Tier, scaled.Tier);   // relevance never changes the tier
        Assert.Equal(Math.Round(tierBase + scoreA * Math.Sqrt(0.5), 4), scaled.Score, 3);
    }

    [Fact]
    public async Task Switching_topic_relevance_off_ranks_by_score_a_alone()
    {
        var unscaled = await TheIncident();
        _kit.Options.TopicRelevance = false;
        _kit.Relevance = new TopicRelevance(
            new ScriptedEmbedder { ["Talvisota"] = [1, 0], [PipelineKit.ClaimEn] = [0, 1] }, NullLogger<TopicRelevance>.Instance);

        Assert.Equal(unscaled.Score, (await TheIncident()).Score);
    }

    [Fact]
    public async Task Without_an_embedder_the_run_is_unranked_and_not_cached()
    {
        _kit.Relevance = new TopicRelevance(new ScriptedEmbedder { Unavailable = true }, NullLogger<TopicRelevance>.Instance);
        using var db = new TestDb();
        var runner = _kit.BuildCaching(db);

        var first = Assert.IsType<Completed>((await PipelineKit.Collect(runner.RunAsync("Talvisota")))[^1]);
        var second = Assert.IsType<Completed>((await PipelineKit.Collect(runner.RunAsync("Talvisota")))[^1]);

        Assert.Equal(1, first.Unranked);
        Assert.Equal(0, first.Degraded);
        Assert.False(second.FromCache);
    }

    // ── helpers ──

    private static EditReading Reading(string inserted)
    {
        var edit = new RemovedEdit(new TopicArticle("fi", 1256, "Talvisota", 0), FakeInvestigationCorpus.Revert(1256, 2000));
        var row = new RevisionRow(1256, 1999, 1998, FakeRevisionStore.T0, "192.0.2.1", null, 10);
        return new EditReading(edit, [row], WikitextDiff.Compare("", inserted), inserted, "", "");
    }

    private static EditClassification Classification(string? claimEn) =>
        new("Rewrote it.", [], claimEn, null, null, EditCategory.FalseFactualClaim, 4, null, null);

    /// <summary>Fixed vectors per text (zero-padded); unknown text → a unit vector on a dimension of its own.</summary>
    private sealed class ScriptedEmbedder : Dictionary<string, float[]>, IQueryEmbedder
    {
        public bool Unavailable { get; set; }

        public Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
        {
            if (Unavailable) throw new EmbeddingUnavailableException("scripted embedder is down");
            return Task.FromResult(TryGetValue(text, out var v) ? v : [0, 0, 1]);
        }
    }
}
