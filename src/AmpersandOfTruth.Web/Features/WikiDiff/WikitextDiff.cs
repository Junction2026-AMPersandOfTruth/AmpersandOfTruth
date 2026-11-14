using System.Text;

namespace AmpersandOfTruth.Web.Features.WikiDiff;

public enum DiffOp { Added, Removed }

/// <summary>What a changed segment is. Only <see cref="Prose"/>, <see cref="Reference"/> and infobox values are substantive.</summary>
public enum SegmentKind { Prose, Reference, Template, Category, Interwiki, File, Markup }

/// <param name="Text">The raw wikitext that was added or removed (an exact substring of that revision).</param>
/// <param name="Prose">Its visible text: words and link labels, the citation text of a reference, the values of an
/// infobox. Empty for markup, inline templates, categories, interwiki links and files.</param>
/// <param name="OldParagraph">0-based block index in the old revision; null for text that is new everywhere.</param>
/// <param name="NewParagraph">0-based block index in the new revision; null for text that is gone everywhere.</param>
/// <param name="Moved">The whole block was moved unchanged. It adds and removes nothing; the stats ignore it.</param>
public sealed record DiffSegment(
    DiffOp Op, SegmentKind Kind, string Text, string Prose, int? OldParagraph, int? NewParagraph, bool Moved);

/// <param name="IsBlanking">The edit blanked the page or a whole section (the evidence standard's blanking exclusion).</param>
/// <param name="IsSubstantive">False = only markup, whitespace, inline templates, categories, interwiki links or
/// files changed (the not-substantive exclusion).</param>
public sealed record DiffStats(
    int AddedProseChars, int RemovedProseChars, int AddedRefs, int RemovedRefs, bool IsBlanking, bool IsSubstantive);

public sealed record WikitextDiffResult(
    IReadOnlyList<DiffSegment> Segments, string AddedProse, string RemovedProse, DiffStats Stats);

/// <summary>
/// "Revision vs parent" diff of two wikitext strings (#29). Pure and thread-safe.
/// <list type="number">
/// <item>Both texts are split into blocks (<see cref="WikitextBlocks"/>) and diffed block by block (Myers).</item>
/// <item>A block removed in one place and added unchanged in another is one <c>moved</c> segment.</item>
/// <item>Each remaining hunk is re-diffed token by token (<see cref="WikitextTokenizer"/>), so a segment holds only the
/// words that changed. Unrelated rewrites (under 30% shared words) are reported as whole blocks instead of
/// confetti.</item>
/// </list>
/// </summary>
public static class WikitextDiff
{
    public const int Contract = 1;

    private const int MaxBlockEdits = 2000;
    private const int MaxTokenEdits = 1000;
    private const double MinSharedWords = 0.3;

    /// <summary>Blanking: the page's prose shrank below this share, or a section lost at least 1 − this share.</summary>
    internal const double BlankingRatio = 0.1;
    internal const int MinPageProseForBlanking = 50;
    internal const int MinSectionProseForBlanking = 100;

    /// <param name="oldText">The parent revision's wikitext; null for a page's first revision.</param>
    public static WikitextDiffResult Compare(string? oldText, string newText)
    {
        ArgumentNullException.ThrowIfNull(newText);
        return new Run(Normalize(oldText ?? ""), Normalize(newText)).Execute();
    }

    private static string Normalize(string s) => s.Contains('\r') ? s.Replace("\r\n", "\n").Replace('\r', '\n') : s;

    private sealed class Run(string oldText, string newText)
    {
        private readonly List<WikiBlock> _oldBlocks = WikitextBlocks.Split(oldText);
        private readonly List<WikiBlock> _newBlocks = WikitextBlocks.Split(newText);
        private readonly Dictionary<string, int> _ids = new(StringComparer.Ordinal);
        private readonly List<DiffSegment> _segments = [];
        private List<WikiToken>?[] _oldTokens = [];
        private List<WikiToken>?[] _newTokens = [];

