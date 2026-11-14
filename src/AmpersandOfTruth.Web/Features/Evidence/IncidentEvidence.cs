namespace AmpersandOfTruth.Web.Features.Evidence;

public enum ContentCategory
{
    Vandalism,
    TestOrNonsense,
    GoodFaithError,
    ContentDispute,
    PovPush,
    FalseFactualClaim,
    SourceManipulation,
    Other,
}

public enum RemovalReason
{
    None,
    Accuracy,
    Neutrality,
    Sourcing,
    Vandalism,
    Other,
}

public enum ExclusionReason
{
    NotSubstantive,
    SelfRemoval,
    Bot,
    Blanking,
    Kept,
    Graffiti,
}

public enum TierReasonKind
{
    RemovedByOthers,
    RemovalReason,
    Reinserted,
    EditWar,
    DisputeTemplate,
    ClaimContent,
    MultipleEditors,
    MultipleArticles,
    Persistence,
    SourceChange,
}

/// <summary>
/// Automated assessment (P1.S9). Confidence 1–5. HasVerifiedQuote = ≥1 quoted span found verbatim in the inserted text.
/// </summary>
public sealed record ContentAssessment(ContentCategory Category, int Confidence, bool HasVerifiedQuote);

public sealed record IncidentEvidence
{
    public required bool IsSubstantive { get; init; }
    public required bool RemovedByOtherAccount { get; init; }   // false = self-removal
    public bool InsertedByBot { get; init; }
    public bool IsBlanking { get; init; }
    public bool KeptInLatestRevision { get; init; }
    public ContentAssessment? Assessment { get; init; }          // null = not run / failed / over budget
    public RemovalReason RemovalReason { get; init; } = RemovalReason.None;
    public int ReinsertionCount { get; init; }                   // times put back after a removal, >= 0
    public bool InEditWar { get; init; }
    public bool DisputeTemplateAdded { get; init; }
    public int DistinctInsertingEditors { get; init; } = 1;      // >= 1
    public int DistinctArticles { get; init; } = 1;              // >= 1
    public TimeSpan InsertionSpan { get; init; }                 // first insertion → last re-insertion
    public bool SourceManipulation { get; init; }
}

public sealed record TierThresholds
{
    public int MinModelConfidence { get; init; } = 3;
    public int MinDistinctEditors { get; init; } = 2;
    public int MinDistinctArticles { get; init; } = 2;
    public int MinPersistenceReinsertions { get; init; } = 2;
    public TimeSpan MinPersistenceSpan { get; init; } = TimeSpan.FromDays(7);
    public int MinCoordinationSignals { get; init; } = 3;   // P1.S10 (#37): 2 → 3, overclaim 0.20 → 0.16 on golden dev
    public static TierThresholds Default { get; } = new();
}

public sealed record TierReason(TierReasonKind Kind, string Text);

public sealed record TierAssessment(EvidenceTier? Tier, ExclusionReason? Exclusion, IReadOnlyList<TierReason> Reasons)
{
    public bool IsIncident => Tier is not null;

    /// <summary>
    /// "{EvidenceTiers.Get(tier).Label} — because: {reason texts joined with ", "}."  (U+2014 em dash). null when excluded.
    /// </summary>
    public string? BecauseSentence => Tier is { } tier
        ? $"{EvidenceTiers.Get(tier).Label} \u2014 because: {string.Join(", ", Reasons.Select(r => r.Text))}."
        : null;
}
