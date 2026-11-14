using AmpersandOfTruth.Web.Features.Investigations.View;
using AmpersandOfTruth.Web.Features.WikiDiff;

namespace AmpersandOfTruth.Web.Features.TimeMachine;

public enum ProseBlockKind
{
    Paragraph,
    Heading2,
    Heading3,
    BulletItem,
    NumberedItem,
}

public readonly record struct ProsePiece(string Text, bool Highlighted);

public sealed record ProseBlock(ProseBlockKind Kind, IReadOnlyList<ProsePiece> Pieces)
{
    public string PlainText => string.Concat(Pieces.Select(static p => p.Text));
}

public sealed record ProseHighlight(
    bool FoundInProse,
    IReadOnlyList<(int Start, int Length)> Ranges,
    string? FallbackText);

public sealed record TimeMachineComparison(
    DateOnly DateA,
    DateOnly DateB,
    RevisionAt? RevisionA,
    RevisionAt? RevisionB,
    DiffPassage Passage);

public sealed record TimeMachineSnapshot(
    long PageId,
    string? TitleFi,
    string? TitleEn,
    (DateTime First, DateTime Last, int Count) Range,
    DateOnly SelectedDate,
    RevisionAt? Revision,
    bool BeforeFirst,
    string Prose,
    bool IsTruncated,
    IReadOnlyList<ProseBlock> Blocks,
    ProseHighlight? Highlight,
    TimeMachineComparison? Comparison);

public sealed class TimeMachineService(ITimeMachineCorpus corpus)
{
    public const int MaxProseChars = 40_000;

    private static readonly string ParagraphBreak = new([(char)10, (char)10]);
    private static readonly string[] RedirectPrefixes = ["#OHJAUS", "#UUDELLEENOHJAUS", "#REDIRECT"];

    public static DateTime EndOfUtcDay(DateOnly date) =>
        new(date.Year, date.Month, date.Day, 23, 59, 59, DateTimeKind.Utc);

    /// <summary>
    /// Resolves the revision active at <paramref name="atUtc"/> (or the article's latest revision date when null,
    /// or <paramref name="revId"/> if specified), renders its cleaned prose capped at <see cref="MaxProseChars"/>,
    /// and optionally computes insertion highlights and a two-date comparison.
    /// Returns <c>null</c> when the article has no revisions in the corpus.
    /// </summary>
    public async Task<TimeMachineSnapshot?> ReadAtAsync(
        long pageId,
        DateTime? atUtc = null,
        long? revId = null,
        long? highlightRevId = null,
        long? highlightParentRevId = null,
        WikitextDiffResult? fallbackHighlightDiff = null,
        DateOnly? compareDate = null,
        CancellationToken ct = default)
    {
        var range = await corpus.RangeAsync(pageId, ct);
        if (range is null || range.Value.Count <= 0)
        {
            return null;
        }

        var title = await corpus.ArticleTitleAsync(pageId, ct);
        var normalizedAt = atUtc is DateTime explicitAt
            ? NormalizeUtc(explicitAt)
            : EndOfUtcDay(DateOnly.FromDateTime(range.Value.Last));

        RevisionAt? rev = null;
        if (revId is long rid && rid > 0)
        {
            rev = await corpus.RevisionByIdAsync(pageId, rid, ct);
        }

        rev ??= await corpus.RevisionAtAsync(pageId, normalizedAt, ct);

        var selectedDate = rev is not null && revId is not null
            ? DateOnly.FromDateTime(rev.Ts)
            : DateOnly.FromDateTime(normalizedAt);

        TimeMachineComparison? comparison = null;
        if (compareDate is DateOnly secondDate)
        {
            var primaryDate = selectedDate;
            var revA = revId is not null
                ? rev
                : await corpus.RevisionAtAsync(pageId, EndOfUtcDay(primaryDate), ct);
            var revB = await corpus.RevisionAtAsync(pageId, EndOfUtcDay(secondDate), ct);
            var textA = revA is not null ? await corpus.TextAsync(pageId, revA.RevId, ct) ?? "" : "";
            var textB = revB is not null ? await corpus.TextAsync(pageId, revB.RevId, ct) ?? "" : "";
            var diff = WikitextDiff.Compare(textA, textB);
            var passage = new DiffPassage(
                RevId: revB?.RevId ?? 0,
                ParentRevId: revA?.RevId,
                BeforeWikitext: textA,
                AfterWikitext: textB,
                Diff: diff);
            comparison = new TimeMachineComparison(primaryDate, secondDate, revA, revB, passage);
        }

        if (rev is null)
        {
            return new TimeMachineSnapshot(
                PageId: pageId,
                TitleFi: title?.TitleFi,
                TitleEn: title?.TitleEn,
                Range: range.Value,
                SelectedDate: selectedDate,
                Revision: null,
                BeforeFirst: true,
                Prose: "",
                IsTruncated: false,
                Blocks: [],
                Highlight: null,
                Comparison: comparison);
        }

        var rawWikitext = await corpus.TextAsync(pageId, rev.RevId, ct) ?? "";
        var fullProse = WikitextProse.Of(rawWikitext).Prose;
        var isTruncated = fullProse.Length > MaxProseChars;
        var cappedProse = isTruncated ? TruncateSafe(fullProse, MaxProseChars) : fullProse;

        ProseHighlight? highlight = null;
        if (highlightRevId is long hRevId && hRevId > 0)
        {
            var diff = await ResolveHighlightDiffAsync(
                pageId,
                rev,
                rawWikitext,
                hRevId,
                highlightParentRevId,
                fallbackHighlightDiff,
                ct);

            if (diff is not null)
            {
                highlight = MatchHighlight(cappedProse, diff);
            }
        }
        else if (fallbackHighlightDiff is not null)
        {
            highlight = MatchHighlight(cappedProse, fallbackHighlightDiff);
        }

        var blocks = BuildProseBlocks(rawWikitext, cappedProse, highlight?.Ranges ?? []);

        return new TimeMachineSnapshot(
            PageId: pageId,
            TitleFi: title?.TitleFi,
            TitleEn: title?.TitleEn,
            Range: range.Value,
            SelectedDate: selectedDate,
            Revision: rev,
            BeforeFirst: false,
            Prose: cappedProse,
            IsTruncated: isTruncated,
            Blocks: blocks,
            Highlight: highlight,
            Comparison: comparison);
    }