        public WikitextDiffResult Execute()
        {
            _oldTokens = new List<WikiToken>?[_oldBlocks.Count];
            _newTokens = new List<WikiToken>?[_newBlocks.Count];

            var a = _oldBlocks.Select(b => Intern(b.Key)).ToArray();
            var b = _newBlocks.Select(b => Intern(b.Key)).ToArray();
            var ops = MyersDiff.Diff(a, b, MaxBlockEdits) ?? ReplaceAll(a.Length, b.Length);
            var hunks = Hunks(ops);
            var moves = FindMoves(hunks);

            foreach (var (removed, added) in hunks)
            {
                EmitHunk(removed.Where(r => !moves.ContainsKey(r)).ToList(), added.Where(n => !moves.ContainsValue(n)).ToList());
                foreach (var (from, to) in moves.Where(m => added.Contains(m.Value)).OrderBy(m => m.Value))
                {
                    EmitMoved(from, to);
                }
            }
            return new WikitextDiffResult(_segments, JoinProse(DiffOp.Added), JoinProse(DiffOp.Removed), Stats());
        }

        // ── Block level ────────────────────────────────────────────────────────────────────────────────────────────

        private int Intern(string key)
        {
            if (!_ids.TryGetValue(key, out int id)) _ids[key] = id = _ids.Count;
            return id;
        }

        private static List<EditOp> ReplaceAll(int n, int m) =>
            [.. Enumerable.Repeat(EditOp.Delete, n), .. Enumerable.Repeat(EditOp.Insert, m)];

        private static List<(List<int> Removed, List<int> Added)> Hunks(List<EditOp> ops)
        {
            var hunks = new List<(List<int>, List<int>)>();
            int oi = 0, ni = 0, k = 0;
            while (k < ops.Count)
            {
                if (ops[k] == EditOp.Equal)
                {
                    oi++;
                    ni++;
                    k++;
                    continue;
                }
                var removed = new List<int>();
                var added = new List<int>();
                for (; k < ops.Count && ops[k] != EditOp.Equal; k++)
                {
                    if (ops[k] == EditOp.Delete) removed.Add(oi++);
                    else added.Add(ni++);
                }
                hunks.Add((removed, added));
            }
            return hunks;
        }

        /// <summary>old block index → new block index, for blocks removed in one place and added verbatim in another.</summary>
        private Dictionary<int, int> FindMoves(List<(List<int> Removed, List<int> Added)> hunks)
        {
            var addedByKey = new Dictionary<string, Queue<int>>(StringComparer.Ordinal);
            foreach (var (_, added) in hunks)
            {
                foreach (int n in added)
                {
                    var key = _newBlocks[n].Key;
                    if (key.Length < 3) continue;
                    if (!addedByKey.TryGetValue(key, out var queue)) addedByKey[key] = queue = new Queue<int>();
                    queue.Enqueue(n);
                }
            }
            var moves = new Dictionary<int, int>();
            foreach (var (removed, _) in hunks)
            {
                foreach (int o in removed)
                {
                    if (addedByKey.TryGetValue(_oldBlocks[o].Key, out var queue) && queue.Count > 0) moves[o] = queue.Dequeue();
                }
            }
            return moves;
        }

        private List<WikiToken> OldTokens(int block) => _oldTokens[block] ??= Tokenize(oldText, _oldBlocks[block]);

        private List<WikiToken> NewTokens(int block) => _newTokens[block] ??= Tokenize(newText, _newBlocks[block]);

        private static List<WikiToken> Tokenize(string text, WikiBlock block)
        {
            var tokens = new List<WikiToken>();
            WikitextTokenizer.Tokenize(text, block.Start, block.End, block.Index, tokens);
            return tokens;
        }

        // ── Token level ────────────────────────────────────────────────────────────────────────────────────────────

