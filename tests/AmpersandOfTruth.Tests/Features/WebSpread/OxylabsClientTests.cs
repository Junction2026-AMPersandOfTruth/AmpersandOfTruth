using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using AmpersandOfTruth.Web.Features.WebSpread;
using AmpersandOfTruth.Web.Features.WebSpread.Oxylabs;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace AmpersandOfTruth.Tests.Features.WebSpread;

public sealed class OxylabsClientTests
{
    private const string GoogleParsed = """
        {"results":[{"content":{"results":{"organic":[
          {"pos":1,"url":"https://example.fi/2014/03/05/mainila","title":"Mainila","desc":"5.3.2014 — Suomi aloitti talvisodan ampumalla Mainilan kylään"},
          {"pos":2,"url":"https://news.example.com/a","title":"Winter War","desc":"Mar 3, 2014 — The shots at Mainila"},
          {"pos":3,"title":"no url"}
        ]}},"status_code":200}],"job":{"id":"1"}}
        """;

    private const string WebApiSearch = """
        {"results":[{"title":"T","url":"https://a.example/x","outline":"snippet","metadata":{"position":1}},
                    {"title":"U","url":"https://b.example/y","outline":"other","metadata":{"position":2}}],"state":"done"}
        """;

    private static (OxylabsClient Client, StubHandler Handler) Sut(OxylabsOptions o)
    {
        o.RetryBaseDelay = TimeSpan.Zero;
        var handler = new StubHandler();
        var client = new OxylabsClient(new HttpClient(handler), new StaticOptions<OxylabsOptions>(o),
            new FakeTimeProvider(DateTimeOffset.UtcNow), NullLogger<OxylabsClient>.Instance);
        return (client, handler);
    }

    // ── wire format ──

    [Fact]
    public void Google_date_range_uses_tbs_cdr_with_open_ends_left_out()
    {
        Assert.Equal("cdr:1,cd_min:3/1/2014,cd_max:3/20/2014",
            OxylabsWire.GoogleDateRange(new DateWindow(new DateOnly(2014, 3, 1), new DateOnly(2014, 3, 20))));
        Assert.Equal("cdr:1,cd_max:2/28/2014", OxylabsWire.GoogleDateRange(new DateWindow(null, new DateOnly(2014, 2, 28))));
    }

    [Fact]
    public void Scraper_search_body_carries_query_language_domain_and_window()
    {
        var body = OxylabsWire.ScraperSearchBody(
            new WebSearchRequest("\"Mainilan kylään\"", "fi", new DateWindow(new DateOnly(2014, 3, 1), null), 10), "google_search");

        Assert.Equal("google_search", (string?)body["source"]);
        Assert.Equal("\"Mainilan kylään\"", (string?)body["query"]);
        Assert.True((bool)body["parse"]!);
        Assert.Equal("fi", (string?)body["domain"]);
        var context = body["context"]!.AsArray().Select(n => ((string?)n!["key"], (string?)n["value"])).ToList();
        Assert.Contains(("results_language", "fi"), context);
        Assert.Contains(("tbs", "cdr:1,cd_min:3/1/2014"), context);
    }

    [Fact]
    public void Parsed_google_results_become_ranked_results_with_snippet_dates()
    {
        var results = OxylabsWire.ParseScraperSearch(JsonNode.Parse(GoogleParsed), "google");

        Assert.Equal(2, results.Count);
        Assert.Equal("https://example.fi/2014/03/05/mainila", results[0].Url);
        Assert.Equal(new DateOnly(2014, 3, 5), results[0].SerpDate);
        Assert.Equal(new DateOnly(2014, 3, 3), results[1].SerpDate);
        Assert.Equal(2, results[1].Rank);
    }

    [Fact]
    public void Web_api_results_and_scrape_are_parsed()
    {
        var results = OxylabsWire.ParseWebApiSearch(JsonNode.Parse(WebApiSearch));
        Assert.Equal(["https://a.example/x", "https://b.example/y"], results.Select(r => r.Url));
        Assert.Equal("snippet", results[0].Snippet);

        var page = OxylabsWire.ParseWebApiScrape(JsonNode.Parse("""{"results":[{"markdown":"# Otsikko\nTeksti","html":"<title>T</title>"}]}"""),
            "https://a.example/x", 1000)!;
        Assert.Equal("# Otsikko\nTeksti", page.Text);
        Assert.Equal("T", page.Title);
    }

