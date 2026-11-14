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

    [Theory]
    [InlineData("http_500: {\"status\":500,\"title\":\"REQUEST_FAILED_RESPONSE_TOO_BIG\"}", true)]
    [InlineData("http_400: {\"status\":400,\"title\":\"INVALID_BODY_PROPERTY\"}", true)]
    [InlineData("http_404: not found", true)]
    [InlineData("http_401: Oxylabs rejected the credentials", false)]
    [InlineData("http_403: forbidden", false)]
    [InlineData("http_429: slow down", false)]
    [InlineData("http_502: bad gateway", false)]
    [InlineData("timeout after 90 s", false)]
    [InlineData("network: connection reset", false)]
    public async Task Only_failures_a_retry_wont_fix_are_remembered(string error, bool remembered)
    {
        const string url = "https://b.example/page";
        _fetch.Errors[url] = error;

        await _gateway.FetchAsync(url);
        var again = await _gateway.FetchAsync(url);

        Assert.Equal(remembered, again.FromCache);
        Assert.Equal(remembered ? 1 : 2, _fetch.Requests.Count);
    }

    [Fact]
    public async Task Ordinary_failures_are_not_cached()
    {
        await _gateway.FetchAsync("https://missing.example/");
        await _gateway.FetchAsync("https://missing.example/");

        Assert.Equal(2, _fetch.Requests.Count);
    }

    [Fact]
    public async Task A_page_oxylabs_gave_up_on_replays_as_a_failure_for_a_day_then_is_tried_again()
    {
        const string url = "https://www.facebook.com/some/post";
        _fetch.Errors[url] = "http_500: {\"status\":500,\"title\":\"REQUEST_FAILED_AFTER_TOO_MANY_RETRIES\",\"state\":\"faulted\"}";

        var first = await _gateway.FetchAsync(url, "t1");
        _time.Advance(TimeSpan.FromHours(23));
        var replay = await _gateway.FetchAsync(url, "t1");

        Assert.Equal(WebCallOutcome.Failed, first.Outcome);
        Assert.False(first.FromCache);
        Assert.Equal(WebCallOutcome.Failed, replay.Outcome);
        Assert.True(replay.FromCache);
        Assert.Contains("REQUEST_FAILED_AFTER_TOO_MANY_RETRIES", replay.Error);
        Assert.Single(_fetch.Requests);
        var usage = await _store.UsageAsync("t1"); // (23 h later is the next UTC day, so "today" has reset)
        Assert.Equal(1, usage.LiveInScope); // the replay cost nothing
        Assert.Equal(1, usage.CacheHitsToday);

        _time.Advance(TimeSpan.FromHours(2));
        _fetch.Add(url, "now public");
        var later = await _gateway.FetchAsync(url, "t1");

        Assert.Equal(WebCallOutcome.Ok, later.Outcome);
        Assert.Equal(2, _fetch.Requests.Count);
        Assert.True((await _gateway.FetchAsync(url, "t1")).FromCache); // and the success replaces the failure
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
    [InlineData("https://erepo.uef.fi/server/api/core/bitstreams/dee2917a-72a3-46d8-9ab/content")]
    [InlineData("https://helda.helsinki.fi/bitstream/handle/10138/1234/thesis?sequence=1")]
    [InlineData("ftp://example.com/x")]
    public async Task Skip_list_urls_are_never_fetched(string url)
    {
        var r = await _gateway.FetchAsync(url);

        Assert.Equal(WebCallOutcome.Skipped, r.Outcome);
        Assert.Empty(_fetch.Requests);
    }
}
