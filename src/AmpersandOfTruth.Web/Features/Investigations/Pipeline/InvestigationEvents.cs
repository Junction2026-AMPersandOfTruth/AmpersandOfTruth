namespace AmpersandOfTruth.Web.Features.Investigations.Pipeline;

/// <summary>
/// The four stages the user sees (<c>docs/design/components/StageTracker</c>): Finding articles → Scanning edit history →
/// Reading suspicious edits → Assembling incidents.
/// </summary>
public enum InvestigationStage { FindArticles, ScanHistory, ReadEdits, AssembleIncidents }

/// <summary>Why a stage failed, so the UI can pick the right banner without parsing messages.</summary>
public enum StageFailureKind { EmbeddingUnavailable, CorpusUnavailable, Error }

/// <summary>
/// What <see cref="IInvestigationRunner.RunAsync"/> streams (#98 §1, adopted by #39). Order of one run:
/// <see cref="ArticlesFound"/> → <see cref="RevisionsScanned"/> → <see cref="CandidateScored"/> × N → <see cref="Completed"/>.
/// A <see cref="StageFailed"/> ends the run early (no <see cref="Completed"/> follows). Cancellation ends the stream
/// with <see cref="OperationCanceledException"/>.
/// </summary>
public abstract record InvestigationEvent;

/// <summary>Stage 1 done: articles closest to the topic, closest first (all languages; only fi ones have history).</summary>
public sealed record ArticlesFound(int Count, IReadOnlyList<TopicArticle> Items) : InvestigationEvent;

/// <summary>
/// Stage 2 done: <paramref name="Count"/> revisions of the found fi articles were scanned and these removed edits
/// will be read. <paramref name="Candidates"/> extends #98's <c>RevisionsScanned(n)</c>: the overview needs the
/// removed edits that did not become incidents, and the tracker needs the "of N" before the first incident lands.
/// </summary>
public sealed record RevisionsScanned(int Count, IReadOnlyList<RemovedEdit> Candidates) : InvestigationEvent;

/// <summary>
/// Stages 3–4 progress, one per candidate in completion order (<paramref name="Index"/> is 1-based, ≤ <paramref name="Of"/>).
/// <paramref name="Incident"/> null = the candidate was read and excluded by the evidence standard (not substantive,
/// self-removal, bot, blanking, graffiti) or could not be read; <paramref name="Exclusion"/> says which when known.
/// </summary>
public sealed record CandidateScored(int Index, int Of, RemovedEdit Candidate, IncidentRecord? Incident, string? Exclusion = null)
    : InvestigationEvent;

public sealed record StageFailed(InvestigationStage Stage, StageFailureKind Kind, string Error) : InvestigationEvent;

/// <summary>The run finished. <paramref name="Incidents"/> = every incident emitted, ranked (score descending).</summary>
public sealed record Completed(bool FromCache, IReadOnlyList<IncidentRecord> Incidents, TimeSpan Elapsed) : InvestigationEvent;
