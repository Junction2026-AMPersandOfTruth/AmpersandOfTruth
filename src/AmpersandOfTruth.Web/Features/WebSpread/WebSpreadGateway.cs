using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AmpersandOfTruth.Web.Features.WebSpread.Store;
using Microsoft.Extensions.Options;

namespace AmpersandOfTruth.Web.Features.WebSpread;

/// <summary>
/// What the rest of the app uses for web search and page fetch (P3.3): skip list → cache → budget → the provider.
/// Registered as <see cref="IWebSearchClient"/> and <see cref="IPageFetcher"/>; the raw provider (<c>OxylabsClient</c>,
/// or a fake in tests) is passed in. Caps fail closed with <see cref="WebCallOutcome.BudgetExceeded"/>.
/// Every call, cached or live, is written to the <c>WebSpreadCalls</c> ledger. Singleton; thread-safe.
/// </summary>
public sealed class WebSpreadGateway(
    IWebSearchClient search,
    IPageFetcher fetcher,
    WebSpreadStore store,
    IOptionsMonitor<OxylabsOptions> options,
    TimeProvider time,
    ILogger<WebSpreadGateway> log) : IWebSearchClient, IPageFetcher
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // Live requests admitted but not yet in the ledger, so concurrent callers can't all slip under a cap.
    private readonly SemaphoreSlim _admit = new(1, 1);
    private readonly Dictionary<string, int> _pendingByScope = [];
    private int _pending;

    public bool SupportsDateWindow => search.SupportsDateWindow;

    public async Task<WebSearchResponse> SearchAsync(WebSearchRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var opt = options.CurrentValue;
        var describe = $"[{request.Language}] {request.Query} {request.Window}";
        var key = Key("search", opt, request.Query.Trim(), request.Language, request.Window?.ToString() ?? "", request.MaxResults.ToString(CultureInfo.InvariantCulture),
            SupportsDateWindow ? "w" : "nw");

        if (opt.CacheEnabled && await store.GetAsync(key, ct) is { } hit)
        {
            var cached = JsonSerializer.Deserialize<WebSearchResponse>(hit.ResponseJson, Json)! with { FromCache = true };
            await RecordAsync("search", request.Scope, describe, cached.Outcome, fromCache: true, live: false, 0, null, ct);
            return cached;
        }

        var started = time.GetTimestamp();
        var admitted = await AdmitAsync(request.Scope, opt, ct);
        if (admitted is not null)
        {
            await RecordAsync("search", request.Scope, describe, WebCallOutcome.BudgetExceeded, false, false, 0, admitted, ct);
            return WebSearchResponse.Fail(WebCallOutcome.BudgetExceeded, admitted);
        }

        WebSearchResponse response;
        try
        {
            response = await search.SearchAsync(request, ct);
            if (response.Outcome == WebCallOutcome.Ok && opt.CacheEnabled)
            {
                await store.PutAsync(new WebCacheEntry
                {
                    Key = key, Kind = "search", Request = Trim(describe, 2000),
                    ResponseJson = JsonSerializer.Serialize(response, Json), CreatedAt = time.GetUtcNow(),
                }, ct);
            }
            var live = response.Outcome is WebCallOutcome.Ok or WebCallOutcome.Failed;
            await RecordAsync("search", request.Scope, describe, response.Outcome, false, live,
                (long)time.GetElapsedTime(started).TotalMilliseconds, response.Error, ct);
        }
        finally
        {
            await ReleaseAsync(request.Scope);
        }
        return response;
    }

    public async Task<PageFetchResponse> FetchAsync(string url, string? scope = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        var opt = options.CurrentValue;

        var kind = UrlPolicy.Classify(url);
        if (kind is UrlKind.Wikipedia or UrlKind.File or UrlKind.Invalid)
        {
            var reason = $"skipped: {kind.ToString().ToLowerInvariant()} URL";
            await RecordAsync("fetch", scope, url, WebCallOutcome.Skipped, false, false, 0, reason, ct);
            return PageFetchResponse.Fail(WebCallOutcome.Skipped, reason);
        }

        var key = Key("fetch", opt, UrlPolicy.Normalize(url), opt.RenderJavaScript ? "js" : "nojs");
        if (opt.CacheEnabled && await store.GetAsync(key, ct) is { } hit)
        {
            var page = JsonSerializer.Deserialize<FetchedPage>(hit.ResponseJson, Json)!;
            await RecordAsync("fetch", scope, url, WebCallOutcome.Ok, true, false, 0, null, ct);
            return new PageFetchResponse(WebCallOutcome.Ok, page) { FromCache = true };
        }

        var started = time.GetTimestamp();
        var denied = await AdmitAsync(scope, opt, ct);
        if (denied is not null)
        {
            await RecordAsync("fetch", scope, url, WebCallOutcome.BudgetExceeded, false, false, 0, denied, ct);
            return PageFetchResponse.Fail(WebCallOutcome.BudgetExceeded, denied);
        }

        PageFetchResponse response;
        try
        {
            response = await fetcher.FetchAsync(url, scope, ct);
            if (response is { Outcome: WebCallOutcome.Ok, Page: { } page } && opt.CacheEnabled)
            {
                await store.PutAsync(new WebCacheEntry
                {
                    Key = key, Kind = "fetch", Request = Trim(url, 2000),
                    ResponseJson = JsonSerializer.Serialize(page, Json), CreatedAt = time.GetUtcNow(),
                }, ct);
            }
            var live = response.Outcome is WebCallOutcome.Ok or WebCallOutcome.Failed;
            await RecordAsync("fetch", scope, url, response.Outcome, false, live,
                (long)time.GetElapsedTime(started).TotalMilliseconds, response.Error, ct);
        }
        finally
        {
            await ReleaseAsync(scope);
        }
        return response;
    }

    /// <summary>Null = admitted (and reserved); otherwise the reason it was refused.</summary>
    private async Task<string?> AdmitAsync(string? scope, OxylabsOptions opt, CancellationToken ct)
    {
        if (opt.EffectiveMode == OxylabsMode.None) return null; // the provider answers NotConfigured itself, no budget spent
        await _admit.WaitAsync(ct);
        try
        {
            var usage = await store.UsageAsync(scope, ct);
            if (usage.LiveToday + _pending >= opt.DailyRequestCap)
                return $"Daily web request cap reached ({opt.DailyRequestCap}). Raise Oxylabs:DailyRequestCap to continue.";
            var pendingInScope = scope is not null && _pendingByScope.TryGetValue(scope, out var p) ? p : 0;
            if (scope is not null && usage.LiveInScope + pendingInScope >= opt.PerScopeRequestCap)
                return $"Web request cap for this trace reached ({opt.PerScopeRequestCap}). Raise Oxylabs:PerScopeRequestCap to continue.";

            _pending++;
            if (scope is not null) _pendingByScope[scope] = pendingInScope + 1;
            return null;
        }
        finally
        {
            _admit.Release();
        }
    }

    private async Task ReleaseAsync(string? scope)
    {
        if (options.CurrentValue.EffectiveMode == OxylabsMode.None) return;
        await _admit.WaitAsync();
        try
        {
            _pending = Math.Max(0, _pending - 1);
            if (scope is not null && _pendingByScope.TryGetValue(scope, out var p))
            {
                if (p <= 1) _pendingByScope.Remove(scope);
                else _pendingByScope[scope] = p - 1;
            }
        }
        finally
        {
            _admit.Release();
        }
    }

    private async Task RecordAsync(string kind, string? scope, string request, WebCallOutcome outcome, bool fromCache, bool live,
        long latencyMs, string? error, CancellationToken ct)
    {
        try
        {
            await store.RecordAsync(new WebCallRecord
            {
                At = time.GetUtcNow(), Scope = scope, Kind = kind, Outcome = outcome, FromCache = fromCache, Live = live,
                Request = Trim(request, 2000), LatencyMs = latencyMs, Error = error is null ? null : Trim(error, 1000),
            }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "Could not write the web spread ledger");
        }
    }

    /// <summary>Provider mode is part of the key: the two Oxylabs products answer differently.</summary>
    private static string Key(string kind, OxylabsOptions opt, params string[] parts)
    {
        var text = string.Join('\u001f', [kind, opt.EffectiveMode.ToString(), opt.SearchSource, .. parts]);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    private static string Trim(string s, int max) => s.Length > max ? s[..max] : s;
}
