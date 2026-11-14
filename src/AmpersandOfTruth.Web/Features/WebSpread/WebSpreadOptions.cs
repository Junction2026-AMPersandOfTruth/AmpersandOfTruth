namespace AmpersandOfTruth.Web.Features.WebSpread;

/// <summary>Config section <c>WebSpread</c>: how a trace plans and spends (P3.2 / P3.6). Provider settings are in <see cref="OxylabsOptions"/>.</summary>
public sealed class WebSpreadOptions
{
    public const string Section = "WebSpread";

    /// <summary>Languages for keyword/translation queries. P3.2's default plan: 2 languages.</summary>
    public List<string> Languages { get; set; } = ["fi", "en"];

    /// <summary>Hard cap on searches per trace, after the plan multiplies queries × windows (default 2 × 2 × 3 = 12).</summary>
    public int MaxSearches { get; set; } = 12;

    public int ResultsPerSearch { get; set; } = 10;

    /// <summary>Distinct pages fetched per trace (best-ranked first). Mirrors count separately.</summary>
    public int MaxPagesToFetch { get; set; } = 25;

    /// <summary>Known Wikipedia mirrors fetched per trace (the rest are listed unfetched).</summary>
    public int MaxMirrorsToFetch { get; set; } = 3;

    /// <summary>Pages read in parallel within one trace (the provider has its own app-wide limit too).</summary>
    public int FetchParallelism { get; set; } = 4;

    /// <summary>Append <c>-site:wikipedia.org</c> to queries so Wikipedia itself doesn't fill the results.</summary>
    public bool ExcludeWikipedia { get; set; } = true;

    /// <summary>Ask the LLM (<c>plan-claim-search</c>) for keyword and cross-language queries. Off = deterministic fragments only.</summary>
    public bool UseLlmPlan { get; set; } = true;

    /// <summary>Ask the LLM (<c>match-claim</c>) for stance on pages with a candidate passage. Off = lexical matching only.</summary>
    public bool UseLlmMatch { get; set; } = true;

    /// <summary>Look up the Internet Archive's first capture for every matching page (P3.5 source 2).</summary>
    public bool UseArchive { get; set; } = true;

    /// <summary>Words in the distinctive fragment used as the exact-phrase query.</summary>
    public int FragmentMinWords { get; set; } = 5;

    public int FragmentMaxWords { get; set; } = 8;
}
