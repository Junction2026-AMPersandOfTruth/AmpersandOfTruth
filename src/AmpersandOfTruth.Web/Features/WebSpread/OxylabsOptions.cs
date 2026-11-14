namespace AmpersandOfTruth.Web.Features.WebSpread;

/// <summary>Which Oxylabs product the credentials belong to. Oxylabs sells two with different auth and wire formats.</summary>
public enum OxylabsMode
{
    /// <summary>No credentials: web spread is off (every call returns <see cref="WebCallOutcome.NotConfigured"/>).</summary>
    None,
    /// <summary>Web Scraper API, <c>POST https://realtime.oxylabs.io/v1/queries</c>, HTTP Basic auth (dashboard username +
    /// password). Sources <c>google_search</c> (date range via Google's <c>tbs</c> context) and <c>universal</c> (fetch).</summary>
    ScraperApi,
    /// <summary>Web API, <c>https://webapi.oxylabs.io/v1/search</c> + <c>/v1/scrape</c>, <c>Authorization: Bearer &lt;key&gt;</c>.
    /// Its documented search has no date filter, so every search runs unwindowed.</summary>
    WebApi,
}

/// <summary>
/// Config section <c>Oxylabs</c> (P3.3, #56). Non-secret defaults live in <c>appsettings.json</c>. The secrets are
/// <b>only</b> ever in user-secrets or the environment, never in the repo, logs or screenshots:
/// <code>
/// dotnet user-secrets set "Oxylabs:ApiKey" "&lt;key&gt;" --project src/AmpersandOfTruth.Web                 # Web API
/// dotnet user-secrets set "Oxylabs:Username" "&lt;user&gt;" --project src/AmpersandOfTruth.Web              # Web Scraper API
/// dotnet user-secrets set "Oxylabs:Password" "&lt;password&gt;" --project src/AmpersandOfTruth.Web
/// </code>
/// With both kinds set, <see cref="Mode"/> picks; by default the username/password (date-windowed search) wins.
/// </summary>
public sealed class OxylabsOptions
{
    public const string Section = "Oxylabs";

    /// <summary>Secret (Web API). Sent as <c>Authorization: Bearer</c>.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Secret (Web Scraper API). HTTP Basic auth.</summary>
    public string? Username { get; set; }

    /// <summary>Secret (Web Scraper API). HTTP Basic auth.</summary>
    public string? Password { get; set; }

    /// <summary>Force a product. Null = infer from which credentials are set.</summary>
    public OxylabsMode? Mode { get; set; }

    public string ScraperApiUrl { get; set; } = "https://realtime.oxylabs.io/v1/queries";

    public string WebApiBaseUrl { get; set; } = "https://webapi.oxylabs.io/v1/";

    /// <summary>Web Scraper API search source (<c>google_search</c> or <c>bing_search</c>).</summary>
    public string SearchSource { get; set; } = "google_search";

    /// <summary>Fetch pages with JavaScript rendering (slower, more credits). Off: most news/blog pages are server-rendered.</summary>
    public bool RenderJavaScript { get; set; }

    /// <summary>Max in-flight requests across the whole app.</summary>
    public int MaxConcurrency { get; set; } = 6;

    /// <summary>Total HTTP attempts for retryable failures (429, 5xx, timeouts, network errors).</summary>
    public int MaxAttempts { get; set; } = 3;

    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(2);

    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Per attempt. The Web API README advises at least 150 s when JavaScript rendering is on.</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(90);

    /// <summary>Live (non-cached) requests per trace (one incident). Searches + fetches together.</summary>
    public int PerScopeRequestCap { get; set; } = 60;

    /// <summary>Live requests per UTC day, whole app.</summary>
    public int DailyRequestCap { get; set; } = 1_000;

    /// <summary>Fetched pages larger than this (characters of text) are cut; the HTML kept for dating is cut too.</summary>
    public int MaxPageChars { get; set; } = 400_000;

    public bool CacheEnabled { get; set; } = true;

    /// <summary>How long a fetch that failed for good (Oxylabs gave up on it, or rejected it with a 4xx; see
    /// <c>OxylabsClient.IsPermanentFailure</c>) is replayed as a failure instead of fetched again. Transient failures
    /// (timeouts, 429, 5xx, network) are never cached. Zero = off.</summary>
    public TimeSpan GaveUpCacheTime { get; set; } = TimeSpan.FromHours(24);

    public OxylabsMode EffectiveMode =>
        Mode ?? (!string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(Password) ? OxylabsMode.ScraperApi
            : !string.IsNullOrWhiteSpace(ApiKey) ? OxylabsMode.WebApi
            : OxylabsMode.None);
}
