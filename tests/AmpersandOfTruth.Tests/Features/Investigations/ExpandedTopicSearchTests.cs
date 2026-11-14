using System.Text.Json;
using AmpersandOfTruth.Tests.Infrastructure;
using AmpersandOfTruth.Web.Features.Analysis;
using AmpersandOfTruth.Web.Features.Analysis.Prompts;
using AmpersandOfTruth.Web.Features.Embedding;
using AmpersandOfTruth.Web.Features.Investigations;
using AmpersandOfTruth.Web.Features.Investigations.Pipeline;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AmpersandOfTruth.Tests.Features.Investigations;

/// <summary>Stage 1 = R3-D (R6 decision, #27), ported from <c>research/24-r3-query/variant.py</c> mode D.</summary>
public sealed class ExpandedTopicSearchTests
{
    private readonly PipelineKit _kit = new() { ExpandedSearch = true };

    public ExpandedTopicSearchTests()
    {
        _kit.Corpus.OtherFiArticles.AddRange([
            new TopicArticle("fi", 7000, "Mainilan laukaukset", 0, "Q7000"),
            new TopicArticle("fi", 7002, "Talvisodan taustat ja syyt", 0, "Q7002"),
            new TopicArticle("fi", 7001, "Talvisodan tausta", 0, "Q7001"),
        ]);
    }

    [Fact]
    public async Task Title_guesses_come_first_in_guess_order_then_the_fused_vector_hits()
    {
        // Underscores and [[brackets]] are cleaned, a case-insensitive duplicate is dropped, "Talvisodan taus" has no exact
        // match so the two shortest titles starting with it stand in, and "Ei ole" matches nothing.
        _kit.Model.Returns(ExpandTopicPrompt.Id, PipelineKit.ExpansionJson(
            titles: "\"Mainilan_laukaukset\", \"[[Jatkosota]]\", \"Talvisodan taus\", \"Ei ole\", \"jatkosota\""));

        var result = await _kit.Search().SearchAsync("Talvisota");

        Assert.False(result.Fallback);
        Assert.Equal(new TopicScope(true, "Finnish history."), result.Scope);
        Assert.Equal([7000L, 5263, 7001, 7002, 1256, 9012], result.Articles.Select(a => a.PageId));
        Assert.Equal([true, true, true, true, false, false], result.Articles.Select(a => a.TitleMatch));
        Assert.Equal(1.0, result.Articles[0].Distance);   // a title-only hit has no vector distance
        Assert.Equal(0.20, result.Articles[1].Distance);  // Jatkosota is also a vector hit: it keeps its distance
        Assert.All(result.Articles, a => Assert.Equal("fi", a.Lang));
    }

    [Fact]
    public async Task One_llm_call_and_one_embedding_per_distinct_query()
    {
        _kit.Model.Returns(ExpandTopicPrompt.Id, PipelineKit.ExpansionJson());

        await _kit.Search().SearchAsync("Talvisota");

        var request = Assert.Single(_kit.Model.Requests);
        Assert.Equal(ExpandTopicPrompt.Id, request.PromptId);
        Assert.Equal("Talvisota", request.Variables["topic"]);
        Assert.StartsWith("topic-search:", request.Scope);
        // topic + topic_fi (the same text, counted once) + 3 reformulations
        Assert.Equal(4, _kit.Embedder.Calls);
    }

    [Fact]
    public async Task Article_limit_cuts_the_merged_list()
    {
        _kit.Options.ArticleLimit = 2;
        _kit.Model.Returns(ExpandTopicPrompt.Id, PipelineKit.ExpansionJson(titles: "\"Mainilan laukaukset\""));

        var result = await _kit.Search().SearchAsync("Talvisota");

        Assert.Equal([7000L, 1256], result.Articles.Select(a => a.PageId));
    }

    [Fact]
    public async Task Out_of_scope_is_a_warning_and_results_are_still_returned()
    {
        _kit.Model.Returns(ExpandTopicPrompt.Id, PipelineKit.ExpansionJson(inScope: false, titles: "", reason: "Not about Finland."));

        var result = await _kit.Search().SearchAsync("Bitcoin price history");

        Assert.Equal(new TopicScope(false, "Not about Finland."), result.Scope);
        Assert.Equal([1256L, 5263, 9012], result.Articles.Select(a => a.PageId));
    }

    [Theory]
    [InlineData(AnalysisOutcome.Failed)]
    [InlineData(AnalysisOutcome.BudgetExceeded)]
    [InlineData(AnalysisOutcome.Blocked)]
    public async Task Llm_unavailable_falls_back_to_R1_and_says_so(AnalysisOutcome outcome)
    {
        _kit.Model.Returns(ExpandTopicPrompt.Id, outcome);

        var result = await _kit.Search().SearchAsync("Talvisota");

        Assert.True(result.Fallback);
        Assert.Null(result.Scope);
        var r1 = await new PipelineKit().Search().SearchAsync("Talvisota");
        Assert.Equal(r1.Articles, result.Articles);
    }

    [Fact]
    public async Task Embedder_down_throws_so_the_pipeline_reports_the_stage()
    {
        _kit.Model.Returns(ExpandTopicPrompt.Id, PipelineKit.ExpansionJson());
        _kit.Embedder.Unavailable = true;

        await Assert.ThrowsAsync<EmbeddingUnavailableException>(() => _kit.Search().SearchAsync("Talvisota"));
    }

    [Fact]
    public async Task Pipeline_streams_the_scope_verdict_with_the_articles()
    {
        _kit.Model.Returns(ExpandTopicPrompt.Id, PipelineKit.ExpansionJson(inScope: false, titles: "", reason: "Not about Finland."));

        var found = Assert.IsType<ArticlesFound>((await _kit.RunAsync("K-pop fandom"))[0]);

        Assert.Equal(new TopicScope(false, "Not about Finland."), found.Scope);
        Assert.False(found.SearchFallback);
        Assert.NotEmpty(found.Items);
    }

