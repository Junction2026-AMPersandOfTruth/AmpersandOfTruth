using System.Text.Json;
using AmpersandOfTruth.Tests.Infrastructure;
using AmpersandOfTruth.Web.Features.Analysis.Prompts;
using AmpersandOfTruth.Web.Features.Evidence;
using AmpersandOfTruth.Web.Features.Investigations;
using AmpersandOfTruth.Web.Features.Investigations.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit.Sdk;

namespace AmpersandOfTruth.Tests.Features.Investigations;

/// <summary>
/// Opt-in demo-topic regression test against the real corpus. Skipped unless <c>CORPUS_TESTS=1</c> and
/// <c>Fixtures/demo-pack.json</c> exists in the test output.
/// </summary>
public sealed class DemoPackFactAttribute : FactAttribute
{
    public DemoPackFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("CORPUS_TESTS") != "1")
        {
            Skip = "Demo-pack regression test — set CORPUS_TESTS=1 (and record Fixtures/demo-pack.json with `python3 research/41-regression/pack.py record`).";
            return;
        }

        if (!File.Exists(DemoPack.DefaultPath))
        {
            Skip = "Missing Fixtures/demo-pack.json in test output — record it with `python3 research/41-regression/pack.py record`.";
        }
    }
}

public class DemoPackRegressionTests(AppFactory app) : IClassFixture<AppFactory>
{
    [Trait("Category", "Corpus")]
    [DemoPackFact]
    public async Task Demo_topics_keep_their_recorded_incidents()
    {
        var pack = DemoPack.Load(DemoPack.DefaultPath);
        var demoTopics = pack.Topics.Where(t => t.Kind == "demo").ToList();
        Assert.NotEmpty(demoTopics);

        var corpus = app.Services.GetRequiredService<IInvestigationCorpus>();
        var revisions = app.Services.GetRequiredService<IRevisionStore>();
        var conflicts = app.Services.GetRequiredService<IConflictEpisodes>();
        var options = app.Services.GetRequiredService<IOptions<InvestigationPipelineOptions>>();
        var relevance = app.Services.GetRequiredService<TopicRelevance>();

        var candidates = new RevertCandidateSource(corpus, revisions, options);
        var reader = new CorpusEditReader(revisions, options, conflicts);

        foreach (var topic in demoTopics)
        {
            var pipeline = new InvestigationPipeline(
                new FixtureTopicSearch(topic.Articles, topic.Scope),
                candidates,
                reader,
                new ReplayingEditClassifier(topic.Classifications),
                options,
                TimeProvider.System,
                NullLogger<InvestigationPipeline>.Instance,
                relevance);

            var events = await PipelineKit.Collect(pipeline.RunAsync(topic.Topic));
            AssertTopicRegression(pack.TopN, topic, events);
        }
    }

    [Fact]
    public void Sample_fixture_loads_from_test_output_and_maps_contract_fields()
    {
        Assert.True(File.Exists(DemoPack.SamplePath), $"Expected sample fixture at {DemoPack.SamplePath}");
        var pack = DemoPack.Load(DemoPack.SamplePath);

        Assert.Equal(1, pack.Version);
        Assert.Equal(new DateTimeOffset(2026, 11, 14, 21, 0, 0, TimeSpan.Zero), pack.RecordedAt);
        Assert.Equal("0000000", pack.GitCommit);
        Assert.Equal(3, pack.PipelineVersion);
        Assert.Equal(10, pack.TopN);
        Assert.Equal(2, pack.Topics.Count);

        var demo = pack.Topics[0];
        Assert.Equal("Talvisota", demo.Topic);
        Assert.Equal("demo", demo.Kind);
        Assert.Equal(new TopicScope(true, "Finnish Winter War history."), demo.Scope);
        Assert.Equal([1256L, 5263L, 9012L], demo.Articles.Select(a => a.PageId));
        Assert.Equal("en", demo.Articles[2].ViaLang);
        Assert.Equal(3, demo.Candidates);

        var claim = demo.Classifications["1256-2000"];
        Assert.Equal(EditCategory.FalseFactualClaim, claim.Category);
        Assert.Equal(4, claim.Confidence);
        Assert.Equal("Rewrote who started the war.", claim.WhatChangedEn);
        Assert.Equal(["kun Suomi hyökkäsi ensin"], claim.Evidence);
        Assert.Equal(PipelineKit.ClaimEn, claim.ClaimEn);
        Assert.Equal("Suomi hyökkäsi ensin.", claim.ClaimFi);
        Assert.Equal("Soviet justification of the Winter War", claim.NarrativeEn);

        var graffiti = demo.Classifications["1256-3000"];
        Assert.Equal(EditCategory.Vandalism, graffiti.Category);
        Assert.Equal(5, graffiti.Confidence);
        Assert.Equal("", graffiti.WhatChangedEn);
        Assert.Empty(graffiti.Evidence);
        Assert.Null(graffiti.ClaimEn);

        var incident = Assert.Single(demo.Incidents);
        Assert.Equal(1, incident.Rank);
        Assert.Equal("1256-2000", incident.Id);
        Assert.Equal("1999", incident.Key);
        Assert.Equal(1256, incident.PageId);
        Assert.Equal(EvidenceTier.Contested, incident.Tier);
        Assert.Equal([1999L], incident.InsertingRevIds);
        Assert.Equal([2000L], incident.RemovingRevIds);
        Assert.True(demo.Expect.NoNewCampaign);

        var oos = pack.Topics[1];
        Assert.Equal("out-of-scope", oos.Kind);
        Assert.False(oos.Scope!.InScope);
        Assert.True(oos.Expect.OutOfScope);
        Assert.Null(oos.Expect.NoNewCampaign);
    }

