using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AmpersandOfTruth.Web.Features.WebSpread.Dating;

/// <summary>
/// P3.5 sources 1, 3, 5 (+ archive first capture when given): structured metadata, the URL path and the SERP date.
/// Not yet: visible byline dates (regex/LLM), forum timestamps, and the archive snapshot check for the claim itself.
/// <para>Confidence: metadata alone is <b>medium</b> at best — a page published before the Wikipedia edit may have had the
/// claim added later, which only an archive check can rule out (then <b>high</b>). A URL date or SERP date alone is <b>low</b>.
/// Candidates more than <see cref="ConflictDays"/> apart are listed as conflicts and lower the confidence one step.</para>
/// Pure; thread-safe.
/// </summary>
public static partial class PageDateExtractor
{
    public const int ConflictDays = 2;

    public static PageDating Extract(string url, string? html, DateOnly? serpDate = null, DateOnly? archiveFirstCapture = null,
        DateOnly? archiveWithClaim = null)
    {
        var c = new List<DateCandidate>();
        if (archiveWithClaim is { } awc) c.Add(new(awc, DateMethod.ArchiveWithClaim));
        if (!string.IsNullOrEmpty(html))
        {
            foreach (var (key, value) in JsonLdDates(html))
            {
                if (ParseDate(value) is not { } d) continue;
                c.Add(new(d, key == "datePublished" || key == "dateCreated" ? DateMethod.JsonLdPublished : DateMethod.Modified));
            }
            foreach (Match m in MetaTag().Matches(html))
            {
                var name = (m.Groups["n1"].Success ? m.Groups["n1"].Value : m.Groups["n2"].Value).ToLowerInvariant();
                var content = m.Groups["c1"].Success ? m.Groups["c1"].Value : m.Groups["c2"].Value;
                if (ParseDate(content) is not { } d) continue;
                if (PublishedMetaNames.Contains(name)) c.Add(new(d, DateMethod.MetaPublished));
                else if (ModifiedMetaNames.Contains(name)) c.Add(new(d, DateMethod.Modified));
            }
            foreach (Match m in ItempropPublished().Matches(html))
                if (ParseDate(m.Groups["v"].Value) is { } d) c.Add(new(d, DateMethod.MetaPublished));
            if (TimeElement().Match(html) is { Success: true } t && ParseDate(t.Groups["v"].Value) is { } td)
                c.Add(new(td, DateMethod.TimeElement));
        }
        if (archiveFirstCapture is { } afc) c.Add(new(afc, DateMethod.ArchiveFirstCapture));
        if (UrlDate(url) is { } ud) c.Add(new(ud, DateMethod.UrlPath));
        if (serpDate is { } sd) c.Add(new(sd, DateMethod.SerpDate));

        var candidates = c.Distinct().OrderBy(x => x.Method).ThenBy(x => x.Date).ToList();
        var publishing = candidates.Where(x => x.Method is not DateMethod.Modified and not DateMethod.ArchiveFirstCapture
            and not DateMethod.ArchiveWithClaim).ToList();
        var best = publishing.FirstOrDefault();

        // An archive capture is an upper bound: the page existed by then. A "published" date after it is wrong.
        if (best is not null && archiveFirstCapture is { } cap && best.Date > cap.AddDays(ConflictDays))
            best = new DateCandidate(cap, DateMethod.ArchiveFirstCapture);
        if (best is null && archiveFirstCapture is { } onlyCap) best = new DateCandidate(onlyCap, DateMethod.ArchiveFirstCapture);

        var conflicts = new List<string>();
        if (best is not null)
        {
            foreach (var other in publishing.Where(x => x != best && Math.Abs(x.Date.DayNumber - best.Date.DayNumber) > ConflictDays))
                conflicts.Add($"{Label(other.Method)} says {other.Date:yyyy-MM-dd}, {Label(best.Method)} says {best.Date:yyyy-MM-dd}");
        }

        var confidence = archiveWithClaim is not null ? DateConfidence.High
            : best?.Method switch
            {
                DateMethod.JsonLdPublished or DateMethod.MetaPublished or DateMethod.TimeElement => DateConfidence.Medium,
                DateMethod.ArchiveFirstCapture => DateConfidence.Medium,
                null => DateConfidence.Low,
                _ => DateConfidence.Low,
            };
        if (conflicts.Count > 0 && confidence > DateConfidence.Low) confidence--;

        return new PageDating(best?.Date, archiveFirstCapture, archiveWithClaim, best?.Method, confidence, candidates, conflicts);
    }

