using AmpersandOfTruth.Tests.Infrastructure;
using AmpersandOfTruth.Web.Features.WebSpread;
using AmpersandOfTruth.Web.Features.WebSpread.Store;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace AmpersandOfTruth.Tests.Features.WebSpread;

public sealed class WebSpreadGatewayTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 14, 10, 0, 0, TimeSpan.Zero));
    private readonly FakeWebSearch _search = new();
    private readonly FakePageFetcher _fetch = new();
    private readonly OxylabsOptions _options = new() { ApiKey = "k" };
    private readonly WebSpreadStore _store;
    private readonly WebSpreadGateway _gateway;

    public WebSpreadGatewayTests()
    {
        _store = new WebSpreadStore(_db.Factory, _time);
        _gateway = new WebSpreadGateway(_search, _fetch, _store, new StaticOptions<OxylabsOptions>(_options), _time,
            NullLogger<WebSpreadGateway>.Instance);
        _search.ByQuery["mainila"] = [new WebSearchResult("https://a.example/x", "A", "s", new DateOnly(2014, 3, 5), 1, "google")];
        _fetch.Add("https://a.example/x", "teksti", "<p>teksti</p>");
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Second_identical_search_is_served_from_the_cache()
    {
        var request = new WebSearchRequest("mainila", "fi", new DateWindow(null, new DateOnly(2014, 3, 1))) { Scope = "t1" };

        var first = await _gateway.SearchAsync(request);
        var second = await _gateway.SearchAsync(request);

        Assert.False(first.FromCache);
        Assert.True(second.FromCache);
        Assert.Single(_search.Requests);
        Assert.Equal(first.Results, second.Results);
        Assert.Equal(new WebUsage(1, 1, 1), await _store.UsageAsync("t1"));
    }

    [Fact]
    public async Task Fetched_pages_replay_from_the_cache_by_normalised_url()
    {
        await _gateway.FetchAsync("https://a.example/x");
        var again = await _gateway.FetchAsync("https://www.a.example/x/?utm_source=feed");

        Assert.True(again.FromCache);
        Assert.Equal("teksti", again.Page!.Text);
        Assert.Single(_fetch.Requests);
    }

    [Fact]
    public async Task Failures_are_not_cached()
    {
        await _gateway.FetchAsync("https://missing.example/");
        await _gateway.FetchAsync("https://missing.example/");

        Assert.Equal(2, _fetch.Requests.Count);
    }

    [Fact]
    public async Task Per_trace_cap_fails_closed_and_cache_hits_stay_free()
    {
        _options.PerScopeRequestCap = 2;
        await _gateway.SearchAsync(new WebSearchRequest("mainila", "fi") { Scope = "t1" });
        await _gateway.SearchAsync(new WebSearchRequest("other", "fi") { Scope = "t1" });

        var over = await _gateway.SearchAsync(new WebSearchRequest("third", "fi") { Scope = "t1" });
        var cached = await _gateway.SearchAsync(new WebSearchRequest("mainila", "fi") { Scope = "t1" });
        var otherTrace = await _gateway.SearchAsync(new WebSearchRequest("third", "fi") { Scope = "t2" });

        Assert.Equal(WebCallOutcome.BudgetExceeded, over.Outcome);
        Assert.Contains("PerScopeRequestCap", over.Error);
        Assert.Equal(WebCallOutcome.Ok, cached.Outcome);
        Assert.Equal(WebCallOutcome.Ok, otherTrace.Outcome);
        Assert.Equal(3, _search.Requests.Count);
    }

    [Fact]
    public async Task Daily_cap_holds_across_traces()
    {
        _options.DailyRequestCap = 1;
        await _gateway.SearchAsync(new WebSearchRequest("mainila", "fi") { Scope = "t1" });

        var over = await _gateway.FetchAsync("https://a.example/x", "t2");

        Assert.Equal(WebCallOutcome.BudgetExceeded, over.Outcome);
        Assert.Empty(_fetch.Requests);
    }

    [Fact]
    public async Task Concurrent_calls_cannot_overshoot_the_cap()
    {
        _options.PerScopeRequestCap = 3;
        var results = await Task.WhenAll(Enumerable.Range(0, 10)
            .Select(i => _gateway.SearchAsync(new WebSearchRequest($"q{i}", "fi") { Scope = "t1" })));

        Assert.Equal(3, results.Count(r => r.Outcome == WebCallOutcome.Ok));
        Assert.Equal(7, results.Count(r => r.Outcome == WebCallOutcome.BudgetExceeded));
    }

    [Theory]
    [InlineData("https://fi.wikipedia.org/wiki/Talvisota")]
    [InlineData("https://example.com/report.pdf")]
    [InlineData("ftp://example.com/x")]
    public async Task Skip_list_urls_are_never_fetched(string url)
    {
        var r = await _gateway.FetchAsync(url);

        Assert.Equal(WebCallOutcome.Skipped, r.Outcome);
        Assert.Empty(_fetch.Requests);
    }
}
