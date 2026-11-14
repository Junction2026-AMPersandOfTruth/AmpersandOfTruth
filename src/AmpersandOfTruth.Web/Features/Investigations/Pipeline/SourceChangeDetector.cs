using System.Text.RegularExpressions;
using AmpersandOfTruth.Web.Features.WikiDiff;

namespace AmpersandOfTruth.Web.Features.Investigations.Pipeline;

public enum SourceChangeKind { CitationRemoved, CitationSwapped }

/// <summary>A citation an edit took away or replaced (P1.S8, #35).</summary>
/// <param name="Label">What happened, in plain words: "Removed citation (yle.fi) while keeping or altering text",
/// "Citation replaced: hs.fi (Arkistotutkimus) → example.net (Toinen näkemys)". Shown as the signal's evidence.</param>
/// <param name="Evidence">The citation's wikitext, at most 200 characters: the removed one, or the new one of a swap.</param>
public sealed record SourceChange(SourceChangeKind Kind, string Label, string Evidence);

/// <summary>
/// P1.S8's two citation detectors, ported from <c>research/35-s8-signals/detect_citation_removal.py</c> and
/// <c>detect_citation_swap.py</c> (<c>Fixtures/s8-parity.jsonl</c> + <c>s8_parity.py</c> keep the port identical to the Python).
/// Input is a <see cref="WikitextDiff"/> result; the tier rules read any hit as "came with changes to the cited sources".
/// <list type="bullet">
/// <item><b>Citation removed:</b> a <c>&lt;ref&gt;</c> is gone while its paragraph is still there. Not counted: blanking,
/// a whole paragraph or a whole sentence removed together with its footnote, an empty citation shell, and the same source
/// written differently (named-ref shortening, template reformatting, archive links, a move to another paragraph).</item>
/// <item><b>Citation swapped:</b> a <c>&lt;ref&gt;</c> is replaced in the same paragraph by a different source.</item>
/// </list>
/// Not ported: the Python's handling of references that swallowed a heading or another reference. The diff engine no
/// longer produces those (<see cref="RefTags"/>), so a reference segment is always exactly one reference.
/// Pure and thread-safe.
/// </summary>
public static partial class SourceChangeDetector
{
    private const int MaxEvidenceChars = 200;
    private const int MaxTitleChars = 45;

    /// <param name="before">The wikitext the diff starts from (which named references already existed); null = none.</param>
    public static IReadOnlyList<SourceChange> Detect(string? before, WikitextDiffResult diff)
    {
        ArgumentNullException.ThrowIfNull(diff);
        if (diff.Stats.IsBlanking) return [];

        var segments = diff.Segments.Where(s => !s.Moved).ToList();
        var removed = new List<(int Index, DiffSegment Segment, Citation Ref)>();
        var added = new List<(DiffSegment Segment, Citation Ref)>();
        for (int i = 0; i < segments.Count; i++)
        {
            var s = segments[i];
            if (s.Kind != SegmentKind.Reference) continue;
            if (s.Op == DiffOp.Removed) removed.Add((i, s, Citation.Parse(s.Text)));
            else added.Add((s, Citation.Parse(s.Text)));
        }
        if (removed.Count == 0) return [];

        var oldNames = RefName().Matches(before ?? "").Select(NameOf).Where(n => n.Length > 0).ToHashSet(StringComparer.Ordinal);

        // Both detectors first cancel citations that survived the edit in another form (same paragraph first).
        var keptRemoved = new HashSet<int>();
        var keptAdded = new HashSet<int>();
        foreach (bool sameParagraphOnly in new[] { true, false })
        {
            for (int r = 0; r < removed.Count; r++)
            {
                if (keptRemoved.Contains(r)) continue;
                if (removed[r].Ref.IsEmpty)
                {
                    keptRemoved.Add(r);
                    continue;
                }
                for (int a = 0; a < added.Count; a++)
                {
                    if (keptAdded.Contains(a)) continue;
                    bool sameParagraph = removed[r].Segment.NewParagraph is { } p && p == added[a].Segment.NewParagraph;
                    if (sameParagraphOnly && !sameParagraph) continue;
                    if (!SameSource(removed[r].Ref, added[a].Ref)) continue;
                    keptRemoved.Add(r);
                    keptAdded.Add(a);
                    break;
                }
            }
        }

        var result = new List<SourceChange>();
        result.AddRange(Removals(segments, removed, added, oldNames, keptRemoved, keptAdded));
        result.AddRange(Swaps(removed, added, oldNames, keptRemoved, keptAdded));
        return result;
    }