        private void EmitHunk(List<int> removed, List<int> added)
        {
            var a = Concat(removed, OldTokens);
            var b = Concat(added, NewTokens);
            if (a.Count == 0 || b.Count == 0 || !ShareEnoughWords(a, b))
            {
                Emit(a, 0, a.Count, DiffOp.Removed, null);
                Emit(b, 0, b.Count, DiffOp.Added, null);
                return;
            }

            var ops = MyersDiff.Diff(a.Select(t => Intern(t.Key)).ToArray(), b.Select(t => Intern(t.Key)).ToArray(), MaxTokenEdits);
            if (ops is null)
            {
                Emit(a, 0, a.Count, DiffOp.Removed, null);
                Emit(b, 0, b.Count, DiffOp.Added, null);
                return;
            }

            var runs = Runs(ops);
            var oldToNew = new Dictionary<int, int>();
            var newToOld = new Dictionary<int, int>();
            foreach (var r in runs.Where(r => r.Equal))
            {
                for (int k = 0; k < r.OldLength; k++)
                {
                    var (x, y) = (a[r.OldStart + k], b[r.NewStart + k]);
                    if (x.Class == TokenClass.Separator || x.IsSpace) continue;
                    oldToNew.TryAdd(x.Block, y.Block);
                    newToOld.TryAdd(y.Block, x.Block);
                }
            }
            foreach (var r in Absorb(runs, a).Where(r => !r.Equal))
            {
                Emit(a, r.OldStart, r.OldStart + r.OldLength, DiffOp.Removed, oldToNew);
                Emit(b, r.NewStart, r.NewStart + r.NewLength, DiffOp.Added, newToOld);
            }
        }

        private static List<WikiToken> Concat(List<int> blocks, Func<int, List<WikiToken>> tokens)
        {
            var all = new List<WikiToken>();
            foreach (int block in blocks)
            {
                if (all.Count > 0) all.Add(WikiToken.Separator(block));
                all.AddRange(tokens(block));
            }
            return all;
        }

        /// <summary>Shared words / words of the shorter side. Below 30% a word diff is noise: report whole blocks.</summary>
        private static bool ShareEnoughWords(List<WikiToken> a, List<WikiToken> b)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            int wa = 0, wb = 0, shared = 0;
            foreach (var t in a.Where(t => t.IsWord))
            {
                wa++;
                counts[t.Key] = counts.GetValueOrDefault(t.Key) + 1;
            }
            foreach (var t in b.Where(t => t.IsWord))
            {
                wb++;
                if (counts.TryGetValue(t.Key, out int c) && c > 0)
                {
                    counts[t.Key] = c - 1;
                    shared++;
                }
            }
            int shorter = Math.Min(wa, wb);
            return shorter == 0 || shared >= MinSharedWords * shorter;
        }

        private readonly record struct TokenRun(bool Equal, int OldStart, int OldLength, int NewStart, int NewLength);

        private static List<TokenRun> Runs(List<EditOp> ops)
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

        /// <summary>
        /// Folds a short unchanged stretch of whitespace and punctuation between two changes into one replacement, so
        /// "alkoi, kun Neuvostoliitto" → "syttyi, koska Suomi" is one replacement rather than three. Two pure insertions
        /// (or deletions) stay apart: "Teksti." → "Teksti on pidempi.&lt;ref&gt;" keeps its full stop.
        /// </summary>
        private static List<TokenRun> Absorb(List<TokenRun> runs, List<WikiToken> a)
        {
            var result = new List<TokenRun>();
            foreach (var run in runs)
            {
                if (!run.Equal && result.Count >= 2 && !result[^2].Equal && result[^1].Equal && IsWeak(result[^1], a)
                    && run.OldLength + result[^2].OldLength > 0 && run.NewLength + result[^2].NewLength > 0)
                {
                    var first = result[^2];
                    result.RemoveRange(result.Count - 2, 2);
                    result.Add(new TokenRun(false, first.OldStart, run.OldStart + run.OldLength - first.OldStart,
                        first.NewStart, run.NewStart + run.NewLength - first.NewStart));
                }
                else
                {
                    result.Add(run);
                }
            }
            return result;
        }

        /// <summary>At most three tokens of whitespace and punctuation: no word, no reference, no paragraph break.</summary>
        private static bool IsWeak(TokenRun equal, List<WikiToken> a)
        {
            int visible = 0;
            for (int k = equal.OldStart; k < equal.OldStart + equal.OldLength; k++)
            {
                var t = a[k];
                if (t.IsSpace) continue;
                if (t.IsWord || t.Class is not (TokenClass.Prose or TokenClass.Markup) || ++visible > 3) return false;
            }
            return true;
        }

