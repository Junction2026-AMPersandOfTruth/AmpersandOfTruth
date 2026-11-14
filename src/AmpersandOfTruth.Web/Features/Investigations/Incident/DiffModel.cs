using System.Globalization;
using System.Net;
using System.Text;
using AmpersandOfTruth.Web.Features.Investigations.View;
using AmpersandOfTruth.Web.Features.Translation;
using AmpersandOfTruth.Web.Features.WikiDiff;

namespace AmpersandOfTruth.Web.Features.Investigations.Incident;

public enum DiffTokenKind { Same, Added, Removed }

public readonly record struct DiffSpan(string Text, bool Highlighted);

public sealed record DiffToken(string Text, DiffTokenKind Kind);

public sealed record DiffRenderToken(DiffTokenKind Kind, IReadOnlyList<DiffSpan> Spans);

public sealed record DiffBlock(
    int? OldParagraph,
    int? NewParagraph,
    bool IsChanged,
    IReadOnlyList<DiffToken> Tokens)
{
    public string BeforeText => string.Concat(Tokens.Where(t => t.Kind != DiffTokenKind.Added).Select(t => t.Text));
    public string AfterText => string.Concat(Tokens.Where(t => t.Kind != DiffTokenKind.Removed).Select(t => t.Text));
}

public abstract record DiffItem;

public sealed record DiffBlockItem(DiffBlock Block) : DiffItem;

public sealed record DiffFoldItem(int Id, IReadOnlyList<DiffBlock> Blocks) : DiffItem
{
    public int Count => Blocks.Count;

    public string Label => Count == 1
        ? "⋯ 1 unchanged line"
        : $"⋯ {Count.ToString("N0", CultureInfo.InvariantCulture)} unchanged lines";
}

public sealed record TranslationPassageInput(
    string Before,
    string After,
    IReadOnlyList<(int Start, int Length)> ChangedInAfter,
    IReadOnlyList<(int Start, int Length)> ChangedInBefore)
{
    public bool IsEmpty => string.IsNullOrWhiteSpace(Before) && string.IsNullOrWhiteSpace(After);
}

