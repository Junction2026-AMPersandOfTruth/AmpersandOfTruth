namespace AmpersandOfTruth.Web.Features.WebSpread;

/// <summary>A closed date range in UTC days, both ends inclusive. Either end may be open (null).</summary>
public sealed record DateWindow(DateOnly? From, DateOnly? To)
{
    public bool Contains(DateOnly day) => (From is null || day >= From) && (To is null || day <= To);

    public override string ToString() => $"{From?.ToString("yyyy-MM-dd") ?? "…"}..{To?.ToString("yyyy-MM-dd") ?? "…"}";
}

/// <summary>One web search (P3.3). <paramref name="Language"/> is an ISO 639-1 code (<c>fi</c>, <c>en</c>, <c>sv</c>, <c>ru</c>).</summary>
/// <param name="Window">Restrict to pages the engine dates inside this window. Ignored (and reported via
/// <see cref="WebSearchResponse.WindowApplied"/>) when the provider can't date-restrict.</param>
/// <param name="MaxResults">Results wanted (one SERP page is ~10).</param>
public sealed record WebSearchRequest(string Query, string Language, DateWindow? Window = null, int MaxResults = 10)
{
    /// <summary>Budget/ledger scope: one trace (one incident), like <c>AnalysisRequest.Scope</c>.</summary>
    public string? Scope { get; init; }
}

/// <param name="SerpDate">The date the engine shows for the result, if any — convenient and often wrong (P3.5 trust level 5).</param>
/// <param name="Rank">1-based position in the engine's organic results.</param>
public sealed record WebSearchResult(string Url, string? Title, string? Snippet, DateOnly? SerpDate, int Rank, string Engine);

/// <summary>Outcome of a search or fetch. Never an exception for provider-side problems (like <c>AnalysisOutcome</c>).</summary>
public enum WebCallOutcome
{
    Ok,
    /// <summary>No credentials configured — the app runs fine, web spread is just off.</summary>
    NotConfigured,
    /// <summary>A request cap (per trace or per day) is hit; fail closed with a clear message.</summary>
    BudgetExceeded,
    /// <summary>The URL is on the skip list (Wikipedia, mirrors beyond the first few, files, oversized pages).</summary>
    Skipped,
    /// <summary>HTTP/provider error after retries, timeout, unparseable response.</summary>
    Failed,
}

/// <param name="WindowApplied">False when a window was asked for but the provider searched without it.</param>
public sealed record WebSearchResponse(
    WebCallOutcome Outcome,
    IReadOnlyList<WebSearchResult> Results,
    bool WindowApplied,
    string? Error = null)
{
    public bool FromCache { get; init; }

    public static WebSearchResponse Fail(WebCallOutcome outcome, string error) => new(outcome, [], false, error);
}

/// <summary>A fetched page. <see cref="Text"/> is plain text (Markdown when the provider gives it) and is <b>untrusted</b>:
/// never rendered as HTML in our UI, always passed to an LLM inside a <c>{{data:…}}</c> block.</summary>
/// <param name="Html">Raw HTML, kept for date extraction (P3.5). Null when the provider returned only Markdown.</param>
public sealed record FetchedPage(string Url, string? FinalUrl, int? StatusCode, string? Title, string Text, string? Html);

public sealed record PageFetchResponse(WebCallOutcome Outcome, FetchedPage? Page, string? Error = null)
{
    public bool FromCache { get; init; }

    public static PageFetchResponse Fail(WebCallOutcome outcome, string error) => new(outcome, null, error);
}

/// <summary>Web search (P3.3). An interface because it is an external API we must fake in tests.</summary>
public interface IWebSearchClient
{
    /// <summary>True when the provider honours <see cref="WebSearchRequest.Window"/> (the plan then runs one search per window).</summary>
    bool SupportsDateWindow { get; }

    Task<WebSearchResponse> SearchAsync(WebSearchRequest request, CancellationToken ct = default);
}

/// <summary>Page fetch (P3.3). An interface because it is an external API we must fake in tests.</summary>
public interface IPageFetcher
{
    Task<PageFetchResponse> FetchAsync(string url, string? scope = null, CancellationToken ct = default);
}
