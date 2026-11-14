using System.Text.Json;
using AmpersandOfTruth.Tests.Infrastructure;
using AmpersandOfTruth.Web.Features.Analysis;
using AmpersandOfTruth.Web.Features.Analysis.Prompts;
using AmpersandOfTruth.Web.Features.WebSpread;
using AmpersandOfTruth.Web.Features.WebSpread.Dating;
using AmpersandOfTruth.Web.Features.WebSpread.Matching;
using AmpersandOfTruth.Web.Features.WebSpread.Planning;
using AmpersandOfTruth.Web.Features.WebSpread.Spread;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using static AmpersandOfTruth.Tests.Features.WebSpread.WebSpreadKit;

namespace AmpersandOfTruth.Tests.Features.WebSpread;

public sealed class WebSpreadTracerTests
{
    private const string PlanJson = """
        {"contested_detail":"Finland fired the Mainila shots",
         "queries":[{"lang":"fi","kind":"keywords","text":"Suomi ampui Mainilan laukaukset"},
                    {"lang":"en","kind":"translation","text":"Finland fired Mainila shots"},
                    {"lang":"ru","kind":"translation","text":"Финляндия Майнила выстрелы"}],
         "notes":""}
        """;

    private static string Judgement(string stance, string matchType, string quote, bool cites = false) => JsonSerializer.Serialize(new
    {
        stance, match_type = matchType, quote, cites_wikipedia = cites, source_type = "news", reasoning = "The passage states it.",
    });

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 14, 10, 0, 0, TimeSpan.Zero));
    private readonly FakeWebSearch _search = new();
    private readonly FakePageFetcher _fetch = new();
    private readonly FakeWebArchive _archive = new();
    private readonly FakeAnalysisModel _llm = new();
    private readonly WebSpreadOptions _options = new();

    private ClaimSearchPlanner Planner() =>
        new(_llm, _search, new StaticOptions<WebSpreadOptions>(_options), _time, NullLogger<ClaimSearchPlanner>.Instance);

    private WebSpreadTracer Tracer() => new(Planner(), _search, _fetch,
        new ClaimMatcher(_llm, new StaticOptions<WebSpreadOptions>(_options), NullLogger<ClaimMatcher>.Instance),
        _archive, new StaticOptions<WebSpreadOptions>(_options), _time, NullLogger<WebSpreadTracer>.Instance);

    // ── plan (P3.2) ──

    [Fact]
    public async Task Plan_is_exact_phrase_then_keywords_times_three_windows()
    {
        _llm.Returns(PlanClaimSearchPrompt.Id, PlanJson);

        var plan = await Planner().PlanAsync(Claim());

        // exact phrase + fi keywords + en translation (ru isn't a requested language) × before/during/after
        Assert.Equal(9, plan.Queries.Count);
        Assert.Equal(0, plan.Dropped);
        Assert.All(plan.Queries.Take(3), q => Assert.Equal(QueryKind.ExactPhrase, q.Kind));
        Assert.StartsWith("\"", plan.Queries[0].Text);
        Assert.EndsWith("-site:wikipedia.org", plan.Queries[0].Text);
        Assert.Equal([SearchWindowKind.Before, SearchWindowKind.During, SearchWindowKind.After], plan.Queries.Take(3).Select(q => q.WindowKind));
        Assert.DoesNotContain(plan.Queries, q => q.Language == "ru");
        Assert.Equal("Finland fired the Mainila shots", plan.ContestedDetail);
        Assert.Null(plan.Degraded);
    }

    [Fact]
    public async Task Plan_without_date_search_runs_each_query_once_and_says_so()
    {
        _llm.Returns(PlanClaimSearchPrompt.Id, PlanJson);
        _search.SupportsDateWindow = false;

        var plan = await Planner().PlanAsync(Claim());

        Assert.Equal(3, plan.Queries.Count);
        Assert.All(plan.Queries, q => Assert.Null(q.Window));
        Assert.Contains("date-restricted", plan.Degraded);
    }

    [Fact]
    public async Task Plan_survives_the_llm_being_down()
    {
        _llm.Returns(PlanClaimSearchPrompt.Id, AnalysisOutcome.Failed);

        var plan = await Planner().PlanAsync(Claim());

        Assert.Contains(plan.Queries, q => q.Kind == QueryKind.ExactPhrase);
        Assert.Contains(plan.Queries, q => q.Kind == QueryKind.Keywords && q.Language == "fi");
        Assert.Contains(plan.Queries, q => q.Kind == QueryKind.Keywords && q.Language == "en" && q.Text.StartsWith("Finland fired"));
        Assert.Contains("query expansion unavailable", plan.Degraded);
    }

    [Fact]
    public async Task Search_cap_drops_the_lowest_priority_queries()
    {
        _llm.Returns(PlanClaimSearchPrompt.Id, PlanJson);
        _options.MaxSearches = 4;

        var plan = await Planner().PlanAsync(Claim());

        Assert.Equal(4, plan.Queries.Count);
        Assert.Equal(5, plan.Dropped);
        Assert.Equal(QueryKind.Keywords, plan.Queries[3].Kind);
    }

    // ── trace (P3 end to end over fakes) ──

    [Fact]
    public async Task Trace_streams_sources_and_buckets_them_against_the_exposure()
    {
        _llm.Returns(PlanClaimSearchPrompt.Id, PlanJson)
            .Handle(MatchClaimPrompt.Id, r => FakeReply.Ok(r.Variables["url"] switch
            {
                "https://copy.example/2015/06/01/x" => Judgement("asserts", "verbatim_copy", "Suomi aloitti talvisodan ampumalla Mainilan kylään"),
                "https://old.example/a" => Judgement("asserts", "same_claim_different_wording", "Mainilan laukaukset ampui Suomi"),
                "https://check.example/b" => Judgement("refutes", "same_claim_different_wording", "väite on myytti"),
                _ => Judgement("unrelated", "no_match", ""),
            }));
        _search.ByQuery["Mainilan"] =
        [
            new("https://copy.example/2015/06/01/x", "Copy", "s", null, 1, "google"),
            new("https://fi.wikipedia.org/wiki/Talvisota", "Wikipedia", "s", null, 2, "google"),
            new("https://old.example/a", "Old", "s", new DateOnly(2010, 1, 1), 3, "google"),
            new("https://check.example/b", "Check", "s", null, 4, "google"),
            new("https://fishing.example/c", "Fish", "s", null, 5, "google"),
        ];
        _fetch.Add("https://copy.example/2015/06/01/x", "Blogi. " + InsertedText)
              .Add("https://old.example/a", "Vanha juttu: Mainilan laukaukset ampui Suomi, väittää kirjoittaja.")
              .Add("https://check.example/b", "Faktantarkistus: väite on myytti. Mainilan laukaukset ampui Neuvostoliitto.",
                   Html("x", """<meta property="article:published_time" content="2014-03-10">"""))
              .Add("https://fishing.example/c", "Kalastus Mainilan lähellä on hauskaa ja Suomi on kaunis maa talvisin.");

        var events = new List<WebSpreadEvent>();
        await foreach (var e in Tracer().TraceAsync(Claim())) events.Add(e);

        Assert.IsType<WebSpreadPlanned>(events[0]);
        var result = Assert.IsType<WebSpreadCompleted>(events[^1]).Result;
        Assert.Equal(3, events.OfType<WebSpreadSourceFound>().Count());
        Assert.Equal(WebSpreadStatus.Completed, result.Status);
        Assert.DoesNotContain(_fetch.Requests, u => u.Contains("wikipedia"));

        var copy = result.Sources.Single(s => s.Domain == "copy.example");
        Assert.Equal(SpreadBucket.AfterRemoval, copy.Bucket);
        Assert.Equal(MatchKind.VerbatimCopy, copy.MatchType);
        Assert.True(copy.LikelyFromWikipedia);
        Assert.Equal(DateMethod.UrlPath, copy.Dating.Method);

        var old = result.Sources.Single(s => s.Domain == "old.example");
        Assert.Equal(SpreadBucket.Before, old.Bucket);
        Assert.False(old.LikelyFromWikipedia);
        Assert.Equal(old, result.Earliest);

        var check = result.Sources.Single(s => s.Domain == "check.example");
        Assert.True(check.IsRefutation);
        Assert.Equal(SpreadBucket.During, check.Bucket);

        Assert.Equal("Found on 1 site before the Wikipedia edit and 1 site after it; 1 of the later ones reuses Wikipedia's wording or credits it. One site argues against it.",
            result.Headline);
        Assert.Contains("Timing shows sequence, not causation.", result.Notes);
    }

    [Fact]
    public async Task Llm_quotes_that_are_not_on_the_page_are_never_shown()
    {
        _llm.Returns(PlanClaimSearchPrompt.Id, PlanJson)
            .Returns(MatchClaimPrompt.Id, Judgement("asserts", "same_claim_different_wording", "a sentence the model made up"));
        _search.ByQuery["Mainilan"] = [new("https://x.example/a", "X", "s", null, 1, "google")];
        _fetch.Add("https://x.example/a", "Mainilan laukaukset ampui Suomi.");

        WebSpreadResult? result = null;
        await foreach (var e in Tracer().TraceAsync(Claim())) if (e is WebSpreadCompleted c) result = c.Result;

        var s = Assert.Single(result!.Sources);
        Assert.NotEqual("a sentence the model made up", s.Quote);
    }

    [Fact]
    public async Task Not_configured_ends_after_the_first_search_with_a_clear_status()
    {
        _llm.Returns(PlanClaimSearchPrompt.Id, PlanJson);
        _search.Outcome = WebCallOutcome.NotConfigured;

        var events = new List<WebSpreadEvent>();
        await foreach (var e in Tracer().TraceAsync(Claim())) events.Add(e);

        Assert.Single(events.OfType<WebSpreadSearched>());
        var result = Assert.IsType<WebSpreadCompleted>(events[^1]).Result;
        Assert.Equal(WebSpreadStatus.NotConfigured, result.Status);
        Assert.Empty(_fetch.Requests);
    }

    // ── prompts ↔ records, DI ──

    [Theory]
    [InlineData(PlanClaimSearchPrompt.Id, typeof(ClaimQueryExpansion), AnalysisJob.QueryExpand)]
    [InlineData(MatchClaimPrompt.Id, typeof(ClaimMatchJudgement), AnalysisJob.Classify)]
    public void Embedded_prompt_schemas_match_their_records(string id, Type record, AnalysisJob job)
    {
        var def = new PromptLibrary(Options.Create(new GeminiOptions()), NullLogger<PromptLibrary>.Instance).Get(id);

        Assert.Equal(job, def.Job);
        var properties = record.GetProperties().Select(p => JsonNamingPolicy.SnakeCaseLower.ConvertName(p.Name)).Order().ToList();
        var schemaProperties = def.Schema["properties"]!.AsObject().Select(kv => kv.Key).Order().ToList();
        var required = def.Schema["required"]!.AsArray().Select(n => n!.GetValue<string>()).Order().ToList();
        Assert.Equal(schemaProperties, properties);
        Assert.Equal(schemaProperties, required);
    }

    [Fact]
    public void Prompt_variables_cover_what_the_templates_use()
    {
        var library = new PromptLibrary(Options.Create(new GeminiOptions()), NullLogger<PromptLibrary>.Instance);

        library.Render(PlanClaimSearchPrompt.Id, PlanClaimSearchPrompt.Variables(Claim(), ["fi", "en"]));
        library.Render(MatchClaimPrompt.Id, MatchClaimPrompt.Variables("en", "fi", "ins", "https://x", "t", "passage"));
    }

    [Fact]
    public void Plan_reply_deserialises()
    {
        var parsed = JsonSerializer.Deserialize<ClaimQueryExpansion>(PlanJson, AnalysisJson.Options)!;
        Assert.Equal(3, parsed.Queries.Count);
        Assert.Equal("translation", parsed.Queries[1].Kind);
    }

    [Fact]
    public async Task App_resolves_the_tracer_and_the_gateway()
    {
        await using var app = new AppFactory();
        using var scope = app.Services.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<WebSpreadTracer>());
        Assert.IsType<WebSpreadGateway>(scope.ServiceProvider.GetRequiredService<IWebSearchClient>());
        Assert.IsType<WebSpreadGateway>(scope.ServiceProvider.GetRequiredService<IPageFetcher>());
    }
}
