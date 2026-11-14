using AmpersandOfTruth.Web.Features.Evidence;
using AmpersandOfTruth.Web.Features.Investigations;

namespace AmpersandOfTruth.Web.Features.Library;

public sealed class SavedInvestigation
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public string Topic { get; set; } = "";
    public string TopicSlug { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string OwnerKey { get; set; } = "";
    public string PipelineVersion { get; set; } = "";
    public string SnapshotJson { get; set; } = "";
    public int IncidentCount { get; set; }
    public string TierCountsJson { get; set; } = "";
}

public sealed class PinnedIncident
{
    public Guid InvestigationId { get; set; }
    public string IncidentId { get; set; } = "";
    public string? Note { get; set; }
    public DateTimeOffset PinnedAt { get; set; }
}

public sealed record SavedSnapshot(
    string Topic,
    IReadOnlyList<TopicArticle> Articles,
    IReadOnlyList<IncidentRecord> Incidents,
    DateTimeOffset AnalysedAt);

public sealed record SavedSummary(
    Guid Id,
    string Title,
    string Topic,
    string TopicSlug,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int IncidentCount,
    IReadOnlyDictionary<EvidenceTier, int> TierCounts);
