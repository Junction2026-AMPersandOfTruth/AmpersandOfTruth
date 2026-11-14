using AmpersandOfTruth.Tests.Infrastructure;
using AmpersandOfTruth.Web.Features.Analysis;
using AmpersandOfTruth.Web.Features.Demo;
using AmpersandOfTruth.Web.Features.WebSpread;
using AmpersandOfTruth.Web.Features.WebSpread.Dating;
using AmpersandOfTruth.Web.Features.WebSpread.Matching;
using AmpersandOfTruth.Web.Features.WebSpread.Planning;
using AmpersandOfTruth.Web.Features.WebSpread.Spread;
using AmpersandOfTruth.Web.Features.WebSpread.Store;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using static AmpersandOfTruth.Tests.Features.WebSpread.WebSpreadKit;

namespace AmpersandOfTruth.Tests.Features.WebSpread;

/// <summary>Builds the P3.7 service stack over fakes: real tracer, planner, matcher and store; fake web, archive and LLM.</summary>
public sealed class WebSpreadServiceKit : IDisposable
{
    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 11, 14, 10, 0, 0, TimeSpan.Zero));
    public FakeWebSearch Search { get; } = new();
    public FakePageFetcher Fetch { get; } = new();
    public FakeAnalysisModel Llm { get; } = new();
    public WebSpreadOptions Options { get; } = new() { UseArchive = false };
    public OxylabsOptions Oxylabs { get; } = new() { ApiKey = "test-key" };
    public DemoOptions Demo { get; } = new();
    public TestDb Db { get; } = new();

    public WebSpreadResults Results => new(Db.Factory, Time, NullLogger<WebSpreadResults>.Instance);

    public WebSpreadService Service()
    {
        var options = new StaticOptions<WebSpreadOptions>(Options);
        var tracer = new WebSpreadTracer(
            new ClaimSearchPlanner(Llm, Search, options, Time, NullLogger<ClaimSearchPlanner>.Instance), Search, Fetch,
            new ClaimMatcher(Llm, options, NullLogger<ClaimMatcher>.Instance), new FakeWebArchive(), options, Time,
            NullLogger<WebSpreadTracer>.Instance);
        return new WebSpreadService(tracer, Results, new StaticOptions<OxylabsOptions>(Oxylabs), new StaticOptions<DemoOptions>(Demo),
            NullLogger<WebSpreadService>.Instance);
    }

    /// <summary>One page that states the claim word for word, found by every search.</summary>
    public WebSpreadServiceKit WithOneCopy(string matchReply = """{"stance":"asserts","match_type":"verbatim_copy","quote":"Suomi aloitti talvisodan ampumalla Mainilan kylään","cites_wikipedia":false,"source_type":"blog","reasoning":"r"}""")
    {
        Llm.Returns(PlanClaimSearchPrompt.Id, """{"contested_detail":"d","queries":[{"lang":"fi","kind":"keywords","text":"Mainilan laukaukset"}],"notes":""}""")
           .Returns(MatchClaimPrompt.Id, matchReply);
        Search.ByQuery["Mainilan"] = [new("https://copy.example/2015/06/01/x", "Copy", "s", null, 1, "google")];
        Fetch.Add("https://copy.example/2015/06/01/x", "Blogi. " + InsertedText);
        return this;
    }

    public void Dispose() => Db.Dispose();
}

public sealed class WebSpreadResultsTests : IDisposable
{
    private readonly WebSpreadServiceKit _kit = new();

    public void Dispose() => _kit.Dispose();

    private static async Task<WebSpreadResult> Drain(IAsyncEnumerable<WebSpreadEvent> events)
    {
        WebSpreadResult? result = null;
        await foreach (var e in events) if (e is WebSpreadCompleted c) result = c.Result;
        return result!;
    }

    [Fact]
    public async Task A_clean_live_trace_is_stored_and_reads_back_whole()
    {
        _kit.WithOneCopy();

        var traced = await Drain(_kit.Service().TraceAsync(Claim()));
        var stored = await _kit.Service().StoredAsync("inc-1");

        Assert.False(traced.AssessmentDegraded);
        Assert.NotNull(stored);
        Assert.Equal(traced.Headline, stored.Headline);
        Assert.Equal(traced.TracedAt, stored.TracedAt);
        var source = Assert.Single(stored.Sources);
        Assert.Equal(SpreadBucket.AfterRemoval, source.Bucket);
        Assert.Equal(Stance.Asserts, source.Stance);
        Assert.Equal(new DateOnly(2015, 6, 1), source.Dating.EffectiveDate);
        Assert.Equal(DateMethod.UrlPath, source.Dating.Method);
        Assert.Equal(traced.Plan.Queries.Select(q => q.Text), stored.Plan.Queries.Select(q => q.Text));
    }