    private static List<SourceChange> Removals(
        List<DiffSegment> segments, List<(int Index, DiffSegment Segment, Citation Ref)> removed, List<(DiffSegment Segment, Citation Ref)> added,
        HashSet<string> oldNames, HashSet<int> keptRemoved, HashSet<int> keptAdded)
    {
        var usedRemoved = new HashSet<int>(keptRemoved);
        var usedAdded = new HashSet<int>(keptAdded);

        // A different citation put in the same paragraph is a swap, reported by Swaps.
        for (int r = 0; r < removed.Count; r++)
        {
            if (usedRemoved.Contains(r) || removed[r].Segment.NewParagraph is not { } paragraph) continue;
            for (int a = 0; a < added.Count; a++)
            {
                if (usedAdded.Contains(a)) continue;
                if (added[a].Ref.SelfClosing && oldNames.Contains(added[a].Ref.Name)) continue;
                if (added[a].Segment.NewParagraph != paragraph) continue;
                usedRemoved.Add(r);
                usedAdded.Add(a);
                break;
            }
        }

        var result = new List<SourceChange>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int r = 0; r < removed.Count; r++)
        {
            var (index, segment, citation) = removed[r];
            if (usedRemoved.Contains(r) || segment.NewParagraph is not { } paragraph) continue;   // null: the whole paragraph went
            var oldParagraph = segment.OldParagraph;

            // A whole sentence deleted together with its footnote is pruning, not a claim left without its source.
            bool proseAddedHere = segments.Any(s => s.Op == DiffOp.Added && s.Kind == SegmentKind.Prose
                && (s.NewParagraph == paragraph || (oldParagraph is not null && s.OldParagraph == oldParagraph)));
            if (!proseAddedHere && index > 0 && segments[index - 1] is { Op: DiffOp.Removed, Kind: SegmentKind.Prose } previous
                && previous.OldParagraph == oldParagraph)
            {
                var sentence = previous.Prose.Trim();
                if (sentence.Length >= 40 && (char.IsUpper(sentence[0]) || previous.Text.TrimStart().StartsWith('*'))
                    && sentence[^1] is '.' or '!' or '?')
                    continue;
            }

            var evidence = Head(citation.Raw);
            if (!seen.Add(evidence)) continue;
            result.Add(new SourceChange(SourceChangeKind.CitationRemoved,
                citation.Domains.Count > 0
                    ? $"Removed citation ({citation.Domains[0]}) while keeping or altering text"
                    : "Removed citation while keeping or altering surrounding text",
                evidence));
        }
        return result;
    }

    private static List<SourceChange> Swaps(
        List<(int Index, DiffSegment Segment, Citation Ref)> removed, List<(DiffSegment Segment, Citation Ref)> added,
        HashSet<string> oldNames, HashSet<int> keptRemoved, HashSet<int> keptAdded)
    {
        var usedRemoved = new HashSet<int>(keptRemoved);
        var usedAdded = new HashSet<int>(keptAdded);
        var result = new List<SourceChange>();
        var seen = new HashSet<(string, string)>();
        for (int r = 0; r < removed.Count; r++)
        {
            if (usedRemoved.Contains(r) || removed[r].Segment.NewParagraph is not { } paragraph) continue;
            for (int a = 0; a < added.Count; a++)
            {
                var replacement = added[a].Ref;
                if (usedAdded.Contains(a) || replacement.IsEmpty) continue;
                if (replacement.SelfClosing && oldNames.Contains(replacement.Name)) continue;   // reuses a source the page already had
                if (added[a].Segment.NewParagraph != paragraph) continue;
                usedRemoved.Add(r);
                usedAdded.Add(a);
                if (seen.Add((removed[r].Ref.Description, replacement.Description)))
                {
                    result.Add(new SourceChange(SourceChangeKind.CitationSwapped,
                        $"Citation replaced: {removed[r].Ref.Description} → {replacement.Description}", Head(replacement.Raw)));
                }
                break;
            }
        }
        return result;
    }

    private static string Head(string s) => s.Length <= MaxEvidenceChars ? s : s[..MaxEvidenceChars];

    // ── One citation ───────────────────────────────────────────────────────────────────────────────────────────────

    /// <param name="Body">The citation's content without comments.</param>
    /// <param name="Urls">Its URLs, archive wrappers removed.</param>
    /// <param name="Tokens">Its words and numbers, without citation-template names, parameter names and URLs.</param>
    /// <param name="IsEmpty">A shell: no domain and no word (e.g. a template with only an access date).</param>
    private sealed record Citation(
        string Raw, string Name, bool SelfClosing, string Body, IReadOnlyList<string> Urls, IReadOnlyList<string> Domains,
        HashSet<string> Tokens, bool IsEmpty, string Description)
    {
        public static Citation Parse(string raw)
        {
            var nameMatch = RefName().Match(raw);
            var name = nameMatch.Success ? NameOf(nameMatch) : "";
            bool selfClosing = SelfClosingTag().IsMatch(raw);
            var body = ClosingTag().Replace(OpeningTag().Replace(raw, "", 1), "", 1);
            var active = Comment().Replace(body, " ");

            var urls = new List<string>();
            var domains = new List<string>();
            foreach (Match m in Url().Matches(active))
            {
                var url = m.Value.TrimEnd('.', ',', ';', ':', '!', '?', ')');
                var archived = ArchiveWrapper().Match(url);
                if (archived.Success) url = archived.Groups[1].Value;
                urls.Add(url);
                var domain = Host(url);
                if (domain.StartsWith("www.", StringComparison.Ordinal)) domain = domain[4..];
                if (domain.Length > 0 && domain != "web.archive.org" && !domains.Contains(domain)) domains.Add(domain);
            }

            var title = TitleParameter().Match(active) is { Success: true } t ? Display(t.Groups[1].Value) : "";
            if (title.Length == 0 && LabelledLink().Match(active) is { Success: true } link) title = Display(link.Groups[1].Value);

            var cleaned = BareUrl().Replace(ParameterName().Replace(CitationTemplate().Replace(active, " "), " "), " ");
            var tokens = Word().Matches(cleaned.ToLowerInvariant()).Select(m => m.Value).Where(w => !StopTokens.Contains(w)).ToList();
            if (title.Length == 0) title = Display(cleaned);

            bool isEmpty = !selfClosing && domains.Count == 0 && !tokens.Any(w => w.Any(char.IsLetter));
            var description = (domains.Count > 0, title.Length > 0) switch
            {
                (true, true) => $"{domains[0]} ({title})",
                (true, false) => domains[0],
                (false, true) => title,
                _ => name.Length > 0 ? $"ref:{name}" : "citation",
            };
            return new Citation(raw, name, selfClosing, active, urls, domains, tokens.ToHashSet(StringComparer.Ordinal), isEmpty, description);
        }
    }

    /// <summary>The same source written differently: a named reference and its reuse, the old text inside the new, a
    /// shared URL, a shared domain with mostly the same words, or (without URLs) nearly the same words.</summary>
    private static bool SameSource(Citation old, Citation @new)
    {
        if (old.Name.Length > 0 && old.Name == @new.Name && (old.SelfClosing || @new.SelfClosing)) return true;

        var oldCore = Squeeze(old.Body);
        if (oldCore.Length >= 15 && Squeeze(@new.Body).Contains(oldCore, StringComparison.Ordinal)) return true;

        var oldUrls = old.Urls.Select(NormalizeUrl).ToHashSet(StringComparer.Ordinal);
        if (oldUrls.Count > 0 && @new.Urls.Select(NormalizeUrl).Any(oldUrls.Contains)) return true;

        var (a, b) = (old.Tokens, @new.Tokens);
        if (a.Count == 0 || b.Count == 0) return false;
        int shared = a.Count(b.Contains);
        if (old.Domains.Count > 0 && @new.Domains.Count > 0)
            return old.Domains.Any(@new.Domains.Contains) && (double)shared / Math.Min(a.Count, b.Count) >= 0.6;
        if (old.Domains.Count == 0 && @new.Domains.Count == 0)
            return shared == a.Count || shared == b.Count || (double)shared / (a.Count + b.Count - shared) >= 0.85;
        return false;
    }

    private static string NameOf(Match m) =>
        (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value).Trim().ToLowerInvariant();

    private static string Squeeze(string s) => string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string NormalizeUrl(string url) => SchemeAndWww().Replace(url.ToLowerInvariant(), "", 1).TrimEnd('/');

    /// <summary>Host of an http(s) URL, lower-cased, without user info and port (Python's <c>urlparse(u).hostname</c>).</summary>
    private static string Host(string url)
    {
        int start = url.IndexOf("://", StringComparison.Ordinal);
        if (start < 0) return "";
        start += 3;
        int end = url.IndexOfAny(['/', '?', '#'], start);
        var authority = end < 0 ? url[start..] : url[start..end];
        int at = authority.LastIndexOf('@');
        if (at >= 0) authority = authority[(at + 1)..];
        if (authority.StartsWith('['))
        {
            int close = authority.IndexOf(']');
            return close < 0 ? "" : authority[1..close].ToLowerInvariant();
        }
        int colon = authority.IndexOf(':');
        return (colon < 0 ? authority : authority[..colon]).ToLowerInvariant();
    }

    /// <summary>A citation title for a label: link labels kept, markup dropped, at most 45 characters.</summary>
    private static string Display(string text)
    {
        text = Squeeze(MarkupChars().Replace(WikiLink().Replace(text, "$2"), " ")).Trim(' ', ',', '.', ';', ':', '-');
        return text.Length <= MaxTitleChars ? text : text[..(MaxTitleChars - 1)].TrimEnd() + "…";
    }

    private static readonly HashSet<string> StopTokens = new(StringComparer.Ordinal)
    {
        "http", "https", "www", "html", "htm", "pdf", "sivu", "sivut", "painos", "toim",
    };

    private const RegexOptions I = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    [GeneratedRegex("""<ref\b[^>]*?\bname\s*=\s*(?:"([^"]+)"|'([^']+)'|([^\s/>]+))""", I)]
    private static partial Regex RefName();

    [GeneratedRegex(@"^\s*<ref\b[^>]*/>\s*$", I)]
    private static partial Regex SelfClosingTag();

    [GeneratedRegex(@"^\s*<ref\b[^>]*>", I)]
    private static partial Regex OpeningTag();

    [GeneratedRegex(@"</ref\s*>\s*$", I)]
    private static partial Regex ClosingTag();

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex Comment();

    [GeneratedRegex("""https?://[^\s<>\]|}'"]+""", I)]
    private static partial Regex Url();

    [GeneratedRegex(@"^https?://(?:web\.archive\.org/web/\d+[a-z_]*|archive\.(?:is|today|ph)/[^/]+)/(https?://.+)$", I)]
    private static partial Regex ArchiveWrapper();

    [GeneratedRegex(@"\|\s*(?:nimeke|otsikko|title)\s*=\s*([^|}\n]+)", I)]
    private static partial Regex TitleParameter();

    [GeneratedRegex(@"\[https?://[^\s\]]+\s+([^\]]+)\]")]
    private static partial Regex LabelledLink();

    [GeneratedRegex(@"\{\{\s*(?:verkkoviite|kirjaviite|lehtiviite|jaksoviite|cite\s+\w+|wayback)", I)]
    private static partial Regex CitationTemplate();

    [GeneratedRegex(
        @"\b(?:osoite|url|nimeke|otsikko|title|tekijä|author|julkaisu|julkaisija|ajankohta|vuosi|sivu|sivut|viitattu|luettu|kieli|" +
        @"ietf-kielikoodi|arkisto|arkistoitu|tiedostomuoto|selite|tunniste|isbn|issn|kappale|luku|numero|vuosikerta|julkaisupaikka)\s*=", I)]
    private static partial Regex ParameterName();

    [GeneratedRegex(@"https?://\S+")]
    private static partial Regex BareUrl();

    [GeneratedRegex("[0-9a-zA-ZäöåÄÖÅšžŠŽ]{2,}")]
    private static partial Regex Word();

    [GeneratedRegex(@"^https?://(www\.)?")]
    private static partial Regex SchemeAndWww();

    [GeneratedRegex(@"\[\[([^|\]]+\|)?([^\]]+)\]\]")]
    private static partial Regex WikiLink();

    [GeneratedRegex("""[{}|\[\]'"]+""")]
    private static partial Regex MarkupChars();
}
