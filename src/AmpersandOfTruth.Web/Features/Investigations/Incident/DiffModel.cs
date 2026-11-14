using System.Globalization;
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

        return new DiffModel(textMode, blocks, FoldBlocks(blocks));
    }

    public static TranslationPassageInput BuildTranslationInput(DiffPassage passage)
    {
        ArgumentNullException.ThrowIfNull(passage);

        var proseModel = Build(passage, DiffTextMode.Prose);
        var beforeSb = new StringBuilder();
        var afterSb = new StringBuilder();
        var changedBefore = new List<(int Start, int Length)>();
        var changedAfter = new List<(int Start, int Length)>();

        foreach (var block in proseModel.Blocks.Where(b => b.IsChanged))
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

        var quotes = evidenceQuotes?
            .Where(static q => !string.IsNullOrWhiteSpace(q))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (quotes is null || quotes.Length == 0)
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

    private static IReadOnlyList<DiffItem> FoldBlocks(IReadOnlyList<DiffBlock> blocks)
    {
        if (blocks.Count == 0) return [];

        bool anyChanged = blocks.Any(b => b.IsChanged);
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
                if (i > 0) keepVisible[i - 1] = true;
                if (i + 1 < blocks.Count) keepVisible[i + 1] = true;
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
