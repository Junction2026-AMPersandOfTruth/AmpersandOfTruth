using AmpersandOfTruth.Web.Features.Evidence;
using AmpersandOfTruth.Web.Features.Investigations;

namespace AmpersandOfTruth.Web.Features.Annotations;

public enum AnalystVerdict
{
    Confirmed,
    Plausible,
    NotAnIncident,
    NeedsMoreEvidence,
}

public enum DismissReason
{
    OrdinaryVandalism,
    LegitimateDispute,
    WrongTranslation,
    WrongGrouping,
    Other,
}

public sealed class IncidentAnnotation
{
    public Guid InvestigationId { get; set; }
    public string IncidentId { get; set; } = "";
    public AnalystVerdict? Verdict { get; set; }
    public DismissReason? DismissReason { get; set; }
    public string? Note { get; set; }
    public string? Source { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed record AnnotatedIncident(
    Guid InvestigationId,
    string Topic,
    string TopicSlug,
    string IncidentId,
    IncidentRecord Incident,
    IncidentAnnotation Annotation);

public static class AnnotationLabels
{
    public static string ForVerdict(AnalystVerdict verdict) =>
        verdict switch
        {
            AnalystVerdict.Confirmed => "Analyst: confirmed",
            AnalystVerdict.Plausible => "Analyst: plausible",
            AnalystVerdict.NotAnIncident => "Analyst: not an incident",
            AnalystVerdict.NeedsMoreEvidence => "Analyst: needs more evidence",
            _ => throw new ArgumentOutOfRangeException(nameof(verdict), verdict, null),
        };

    public static string ForDismissReason(DismissReason reason) =>
        reason switch
        {
            DismissReason.OrdinaryVandalism => "Ordinary vandalism",
            DismissReason.LegitimateDispute => "Legitimate dispute",
            DismissReason.WrongTranslation => "Wrong translation",
            DismissReason.WrongGrouping => "Wrong grouping",
            DismissReason.Other => "Other",
            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null),
        };

    public static bool IsDismissed(IncidentAnnotation? annotation) =>
        annotation is not null
        && (annotation.Verdict == AnalystVerdict.NotAnIncident || annotation.DismissReason is not null);

    public static bool IsDisagreement(EvidenceTier tier, AnalystVerdict? verdict) =>
        verdict switch
        {
            AnalystVerdict.NotAnIncident => true,
            AnalystVerdict.Confirmed => tier == EvidenceTier.Reverted,
            AnalystVerdict.NeedsMoreEvidence => tier is EvidenceTier.Contested or EvidenceTier.Campaign,
            _ => false,
        };
}
