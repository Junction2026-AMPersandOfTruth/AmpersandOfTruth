using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace AmpersandOfTruth.Web.Features.WebSpread.Oxylabs;

/// <summary>
/// Request bodies and response parsing for both Oxylabs products — pure functions, unit-tested on recorded shapes.
/// <para><b>Verification status</b> (developers.oxylabs.io was unreachable when this was written; sources were the
/// <c>oxylabs/web-scraper-api</c> and <c>oxylabs/web-api</c> GitHub READMEs). Confirmed there: endpoints, auth, the
/// <c>google_search</c>/<c>universal</c> sources, <c>parse</c>, <c>geo_location</c>, <c>render</c>; Web API
/// <c>/v1/search</c> <c>{query, max_results}</c> → <c>results[]{title,url,outline[],metadata.position}</c> and
/// <c>/v1/scrape</c> <c>{url, output[], run_js}</c> → <c>results[0].markdown</c>.
/// <b>Unverified (the P3.1 spike checks them):</b> the <c>tbs</c> date-range context key and its format, the
/// <c>results_language</c> context key, the parsed path <c>results[0].content.results.organic[]</c> and its
/// <c>pos/url/title/desc</c> fields, a per-result date field, and <c>results[0].html</c> on the Web API.
/// The parsers are tolerant (they look for the fields under a few names) so a small shape difference degrades, not breaks.</para>
/// </summary>
public static partial class OxylabsWire
{
    // ── Web Scraper API (realtime) ──

    public static JsonObject ScraperSearchBody(WebSearchRequest r, string source)
    {
        var body = new JsonObject
        {
            ["source"] = source,
            ["query"] = r.Query,
            ["parse"] = true,
            ["limit"] = Math.Clamp(r.MaxResults, 1, 100),
        };
        if (GoogleDomain(r.Language) is { } domain) body["domain"] = domain;
        if (GeoLocation(r.Language) is { } geo) body["geo_location"] = geo;

        var context = new JsonArray { new JsonObject { ["key"] = "results_language", ["value"] = r.Language } };
        if (r.Window is { } w && (w.From is not null || w.To is not null))
        {
            context.Add(new JsonObject { ["key"] = "tbs", ["value"] = GoogleDateRange(w) });
        }
        body["context"] = context;
        return body;
    }

    public static JsonObject ScraperFetchBody(string url, bool renderJs)
    {
        var body = new JsonObject { ["source"] = "universal", ["url"] = url };
        if (renderJs) body["render"] = "html";
        return body;
    }

    /// <summary>Google's custom date range, <c>cdr:1,cd_min:M/D/YYYY,cd_max:M/D/YYYY</c>. Open ends are left out.</summary>
    public static string GoogleDateRange(DateWindow w)
    {
        var parts = new List<string> { "cdr:1" };
        if (w.From is { } from) parts.Add("cd_min:" + from.ToString("M/d/yyyy", CultureInfo.InvariantCulture));
        if (w.To is { } to) parts.Add("cd_max:" + to.ToString("M/d/yyyy", CultureInfo.InvariantCulture));
        return string.Join(',', parts);
    }

    /// <summary>Parses a parsed <c>google_search</c>/<c>bing_search</c> response into organic results.</summary>
    public static IReadOnlyList<WebSearchResult> ParseScraperSearch(JsonNode? root, string engine)
    {
        var content = root?["results"]?.AsArray().FirstOrDefault()?["content"];
        var organic = content?["results"]?["organic"] as JsonArray ?? content?["organic"] as JsonArray;
        if (organic is null) return [];

        var results = new List<WebSearchResult>();
        foreach (var item in organic)
        {
            if (item is not JsonObject o || Str(o, "url") is not { } url) continue;
            var desc = Str(o, "desc") ?? Str(o, "description") ?? Str(o, "snippet");
            var date = ParseLooseDate(Str(o, "date")) ?? ParseLeadingDate(desc);
            var rank = Int(o, "pos") ?? Int(o, "position") ?? results.Count + 1;
            results.Add(new WebSearchResult(url, Str(o, "title"), desc, date, rank, engine));
        }
        return results;
    }

    /// <summary>A <c>universal</c> fetch: <c>results[0].content</c> is the HTML string.</summary>
    public static FetchedPage? ParseScraperFetch(JsonNode? root, string url, int maxChars)
    {
        var first = root?["results"]?.AsArray().FirstOrDefault();
        if (first?["content"] is not JsonValue v || !v.TryGetValue<string>(out var html)) return null;
        var status = first["status_code"] is JsonValue s && s.TryGetValue<int>(out var code) ? code : (int?)null;
        var finalUrl = first["url"] is JsonValue u && u.TryGetValue<string>(out var fu) ? fu : null;
        return new FetchedPage(url, finalUrl, status, HtmlText.Title(html), HtmlText.ToText(html, maxChars), Cap(html, maxChars));
    }

    // ── Web API ──

    public static JsonObject WebApiSearchBody(WebSearchRequest r) => new()
    {
        ["query"] = r.Query,
        ["max_results"] = Math.Clamp(r.MaxResults, 1, 100),
    };

