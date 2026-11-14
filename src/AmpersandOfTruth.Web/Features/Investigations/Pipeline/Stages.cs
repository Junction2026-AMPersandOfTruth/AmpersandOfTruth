using AmpersandOfTruth.Web.Features.Analysis.Prompts;
using AmpersandOfTruth.Web.Features.WikiDiff;

namespace AmpersandOfTruth.Web.Features.Investigations.Pipeline;

/// <summary>Runs one investigation and streams its progress. Implementations: <see cref="InvestigationPipeline"/> (real);
/// the P2 fixture runner (#98) replays sample incidents through the same events.</summary>
public interface IInvestigationRunner
{
    /// <param name="refresh">Ignore a cached analysis of this topic and run it again (the "Re-run" action).</param>
    IAsyncEnumerable<InvestigationEvent> RunAsync(string topic, bool refresh = false, CancellationToken ct = default);
}

// ── Stage contracts. Each real stage is a placeholder built from what main had at W2; W4 swaps in the winners of the
//    P1 experiments (R6 → ITopicSearch, S1/S4 → ICandidateSource, S9 → IEditClassifier, S10 → IncidentAssembler)
//    without touching the orchestrator or the UI. Stages throw EmbeddingUnavailableException / CorpusUnavailableException
//    for a missing service; the orchestrator turns those into StageFailed.

/// <summary>Stage 1 (P1.R6): topic → articles, closest first.</summary>
public interface ITopicSearch
{
    Task<IReadOnlyList<TopicArticle>> FindArticlesAsync(string topic, CancellationToken ct = default);
}

/// <summary>Stage 2 (P1.S1/S4): the found fi articles → removed edits worth reading, most promising first.</summary>
public interface ICandidateSource
{
    Task<CandidateScan> ScanAsync(IReadOnlyList<TopicArticle> articles, CancellationToken ct = default);
}

/// <summary>Stage 3a (P1.S2/S3): one removed edit → its revisions and the wikitext diff of what it added and removed.</summary>
public interface IEditReader
{
    Task<EditReading> ReadAsync(RemovedEdit edit, CancellationToken ct = default);
}

/// <summary>Stage 3b (P1.S9): the LLM's reading of one edit. Never throws for model-side problems — returns null and the
/// incident degrades to the lowest tier.</summary>
public interface IEditClassifier
{
    Task<EditClassification?> ClassifyAsync(EditReading reading, string scope, CancellationToken ct = default);
}

/// <summary>A removed edit: the span of revisions one identity revert undid on a fi article.</summary>
public sealed record RemovedEdit(TopicArticle Article, RevertEvent Revert)
{
    public string Key => $"{Article.PageId}-{Revert.RevId}";
}

public sealed record CandidateScan(int RevisionsScanned, IReadOnlyList<RemovedEdit> Candidates);

/// <summary>One revision of a page (<c>corpus.revision</c> row, without text). <see cref="Sha1"/> = content hash (base 36).</summary>
public sealed record RevisionRow(
    long PageId, long RevId, long? ParentRevId, DateTime Timestamp, string? User, string? Comment, int Bytes, string? Sha1 = null);

/// <summary>
/// What stage 3a learned about a removed edit. <see cref="Span"/> = the undone revisions, oldest first (never empty).
/// <see cref="Diff"/> = restored revision → last undone revision, i.e. everything the span added (and took out) that
/// the revert removed (put back). <see cref="InsertedText"/> / <see cref="RemovedText"/> = the changed wikitext as the
/// LLM sees it (and quotes are verified against): changed spans joined by "…", with a paragraph changed in place shown
/// whole on both sides so a swapped word reads as a sentence. <see cref="Context"/> = prose of the touched paragraphs
/// before the edit.
/// </summary>
public sealed record EditReading(
    RemovedEdit Edit, IReadOnlyList<RevisionRow> Span, WikitextDiffResult Diff, string InsertedText, string RemovedText, string Context)
{
    public RevisionRow FirstInsert => Span[0];

    /// <summary>
    /// Later revisions that restored the exact content of a (non-empty) undone revision after the revert — P1.S1's
    /// re-insertion rule, every occurrence rather than only the first. Oldest first; identical consecutive revisions count once.
    /// </summary>
    public IReadOnlyList<RevisionRow> Reinsertions { get; init; } = [];
}
