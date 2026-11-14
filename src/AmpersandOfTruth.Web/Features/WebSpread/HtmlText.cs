using System.Net;
using System.Text.RegularExpressions;

namespace AmpersandOfTruth.Web.Features.WebSpread;

/// <summary>
/// Crude, dependency-free HTML → plain text for fetched pages (P3.3). Good enough for claim matching and quoting:
/// drops scripts, styles and page chrome (nav, header, footer, aside, forms), keeps block structure as line breaks,
/// decodes entities. The output is untrusted text — it is never rendered as HTML.
/// </summary>
public static partial class HtmlText
{
    public static string ToText(string? html, int maxChars = int.MaxValue)
    {
        if (string.IsNullOrEmpty(html)) return "";

        var s = DropElements().Replace(html, " ");
        s = Comments().Replace(s, " ");
        s = BlockTags().Replace(s, "\n");
        s = Tags().Replace(s, " ");
        s = WebUtility.HtmlDecode(s);
        s = HorizontalSpace().Replace(s, " ");
        s = BlankLines().Replace(s, "\n\n");
        s = s.Trim();
        return s.Length > maxChars ? s[..maxChars] : s;
    }

    /// <summary>The page title: <c>og:title</c>, else <c>&lt;title&gt;</c>.</summary>
    public static string? Title(string? html)
    {
        if (string.IsNullOrEmpty(html)) return null;
        var og = OgTitle().Match(html);
        var raw = og.Success ? og.Groups["v"].Value : TitleTag().Match(html) is { Success: true } t ? t.Groups["v"].Value : null;
        if (raw is null) return null;
        var title = HorizontalSpace().Replace(WebUtility.HtmlDecode(Tags().Replace(raw, " ")), " ").Trim();
        return title.Length == 0 ? null : title;
    }

    [GeneratedRegex(@"<(head|script|style|noscript|template|svg|nav|header|footer|aside|form|iframe)\b[^>]*>.*?</\1\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex DropElements();

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex Comments();

    [GeneratedRegex(@"<(br|/?p|/?div|/?li|/?ul|/?ol|/?h[1-6]|/?tr|/?table|/?blockquote|/?article|/?section|/?pre)\b[^>]*>",
        RegexOptions.IgnoreCase)]
    private static partial Regex BlockTags();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"[ \t\r\f\v ]+")]
    private static partial Regex HorizontalSpace();

    [GeneratedRegex(@"\s*\n\s*\n\s*(\n\s*)*")]
    private static partial Regex BlankLines();

    [GeneratedRegex(@"<meta\s[^>]*property\s*=\s*[""']og:title[""'][^>]*content\s*=\s*[""'](?<v>[^""']*)[""']", RegexOptions.IgnoreCase)]
    private static partial Regex OgTitle();

    [GeneratedRegex(@"<title\b[^>]*>(?<v>.*?)</title\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TitleTag();
}
