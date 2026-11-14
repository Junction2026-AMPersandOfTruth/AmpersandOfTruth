using System.Collections.Concurrent;
using System.Net;
using System.Text;
using AmpersandOfTruth.Web.Features.Investigations;
using AmpersandOfTruth.Web.Features.WebSpread;
using AmpersandOfTruth.Web.Features.WebSpread.Dating;
using AmpersandOfTruth.Web.Features.WebSpread.Spread;
using Microsoft.Extensions.Options;

namespace AmpersandOfTruth.Tests.Features.WebSpread;

/// <summary>Scriptable search: results per exact query text, or a fixed outcome for everything.</summary>
public sealed class FakeWebSearch : IWebSearchClient
{
    public bool SupportsDateWindow { get; set; } = true;
    public Dictionary<string, List<WebSearchResult>> ByQuery { get; } = [];
    public WebCallOutcome Outcome { get; set; } = WebCallOutcome.Ok;
    public ConcurrentQueue<WebSearchRequest> Requests { get; } = new();

    public Task<WebSearchResponse> SearchAsync(WebSearchRequest request, CancellationToken ct = default)
    {
        Requests.Enqueue(request);
        if (Outcome != WebCallOutcome.Ok) return Task.FromResult(WebSearchResponse.Fail(Outcome, $"fake: {Outcome}"));
        var hits = ByQuery.FirstOrDefault(kv => request.Query.Contains(kv.Key, StringComparison.Ordinal)).Value ?? [];
        return Task.FromResult(new WebSearchResponse(WebCallOutcome.Ok, hits, request.Window is not null && SupportsDateWindow));
    }
}

/// <summary>Scriptable fetch: pages by URL; unknown URLs fail.</summary>
public sealed class FakePageFetcher : IPageFetcher
{
    public Dictionary<string, FetchedPage> Pages { get; } = [];
    /// <summary>URL → error text for a failing fetch (default for unknown URLs: "fake: 404").</summary>
    public Dictionary<string, string> Errors { get; } = [];
    public ConcurrentQueue<string> Requests { get; } = new();

    public FakePageFetcher Add(string url, string text, string? html = null, string? title = null)
    {
        Pages[url] = new FetchedPage(url, url, 200, title, text, html);
        return this;
    }

    public Task<PageFetchResponse> FetchAsync(string url, string? scope = null, CancellationToken ct = default)
    {
        Requests.Enqueue(url);
        return Task.FromResult(Pages.TryGetValue(url, out var p)
            ? new PageFetchResponse(WebCallOutcome.Ok, p)
            : PageFetchResponse.Fail(WebCallOutcome.Failed, Errors.GetValueOrDefault(url, "fake: 404")));
    }
}

public sealed class FakeWebArchive : IWebArchive
{
    public Dictionary<string, DateOnly> Captures { get; } = [];

    public Task<DateOnly?> FirstCaptureAsync(string url, CancellationToken ct = default) =>
        Task.FromResult(Captures.TryGetValue(url, out var d) ? d : (DateOnly?)null);
}

public sealed class StaticOptions<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue => value;
    public T Get(string? name) => value;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
}

/// <summary>Records requests; answers from a queue (the last answer repeats).</summary>
public sealed class StubHandler : HttpMessageHandler
{
    private readonly Queue<(HttpStatusCode Status, string Body)> _replies = new();
    private (HttpStatusCode, string)? _last;
    public List<(HttpRequestMessage Request, string Body)> Requests { get; } = [];

    public StubHandler Reply(HttpStatusCode status, string body)
    {
        _replies.Enqueue((status, body));
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
        Requests.Add((request, body));
        var (status, reply) = _replies.Count > 0 ? (_last = _replies.Dequeue()).Value : _last ?? (HttpStatusCode.OK, "{}");
        return new HttpResponseMessage(status) { Content = new StringContent(reply, Encoding.UTF8, "application/json") };
    }
}

public static class WebSpreadKit
{
    public static readonly DateTimeOffset Inserted = new(2014, 3, 1, 12, 0, 0, TimeSpan.Zero);
    public static readonly DateTimeOffset Removed = new(2014, 3, 20, 9, 0, 0, TimeSpan.Zero);

    public const string InsertedText =
        "Suomi aloitti talvisodan ampumalla Mainilan kylään, minkä vuoksi Neuvostoliiton oli pakko puolustautua.";

    public static WebSpreadClaim Claim(IReadOnlyList<ExposureWindow>? windows = null, string? inserted = InsertedText) => new(
        "inc-1", "Talvisota", "Suomi ampui Mainilan laukaukset.", "Finland fired the Mainila shots.", inserted,
        windows ?? [new ExposureWindow(Inserted, Removed)]);

    public static IncidentEdit Edit(long rev, DateTimeOffset? at, IncidentEditRole role) =>
        new(rev, rev - 1, 1, "Editor", false, at, role, null, null, []);

    public static string Html(string body, string head = "") =>
        $"<html><head><title>Sivu</title>{head}</head><body><nav>Menu</nav><article><p>{body}</p></article></body></html>";
}
