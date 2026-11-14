namespace AmpersandOfTruth.Web.Features.WebSpread;

public enum UrlKind
{
    Page,
    /// <summary>Wikipedia/Wikimedia itself: never fetched, never a "source".</summary>
    Wikipedia,
    /// <summary>A known Wikipedia mirror: the plainest "picked up from Wikipedia" evidence (P3.6), kept as its own bucket;
    /// only the first few per trace are fetched.</summary>
    Mirror,
    /// <summary>A file download (PDF, office, media, archive): not fetched.</summary>
    File,
    /// <summary>Not an http(s) URL.</summary>
    Invalid,
}

/// <summary>The skip list (P3.3): what not to fetch, and which hosts are Wikipedia mirrors. Pure; thread-safe.</summary>
public static class UrlPolicy
{
    private static readonly string[] WikipediaHosts = ["wikipedia.org", "wikimedia.org", "wikidata.org", "wikiquote.org", "wikisource.org"];

    /// <summary>Sites that republish Wikipedia articles. Matched on the host and its subdomains.</summary>
    public static readonly IReadOnlyList<string> MirrorHosts =
    [
        "wikiwand.com", "wiki2.org", "wikizero.com", "wikibrief.org", "unionpedia.org", "dbpedia.org",
        "wikimili.com", "everybodywiki.com", "justapedia.org", "infogalactic.com", "alchetron.com",
        "en-academic.com", "academic.ru", "wiki5.ru", "linkfang.org",
    ];

    private static readonly HashSet<string> FileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".odt", ".rtf", ".zip", ".gz", ".rar", ".7z",
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".svg", ".mp3", ".mp4", ".avi", ".mov", ".webm", ".epub", ".csv",
    };

    public static UrlKind Classify(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return UrlKind.Invalid;
        var host = uri.Host.ToLowerInvariant();
        if (WikipediaHosts.Any(h => HostIs(host, h))) return UrlKind.Wikipedia;
        if (MirrorHosts.Any(h => HostIs(host, h))) return UrlKind.Mirror;
        if (FileExtensions.Contains(Path.GetExtension(uri.AbsolutePath))) return UrlKind.File;
        // Repository downloads without an extension (DSpace /bitstream(s)/…: theses, PDFs; Oxylabs answers RESPONSE_TOO_BIG).
        if (uri.AbsolutePath.Contains("/bitstream/", StringComparison.OrdinalIgnoreCase)
            || uri.AbsolutePath.Contains("/bitstreams/", StringComparison.OrdinalIgnoreCase)) return UrlKind.File;
        return UrlKind.Page;
    }

    /// <summary>The registrable-ish domain for display and grouping (<c>www.</c> stripped).</summary>
    public static string Domain(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
            ? uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..].ToLowerInvariant() : uri.Host.ToLowerInvariant()
            : url;

    /// <summary>Canonical form for de-duplication: scheme + host without <c>www.</c> + path without a trailing slash,
    /// query kept, fragment and common tracking parameters dropped.</summary>
    public static string Normalize(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return url.Trim();
        var query = string.Join('&', uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => !p.StartsWith("utm_", StringComparison.OrdinalIgnoreCase) && !p.StartsWith("fbclid", StringComparison.OrdinalIgnoreCase)
                && !p.StartsWith("gclid", StringComparison.OrdinalIgnoreCase)));
        var path = uri.AbsolutePath.Length > 1 ? uri.AbsolutePath.TrimEnd('/') : "";
        return $"{uri.Scheme}://{Domain(url)}{path}{(query.Length > 0 ? "?" + query : "")}";
    }

    private static bool HostIs(string host, string domain) => host == domain || host.EndsWith("." + domain, StringComparison.Ordinal);
}