    [Fact]
    public void Universal_fetch_keeps_html_and_extracts_text_without_chrome()
    {
        var html = WebSpreadKit.Html("Suomi &amp; Neuvostoliitto<script>evil()</script>");
        var root = new JsonObject { ["results"] = new JsonArray(new JsonObject { ["content"] = html, ["status_code"] = 200 }) };

        var page = OxylabsWire.ParseScraperFetch(root, "https://x.example", 10_000)!;

        Assert.Equal("Suomi & Neuvostoliitto", page.Text);
        Assert.Equal("Sivu", page.Title);
        Assert.Equal(html, page.Html);
        Assert.Equal(200, page.StatusCode);
    }

    // ── transport ──

    [Fact]
    public async Task No_credentials_means_not_configured_and_no_request()
    {
        var (client, handler) = Sut(new OxylabsOptions());

        var r = await client.SearchAsync(new WebSearchRequest("q", "fi"));

        Assert.Equal(WebCallOutcome.NotConfigured, r.Outcome);
        Assert.Empty(handler.Requests);
        Assert.False(client.SupportsDateWindow);
    }

    [Fact]
    public async Task Scraper_api_uses_basic_auth_and_honours_windows()
    {
        var (client, handler) = Sut(new OxylabsOptions { Username = "user", Password = "pw" });
        handler.Reply(HttpStatusCode.OK, GoogleParsed);

        var r = await client.SearchAsync(new WebSearchRequest("q", "fi", new DateWindow(null, new DateOnly(2014, 1, 1))));

        Assert.Equal(WebCallOutcome.Ok, r.Outcome);
        Assert.True(r.WindowApplied);
        var (request, _) = Assert.Single(handler.Requests);
        Assert.Equal("https://realtime.oxylabs.io/v1/queries", request.RequestUri!.ToString());
        Assert.Equal("Basic", request.Headers.Authorization!.Scheme);
        Assert.Equal(Convert.ToBase64String(Encoding.UTF8.GetBytes("user:pw")), request.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task Web_api_uses_bearer_and_reports_the_window_as_not_applied()
    {
        var (client, handler) = Sut(new OxylabsOptions { ApiKey = "key-123" });
        handler.Reply(HttpStatusCode.OK, WebApiSearch);

        var r = await client.SearchAsync(new WebSearchRequest("q", "fi", new DateWindow(null, new DateOnly(2014, 1, 1)), MaxResults: 1));

        Assert.False(client.SupportsDateWindow);
        Assert.False(r.WindowApplied);
        Assert.Single(r.Results);
        var (request, body) = Assert.Single(handler.Requests);
        Assert.Equal("https://webapi.oxylabs.io/v1/search", request.RequestUri!.ToString());
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal("key-123", request.Headers.Authorization.Parameter);
        Assert.Contains("\"max_results\":1", body);
    }

    [Fact]
    public async Task Retries_429_and_5xx_then_succeeds()
    {
        var (client, handler) = Sut(new OxylabsOptions { ApiKey = "k", MaxAttempts = 3 });
        handler.Reply(HttpStatusCode.TooManyRequests, "{}").Reply(HttpStatusCode.BadGateway, "{}").Reply(HttpStatusCode.OK, WebApiSearch);

        var r = await client.SearchAsync(new WebSearchRequest("q", "en"));

        Assert.Equal(WebCallOutcome.Ok, r.Outcome);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task Rejected_credentials_are_not_retried_and_never_echo_the_secret()
    {
        var (client, handler) = Sut(new OxylabsOptions { ApiKey = "super-secret-key", MaxAttempts = 3 });
        handler.Reply(HttpStatusCode.Unauthorized, """{"message":"Unauthorized"}""");

        var r = await client.FetchAsync("https://example.com/page");

        Assert.Equal(WebCallOutcome.Failed, r.Outcome);
        Assert.Single(handler.Requests);
        Assert.Contains("http_401", r.Error);
        Assert.DoesNotContain("super-secret-key", r.Error);
    }

    [Fact]
    public async Task Gives_up_after_max_attempts_with_the_last_error()
    {
        var (client, handler) = Sut(new OxylabsOptions { Username = "u", Password = "p", MaxAttempts = 2 });
        handler.Reply(HttpStatusCode.ServiceUnavailable, "down");

        var r = await client.SearchAsync(new WebSearchRequest("q", "fi"));

        Assert.Equal(WebCallOutcome.Failed, r.Outcome);
        Assert.Equal(2, handler.Requests.Count);
        Assert.StartsWith("http_503", r.Error);
    }

    [Theory]
    [InlineData(429, true)]
    [InlineData(500, true)]
    [InlineData(613, true)]
    [InlineData(400, false)]
    [InlineData(404, false)]
    public void Retryable_statuses(int status, bool retry) => Assert.Equal(retry, OxylabsClient.IsRetryable(status));
}
