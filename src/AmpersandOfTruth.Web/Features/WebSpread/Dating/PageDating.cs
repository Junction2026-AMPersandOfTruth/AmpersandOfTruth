namespace AmpersandOfTruth.Web.Features.WebSpread.Dating;

/// <summary>Where a date came from, roughly in P3.5's order of trust.</summary>
public enum DateMethod
{
    /// <summary>The earliest Internet Archive capture that already contains the claim (strongest; not wired yet).</summary>
    ArchiveWithClaim,
    /// <summary>JSON-LD <c>datePublished</c>.</summary>
    JsonLdPublished,
    /// <summary><c>article:published_time</c>, Dublin Core, <c>itemprop=datePublished</c>, <c>name=date</c> meta tags.</summary>
    MetaPublished,
    /// <summary>The earliest Internet Archive capture of the URL: the page existed by then (an upper bound).</summary>
    ArchiveFirstCapture,
    /// <summary>A <c>&lt;time datetime&gt;</c> element.</summary>
    TimeElement,
    /// <summary><c>/2014/03/…</c> in the URL path.</summary>
    UrlPath,
    /// <summary>The date the search engine showed (convenient, frequently wrong).</summary>
    SerpDate,
    /// <summary><c>dateModified</c> / <c>article:modified_time</c>: never the publication date, kept to show conflicts.</summary>
    Modified,
}

public enum DateConfidence { Low, Medium, High }

public sealed record DateCandidate(DateOnly Date, DateMethod Method);

/// <summary>P3.5 output per page. Every candidate is kept; disagreement is reported, not resolved silently.</summary>
/// <param name="PublishedAt">The chosen publication day, or null (undated → its own bucket, never guessed).</param>
/// <param name="ClaimPresentSince">The earliest day the claim is known to be on the page (archive check), when known.</param>
/// <param name="Conflicts">Human-readable disagreements, e.g. "URL path says 2014-03-01, metadata says 2016-05-02".</param>
public sealed record PageDating(
    DateOnly? PublishedAt,
    DateOnly? FirstSeenAt,
    DateOnly? ClaimPresentSince,
    DateMethod? Method,
    DateConfidence Confidence,
    IReadOnlyList<DateCandidate> Candidates,
    IReadOnlyList<string> Conflicts)
{
    public static readonly PageDating Undated = new(null, null, null, null, DateConfidence.Low, [], []);

    /// <summary>The day used for before/during/after: when the claim was known present, else publication.</summary>
    public DateOnly? EffectiveDate => ClaimPresentSince ?? PublishedAt;
}