        // ── Segments ───────────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>Groups changed tokens [from, to) into segments: one per reference, category, interwiki or file
        /// link; one per run of template tokens; one per run of inline text (words, links, formatting).</summary>
        private void Emit(List<WikiToken> tokens, int from, int to, DiffOp op, Dictionary<int, int>? links)
        {
            int groupStart = -1;
            for (int k = from; k <= to; k++)
            {
                bool boundary = k == to
                    || tokens[k].Class == TokenClass.Separator
                    || (groupStart >= 0 && (IsAtomic(tokens[k].Class) || Group(tokens[k].Class) != Group(tokens[groupStart].Class)
                        || tokens[k].Block != tokens[groupStart].Block));
                if (boundary && groupStart >= 0)
                {
                    AddSegment(tokens, groupStart, k, op, links);
                    groupStart = -1;
                }
                if (k == to || tokens[k].Class == TokenClass.Separator) continue;
                if (groupStart < 0) groupStart = k;
                if (IsAtomic(tokens[k].Class))
                {
                    AddSegment(tokens, k, k + 1, op, links);
                    groupStart = -1;
                }
            }
        }

        private static bool IsAtomic(TokenClass c) => c is TokenClass.Reference or TokenClass.Category or TokenClass.Interwiki or TokenClass.File;

        private static int Group(TokenClass c) => c is TokenClass.Prose or TokenClass.Markup ? 0 : (int)c;

        private void AddSegment(List<WikiToken> tokens, int from, int to, DiffOp op, Dictionary<int, int>? links)
        {
            while (from < to && tokens[from].IsSpace) from++;
            while (to > from && tokens[to - 1].IsSpace) to--;
            if (from == to) return;

            var source = op == DiffOp.Added ? newText : oldText;
            var first = tokens[from];
            var text = source[first.Start..tokens[to - 1].End];
            var (kind, prose) = Describe(tokens, from, to);
            int? other = links is not null && links.TryGetValue(first.Block, out int linked) ? linked : null;
            _segments.Add(op == DiffOp.Added
                ? new DiffSegment(op, kind, text, prose, other, first.Block, false)
                : new DiffSegment(op, kind, text, prose, first.Block, other, false));
        }

        private void EmitMoved(int from, int to)
        {
            var tokens = NewTokens(to);
            var (kind, prose) = Describe(tokens, 0, tokens.Count);
            var block = _newBlocks[to];
            _segments.Add(new DiffSegment(DiffOp.Added, kind, newText[block.Start..block.End], prose, from, to, true));
        }

        private static (SegmentKind Kind, string Prose) Describe(List<WikiToken> tokens, int from, int to)
        {
            var classes = new HashSet<TokenClass>();
            var sb = new StringBuilder();
            for (int k = from; k < to; k++)
            {
                var t = tokens[k];
                if (t.Class == TokenClass.Separator) continue;
                if (!t.IsSpace) classes.Add(t.Class);
                if (t.Class != TokenClass.Reference || to - from == 1) sb.Append(t.Prose);
            }
            string prose = TextUtil.CleanValues(sb.ToString());
            if (classes.Count == 1 && classes.First() is var only && only != TokenClass.Prose && only != TokenClass.Markup)
            {
                return (only switch
                {
                    TokenClass.Reference => SegmentKind.Reference,
                    TokenClass.Category => SegmentKind.Category,
                    TokenClass.Interwiki => SegmentKind.Interwiki,
                    TokenClass.File => SegmentKind.File,
                    _ => SegmentKind.Template,
                }, prose);
            }
            bool inlineText = classes.Contains(TokenClass.Prose) || classes.Contains(TokenClass.Markup);
            if (inlineText && TextUtil.HasAlnum(prose)) return (SegmentKind.Prose, prose);
            return (classes.Contains(TokenClass.Template) ? SegmentKind.Template : SegmentKind.Markup, prose);
        }

        // ── Summary ────────────────────────────────────────────────────────────────────────────────────────────────