    [Theory]
    [InlineData("vandalism", EditCategory.Vandalism)]
    [InlineData("test_or_nonsense", EditCategory.TestOrNonsense)]
    [InlineData("good_faith_error", EditCategory.GoodFaithError)]
    [InlineData("content_dispute", EditCategory.ContentDispute)]
    [InlineData("pov_push", EditCategory.PovPush)]
    [InlineData("false_factual_claim", EditCategory.FalseFactualClaim)]
    [InlineData("source_manipulation", EditCategory.SourceManipulation)]
    [InlineData("other", EditCategory.Other)]
    public void Category_strings_map_to_EditCategory(string raw, EditCategory expected)
    {
        var parsed = JsonSerializer.Deserialize<EditCategory>('"' + raw + '"', DemoPack.JsonOptions);
        Assert.Equal(expected, parsed);
    }

    [Fact]
    public async Task Replaying_edit_classifier_returns_recorded_classification_for_known_key_and_null_for_unknown()
    {
        var topic = DemoPack.Load(DemoPack.SamplePath).Topics[0];
        var classifier = new ReplayingEditClassifier(topic.Classifications);

        var kit = new PipelineKit();
        var reader = new CorpusEditReader(kit.Revisions, Options.Create(kit.Options));
        var article = new TopicArticle("fi", 1256, "Talvisota", 0.10);

        var knownReading = await reader.ReadAsync(new RemovedEdit(article, FakeInvestigationCorpus.Revert(1256, 2000)));
        var unknownReading = await reader.ReadAsync(new RemovedEdit(article, FakeInvestigationCorpus.Revert(1256, 1000)));

        var known = await classifier.ClassifyAsync(knownReading, "investigation:test");
        var unknown = await classifier.ClassifyAsync(unknownReading, "investigation:test");

        Assert.NotNull(known);
        Assert.Equal(EditCategory.FalseFactualClaim, known.Category);
        Assert.Equal(PipelineKit.ClaimEn, known.ClaimEn);
        Assert.Null(unknown);
    }

    [Fact]
    public async Task Fixture_topic_search_returns_recorded_articles_in_order()
    {
        var topic = DemoPack.Load(DemoPack.SamplePath).Topics[0];
        var search = new FixtureTopicSearch(topic.Articles, topic.Scope);

        Assert.Equal("fixture", search.Fingerprint);

        var result = await search.SearchAsync("Talvisota");

        Assert.Equal(topic.Articles, result.Articles);
        Assert.Equal([1256L, 5263L, 9012L], result.Articles.Select(a => a.PageId));
        Assert.Equal(topic.Scope, result.Scope);
        Assert.False(result.Fallback);
    }

