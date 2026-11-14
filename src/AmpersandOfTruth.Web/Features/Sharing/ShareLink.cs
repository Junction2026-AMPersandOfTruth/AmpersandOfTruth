namespace AmpersandOfTruth.Web.Features.Sharing;

public sealed class ShareLink
{
    public string TokenHash { get; set; } = "";
    public Guid InvestigationId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

public sealed record ShareLinkInfo(
    string TokenHashPrefix,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt);
