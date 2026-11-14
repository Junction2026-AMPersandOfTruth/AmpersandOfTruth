using AmpersandOfTruth.Web.Features.Embedding;
using AmpersandOfTruth.Web.Features.WikiDiff;
using Microsoft.Extensions.Options;

namespace AmpersandOfTruth.Web.Features.Investigations.Pipeline;

/// <summary>Limits of one run. Bound from <c>Investigations:Pipeline</c>; the defaults keep a topic inside the #40 budget.</summary>
public sealed class InvestigationPipelineOptions
{
    public const string Section = "Investigations:Pipeline";

    /// <summary>fi articles retrieved per topic (after folding en/sv hits onto their fi sibling).</summary>
    public int ArticleLimit { get; set; } = 10;

    /// <summary>Newest identity reverts considered per fi article.</summary>
    public int RevertsPerArticle { get; set; } = 50;

    /// <summary>Removed edits read and classified per topic, so a 3,000-revision article can't blow the budget.</summary>
    public int MaxCandidates { get; set; } = 40;

    /// <summary>Reverts that undid more revisions than this are skipped (P1.S1's <c>wide_span</c> exclusion).</summary>
    public int MaxSpanRevisions { get; set; } = 15;

    /// <summary>Candidates read + classified at once (corpus connections and LLM calls in flight).</summary>
    public int Parallelism { get; set; } = 4;

    /// <summary>Cap on each text variable sent to the LLM (inserted, removed, context).</summary>
    public int MaxPromptChars { get; set; } = 6000;
}

/// <summary>
/// Stage 1 = the R1 baseline (#8, <c>research/lib/aot.py</c> <c>nearest</c> + <c>fold_to_fi</c>): bge-m3 vector of the topic →
/// nearest article vectors in every language (3 × the limit) → en/sv hits folded onto their fi sibling by wikidata id,
/// best rank per fi article, hits without a fi article dropped. Only fi articles have edit history. P1.R6 (#27) replaces it.
/// </summary>
public sealed class VectorTopicSearch(IQueryEmbedder embedder, IInvestigationCorpus corpus, IOptions<InvestigationPipelineOptions> options)
    : ITopicSearch
{
    public async Task<IReadOnlyList<TopicArticle>> FindArticlesAsync(string topic, CancellationToken ct = default)
    {
        var k = options.Value.ArticleLimit;
        var vector = await embedder.EmbedAsync(topic, ct);
        var hits = await corpus.NearestArticlesAsync(vector, k * 3, ct);
        var siblings = await corpus.FiSiblingsAsync(
            hits.Where(h => !h.HasHistory && h.WikidataId is not null).Select(h => h.WikidataId!).ToList(), ct);

        var folded = new List<TopicArticle>();
        var seen = new HashSet<long>();
        foreach (var h in hits)
        {
            var fi = h.HasHistory ? h
                : h.WikidataId is { } w && siblings.TryGetValue(w, out var s) ? s with { Distance = h.Distance, ViaLang = h.Lang }
                : null;
            if (fi is not null && seen.Add(fi.PageId)) folded.Add(fi);
            if (folded.Count == k) break;
        }
        return folded;
    }
}

/// <summary>
/// Stage 2 placeholder (P0.8 query): identity reverts on the fi articles, newest first, taken round-robin over the
/// articles in retrieval order so the closest article doesn't use up the whole candidate cap. P1.S1/S4 replace it.
/// </summary>
public sealed class RevertCandidateSource(IInvestigationCorpus corpus, IRevisionStore revisions, IOptions<InvestigationPipelineOptions> options)
    : ICandidateSource
{
    public async Task<CandidateScan> ScanAsync(IReadOnlyList<TopicArticle> articles, CancellationToken ct = default)
    {
        var o = options.Value;
        var fi = articles.Where(a => a.HasHistory).DistinctBy(a => a.PageId).ToList();
        if (fi.Count == 0) return new CandidateScan(0, []);

        var pageIds = fi.Select(a => a.PageId).ToList();
        var count = await revisions.RevisionCountAsync(pageIds, ct);
        var reverts = (await corpus.RevertsAsync(pageIds, o.RevertsPerArticle, ct))
            .Where(r => r.RevertedCount >= 1 && r.RevertedCount <= o.MaxSpanRevisions)
            .ToLookup(r => r.PageId);

        var queues = fi
            .Select(a => new Queue<RemovedEdit>(reverts[a.PageId]
                .OrderByDescending(r => r.Timestamp).ThenByDescending(r => r.RevId)
                .Select(r => new RemovedEdit(a, r))))
            .Where(q => q.Count > 0)
            .ToList();

        var picked = new List<RemovedEdit>();
        while (picked.Count < o.MaxCandidates && queues.Any(q => q.Count > 0))
        {
            foreach (var q in queues.Where(q => q.Count > 0))
            {
                if (picked.Count == o.MaxCandidates) break;
                picked.Add(q.Dequeue());
            }
        }
        return new CandidateScan(count, picked);
    }
}