    public static string Label(DateMethod m) => m switch
    {
        DateMethod.ArchiveWithClaim => "the web archive (claim present)",
        DateMethod.JsonLdPublished => "structured data",
        DateMethod.MetaPublished => "page metadata",
        DateMethod.ArchiveFirstCapture => "the web archive's first capture",
        DateMethod.TimeElement => "a date on the page",
        DateMethod.UrlPath => "the URL",
        DateMethod.SerpDate => "the search engine",
        DateMethod.Modified => "the last-modified date",
        _ => m.ToString(),
    };

    private static readonly HashSet<string> PublishedMetaNames =
    [
        "article:published_time", "og:published_time", "datepublished", "date", "dc.date", "dc.date.issued", "dcterms.date",
        "dcterms.created", "dcterms.issued", "pubdate", "publishdate", "publish-date", "sailthru.date", "parsely-pub-date",
        "citation_publication_date", "citation_date",
    ];

    private static readonly HashSet<string> ModifiedMetaNames =
        ["article:modified_time", "og:updated_time", "datemodified", "dcterms.modified", "last-modified"];

    /// <summary>Every <c>datePublished</c>/<c>dateCreated</c>/<c>dateModified</c> in the page's JSON-LD blocks (any depth, @graph included).</summary>
    public static IEnumerable<(string Key, string Value)> JsonLdDates(string html)
    {
        var found = new List<(string, string)>();
        foreach (Match m in JsonLd().Matches(html))
        {
            try
            {
                using var doc = JsonDocument.Parse(m.Groups["j"].Value, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
                Walk(doc.RootElement, found, 0);
            }
            catch (JsonException)
            {
                // Broken JSON-LD is common; skip the block.
            }
        }
        return found;
    }

    private static void Walk(JsonElement e, List<(string, string)> found, int depth)
    {
        if (depth > 8) return;
        if (e.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in e.EnumerateObject())
            {
                if (p.Value.ValueKind == JsonValueKind.String && p.Name is "datePublished" or "dateCreated" or "dateModified")
                    found.Add((p.Name, p.Value.GetString()!));
                else Walk(p.Value, found, depth + 1);
            }
        }
        else if (e.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in e.EnumerateArray()) Walk(item, found, depth + 1);
        }
    }

    public static DateOnly? ParseDate(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim();
        if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto)
            && dto.Year is >= 1990 and <= 2100)
            return DateOnly.FromDateTime(dto.UtcDateTime);
        if (DateTime.TryParseExact(s, ["d.M.yyyy", "yyyyMMdd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt) && dt.Year >= 1990)
            return DateOnly.FromDateTime(dt);
        return null;
    }

    /// <summary><c>/2014/03/05/…</c>, <c>/2014/03/…</c> or <c>/2014-03-05…</c> in the path. A year alone is too weak to use.</summary>
    public static DateOnly? UrlDate(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        var m = UrlYmd().Match(uri.AbsolutePath);
        if (!m.Success) return null;
        var y = int.Parse(m.Groups["y"].Value, CultureInfo.InvariantCulture);
        var mo = int.Parse(m.Groups["m"].Value, CultureInfo.InvariantCulture);
        var d = m.Groups["d"].Success ? int.Parse(m.Groups["d"].Value, CultureInfo.InvariantCulture) : 1;
        if (y is < 1995 or > 2100 || mo is < 1 or > 12 || d < 1 || d > DateTime.DaysInMonth(y, mo)) return null;
        return new DateOnly(y, mo, d);
    }

    [GeneratedRegex(@"<script[^>]*type\s*=\s*[""']application/ld\+json[""'][^>]*>(?<j>.*?)</script>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex JsonLd();

    // <meta property|name|itemprop="…" content="…"> in either attribute order.
    [GeneratedRegex(@"<meta\s[^>]*?(?:(?:property|name|itemprop)\s*=\s*[""'](?<n1>[^""']+)[""'][^>]*?content\s*=\s*[""'](?<c1>[^""']*)[""']|content\s*=\s*[""'](?<c2>[^""']*)[""'][^>]*?(?:property|name|itemprop)\s*=\s*[""'](?<n2>[^""']+)[""'])",
        RegexOptions.IgnoreCase)]
    private static partial Regex MetaTag();

    [GeneratedRegex(@"itemprop\s*=\s*[""']datePublished[""'][^>]*?datetime\s*=\s*[""'](?<v>[^""']+)[""']", RegexOptions.IgnoreCase)]
    private static partial Regex ItempropPublished();

    [GeneratedRegex(@"<time\b[^>]*datetime\s*=\s*[""'](?<v>[^""']+)[""']", RegexOptions.IgnoreCase)]
    private static partial Regex TimeElement();

    [GeneratedRegex(@"/(?<y>(19|20)\d{2})[/-](?<m>\d{1,2})(?:[/-](?<d>\d{1,2}))?(?=[/-]|$|\.)")]
    private static partial Regex UrlYmd();
}