    [Fact]
    public async Task Sample_fixture_replays_through_pipeline_and_enforces_regression_checks()
    {
        var pack = DemoPack.Load(DemoPack.SamplePath);
        var topic = pack.Topics[0];

        var kit = new PipelineKit();
        kit.Corpus.Reverts.RemoveAll(r => r.RevId == 2000);
        kit.Corpus.Reverts.Add(FakeInvestigationCorpus.Revert(1256, 2000, comment: "Kumottu muokkaus 1999: väärää tietoa"));
        kit.Revisions.Edit(1256, 2998, 2999, "198.51.100.1", PipelineKit.Before, PipelineKit.Before + " sotkua");
        kit.Corpus.Reverts.Add(FakeInvestigationCorpus.Revert(1256, 3000));

        var options = Options.Create(kit.Options);
        var pipeline = new InvestigationPipeline(
            new FixtureTopicSearch(topic.Articles, topic.Scope),
            new RevertCandidateSource(kit.Corpus, kit.Revisions, options),
            new CorpusEditReader(kit.Revisions, options, kit.Conflicts),
            new ReplayingEditClassifier(topic.Classifications),
            options,
            TimeProvider.System,
            NullLogger<InvestigationPipeline>.Instance);

        var events = await PipelineKit.Collect(pipeline.RunAsync(topic.Topic));

        AssertTopicRegression(pack.TopN, topic, events);
        Assert.Contains(events.OfType<CandidateScored>(), c => c.Candidate.Key == "1256-3000" && c.Exclusion == "graffiti");

        // Tier change is caught with topic + incident id in the message.
        var downgraded = new Completed(false,
            [new IncidentRecord { Id = "1256-2000", PageId = 1256, InsertingRevIds = [1999], RemovingRevIds = [2000], Tier = EvidenceTier.Reverted, Score = 105 }],
            TimeSpan.Zero);
        var tierEx = Assert.ThrowsAny<XunitException>(() => AssertTopicRegression(pack.TopN, topic, [downgraded]));
        Assert.Contains("Talvisota", tierEx.Message);
        Assert.Contains("1256-2000", tierEx.Message);

        // Unreviewed campaign incident is caught with topic + incident id in the message.
        var newCampaign = new Completed(false,
            [
                new IncidentRecord { Id = "1256-9999", PageId = 1256, InsertingRevIds = [9998], RemovingRevIds = [9999], Tier = EvidenceTier.Campaign, Score = 310 },
                new IncidentRecord { Id = "1256-2000", PageId = 1256, InsertingRevIds = [1999], RemovingRevIds = [2000], Tier = EvidenceTier.Contested, Score = 208 },
            ],
            TimeSpan.Zero);
        var campaignEx = Assert.ThrowsAny<XunitException>(() => AssertTopicRegression(pack.TopN, topic, [newCampaign]));
        Assert.Contains("Talvisota", campaignEx.Message);
        Assert.Contains("1256-9999", campaignEx.Message);

        // StageFailed is caught with topic in the message.
        var stageFailed = new StageFailed(InvestigationStage.ScanHistory, StageFailureKind.CorpusUnavailable, "down");
        var failedEx = Assert.ThrowsAny<XunitException>(() => AssertTopicRegression(pack.TopN, topic, [stageFailed]));
        Assert.Contains("Talvisota", failedEx.Message);
    }

    internal static void AssertTopicRegression(int topN, DemoPackTopic topic, IReadOnlyList<InvestigationEvent> events)
    {
        if (events.OfType<StageFailed>().FirstOrDefault() is { } failed)
            Assert.Fail($"[{topic.Topic}] StageFailed in {failed.Stage} ({failed.Kind}): {failed.Error}");

        var completed = events.OfType<Completed>().SingleOrDefault();
        if (completed is null)
            Assert.Fail($"[{topic.Topic}] Pipeline ended without a Completed event.");

        var window = completed.Incidents.Take(topN + 5).ToList();
        foreach (var expected in topic.Incidents)
        {
            var actual = window.FirstOrDefault(i => i.Id == expected.Id);
            if (actual is null)
            {
                Assert.Fail(
                    $"[{topic.Topic}] Incident {expected.Id} (recorded rank {expected.Rank}, tier {expected.Tier}) " +
                    $"missing from top {topN + 5} (got: [{string.Join(", ", window.Select(i => i.Id))}]).");
            }

            Assert.True(actual.Tier == expected.Tier,
                $"[{topic.Topic}] Incident {expected.Id} tier changed: expected {expected.Tier}, got {actual.Tier}.");
        }

        if (topic.Expect.NoNewCampaign != false)
        {
            var allowedCampaigns = topic.Incidents
                .Where(i => i.Tier == EvidenceTier.Campaign)
                .Select(i => i.Id)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var campaign in completed.Incidents.Where(i => i.Tier == EvidenceTier.Campaign))
            {
                var id = campaign.Id ?? "<null>";
                Assert.True(campaign.Id is not null && allowedCampaigns.Contains(campaign.Id),
                    $"[{topic.Topic}] Incident {id} reached tier campaign without review (not recorded as campaign in fixture).");
            }
        }
    }
}