    [Fact]
    public async Task A_trace_whose_page_assessment_failed_says_so_and_is_not_stored()
    {
        _kit.WithOneCopy();
        _kit.Llm.Returns(MatchClaimPrompt.Id, AnalysisOutcome.Failed);

        var traced = await Drain(_kit.Service().TraceAsync(Claim()));

        Assert.True(traced.AssessmentDegraded);
        Assert.Contains(traced.Notes, n => n.Contains("compared on wording only", StringComparison.Ordinal));
        Assert.Null(await _kit.Service().StoredAsync("inc-1"));
    }

    [Fact]
    public async Task A_trace_whose_search_planning_failed_is_not_stored()
    {
        _kit.WithOneCopy();
        _kit.Llm.Returns(PlanClaimSearchPrompt.Id, AnalysisOutcome.Failed);

        var traced = await Drain(_kit.Service().TraceAsync(Claim()));

        Assert.True(traced.Plan.ExpansionFailed);
        Assert.True(traced.AssessmentDegraded);
        Assert.Null(await _kit.Service().StoredAsync("inc-1"));
    }

    [Theory]
    [InlineData(WebSpreadStatus.Completed, false, 3, true)]
    [InlineData(WebSpreadStatus.Partial, false, 3, true)]
    [InlineData(WebSpreadStatus.Partial, true, 3, false)]
    [InlineData(WebSpreadStatus.Failed, false, 0, false)]
    [InlineData(WebSpreadStatus.BudgetExceeded, false, 0, false)]
    [InlineData(WebSpreadStatus.NotConfigured, false, 0, false)]
    [InlineData(WebSpreadStatus.Completed, false, 0, false)]
    public void Only_clean_results_that_searched_are_storable(WebSpreadStatus status, bool degraded, int searches, bool storable)
    {
        var result = Result(status, searches) with { AssessmentDegraded = degraded };
        Assert.Equal(storable, WebSpreadResults.IsStorable(result));
    }

    [Fact]
    public async Task Results_from_another_tracer_version_are_ignored()
    {
        await using (var db = _kit.Db.CreateContext())
        {
            db.Set<WebSpreadResultEntry>().Add(new WebSpreadResultEntry
            {
                IncidentId = "inc-1", Version = WebSpreadTracer.Version - 1, Status = WebSpreadStatus.Completed,
                ResultJson = System.Text.Json.JsonSerializer.Serialize(Result(WebSpreadStatus.Completed, 2), WebSpreadJson.Options),
                CreatedAt = _kit.Time.GetUtcNow(),
            });
            await db.SaveChangesAsync();
        }

        Assert.Null(await _kit.Results.GetAsync("inc-1"));
    }

    [Fact]
    public async Task Safe_mode_offers_stored_results_only_and_refuses_a_live_trace()
    {
        _kit.Demo.SafeMode = true;
        var service = _kit.Service();

        Assert.False(service.CanTraceLive);
        Assert.Equal(WebSpreadAvailability.StoredOnly, service.Availability(hasStored: true));
        Assert.Equal(WebSpreadAvailability.Off, service.Availability(hasStored: false));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Drain(service.TraceAsync(Claim())));
        Assert.Empty(_kit.Search.Requests);
    }

    [Fact]
    public void Without_credentials_web_spread_is_off_unless_something_is_stored()
    {
        _kit.Oxylabs.ApiKey = null;
        var service = _kit.Service();

        Assert.False(service.IsConfigured);
        Assert.Equal(WebSpreadAvailability.Off, service.Availability(hasStored: false));
        Assert.Equal(WebSpreadAvailability.StoredOnly, service.Availability(hasStored: true));
    }

    internal static WebSpreadResult Result(WebSpreadStatus status, int searches, IReadOnlyList<WebSpreadSource>? sources = null) => new(
        Claim(), status,
        new ClaimSearchPlan([new PlannedQuery("\"Mainilan laukaukset\" -site:wikipedia.org", "fi", QueryKind.ExactPhrase, SearchWindowKind.Any, null)], 0, null, null, null),
        sources ?? [], null, "No page carrying this claim was found (2 searches in Finnish and English).", searches, 10, ["Timing shows sequence, not causation."])
    {
        TracedAt = new DateTimeOffset(2026, 11, 14, 9, 30, 0, TimeSpan.Zero),
    };
}
