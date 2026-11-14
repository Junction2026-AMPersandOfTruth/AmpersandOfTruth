using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace AmpersandOfTruth.Web.Features.WebSpread.Oxylabs;

/// <summary>
/// The raw Oxylabs integration (P3.3): one HTTP call per search or fetch, with auth, per-attempt timeouts, retries with
/// backoff on 429/5xx/timeouts and an app-wide concurrency limit. No cache and no budget — <see cref="WebSpreadGateway"/>
/// wraps it with both. Never throws for provider problems; never logs credentials. Singleton; thread-safe.
/// </summary>
public sealed class OxylabsClient(
    HttpClient http,
    IOptionsMonitor<OxylabsOptions> options,
    TimeProvider time,
    ILogger<OxylabsClient> log) : IWebSearchClient, IPageFetcher
{
    private SemaphoreSlim? _gate;
    private int _gateSize;
    private readonly Lock _gateLock = new();

    public bool SupportsDateWindow => options.CurrentValue.EffectiveMode == OxylabsMode.ScraperApi;

    public async Task<WebSearchResponse> SearchAsync(WebSearchRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var opt = options.CurrentValue;
        var mode = opt.EffectiveMode;
        if (mode == OxylabsMode.None) return WebSearchResponse.Fail(WebCallOutcome.NotConfigured, NotConfiguredMessage);

        var (url, body) = mode == OxylabsMode.ScraperApi
            ? (opt.ScraperApiUrl, OxylabsWire.ScraperSearchBody(request, opt.SearchSource))
            : (Combine(opt.WebApiBaseUrl, "search"), OxylabsWire.WebApiSearchBody(request));

        var reply = await SendAsync(mode, url, body, "search", ct);
        if (reply.Error is not null) return WebSearchResponse.Fail(WebCallOutcome.Failed, reply.Error);

        var results = mode == OxylabsMode.ScraperApi
            ? OxylabsWire.ParseScraperSearch(reply.Json, opt.SearchSource.Replace("_search", "", StringComparison.Ordinal))
            : OxylabsWire.ParseWebApiSearch(reply.Json);
        var windowApplied = request.Window is not null && mode == OxylabsMode.ScraperApi;
        return new WebSearchResponse(WebCallOutcome.Ok, results.Take(request.MaxResults).ToList(), windowApplied);
    }

    public async Task<PageFetchResponse> FetchAsync(string url, string? scope = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        var opt = options.CurrentValue;
        var mode = opt.EffectiveMode;
        if (mode == OxylabsMode.None) return PageFetchResponse.Fail(WebCallOutcome.NotConfigured, NotConfiguredMessage);

        var (endpoint, body) = mode == OxylabsMode.ScraperApi
            ? (opt.ScraperApiUrl, OxylabsWire.ScraperFetchBody(url, opt.RenderJavaScript))
            : (Combine(opt.WebApiBaseUrl, "scrape"), OxylabsWire.WebApiScrapeBody(url, opt.RenderJavaScript));

        var reply = await SendAsync(mode, endpoint, body, "fetch", ct);
        if (reply.Error is not null) return PageFetchResponse.Fail(WebCallOutcome.Failed, reply.Error);

        var page = mode == OxylabsMode.ScraperApi
            ? OxylabsWire.ParseScraperFetch(reply.Json, url, opt.MaxPageChars)
            : OxylabsWire.ParseWebApiScrape(reply.Json, url, opt.MaxPageChars);
        return page is null
            ? PageFetchResponse.Fail(WebCallOutcome.Failed, "bad_response: no page content in the Oxylabs reply")
            : new PageFetchResponse(WebCallOutcome.Ok, page);
    }

    public const string NotConfiguredMessage =
        "Oxylabs credentials are not configured (user-secrets Oxylabs:ApiKey, or Oxylabs:Username + Oxylabs:Password).";

    private sealed record Reply(JsonNode? Json, string? Error);

    private async Task<Reply> SendAsync(OxylabsMode mode, string url, JsonObject body, string what, CancellationToken ct)
    {
        var opt = options.CurrentValue;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(body);
        var maxAttempts = Math.Max(1, opt.MaxAttempts);
        var gate = Gate(opt.MaxConcurrency);
        string? lastError = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var started = time.GetTimestamp();
            TimeSpan? retryAfter = null;

            await gate.WaitAsync(ct);
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new ByteArrayContent(bytes) };
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                request.Headers.Authorization = mode == OxylabsMode.ScraperApi
                    ? new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{opt.Username}:{opt.Password}")))
                    : new AuthenticationHeaderValue("Bearer", opt.ApiKey);

                using var timeout = new CancellationTokenSource(opt.RequestTimeout, time);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, linked.Token);
                var status = (int)response.StatusCode;
                var raw = await response.Content.ReadAsStringAsync(linked.Token);
                retryAfter = response.Headers.RetryAfter?.Delta;

                log.LogDebug("Oxylabs {What} attempt {Attempt}/{Max} http={Status} latencyMs={Ms:F0}",
                    what, attempt, maxAttempts, status, time.GetElapsedTime(started).TotalMilliseconds);

                if (response.IsSuccessStatusCode)
                {
                    try { return new Reply(JsonNode.Parse(raw), null); }
                    catch (JsonException) { return new Reply(null, "bad_response: Oxylabs returned invalid JSON"); }
                }

                lastError = $"http_{status}: {Short(raw)}";
                if (status is 401 or 403) return new Reply(null, $"http_{status}: Oxylabs rejected the credentials ({Short(raw)})");
                if (!IsRetryable(status) || ProviderGaveUp(raw)) return new Reply(null, lastError);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                lastError = $"timeout after {opt.RequestTimeout.TotalSeconds.ToString(CultureInfo.InvariantCulture)} s";
            }
            catch (HttpRequestException ex)
            {
                lastError = "network: " + ex.Message;
            }
            finally
            {
                gate.Release();
            }

            if (attempt < maxAttempts)
            {
                var delay = RetryDelay(attempt, retryAfter, opt);
                log.LogWarning("Oxylabs {What} retry {Attempt} after {Error}; waiting {Ms:F0} ms", what, attempt, lastError, delay.TotalMilliseconds);
                await Task.Delay(delay, time, ct);
            }
        }

        return new Reply(null, lastError ?? "failed");
    }

    /// <summary>429, 5xx and Oxylabs' own 6xx job-failure codes are worth retrying.</summary>
    public static bool IsRetryable(int status) => status == 429 || status >= 500;

    /// <summary>Oxylabs already retried the target itself (Web API 500 <c>REQUEST_FAILED_AFTER_TOO_MANY_RETRIES</c>, ~40 s
    /// each; seen on facebook.com in the P3.1 spike). Retrying again only triples the wait for the same failure.</summary>
    public static bool ProviderGaveUp(string body) =>
        body.Contains("REQUEST_FAILED_AFTER_TOO_MANY_RETRIES", StringComparison.Ordinal);

    private static TimeSpan RetryDelay(int attempt, TimeSpan? retryAfter, OxylabsOptions opt)
    {
        var max = opt.MaxRetryDelay > TimeSpan.Zero ? opt.MaxRetryDelay : TimeSpan.Zero;
        if (retryAfter is { } server) return server <= TimeSpan.Zero ? TimeSpan.Zero : server < max ? server : max;
        if (opt.RetryBaseDelay <= TimeSpan.Zero) return TimeSpan.Zero;
        var capMs = Math.Min(max.TotalMilliseconds, opt.RetryBaseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1));
        return TimeSpan.FromMilliseconds(Random.Shared.NextDouble() * capMs);
    }

    private SemaphoreSlim Gate(int size)
    {
        size = Math.Max(1, size);
        lock (_gateLock)
        {
            if (_gate is null || _gateSize != size)
            {
                _gate = new SemaphoreSlim(size, size);
                _gateSize = size;
            }
            return _gate;
        }
    }

    private static string Combine(string baseUrl, string path) => baseUrl.EndsWith('/') ? baseUrl + path : $"{baseUrl}/{path}";

    private static string Short(string raw) => raw.Length > 300 ? raw[..300] + "…" : raw;
}