    [Fact]
    public async Task A_run_whose_search_fell_back_is_not_cached_and_a_clean_one_replays_its_scope()
    {
        using var db = new TestDb();
        var runner = _kit.BuildCaching(db);

        var first = await PipelineKit.Collect(runner.RunAsync("Talvisota"));   // no expansion scripted → R1 fallback
        Assert.True(Assert.IsType<ArticlesFound>(first[0]).SearchFallback);
        var second = await PipelineKit.Collect(runner.RunAsync("Talvisota"));
        Assert.False(Assert.IsType<Completed>(second[^1]).FromCache);

        _kit.Model.Returns(ExpandTopicPrompt.Id, PipelineKit.ExpansionJson(reason: "Winter War."));
        await PipelineKit.Collect(runner.RunAsync("Talvisota"));
        var replay = await PipelineKit.Collect(runner.RunAsync("Talvisota"));
        Assert.True(Assert.IsType<Completed>(replay[^1]).FromCache);
        Assert.Equal(new TopicScope(true, "Winter War."), Assert.IsType<ArticlesFound>(replay[0]).Scope);
    }

    [Fact]
    public void The_search_method_and_its_prompt_are_part_of_the_run_cache_fingerprint()
    {
        var expanded = Assert.IsType<ExpandedTopicSearch>(_kit.Search());
        Assert.Matches(@"^r3d\.\d+;expand=1\.[0-9a-f]{8};model=gemini-", expanded.Fingerprint);
        Assert.Equal("r1", new PipelineKit().Search().Fingerprint);
    }

    // ── mode D ranking, as in variant.py ─────────────────────────────────────

    private static TopicArticle Fi(long id, double distance) => new("fi", id, $"A{id}", distance);

    [Fact]
    public void Merge_fuses_arms_by_reciprocal_rank_and_puts_title_hits_first()
    {
        IReadOnlyList<TopicArticle>[] arms =
        [
            [Fi(1, 0.10), Fi(2, 0.20), Fi(3, 0.30)],
            [Fi(2, 0.15), Fi(3, 0.25)],
            [Fi(3, 0.05)],
        ];
        // RRF k=60: 3 = 1/63+1/62+1/61 > 2 = 1/62+1/61 > 1 = 1/61
        Assert.Equal([3L, 2, 1], ExpandedTopicSearch.Merge([], arms, 10).Select(a => a.PageId));

        var merged = ExpandedTopicSearch.Merge([Fi(9, 0), Fi(2, 0)], arms, 3);
        Assert.Equal([9L, 2, 3], merged.Select(a => a.PageId));
        Assert.Equal([1.0, 0.15, 0.05], merged.Select(a => a.Distance));
        Assert.Equal([true, true, false], merged.Select(a => a.TitleMatch));
    }

    [Fact]
    public void Merge_breaks_rrf_ties_by_best_rank_then_distance_then_page_id()
    {
        IReadOnlyList<TopicArticle>[] arms = [[Fi(5, 0.3), Fi(4, 0.2)], [Fi(4, 0.4), Fi(5, 0.1)]];
        // equal RRF and best rank 1 for both → the closer one (5, 0.1) first
        Assert.Equal([5L, 4], ExpandedTopicSearch.Merge([], arms, 10).Select(a => a.PageId));

        IReadOnlyList<TopicArticle>[] same = [[Fi(8, 0.2)], [Fi(7, 0.2)]];
        Assert.Equal([7L, 8], ExpandedTopicSearch.Merge([], same, 10).Select(a => a.PageId));
    }

    [Fact]
    public void Queries_and_title_guesses_are_cleaned_like_the_research_variant()
    {
        Assert.Equal(["Talvisota", "Talvisodan syyt"],
            ExpandedTopicSearch.DistinctQueries(["  Talvisota ", "talvisota", "", null, "Talvisodan \n syyt"]));
        Assert.Equal(["Suomen sisällissota", "Lapuan liike", "Itä–Karjala"],
            ExpandedTopicSearch.CleanTitleGuesses(["[[Suomen_sisällissota]]", "suomen sisällissota", "  Lapuan   liike ", "Itä–Karjala", "itä-karjala", "[]"]));
    }

    // ── prompt ↔ record ──────────────────────────────────────────────────────

    [Fact]
    public void Embedded_prompt_schema_matches_the_record()
    {
        var gemini = Options.Create(new GeminiOptions());
        var def = new PromptLibrary(gemini, NullLogger<PromptLibrary>.Instance).Get(ExpandTopicPrompt.Id);

        Assert.Equal(AnalysisJob.QueryExpand, def.Job);
        Assert.Equal(["topic"], def.Variables);

        var properties = typeof(TopicExpansion).GetProperties().Select(p => JsonNamingPolicy.SnakeCaseLower.ConvertName(p.Name)).Order().ToList();
        var schemaProperties = def.Schema["properties"]!.AsObject().Select(kv => kv.Key).Order().ToList();
        var required = def.Schema["required"]!.AsArray().Select(n => n!.GetValue<string>()).Order().ToList();
        Assert.Equal(schemaProperties, properties);
        Assert.Equal(schemaProperties, required);

        var parsed = JsonSerializer.Deserialize<TopicExpansion>(PipelineKit.ExpansionJson(), AnalysisJson.Options)!;
        Assert.Equal(["Talvisota"], parsed.TitleGuesses);
        Assert.Equal("Talvisota", parsed.TopicFi);
    }
}
