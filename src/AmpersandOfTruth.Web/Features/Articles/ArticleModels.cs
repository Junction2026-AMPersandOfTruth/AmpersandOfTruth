using AmpersandOfTruth.Web.Features.Evidence;

namespace AmpersandOfTruth.Web.Features.Articles;

public sealed record MonthBucket(
    int Year,
    int Month,
    int NamedEdits,
    int AnonEdits,
    int Removals,
    int MaxBytes);

public sealed record ArticleAggregates(
    long PageId,
    string TitleFi,
    string? TitleEn,
    int Revisions,
    DateTime FirstEdit,
    DateTime LastEdit,
    IReadOnlyList<MonthBucket> Months);

public sealed record IncidentMark(
    DateTime At,
    EvidenceTier Tier,
    string IncidentId,
    string Href);

/// <summary>UTC date range with inclusive <see cref="From"/> and exclusive <see cref="To"/>.</summary>
public sealed record DateRange(DateTime From, DateTime To);

public sealed record ArticleRevision(
    long RevId,
    long? ParentId,
    DateTime Ts,
    string? User,
    bool Anonymous,
    string? Comment,
    int Bytes,
    int DeltaBytes,
    bool WasRemoved);