        private static bool CarriesProse(DiffSegment s) =>
            !s.Moved && (s.Kind == SegmentKind.Prose || (s.Kind == SegmentKind.Template && s.Prose.Length > 0));

        /// <summary>Prose and infobox values of the added (or removed) segments: " … " between fragments of one block, a newline between blocks.</summary>
        private string JoinProse(DiffOp op)
        {
            var sb = new StringBuilder();
            int? lastBlock = null;
            foreach (var s in _segments.Where(s => s.Op == op && CarriesProse(s)))
            {
                int? block = op == DiffOp.Added ? s.NewParagraph : s.OldParagraph;
                if (sb.Length > 0) sb.Append(block == lastBlock ? " … " : "\n");
                sb.Append(s.Prose);
                lastBlock = block;
            }
            return sb.ToString();
        }

        private DiffStats Stats()
        {
            int addedChars = 0, removedChars = 0, addedRefs = 0, removedRefs = 0;
            bool substantive = false;
            foreach (var s in _segments.Where(s => !s.Moved))
            {
                bool added = s.Op == DiffOp.Added;
                if (s.Kind == SegmentKind.Reference)
                {
                    if (added) addedRefs++;
                    else removedRefs++;
                    substantive = true;
                }
                if (CarriesProse(s))
                {
                    if (added) addedChars += s.Prose.Length;
                    else removedChars += s.Prose.Length;
                    substantive |= TextUtil.HasAlnum(s.Prose);
                }
            }
            return new DiffStats(addedChars, removedChars, addedRefs, removedRefs, IsBlanking(), substantive);
        }

        /// <summary>
        /// The page lost at least 90% of its prose, or some section (heading to heading, or the lead) lost at least 90%
        /// of its prose, while the edit added back less than 10% of what it removed. Prose here is running text only:
        /// templates, infoboxes and references do not count. An edit that leaves the page empty is always blanking.
        /// </summary>
        private bool IsBlanking()
        {
            if (string.IsNullOrWhiteSpace(newText)) return !string.IsNullOrWhiteSpace(oldText);

            int removed = 0, added = 0;
            var removedByBlock = new Dictionary<int, int>();
            foreach (var s in _segments.Where(s => !s.Moved && s.Kind == SegmentKind.Prose))
            {
                int n = TextUtil.AlnumCount(s.Prose);
                if (s.Op == DiffOp.Added)
                {
                    added += n;
                    continue;
                }
                removed += n;
                if (s.OldParagraph is int block) removedByBlock[block] = removedByBlock.GetValueOrDefault(block) + n;
            }
            if (removed < MinPageProseForBlanking * (1 - BlankingRatio) || added >= BlankingRatio * removed) return false;

            int oldTotal = 0, section = 0;
            var sectionTotal = new Dictionary<int, int>();
            var sectionRemoved = new Dictionary<int, int>();
            foreach (var block in _oldBlocks)
            {
                if (block.IsHeading)
                {
                    section++;
                    continue;
                }
                int n = ProseAlnum(OldTokens(block.Index));
                oldTotal += n;
                sectionTotal[section] = sectionTotal.GetValueOrDefault(section) + n;
                sectionRemoved[section] = sectionRemoved.GetValueOrDefault(section) + removedByBlock.GetValueOrDefault(block.Index);
            }
            int newTotal = _newBlocks.Where(b => !b.IsHeading).Sum(b => ProseAlnum(NewTokens(b.Index)));

            bool pageBlanked = oldTotal >= MinPageProseForBlanking && newTotal < BlankingRatio * oldTotal;
            bool sectionBlanked = sectionTotal.Any(kv =>
                kv.Value >= MinSectionProseForBlanking && sectionRemoved[kv.Key] >= (1 - BlankingRatio) * kv.Value);
            return pageBlanked || sectionBlanked;
        }

        private static int ProseAlnum(List<WikiToken> tokens) =>
            tokens.Where(t => t.Class is TokenClass.Prose or TokenClass.Markup).Sum(t => TextUtil.AlnumCount(t.Prose));
    }
}