    public static JsonObject WebApiScrapeBody(string url, bool renderJs) => new()
    {
        ["url"] = url,
        ["output"] = new JsonArray("markdown", "html"),
        ["run_js"] = renderJs,
    };

    public static IReadOnlyList<WebSearchResult> ParseWebApiSearch(JsonNode? root)
    {
        if (root?["results"] is not JsonArray items) return [];
        var results = new List<WebSearchResult>();
        foreach (var item in items)
        {
            if (item is not JsonObject o || Str(o, "url") is not { } url) continue;
            var snippet = StrOrJoined(o, "outline") ?? Str(o, "snippet") ?? Str(o, "desc");
            var rank = (o["metadata"] as JsonObject) is { } m ? Int(m, "position") : null;
            var date = ParseLooseDate(Str(o, "date") ?? Str(o, "published_at")) ?? ParseLeadingDate(snippet);
            results.Add(new WebSearchResult(url, Str(o, "title"), snippet, date, rank ?? results.Count + 1, "oxylabs-web-api"));
        }
        return results;
    }

    public static FetchedPage? ParseWebApiScrape(JsonNode? root, string url, int maxChars)
    {
        if (root?["results"]?.AsArray().FirstOrDefault() is not JsonObject first) return null;
        var html = Str(first, "html") ?? Str(first, "content");
        var markdown = Str(first, "markdown");
        if (html is null && markdown is null) return null;
        var text = markdown ?? HtmlText.ToText(html, maxChars);
        var status = Int(first, "status_code");
        return new FetchedPage(url, Str(first, "url"), status, HtmlText.Title(html), Cap(text, maxChars), html is null ? null : Cap(html, maxChars));
    }

    // ── Helpers ──

    /// <summary>Google ccTLD per search language, so Finnish queries hit google.fi etc.</summary>
    public static string? GoogleDomain(string language) => language switch
    {
        "fi" => "fi",
        "sv" => "se",
        "ru" => "ru",
        "en" => "com",
        _ => null,
    };

    /// <summary>Country for <c>geo_location</c>, matching the language's main audience.</summary>
    public static string? GeoLocation(string language) => language switch
    {
        "fi" => "Finland",
        "sv" => "Sweden",
        "ru" => "Russia",
        "en" => "United States",
        _ => null,
    };

    private static string Cap(string s, int max) => s.Length > max ? s[..max] : s;

    private static string? Str(JsonObject o, string key) =>
        o[key] is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s) ? s : null;

    /// <summary>A string, or an array of strings joined with " … " (the Web API's <c>outline</c> is an array — seen in the
    /// P3.1 spike, 2026-11-14; its first item carries Google's leading page date).</summary>
    private static string? StrOrJoined(JsonObject o, string key)
    {
        if (o[key] is not JsonArray a) return Str(o, key);
        var parts = a.OfType<JsonValue>()
            .Select(v => v.TryGetValue<string>(out var s) ? s.Trim() : null)
            .Where(s => !string.IsNullOrEmpty(s))
            .ToArray();
        return parts.Length > 0 ? string.Join(" … ", parts) : null;
    }

    private static int? Int(JsonObject o, string key)
    {
        if (o[key] is not JsonValue v) return null;
        if (v.TryGetValue<int>(out var i)) return i;
        if (v.TryGetValue<double>(out var d)) return (int)d;
        return v.TryGetValue<string>(out var s) && int.TryParse(s, CultureInfo.InvariantCulture, out var p) ? p : null;
    }

    /// <summary>ISO dates and "Mar 3, 2014"-style dates.</summary>
    public static DateOnly? ParseLooseDate(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto))
            return DateOnly.FromDateTime(dto.UtcDateTime);
        return ParseLeadingDate(s);
    }

    /// <summary>Google prefixes many snippets with the page date: <c>"Mar 3, 2014 — …"</c> or Finnish <c>"3.3.2014 — …"</c>.</summary>
    public static DateOnly? ParseLeadingDate(string? snippet)
    {
        if (string.IsNullOrWhiteSpace(snippet)) return null;
        var m = LeadingEnglishDate().Match(snippet);
        if (m.Success && DateTime.TryParseExact(m.Groups["d"].Value.Replace(".", ""), ["MMM d, yyyy", "MMMM d, yyyy", "d MMM yyyy"],
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var en))
            return DateOnly.FromDateTime(en);
        m = LeadingNumericDate().Match(snippet);
        if (m.Success && DateTime.TryParseExact(m.Groups["d"].Value, "d.M.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fi))
            return DateOnly.FromDateTime(fi);
        return null;
    }

    [GeneratedRegex(@"^\s*(?<d>[A-Z][a-z]{2,8}\.? \d{1,2}, \d{4}|\d{1,2} [A-Z][a-z]{2,8} \d{4})\s*[—–-]")]
    private static partial Regex LeadingEnglishDate();

    [GeneratedRegex(@"^\s*(?<d>\d{1,2}\.\d{1,2}\.\d{4})\s*[—–-]")]
    private static partial Regex LeadingNumericDate();
}