/// <summary>Stage 3a: the undone revisions + <see cref="WikitextDiff"/> of restored → last undone revision (P1.S2).</summary>
public sealed class CorpusEditReader(IRevisionStore revisions, IOptions<InvestigationPipelineOptions> options) : IEditReader
{
    /// <summary>Bump when what a reading contains changes (span rule, passages, context); invalidates the reading cache.</summary>
    public const int Version = 2;

    /// <summary>Re-insertions read per removed edit.</summary>
    internal const int MaxReinsertions = 50;

    public async Task<EditReading> ReadAsync(RemovedEdit edit, CancellationToken ct = default)
    {
        var r = edit.Revert;
        var pageId = edit.Article.PageId;

        var span = await revisions.SpanAsync(pageId, r.RestoredRevId, r.RevertedRevId, options.Value.MaxSpanRevisions + 5, ct);
        if (span.Count == 0)
            throw new InvalidOperationException($"No revisions between {r.RestoredRevId} and {r.RevertedRevId} on page {pageId}.");

        var before = await revisions.TextAsync(pageId, r.RestoredRevId, ct);
        var after = await revisions.TextAsync(pageId, r.RevertedRevId, ct)
            ?? throw new InvalidOperationException($"No text for revision {r.RevertedRevId} on page {pageId}.");

        var diff = WikitextDiff.Compare(before, after);
        var (inserted, removed) = Passages(before, after, diff);

        // P1.S1 re-insertion: a later revision restoring the exact content of a non-empty undone revision.
        var sha1s = span.Where(s => s.Bytes > 0 && s.Sha1 is not null).Select(s => s.Sha1!).Distinct().ToList();
        var restores = await revisions.RestoresAsync(pageId, r.Timestamp, r.RevId, sha1s, MaxReinsertions, ct);

        return new EditReading(edit, span, diff, inserted, removed, Context(before, diff, options.Value.MaxPromptChars))
        {
            Reinsertions = restores,
        };
    }

    /// <summary>A paragraph changed in place is widened to the whole paragraph on both sides up to this length.</summary>
    internal const int MaxWidenedParagraph = 1500;

    /// <summary>
    /// The changed wikitext per side. Segments are grouped by (old paragraph, new paragraph); a group with both added and
    /// removed text is a change in place and is shown as the whole old vs new paragraph (P0.5's "widen short replacements to
    /// the sentence", at paragraph granularity); everything else is the segment text itself. Moved blocks are skipped.
    /// </summary>
    internal static (string Inserted, string Removed) Passages(string? before, string after, WikitextDiffResult diff)
    {
        var oldText = TextUtil.NormalizeNewlines(before ?? "");
        var newText = TextUtil.NormalizeNewlines(after);
        var oldBlocks = WikitextBlocks.Split(oldText);
        var newBlocks = WikitextBlocks.Split(newText);

        static string? Block(string text, List<WikiBlock> blocks, int? i) =>
            i is { } k && k >= 0 && k < blocks.Count && blocks[k].End - blocks[k].Start <= MaxWidenedParagraph
                ? text[blocks[k].Start..blocks[k].End].Trim()
                : null;

        var inserted = new List<string>();
        var removed = new List<string>();
        foreach (var group in diff.Segments.Where(s => !s.Moved).GroupBy(s => (s.OldParagraph, s.NewParagraph)))
        {
            var added = group.Where(s => s.Op == DiffOp.Added).Select(s => s.Text.Trim()).ToList();
            var taken = group.Where(s => s.Op == DiffOp.Removed).Select(s => s.Text.Trim()).ToList();
            if (added.Count > 0 && taken.Count > 0
                && Block(newText, newBlocks, group.Key.NewParagraph) is { } newParagraph
                && Block(oldText, oldBlocks, group.Key.OldParagraph) is { } oldParagraph)
            {
                inserted.Add(newParagraph);
                removed.Add(oldParagraph);
            }
            else
            {
                inserted.AddRange(added);
                removed.AddRange(taken);
            }
        }

        static string Join(List<string> parts) => string.Join("\n…\n", parts.Where(p => p.Length > 0).Distinct());
        return (Join(inserted), Join(removed));
    }

    /// <summary>
    /// Prose of the paragraphs the diff touched, as they read before the edit (the restored revision) — the prompt's
    /// "article text around the change, before the edit". Empty when the edit only added new paragraphs.
    /// </summary>
    internal static string Context(string? before, WikitextDiffResult diff, int maxChars)
    {
        if (string.IsNullOrEmpty(before)) return "";
        var normalized = TextUtil.NormalizeNewlines(before);
        var blocks = WikitextBlocks.Split(normalized);
        var parts = diff.Segments
            .Where(s => !s.Moved && s.OldParagraph is >= 0 && s.OldParagraph < blocks.Count)
            .Select(s => s.OldParagraph!.Value)
            .Distinct()
            .Order()
            .Select(i => WikitextProse.Of(normalized[blocks[i].Start..blocks[i].End]).Prose.Trim())
            .Where(p => p.Length > 0);
        return Truncate(string.Join("\n\n", parts), maxChars);
    }

    internal static string Truncate(string s, int maxChars) =>
        s.Length <= maxChars ? s : string.Concat(s.AsSpan(0, maxChars), " …");
}