    public static ProseHighlight? MatchHighlight(string displayedProse, WikitextDiffResult diff)
    {
        ArgumentNullException.ThrowIfNull(displayedProse);
        ArgumentNullException.ThrowIfNull(diff);

        var proseSpans = diff.Segments
            .Where(static s => s.Op == DiffOp.Added && !s.Moved && s.Kind == SegmentKind.Prose && !string.IsNullOrWhiteSpace(s.Prose))
            .Select(static s => s.Prose.Trim())
            .Where(static s => s.Length > 0)
            .ToList();

        var ranges = new List<(int Start, int Length)>();
        var cursor = 0;

        foreach (var span in proseSpans)
        {
            var idx = displayedProse.IndexOf(span, cursor, StringComparison.Ordinal);
            if (idx < 0 && cursor > 0)
            {
                idx = displayedProse.IndexOf(span, StringComparison.Ordinal);
            }

            if (idx >= 0)
            {
                ranges.Add((idx, span.Length));
                cursor = idx + span.Length;
            }
        }

        if (ranges.Count > 0)
        {
            var merged = MergeRanges(ranges);
            return new ProseHighlight(FoundInProse: true, Ranges: merged, FallbackText: null);
        }

        var fallback = !string.IsNullOrWhiteSpace(diff.AddedProse)
            ? diff.AddedProse.Trim()
            : string.Join(
                " ",
                diff.Segments
                    .Where(static s => s.Op == DiffOp.Added && !s.Moved && !string.IsNullOrWhiteSpace(s.Prose))
                    .Select(static s => s.Prose.Trim()));

        return fallback.Length > 0
            ? new ProseHighlight(FoundInProse: false, Ranges: [], FallbackText: fallback)
            : null;
    }

    internal static IReadOnlyList<ProseBlock> BuildProseBlocks(
        string rawWikitext,
        string cappedProse,
        IReadOnlyList<(int Start, int Length)> highlightRanges)
    {
        if (cappedProse.Length == 0)
        {
            return [];
        }

        var normalized = TextUtil.NormalizeNewlines(rawWikitext);
        var rawBlocks = WikitextBlocks.Split(normalized);
        var paragraphs = cappedProse.Split(ParagraphBreak, StringSplitOptions.None);
        var kinds = new List<ProseBlockKind>(paragraphs.Length);

        foreach (var rb in rawBlocks)
        {
            if (kinds.Count >= paragraphs.Length)
            {
                break;
            }

            var slice = normalized[rb.Start..rb.End];
            var blockProse = WikitextProse.Of(slice).Prose;
            if (blockProse.Length > 0)
            {
                kinds.Add(ClassifyBlock(slice));
            }
        }

        var result = new List<ProseBlock>(paragraphs.Length);
        var offset = 0;

        for (var i = 0; i < paragraphs.Length; i++)
        {
            var para = paragraphs[i];
            var kind = i < kinds.Count ? kinds[i] : ProseBlockKind.Paragraph;
            if (para.Length > 0)
            {
                var pieces = SliceWithHighlights(para, offset, highlightRanges);
                result.Add(new ProseBlock(kind, pieces));
            }

            offset += para.Length + 2;
        }

        return result;
    }