/// <summary>
/// Pure view-model builder for <see cref="DiffViewer"/> (#48, #98 §3).
/// <para>
/// Block alignment combines <see cref="WikitextBlocks"/> Myers hunks with <see cref="DiffSegment.OldParagraph"/> /
/// <see cref="DiffSegment.NewParagraph"/> links from <see cref="DiffPassage.Diff"/> so modified paragraphs stay paired,
/// pure insertions and deletions keep their surrounding order, and moved blocks stay quiet context.
/// </para>
/// <para>
/// Text mode choice: <see cref="DiffTextMode.Wikitext"/> diffs raw block slices verbatim; <see cref="DiffTextMode.Prose"/>
/// diffs <see cref="WikitextProse.Of"/> per changed block so surrounding context and inline edits read naturally as clean
/// sentences (appending <c>[citation: …]</c> when a block's <c>&lt;ref&gt;</c> citations changed, or falling back to segment
/// prose / raw block text when a changed block has no running prose, such as an infobox number edit).
/// </para>
/// </summary>
public sealed record DiffModel(
    DiffTextMode TextMode,
    IReadOnlyList<DiffBlock> Blocks,
    IReadOnlyList<DiffItem> Items)
{
    public IReadOnlyList<DiffItem> ChangedOnlyItems { get; init; } = [];

    private const int MaxBlockEdits = 2000;
    private const int MaxTokenEdits = 2000;
    private const double MinSharedWords = 0.3;
    private static readonly string ParagraphBreak = new([(char)10, (char)10]);

    public static DiffModel Build(DiffPassage passage, DiffTextMode textMode = DiffTextMode.Prose)
    {
        ArgumentNullException.ThrowIfNull(passage);

        var oldText = TextUtil.NormalizeNewlines(passage.BeforeWikitext ?? "");
        var newText = TextUtil.NormalizeNewlines(passage.AfterWikitext ?? "");
        var oldBlocks = WikitextBlocks.Split(oldText);
        var newBlocks = WikitextBlocks.Split(newText);

        var aligned = AlignBlocks(oldBlocks, newBlocks, passage.Diff.Segments);
        var blocks = new List<DiffBlock>(aligned.Count);

        foreach (var (oi, ni, changed) in aligned)
        {
            var rawOld = oi is int o ? oldText[oldBlocks[o].Start..oldBlocks[o].End] : "";
            var rawNew = ni is int n ? newText[newBlocks[n].Start..newBlocks[n].End] : "";

            if (!changed)
            {
                var contextText = textMode == DiffTextMode.Wikitext
                    ? rawNew
                    : ExtractBlockProse(rawNew).Prose;

                if (contextText.Length == 0)
                {
                    continue;
                }

                blocks.Add(new DiffBlock(oi, ni, IsChanged: false, [new DiffToken(contextText, DiffTokenKind.Same)]));
                continue;
            }

            var (beforeBlock, afterBlock) = textMode == DiffTextMode.Wikitext
                ? (rawOld, rawNew)
                : ExtractChangedProse(rawOld, rawNew, oi, ni, passage.Diff.Segments);

            if (beforeBlock.Length == 0 && afterBlock.Length == 0)
            {
                continue;
            }

            var tokens = DiffStrings(beforeBlock, afterBlock);
            bool hasTokenDiff = tokens.Any(t => t.Kind != DiffTokenKind.Same);
            blocks.Add(new DiffBlock(oi, ni, IsChanged: hasTokenDiff, tokens));
        }

        var items = textMode == DiffTextMode.Prose
            ? FoldProseBlocks(blocks, maxContextSentences: 1)
            : FoldBlocks(blocks, contextBlocks: 1);

        var changedOnlyItems = textMode == DiffTextMode.Prose
            ? FoldProseBlocks(blocks, maxContextSentences: 0)
            : FoldBlocks(blocks, contextBlocks: 0);

        return new DiffModel(textMode, blocks, items)
        {
            ChangedOnlyItems = changedOnlyItems,
        };
    }

    /// <summary>
    /// Cleans a snippet (claim_fi, FI summary, assessment quote) for prose display: HTML-decodes entities
    /// (<c>&amp;nbsp;</c> → space, <c>&amp;amp;</c> → <c>&amp;</c>) and strips infobox/wiki markup
    /// (<c>| field = value</c> → <c>value</c>, <c>[[a|b]]</c> → <c>b</c>, <c>[[a]]</c> → <c>a</c>, <c>'''</c>, <c>''</c>),
    /// while preserving literal non-formatting tags such as <c>&lt;script&gt;</c> so callers render safely as plain text.
    /// </summary>
    public static string CleanProse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "";
        }

        var stripped = StripInfoboxFieldPrefixes(raw);
        var prose = ExtractBlockProse(stripped).Prose;
        if (prose.Length > 0)
        {
            return prose;
        }

        var templateValues = ExtractTemplateValues(raw);
        if (templateValues.Length > 0)
        {
            return templateValues;
        }

        return TextUtil.Collapse(WebUtility.HtmlDecode(stripped));
    }

    public static TranslationPassageInput BuildTranslationInput(DiffPassage passage)
    {
        ArgumentNullException.ThrowIfNull(passage);

        var proseModel = Build(passage, DiffTextMode.Prose);
        var beforeSb = new StringBuilder();
        var afterSb = new StringBuilder();
        var changedBefore = new List<(int Start, int Length)>();
        var changedAfter = new List<(int Start, int Length)>();

        var changedBlocks = proseModel.Items
            .OfType<DiffBlockItem>()
            .Select(static i => i.Block)
            .Where(static b => b.IsChanged)
            .ToList();

        if (changedBlocks.Count == 0)
        {
            changedBlocks = proseModel.Blocks.Where(static b => b.IsChanged).ToList();
        }

        foreach (var block in changedBlocks)
        {
            bool hasBefore = block.Tokens.Any(t => t.Kind != DiffTokenKind.Added && t.Text.Length > 0);
            bool hasAfter = block.Tokens.Any(t => t.Kind != DiffTokenKind.Removed && t.Text.Length > 0);

            if (hasBefore && beforeSb.Length > 0) beforeSb.Append(ParagraphBreak);
            if (hasAfter && afterSb.Length > 0) afterSb.Append(ParagraphBreak);

            foreach (var token in block.Tokens)
            {
                if (token.Text.Length == 0) continue;

                switch (token.Kind)
                {
                    case DiffTokenKind.Same:
                        if (hasBefore) beforeSb.Append(token.Text);
                        if (hasAfter) afterSb.Append(token.Text);
                        break;
                    case DiffTokenKind.Removed:
                        changedBefore.Add((beforeSb.Length, token.Text.Length));
                        beforeSb.Append(token.Text);
                        break;
                    case DiffTokenKind.Added:
                        changedAfter.Add((afterSb.Length, token.Text.Length));
                        afterSb.Append(token.Text);
                        break;
                }
            }
        }

        return new TranslationPassageInput(beforeSb.ToString(), afterSb.ToString(), changedAfter, changedBefore);
    }

    public static IReadOnlyList<IReadOnlyList<DiffToken>> BuildAlignedTranslationParagraphs(AlignedTranslation translation)
    {
        ArgumentNullException.ThrowIfNull(translation);

        var beforeEn = translation.BeforeEn ?? "";
        var afterEn = translation.AfterEn ?? "";
        var beforeSpans = ClampSpans(beforeEn, translation.ChangedInBeforeEn);
        var afterSpans = ClampSpans(afterEn, translation.ChangedInAfterEn);

        List<DiffToken> flat;
        if (beforeSpans.Count == 0 && afterSpans.Count == 0)
        {
            var plain = afterEn.Length > 0 ? afterEn : beforeEn;
            flat = plain.Length > 0 ? [new DiffToken(plain, DiffTokenKind.Same)] : [];
        }
        else if (beforeSpans.Count == 0)
        {
            flat = SliceMarked(afterEn, afterSpans, DiffTokenKind.Added);
        }
        else if (afterSpans.Count == 0)
        {
            flat = SliceMarked(beforeEn, beforeSpans, DiffTokenKind.Removed);
        }
        else
        {
            flat = SpliceReplacements(beforeEn, beforeSpans, afterEn, afterSpans);
        }

        return SplitParagraphs(flat);
    }

    public static IReadOnlyList<DiffRenderToken> Highlight(
        IReadOnlyList<DiffToken> tokens,
        IReadOnlyList<string>? evidenceQuotes)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        if (tokens.Count == 0) return [];

        var quotes = ExpandHighlightQuotes(evidenceQuotes);
        if (quotes.Length == 0)
        {
            return tokens
                .Where(static t => t.Text.Length > 0)
                .Select(static t => new DiffRenderToken(t.Kind, [new DiffSpan(t.Text, Highlighted: false)]))
                .ToArray();
        }

        var flags = tokens.Select(static t => new bool[t.Text.Length]).ToArray();
        MarkProjectionMatches(tokens, flags, quotes, static t => t.Kind != DiffTokenKind.Removed);
        MarkProjectionMatches(tokens, flags, quotes, static t => t.Kind != DiffTokenKind.Added);

        var rendered = new List<DiffRenderToken>(tokens.Count);
        for (int i = 0; i < tokens.Count; i++)
        {
            var text = tokens[i].Text;
            if (text.Length == 0) continue;

            var mask = flags[i];
            var spans = new List<DiffSpan>();
            int runStart = 0;
            for (int k = 1; k <= text.Length; k++)
            {
                if (k == text.Length || mask[k] != mask[runStart])
                {
                    spans.Add(new DiffSpan(text[runStart..k], mask[runStart]));
                    runStart = k;
                }
            }
            rendered.Add(new DiffRenderToken(tokens[i].Kind, spans));
        }

        return rendered;
    }

    // ── Block alignment & folding ──────────────────────────────────────────────────────────────────────────────────

    private static List<(int? OldIndex, int? NewIndex, bool Changed)> AlignBlocks(
        List<WikiBlock> oldBlocks,
        List<WikiBlock> newBlocks,
        IReadOnlyList<DiffSegment> segments)
    {
        var ids = new Dictionary<string, int>(StringComparer.Ordinal);
        int Intern(string key) => ids.TryGetValue(key, out int id) ? id : (ids[key] = ids.Count);

        var a = oldBlocks.Select(b => Intern(b.Key)).ToArray();
        var b = newBlocks.Select(b => Intern(b.Key)).ToArray();
        var ops = MyersDiff.Diff(a, b, MaxBlockEdits)
            ?? [.. Enumerable.Repeat(EditOp.Delete, a.Length), .. Enumerable.Repeat(EditOp.Insert, b.Length)];

        var movedFromTo = new Dictionary<int, int>();
        var movedToFrom = new Dictionary<int, int>();
        var changedOld = new HashSet<int>();
        var changedNew = new HashSet<int>();

        foreach (var s in segments)
        {
            if (s.Moved)
            {
                if (s.OldParagraph is int from && s.NewParagraph is int to)
                {
                    movedFromTo.TryAdd(from, to);
                    movedToFrom.TryAdd(to, from);
                }
                continue;
            }

            if (s.OldParagraph is int oi) changedOld.Add(oi);
            if (s.NewParagraph is int ni) changedNew.Add(ni);
        }

        var result = new List<(int? OldIndex, int? NewIndex, bool Changed)>();
        int oldPos = 0, newPos = 0, cursor = 0;

        while (cursor < ops.Count)
        {
            if (ops[cursor] == EditOp.Equal)
            {
                bool changed = changedOld.Contains(oldPos) || changedNew.Contains(newPos);
                result.Add((oldPos, newPos, changed));
                oldPos++;
                newPos++;
                cursor++;
                continue;
            }

            var removed = new List<int>();
            var added = new List<int>();
            for (; cursor < ops.Count && ops[cursor] != EditOp.Equal; cursor++)
            {
                if (ops[cursor] == EditOp.Delete) removed.Add(oldPos++);
                else added.Add(newPos++);
            }

            var activeRemoved = removed.Where(r => !movedFromTo.ContainsKey(r)).ToList();
            var activeAdded = added.Where(n => !movedToFrom.ContainsKey(n)).ToList();

            var pairs = PairHunkBlocks(activeRemoved, activeAdded, segments);
            var pairedOld = new HashSet<int>(pairs.Values);
            int remCursor = 0;

            foreach (int ni in added)
            {
                if (movedToFrom.TryGetValue(ni, out int movedFrom))
                {
                    result.Add((movedFrom, ni, Changed: changedOld.Contains(movedFrom) || changedNew.Contains(ni)));
                    continue;
                }

                if (pairs.TryGetValue(ni, out int pairedOi))
                {
                    while (remCursor < activeRemoved.Count && activeRemoved[remCursor] < pairedOi)
                    {
                        int orphanOld = activeRemoved[remCursor++];
                        if (!pairedOld.Contains(orphanOld))
                        {
                            result.Add((orphanOld, null, Changed: true));
                        }
                    }
                    if (remCursor < activeRemoved.Count && activeRemoved[remCursor] == pairedOi)
                    {
                        remCursor++;
                    }
                    result.Add((pairedOi, ni, Changed: true));
                }
                else
                {
                    result.Add((null, ni, Changed: true));
                }
            }

            for (; remCursor < activeRemoved.Count; remCursor++)
            {
                int orphanOld = activeRemoved[remCursor];
                if (!pairedOld.Contains(orphanOld))
                {
                    result.Add((orphanOld, null, Changed: true));
                }
            }
        }

        return result;
    }

    private static Dictionary<int, int> PairHunkBlocks(
        List<int> removed,
        List<int> added,
        IReadOnlyList<DiffSegment> segments)
    {
        var pairs = new Dictionary<int, int>(); // newIndex -> oldIndex
        if (removed.Count == 0 || added.Count == 0) return pairs;

        var removedSet = new HashSet<int>(removed);
        var addedSet = new HashSet<int>(added);
        int minOld = -1;

        foreach (var s in segments.Where(s => !s.Moved))
        {
            if (s.OldParagraph is int o && s.NewParagraph is int n
                && removedSet.Contains(o) && addedSet.Contains(n)
                && !pairs.ContainsKey(n) && o > minOld)
            {
                pairs[n] = o;
                minOld = o;
            }
        }

        if (pairs.Count == 0)
        {
            int count = Math.Min(removed.Count, added.Count);
            for (int i = 0; i < count; i++)
            {
                pairs[added[i]] = removed[i];
            }
        }

        return pairs;
    }

    private static IReadOnlyList<DiffItem> FoldBlocks(IReadOnlyList<DiffBlock> blocks, int contextBlocks)
    {
        if (blocks.Count == 0) return [];

        bool anyChanged = blocks.Any(static b => b.IsChanged);
        var keepVisible = new bool[blocks.Count];

        if (!anyChanged)
        {
            keepVisible[0] = true;
        }
        else
        {
            for (int i = 0; i < blocks.Count; i++)
            {
                if (!blocks[i].IsChanged) continue;
                keepVisible[i] = true;
                for (int d = 1; d <= contextBlocks; d++)
                {
                    if (i - d >= 0) keepVisible[i - d] = true;
                    if (i + d < blocks.Count) keepVisible[i + d] = true;
                }
            }
        }

        var items = new List<DiffItem>();
        int foldId = 0;
        int idx = 0;
        while (idx < blocks.Count)
        {
            if (keepVisible[idx])
            {
                items.Add(new DiffBlockItem(blocks[idx]));
                idx++;
                continue;
            }

            int start = idx;
            while (idx < blocks.Count && !keepVisible[idx]) idx++;
            items.Add(new DiffFoldItem(foldId++, blocks.Skip(start).Take(idx - start).ToArray()));
        }

        return items;
    }

    // ── Sentence-level Prose folding (R1-09, R1-01b) ───────────────────────────────────────────────────────────────

    private static readonly HashSet<string> Abbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        "n", "noin", "esim", "mm", "ks", "vrt", "ns", "ko", "ts", "jne", "yms", "ym",
        "s", "ss", "p", "pp", "v", "os", "synt", "ent", "nyk", "alk", "joht", "klo",
        "ev", "kenr", "maj", "kapt", "luutn", "prof", "tri", "dr", "mr", "mrs", "ms", "st", "ca", "cf", "al", "et",
    };

    private sealed record TrimmedChangedBlock(
        IReadOnlyList<DiffBlock> FoldedBefore,
        DiffBlock VisibleBlock,
        IReadOnlyList<DiffBlock> FoldedAfter,
        int LeftContextSentences,
        int RightContextSentences);

    private static IReadOnlyList<DiffItem> FoldProseBlocks(IReadOnlyList<DiffBlock> blocks, int maxContextSentences)
    {
        if (blocks.Count == 0) return [];
        if (!blocks.Any(static b => b.IsChanged))
        {
            return FoldBlocks(blocks, contextBlocks: 0);
        }

        var trimmed = new TrimmedChangedBlock?[blocks.Count];
        for (int i = 0; i < blocks.Count; i++)
        {
            if (blocks[i].IsChanged)
            {
                trimmed[i] = TrimChangedBlockSentences(blocks[i], maxContextSentences);
            }
        }

        var seq = new List<(DiffBlock Block, bool Visible)>(blocks.Count * 2);
        for (int i = 0; i < blocks.Count; i++)
        {
            if (blocks[i].IsChanged)
            {
                var t = trimmed[i]!;
                foreach (var fb in t.FoldedBefore) seq.Add((fb, Visible: false));
                seq.Add((t.VisibleBlock, Visible: true));
                foreach (var fa in t.FoldedAfter) seq.Add((fa, Visible: false));
                continue;
            }

            bool needRight = i > 0
                && blocks[i - 1].IsChanged
                && trimmed[i - 1]!.RightContextSentences < maxContextSentences;
            bool needLeft = i + 1 < blocks.Count
                && blocks[i + 1].IsChanged
                && trimmed[i + 1]!.LeftContextSentences < maxContextSentences;

            if (!needRight && !needLeft)
            {
                seq.Add((blocks[i], Visible: false));
                continue;
            }

            var sentences = SplitIntoSentences(blocks[i].AfterText);
            if (sentences.Count <= 1)
            {
                seq.Add((blocks[i], Visible: true));
            }
            else if (needRight && !needLeft)
            {
                seq.Add((MakeSameBlock(blocks[i], sentences[0]), Visible: true));
                for (int k = 1; k < sentences.Count; k++)
                {
                    seq.Add((MakeSameBlock(blocks[i], sentences[k]), Visible: false));
                }
            }
            else if (!needRight && needLeft)
            {
                for (int k = 0; k < sentences.Count - 1; k++)
                {
                    seq.Add((MakeSameBlock(blocks[i], sentences[k]), Visible: false));
                }
                seq.Add((MakeSameBlock(blocks[i], sentences[^1]), Visible: true));
            }
            else if (sentences.Count == 2)
            {
                seq.Add((blocks[i], Visible: true));
            }
            else
            {
                seq.Add((MakeSameBlock(blocks[i], sentences[0]), Visible: true));
                for (int k = 1; k < sentences.Count - 1; k++)
                {
                    seq.Add((MakeSameBlock(blocks[i], sentences[k]), Visible: false));
                }
                seq.Add((MakeSameBlock(blocks[i], sentences[^1]), Visible: true));
            }
        }

        var items = new List<DiffItem>();
        int foldId = 0;
        int cursor = 0;
        while (cursor < seq.Count)
        {
            if (seq[cursor].Visible)
            {
                items.Add(new DiffBlockItem(seq[cursor].Block));
                cursor++;
                continue;
            }

            var folded = new List<DiffBlock>();
            while (cursor < seq.Count && !seq[cursor].Visible)
            {
                folded.Add(seq[cursor].Block);
                cursor++;
            }
            items.Add(new DiffFoldItem(foldId++, folded));
        }

        return items;
    }

    private static DiffBlock MakeSameBlock(DiffBlock source, string text) =>
        new(source.OldParagraph, source.NewParagraph, IsChanged: false, [new DiffToken(text, DiffTokenKind.Same)]);

    private static TrimmedChangedBlock TrimChangedBlockSentences(DiffBlock block, int maxContextSentences)
    {
        var tokens = block.Tokens;
        int firstDiff = -1, lastDiff = -1;
        for (int i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].Kind != DiffTokenKind.Same)
            {
                if (firstDiff < 0) firstDiff = i;
                lastDiff = i;
            }
        }

        if (firstDiff < 0)
        {
            return new TrimmedChangedBlock([], block, [], 0, 0);
        }

        var foldedBefore = new List<DiffBlock>();
        var foldedAfter = new List<DiffBlock>();
        int leftCtx = 0;
        int rightCtx = 0;

        string? keptLeading = null;
        bool trimFirstDiffLeadingSpace = false;

        if (firstDiff > 0)
        {
            var leadingSame = string.Concat(tokens.Take(firstDiff).Select(static t => t.Text));
            var firstDiffText = tokens[firstDiff].Text;
            var chunks = SplitIntoSentences(leadingSame, preserveEdges: true);

            if (chunks.Count > 0)
            {
                bool endsComplete = EndsWithCompleteSentence(leadingSame, firstDiffText);
                int completeCount = endsComplete ? chunks.Count : chunks.Count - 1;
                string prefixFragment = endsComplete ? "" : chunks[^1];
                bool hadTrailingSpace = leadingSame.Length > 0 && char.IsWhiteSpace(leadingSame[^1]);

                int keepCount = Math.Min(completeCount, maxContextSentences);
                int dropCount = completeCount - keepCount;

                if (dropCount == 0)
                {
                    keptLeading = leadingSame;
                }
                else
                {
                    for (int k = 0; k < dropCount; k++)
                    {
                        var foldedText = chunks[k].Trim();
                        if (foldedText.Length > 0)
                        {
                            foldedBefore.Add(MakeSameBlock(block, foldedText));
                        }
                    }

                    var sb = new StringBuilder();
                    for (int k = dropCount; k < completeCount; k++)
                    {
                        if (sb.Length > 0) sb.Append(' ');
                        sb.Append(chunks[k].Trim());
                    }

                    if (prefixFragment.Length > 0)
                    {
                        if (sb.Length > 0 && !char.IsWhiteSpace(prefixFragment[0])) sb.Append(' ');
                        sb.Append(sb.Length == 0 ? prefixFragment.TrimStart() : prefixFragment);
                    }
                    else if (sb.Length > 0 && hadTrailingSpace)
                    {
                        sb.Append(' ');
                    }

                    keptLeading = sb.ToString();
                    if (keptLeading.Length == 0 && firstDiffText.Length > 0 && char.IsWhiteSpace(firstDiffText[0]))
                    {
                        trimFirstDiffLeadingSpace = true;
                    }
                }

                leftCtx = keepCount;
            }
        }

        string? keptTrailing = null;
        bool trimLastDiffTrailingSpace = false;
        if (lastDiff < tokens.Count - 1)
        {
            var trailingSame = string.Concat(tokens.Skip(lastDiff + 1).Select(static t => t.Text));
            var lastDiffText = tokens[lastDiff].Text;
            bool diffEndedSentence = EndsWithCompleteSentence(lastDiffText, trailingSame);

            var chunks = SplitIntoSentences(trailingSame, preserveEdges: true);
            if (chunks.Count > 0)
            {
                string suffixFragment = diffEndedSentence ? "" : chunks[0];
                int afterStartIdx = diffEndedSentence ? 0 : 1;
                int completeAfterCount = chunks.Count - afterStartIdx;
                int keepAfter = Math.Min(completeAfterCount, maxContextSentences);
                int dropAfterStart = afterStartIdx + keepAfter;

                if (dropAfterStart >= chunks.Count)
                {
                    keptTrailing = trailingSame;
                }
                else
                {
                    bool hadLeadingSpace = trailingSame.Length > 0 && char.IsWhiteSpace(trailingSame[0]);
                    var sb = new StringBuilder();
                    if (suffixFragment.Length > 0)
                    {
                        sb.Append(suffixFragment.TrimEnd());
                    }

                    for (int k = afterStartIdx; k < dropAfterStart; k++)
                    {
                        if (sb.Length > 0) sb.Append(' ');
                        else if (hadLeadingSpace) sb.Append(' ');
                        sb.Append(chunks[k].Trim());
                    }

                    keptTrailing = sb.ToString();
                    if (keptTrailing.Length == 0 && lastDiffText.Length > 0 && char.IsWhiteSpace(lastDiffText[^1]))
                    {
                        trimLastDiffTrailingSpace = true;
                    }

                    for (int k = dropAfterStart; k < chunks.Count; k++)
                    {
                        var foldedText = chunks[k].Trim();
                        if (foldedText.Length > 0)
                        {
                            foldedAfter.Add(MakeSameBlock(block, foldedText));
                        }
                    }
                }

                rightCtx = keepAfter;
            }
        }

        if (foldedBefore.Count == 0 && foldedAfter.Count == 0)
        {
            return new TrimmedChangedBlock([], block, [], leftCtx, rightCtx);
        }

        var updatedTokens = new List<DiffToken>();
        if (!string.IsNullOrEmpty(keptLeading))
        {
            updatedTokens.Add(new DiffToken(keptLeading, DiffTokenKind.Same));
        }

        for (int i = firstDiff; i <= lastDiff; i++)
        {
            var tok = tokens[i];
            var text = tok.Text;
            if (i == firstDiff && trimFirstDiffLeadingSpace)
            {
                text = text.TrimStart();
            }
            if (i == lastDiff && trimLastDiffTrailingSpace)
            {
                text = text.TrimEnd();
            }
            if (text.Length > 0)
            {
                updatedTokens.Add(text.Length == tok.Text.Length ? tok : new DiffToken(text, tok.Kind));
            }
        }

        if (!string.IsNullOrEmpty(keptTrailing))
        {
            updatedTokens.Add(new DiffToken(keptTrailing, DiffTokenKind.Same));
        }

        var visibleBlock = new DiffBlock(block.OldParagraph, block.NewParagraph, IsChanged: true, updatedTokens);
        return new TrimmedChangedBlock(foldedBefore, visibleBlock, foldedAfter, leftCtx, rightCtx);
    }

    private static List<string> SplitIntoSentences(string text, bool preserveEdges = false)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var result = new List<string>();
        int start = 0;
        int i = 0;

        while (i < text.Length)
        {
            char c = text[i];
            if (c is '.' or '!' or '?')
            {
                int punctEnd = i + 1;
                while (punctEnd < text.Length && text[punctEnd] is '.' or '!' or '?') punctEnd++;
                while (punctEnd < text.Length && IsClosingQuoteOrBracket(text[punctEnd])) punctEnd++;

                if (punctEnd < text.Length && char.IsWhiteSpace(text[punctEnd])
                    && (c != '.' || !IsNonSentencePeriod(text, i)))
                {
                    int nextStart = punctEnd;
                    while (nextStart < text.Length && char.IsWhiteSpace(text[nextStart])) nextStart++;

                    if (nextStart < text.Length && StartsSentenceChar(text, nextStart))
                    {
                        var slice = preserveEdges ? text[start..punctEnd] : text[start..punctEnd].Trim();
                        if (slice.Length > 0) result.Add(slice);
                        start = nextStart;
                        i = nextStart;
                        continue;
                    }
                }
            }

            i++;
        }

        if (start < text.Length)
        {
            var tail = preserveEdges ? text[start..] : text[start..].Trim();
            if (tail.Length > 0) result.Add(tail);
        }

        return result;
    }

    private static bool EndsWithCompleteSentence(string text, string followingText)
    {
        var span = text.AsSpan().TrimEnd();
        if (span.IsEmpty) return false;

        if (span[^1] == ']')
        {
            int citeIdx = span.LastIndexOf("[citation", StringComparison.Ordinal);
            if (citeIdx > 0)
            {
                span = span[..citeIdx].TrimEnd();
                if (span.IsEmpty) return false;
            }
        }

        int end = span.Length;
        while (end > 0 && IsClosingQuoteOrBracket(span[end - 1])) end--;
        if (end == 0) return false;

        char punct = span[end - 1];
        if (punct is not ('.' or '!' or '?')) return false;

        if (punct == '.' && IsNonSentencePeriod(span, end - 1))
        {
            return false;
        }

        bool hasBoundarySpace = char.IsWhiteSpace(text[^1])
            || followingText.Length == 0
            || char.IsWhiteSpace(followingText[0]);
        if (!hasBoundarySpace) return false;

        var followTrim = followingText.AsSpan().TrimStart();
        if (!followTrim.IsEmpty)
        {
            int k = 0;
            while (k < followTrim.Length && IsOpeningQuoteOrBracket(followTrim[k])) k++;
            if (k < followTrim.Length && char.IsLower(followTrim[k]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsNonSentencePeriod(ReadOnlySpan<char> text, int periodIndex)
    {
        int wStart = periodIndex;
        while (wStart > 0 && char.IsLetterOrDigit(text[wStart - 1])) wStart--;
        var word = text[wStart..periodIndex];
        if (word.IsEmpty) return false;

        if (word.Length == 1 && char.IsLetter(word[0]))
        {
            return true;
        }

        if (word.Length <= 2)
        {
            bool allDigits = true;
            foreach (char ch in word)
            {
                if (!char.IsDigit(ch)) { allDigits = false; break; }
            }
            if (allDigits) return true;
        }

        return Abbreviations.Contains(word.ToString());
    }

    private static bool StartsSentenceChar(string text, int index)
    {
        int k = index;
        while (k < text.Length && IsOpeningQuoteOrBracket(text[k])) k++;
        return k < text.Length && char.IsUpper(text[k]);
    }

    private static bool IsClosingQuoteOrBracket(char c) =>
        c is '"' or '\'' or '\u201D' or '\u2019' or '\u00BB' or ')' or ']';

    private static bool IsOpeningQuoteOrBracket(char c) =>
        c is '"' or '\'' or '\u201C' or '\u2018' or '\u00AB' or '(' or '[';

    private static string[] ExpandHighlightQuotes(IReadOnlyList<string>? evidenceQuotes)
    {
        if (evidenceQuotes is null || evidenceQuotes.Count == 0)
        {
            return [];
        }

        var set = new HashSet<string>(StringComparer.Ordinal);
        var ordered = new List<string>(evidenceQuotes.Count * 2);
        foreach (var raw in evidenceQuotes)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            if (set.Add(raw)) ordered.Add(raw);

            var cleaned = CleanProse(raw);
            if (!string.IsNullOrWhiteSpace(cleaned) && set.Add(cleaned))
            {
                ordered.Add(cleaned);
            }
        }

        return ordered.ToArray();
    }

    private static string StripInfoboxFieldPrefixes(string raw)
    {
        var normalized = TextUtil.NormalizeNewlines(raw);
        var sb = new StringBuilder(normalized.Length);
        int i = 0;
        int bracketDepth = 0;
        int braceDepth = 0;
        bool strippedAnyField = false;

        while (i < normalized.Length)
        {
            if (i + 1 < normalized.Length && normalized[i] == '[' && normalized[i + 1] == '[')
            {
                bracketDepth++;
                sb.Append("[[");
                i += 2;
                continue;
            }
            if (i + 1 < normalized.Length && normalized[i] == ']' && normalized[i + 1] == ']' && bracketDepth > 0)
            {
                bracketDepth--;
                sb.Append("]]");
                i += 2;
                continue;
            }
            if (i + 1 < normalized.Length && normalized[i] == '{' && normalized[i + 1] == '{')
            {
                braceDepth++;
                sb.Append("{{");
                i += 2;
                continue;
            }
            if (i + 1 < normalized.Length && normalized[i] == '}' && normalized[i + 1] == '}' && braceDepth > 0)
            {
                braceDepth--;
                sb.Append("}}");
                i += 2;
                continue;
            }

            if (bracketDepth == 0 && braceDepth == 0 && normalized[i] == '|'
                && TryMatchInfoboxFieldPrefix(normalized, i, out int afterEquals))
            {
                strippedAnyField = true;
                TrimTrailingWhitespaceOnly(sb);
                if (sb.Length > 0 && sb[^1] is not ('\n' or ';'))
                {
                    sb.Append("; ");
                }
                i = afterEquals;
                continue;
            }

            sb.Append(normalized[i]);
            i++;
        }

        if (strippedAnyField)
        {
            TrimTrailingWhitespaceOnly(sb);
            if (sb.Length >= 2 && sb[^2] == '}' && sb[^1] == '}' && braceDepth == 0)
            {
                sb.Length -= 2;
            }
        }

        return sb.ToString();
    }

    private static bool TryMatchInfoboxFieldPrefix(string s, int barIndex, out int afterEquals)
    {
        afterEquals = -1;
        int k = barIndex + 1;
        while (k < s.Length && s[k] is ' ' or '\t') k++;

        int nameStart = k;
        int limit = Math.Min(s.Length, nameStart + 60);
        while (k < limit && s[k] is not ('=' or '|' or '\n' or '[' or ']' or '{' or '}' or '<' or '>'))
        {
            k++;
        }

        if (k <= nameStart || k >= s.Length || s[k] != '=')
        {
            return false;
        }

        var fieldName = s.AsSpan(nameStart, k - nameStart).Trim();
        if (fieldName.IsEmpty || !ContainsLetter(fieldName))
        {
            return false;
        }

        int after = k + 1;
        while (after < s.Length && s[after] is ' ' or '\t') after++;
        afterEquals = after;
        return true;
    }

    private static bool ContainsLetter(ReadOnlySpan<char> span)
    {
        foreach (char c in span)
        {
            if (char.IsLetter(c)) return true;
        }
        return false;
    }

    private static void TrimTrailingWhitespaceOnly(StringBuilder sb)
    {
        while (sb.Length > 0 && sb[^1] is ' ' or '\t' or '\n')
        {
            sb.Length--;
        }
    }

    private static string ExtractTemplateValues(string raw)
    {
        var text = TextUtil.NormalizeNewlines(raw);
        var tokens = new List<WikiToken>();
        var sb = new StringBuilder();
        foreach (var block in WikitextBlocks.Split(text))
        {
            tokens.Clear();
            WikitextTokenizer.Tokenize(text, block.Start, block.End, block.Index, tokens);
            foreach (var t in tokens)
            {
                if (t.Prose.Length > 0)
                {
                    sb.Append(t.Prose);
                }
            }
        }
        return TextUtil.CleanValues(sb.ToString());
    }

    // ── Prose extraction per changed block ─────────────────────────────────────────────────────────────────────────

    private static readonly HashSet<string> MediaWikiFormattingTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "b", "i", "u", "s", "del", "ins", "em", "strong", "small", "big", "sub", "sup",
        "br", "hr", "p", "div", "span", "blockquote", "center", "code", "tt", "pre",
        "nowiki", "includeonly", "noinclude", "onlyinclude", "poem", "gallery", "math",
        "references", "syntaxhighlight", "source", "timeline", "hiero", "score", "chem",
    };

    private static WikitextProseView ExtractBlockProse(string rawBlock)
    {
        var text = TextUtil.NormalizeNewlines(rawBlock);
        var paragraphs = new List<string>();
        var references = new List<string>();
        var tokens = new List<WikiToken>();

        foreach (var block in WikitextBlocks.Split(text))
        {
            tokens.Clear();
            WikitextTokenizer.Tokenize(text, block.Start, block.End, block.Index, tokens);
            var sb = new StringBuilder();
            foreach (var t in tokens)
            {
                if (t.Class == TokenClass.Reference)
                {
                    references.Add(t.Prose);
                }
                else if (t.Class == TokenClass.Prose)
                {
                    sb.Append(t.Prose);
                }
                else if (t.Class == TokenClass.Markup)
                {
                    if (t.Prose.Length > 0)
                    {
                        sb.Append(t.Prose);
                    }
                    else if (IsRawHtmlTagToKeepLiteral(text.AsSpan(t.Start, t.End - t.Start)))
                    {
                        sb.Append(text.AsSpan(t.Start, t.End - t.Start));
                    }
                }
            }

            var prose = TextUtil.Collapse(sb.ToString());
            if (prose.Length > 0) paragraphs.Add(prose);
        }

        return new WikitextProseView(string.Join(ParagraphBreak, paragraphs), references);
    }

    private static bool IsRawHtmlTagToKeepLiteral(ReadOnlySpan<char> token)
    {
        if (token.Length < 3 || token[0] != '<' || token[^1] != '>') return false;
        if (token.StartsWith("<!--", StringComparison.Ordinal)) return false;

        int i = 1;
        if (i < token.Length && token[i] == '/') i++;
        int nameStart = i;
        while (i < token.Length && char.IsAsciiLetterOrDigit(token[i])) i++;
        if (i == nameStart) return false;

        var tagName = token[nameStart..i].ToString();
        return !MediaWikiFormattingTags.Contains(tagName);
    }

    private static (string Before, string After) ExtractChangedProse(
        string rawOld,
        string rawNew,
        int? oi,
        int? ni,
        IReadOnlyList<DiffSegment> segments)
    {
        var oldView = rawOld.Length > 0 ? ExtractBlockProse(rawOld) : new WikitextProseView("", []);
        var newView = rawNew.Length > 0 ? ExtractBlockProse(rawNew) : new WikitextProseView("", []);

        bool refsChanged = !oldView.References.SequenceEqual(newView.References, StringComparer.Ordinal);
        var before = refsChanged ? FormatWithCitations(oldView) : oldView.Prose;
        var after = refsChanged ? FormatWithCitations(newView) : newView.Prose;

        if (!string.Equals(before, after, StringComparison.Ordinal))
        {
            return (before, after);
        }

        // Infobox / template value edits produce Segment.Prose even though WikitextProse.Of drops templates.
        var blockSegments = segments
            .Where(s => !s.Moved
                && ((oi is not null && s.OldParagraph == oi) || (ni is not null && s.NewParagraph == ni))
                && s.Prose.Length > 0)
            .ToList();

        if (blockSegments.Count > 0)
        {
            var segBefore = string.Join("; ", blockSegments.Where(s => s.Op == DiffOp.Removed).Select(s => s.Prose));
            var segAfter = string.Join("; ", blockSegments.Where(s => s.Op == DiffOp.Added).Select(s => s.Prose));
            if (!string.Equals(segBefore, segAfter, StringComparison.Ordinal))
            {
                return (segBefore, segAfter);
            }
        }

        if (before.Length > 0 || after.Length > 0)
        {
            return (before, after);
        }

        return (rawOld, rawNew);
    }

    private static string FormatWithCitations(WikitextProseView view)
    {
        var sb = new StringBuilder(view.Prose);
        foreach (var citation in view.References)
        {
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(string.IsNullOrWhiteSpace(citation) ? "[citation]" : $"[citation: {citation}]");
        }
        return sb.ToString();
    }

    // ── Token-level diffing ────────────────────────────────────────────────────────────────────────────────────────

    private readonly record struct PlainToken(string Text, bool IsSpace, bool IsWord);

    private readonly record struct TokenRun(bool Equal, int OldStart, int OldLength, int NewStart, int NewLength);

    internal static IReadOnlyList<DiffToken> DiffStrings(string before, string after)
    {
        if (string.Equals(before, after, StringComparison.Ordinal))
        {
            return before.Length == 0 ? [] : [new DiffToken(before, DiffTokenKind.Same)];
        }
        if (before.Length == 0) return [new DiffToken(after, DiffTokenKind.Added)];
        if (after.Length == 0) return [new DiffToken(before, DiffTokenKind.Removed)];

        var a = TokenizePlain(before);
        var b = TokenizePlain(after);

        if (!ShareEnoughWords(a, b))
        {
            return [new DiffToken(before, DiffTokenKind.Removed), new DiffToken(after, DiffTokenKind.Added)];
        }

        var ids = new Dictionary<string, int>(StringComparer.Ordinal);
        int Intern(string key) => ids.TryGetValue(key, out int id) ? id : (ids[key] = ids.Count);

        var ops = MyersDiff.Diff(
            a.Select(t => Intern(t.Text)).ToArray(),
            b.Select(t => Intern(t.Text)).ToArray(),
            MaxTokenEdits);

        if (ops is null)
        {
            return [new DiffToken(before, DiffTokenKind.Removed), new DiffToken(after, DiffTokenKind.Added)];
        }

        var runs = Absorb(BuildRuns(ops), a);
        var tokens = new List<DiffToken>(runs.Count * 2);

        foreach (var run in runs)
        {
            if (run.Equal)
            {
                AppendMerged(tokens, ConcatTokens(b, run.NewStart, run.NewLength), DiffTokenKind.Same);
            }
            else
            {
                if (run.OldLength > 0)
                {
                    AppendMerged(tokens, ConcatTokens(a, run.OldStart, run.OldLength), DiffTokenKind.Removed);
                }
                if (run.NewLength > 0)
                {
                    AppendMerged(tokens, ConcatTokens(b, run.NewStart, run.NewLength), DiffTokenKind.Added);
                }
            }
        }

        return tokens;
    }

    private static List<PlainToken> TokenizePlain(string s)
    {
        var tokens = new List<PlainToken>();
        int i = 0;
        while (i < s.Length)
        {
            char c = s[i];
            if (char.IsWhiteSpace(c))
            {
                int j = i + 1;
                while (j < s.Length && char.IsWhiteSpace(s[j])) j++;
                tokens.Add(new PlainToken(s[i..j], IsSpace: true, IsWord: false));
                i = j;
            }
            else if (WikitextTokenizer.IsWordChar(c))
            {
                int j = i + 1;
                while (j < s.Length && WikitextTokenizer.IsWordChar(s[j])) j++;
                tokens.Add(new PlainToken(s[i..j], IsSpace: false, IsWord: true));
                i = j;
            }
            else
            {
                int j = char.IsHighSurrogate(c) && i + 1 < s.Length ? i + 2 : i + 1;
                tokens.Add(new PlainToken(s[i..j], IsSpace: false, IsWord: false));
                i = j;
            }
        }
        return tokens;
    }

    private static bool ShareEnoughWords(List<PlainToken> a, List<PlainToken> b)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        int wa = 0, wb = 0, shared = 0;
        foreach (var t in a.Where(static t => t.IsWord))
        {
            wa++;
            counts[t.Text] = counts.GetValueOrDefault(t.Text) + 1;
        }
        foreach (var t in b.Where(static t => t.IsWord))
        {
            wb++;
            if (counts.TryGetValue(t.Text, out int c) && c > 0)
            {
                counts[t.Text] = c - 1;
                shared++;
            }
        }
        int shorter = Math.Min(wa, wb);
        return shorter == 0 || shared >= MinSharedWords * shorter;
    }

    private static List<TokenRun> BuildRuns(List<EditOp> ops)
    {
        var runs = new List<TokenRun>();
        int oi = 0, ni = 0, k = 0;
        while (k < ops.Count)
        {
            int os = oi, ns = ni;
            if (ops[k] == EditOp.Equal)
            {
                while (k < ops.Count && ops[k] == EditOp.Equal) { oi++; ni++; k++; }
                runs.Add(new TokenRun(true, os, oi - os, ns, ni - ns));
            }
            else
            {
                for (; k < ops.Count && ops[k] != EditOp.Equal; k++)
                {
                    if (ops[k] == EditOp.Delete) oi++;
                    else ni++;
                }
                runs.Add(new TokenRun(false, os, oi - os, ns, ni - ns));
            }
        }
        return runs;
    }

    private static List<TokenRun> Absorb(List<TokenRun> runs, List<PlainToken> a)
    {
        var result = new List<TokenRun>(runs.Count);
        foreach (var run in runs)
        {
            if (!run.Equal && result.Count >= 2 && !result[^2].Equal && result[^1].Equal
                && IsWeakRun(result[^1], a)
                && run.OldLength + result[^2].OldLength > 0
                && run.NewLength + result[^2].NewLength > 0)
            {
                var first = result[^2];
                result.RemoveRange(result.Count - 2, 2);
                result.Add(new TokenRun(
                    false,
                    first.OldStart,
                    run.OldStart + run.OldLength - first.OldStart,
                    first.NewStart,
                    run.NewStart + run.NewLength - first.NewStart));
            }
            else
            {
                result.Add(run);
            }
        }
        return result;
    }

    private static bool IsWeakRun(TokenRun equal, List<PlainToken> a)
    {
        int visible = 0;
        for (int k = equal.OldStart; k < equal.OldStart + equal.OldLength; k++)
        {
            var t = a[k];
            if (t.IsSpace) continue;
            if (t.IsWord || ++visible > 3) return false;
        }
        return true;
    }

    private static string ConcatTokens(List<PlainToken> tokens, int start, int length)
    {
        if (length <= 0) return "";
        if (length == 1) return tokens[start].Text;
        var sb = new StringBuilder();
        for (int i = start; i < start + length; i++) sb.Append(tokens[i].Text);
        return sb.ToString();
    }

    private static void AppendMerged(List<DiffToken> list, string text, DiffTokenKind kind)
    {
        if (text.Length == 0) return;
        if (list.Count > 0 && list[^1].Kind == kind)
        {
            list[^1] = new DiffToken(list[^1].Text + text, kind);
        }
        else
        {
            list.Add(new DiffToken(text, kind));
        }
    }

    // ── Highlighting & aligned translation helpers ─────────────────────────────────────────────────────────────────

    private static void MarkProjectionMatches(
        IReadOnlyList<DiffToken> tokens,
        bool[][] flags,
        string[] quotes,
        Func<DiffToken, bool> include)
    {
        var sb = new StringBuilder();
        var map = new List<(int TokenIndex, int CharIndex)>();

        for (int ti = 0; ti < tokens.Count; ti++)
        {
            if (!include(tokens[ti])) continue;
            var text = tokens[ti].Text;
            sb.Append(text);
            for (int ci = 0; ci < text.Length; ci++)
            {
                map.Add((ti, ci));
            }
        }

        if (sb.Length == 0) return;
        var projected = sb.ToString();

        foreach (var quote in quotes)
        {
            int searchFrom = 0;
            while (searchFrom <= projected.Length - quote.Length)
            {
                int found = projected.IndexOf(quote, searchFrom, StringComparison.Ordinal);
                if (found < 0) break;

                for (int k = found; k < found + quote.Length; k++)
                {
                    var (ti, ci) = map[k];
                    flags[ti][ci] = true;
                }
                searchFrom = found + 1;
            }
        }
    }

    private static List<(int Start, int End)> ClampSpans(string text, IReadOnlyList<(int Start, int Length)> spans)
    {
        var list = new List<(int Start, int End)>();
        foreach (var (rawStart, rawLength) in spans)
        {
            if (rawLength <= 0 || text.Length == 0) continue;
            int start = Math.Clamp(rawStart, 0, text.Length);
            int end = (int)Math.Clamp((long)rawStart + rawLength, start, text.Length);
            if (end > start) list.Add((start, end));
        }
        list.Sort(static (a, b) => a.Start != b.Start ? a.Start.CompareTo(b.Start) : a.End.CompareTo(b.End));

        var merged = new List<(int Start, int End)>(list.Count);
        foreach (var span in list)
        {
            if (merged.Count > 0 && span.Start < merged[^1].End)
            {
                merged[^1] = (merged[^1].Start, Math.Max(merged[^1].End, span.End));
            }
            else
            {
                merged.Add(span);
            }
        }
        return merged;
    }

    private static List<DiffToken> SliceMarked(string text, List<(int Start, int End)> spans, DiffTokenKind markedKind)
    {
        var tokens = new List<DiffToken>();
        int cursor = 0;
        foreach (var (start, end) in spans)
        {
            if (start > cursor) AppendMerged(tokens, text[cursor..start], DiffTokenKind.Same);
            AppendMerged(tokens, text[start..end], markedKind);
            cursor = end;
        }
        if (cursor < text.Length) AppendMerged(tokens, text[cursor..], DiffTokenKind.Same);
        return tokens;
    }

    private static List<DiffToken> SpliceReplacements(
        string beforeEn,
        List<(int Start, int End)> beforeSpans,
        string afterEn,
        List<(int Start, int End)> afterSpans)
    {
        var tokens = new List<DiffToken>();
        int cursor = 0;
        for (int i = 0; i < afterSpans.Count; i++)
        {
            var (start, end) = afterSpans[i];
            if (start > cursor) AppendMerged(tokens, afterEn[cursor..start], DiffTokenKind.Same);
            if (i < beforeSpans.Count)
            {
                var (bStart, bEnd) = beforeSpans[i];
                AppendMerged(tokens, beforeEn[bStart..bEnd], DiffTokenKind.Removed);
            }
            AppendMerged(tokens, afterEn[start..end], DiffTokenKind.Added);
            cursor = end;
        }

        for (int i = afterSpans.Count; i < beforeSpans.Count; i++)
        {
            var (bStart, bEnd) = beforeSpans[i];
            AppendMerged(tokens, beforeEn[bStart..bEnd], DiffTokenKind.Removed);
        }

        if (cursor < afterEn.Length) AppendMerged(tokens, afterEn[cursor..], DiffTokenKind.Same);
        return tokens;
    }

    private static IReadOnlyList<IReadOnlyList<DiffToken>> SplitParagraphs(List<DiffToken> flat)
    {
        if (flat.Count == 0) return [];

        var paragraphs = new List<List<DiffToken>> { new() };
        foreach (var token in flat)
        {
            var parts = token.Text.Split(ParagraphBreak, StringSplitOptions.None);
            for (int i = 0; i < parts.Length; i++)
            {
                if (i > 0)
                {
                    paragraphs.Add([]);
                }
                if (parts[i].Length > 0)
                {
                    paragraphs[^1].Add(new DiffToken(parts[i], token.Kind));
                }
            }
        }

        return paragraphs.Where(static p => p.Count > 0).ToArray();
    }
}