    private async Task<WikitextDiffResult?> ResolveHighlightDiffAsync(
        long pageId,
        RevisionAt currentRevision,
        string currentWikitext,
        long highlightRevId,
        long? highlightParentRevId,
        WikitextDiffResult? fallbackDiff,
        CancellationToken ct)
    {
        string? revText;
        long? parentId;

        if (currentRevision.RevId == highlightRevId)
        {
            revText = currentWikitext;
            parentId = currentRevision.ParentId ?? highlightParentRevId;
        }
        else
        {
            revText = await corpus.TextAsync(pageId, highlightRevId, ct);
            if (revText is null)
            {
                return fallbackDiff;
            }

            var highlightMeta = await corpus.RevisionByIdAsync(pageId, highlightRevId, ct);
            parentId = highlightMeta?.ParentId ?? highlightParentRevId;
        }

        if (string.IsNullOrEmpty(revText) && fallbackDiff is not null)
        {
            return fallbackDiff;
        }

        var parentText = parentId is long pid && pid > 0
            ? await corpus.TextAsync(pageId, pid, ct)
            : null;

        return WikitextDiff.Compare(parentText, revText ?? "");
    }

    private static ProseBlockKind ClassifyBlock(string rawSlice)
    {
        var trimmed = rawSlice.AsSpan().Trim();
        if (trimmed.Length >= 6 && trimmed.StartsWith("===", StringComparison.Ordinal) && trimmed[^1] == '=')
        {
            return ProseBlockKind.Heading3;
        }

        if (trimmed.Length >= 4 && trimmed.StartsWith("==", StringComparison.Ordinal) && trimmed[^1] == '=')
        {
            return ProseBlockKind.Heading2;
        }

        if (trimmed.Length > 0 && trimmed[0] == '*')
        {
            return ProseBlockKind.BulletItem;
        }

        if (trimmed.Length > 0 && trimmed[0] == '#')
        {
            foreach (var redirect in RedirectPrefixes)
            {
                if (trimmed.StartsWith(redirect, StringComparison.OrdinalIgnoreCase))
                {
                    return ProseBlockKind.Paragraph;
                }
            }

            return ProseBlockKind.NumberedItem;
        }

        return ProseBlockKind.Paragraph;
    }

    private static IReadOnlyList<ProsePiece> SliceWithHighlights(
        string blockText,
        int blockStart,
        IReadOnlyList<(int Start, int Length)> highlightRanges)
    {
        if (highlightRanges.Count == 0)
        {
            return [new ProsePiece(blockText, Highlighted: false)];
        }

        var blockEnd = blockStart + blockText.Length;
        var pieces = new List<ProsePiece>();
        var pos = blockStart;

        foreach (var (hStart, hLen) in highlightRanges)
        {
            var hEnd = hStart + hLen;
            if (hEnd <= pos) continue;
            if (hStart >= blockEnd) break;

            var overlapStart = Math.Max(pos, hStart);
            var overlapEnd = Math.Min(blockEnd, hEnd);

            if (overlapStart > pos)
            {
                pieces.Add(new ProsePiece(blockText[(pos - blockStart)..(overlapStart - blockStart)], Highlighted: false));
            }

            if (overlapEnd > overlapStart)
            {
                pieces.Add(new ProsePiece(blockText[(overlapStart - blockStart)..(overlapEnd - blockStart)], Highlighted: true));
                pos = overlapEnd;
            }
        }

        if (pos < blockEnd)
        {
            pieces.Add(new ProsePiece(blockText[(pos - blockStart)..], Highlighted: false));
        }

        return pieces.Count > 0 ? pieces : [new ProsePiece(blockText, Highlighted: false)];
    }

    private static List<(int Start, int Length)> MergeRanges(List<(int Start, int Length)> ranges)
    {
        var ordered = ranges.OrderBy(static r => r.Start).ThenByDescending(static r => r.Length).ToList();
        var merged = new List<(int Start, int Length)>(ordered.Count);

        foreach (var (start, length) in ordered)
        {
            if (length <= 0) continue;
            if (merged.Count == 0)
            {
                merged.Add((start, length));
                continue;
            }

            var (prevStart, prevLen) = merged[^1];
            var prevEnd = prevStart + prevLen;
            var end = start + length;

            if (start <= prevEnd)
            {
                merged[^1] = (prevStart, Math.Max(prevEnd, end) - prevStart);
            }
            else
            {
                merged.Add((start, length));
            }
        }

        return merged;
    }

    private static string TruncateSafe(string s, int maxChars)
    {
        if (s.Length <= maxChars) return s;
        var end = maxChars;
        if (end > 0 && char.IsHighSurrogate(s[end - 1]))
        {
            end--;
        }

        return s[..end];
    }

    private static DateTime NormalizeUtc(DateTime dt) =>
        dt.Kind == DateTimeKind.Local ? dt.ToUniversalTime() : DateTime.SpecifyKind(dt, DateTimeKind.Utc);
}
