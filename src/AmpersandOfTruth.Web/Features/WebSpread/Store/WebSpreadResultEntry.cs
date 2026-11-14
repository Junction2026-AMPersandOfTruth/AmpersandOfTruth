using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AmpersandOfTruth.Web.Features.WebSpread.Store;

/// <summary>
/// One finished trace per incident (table <c>WebSpreadResults</c>, P3.7 "results persist with the incident"), so reopening
/// an incident is instant and demo safe mode can show it without any live call. Only clean results are stored
/// (<see cref="WebSpreadResults.IsStorable"/>).
/// </summary>
public sealed class WebSpreadResultEntry
{
    /// <summary>PK: the incident id (<c>IncidentView.Id</c>).</summary>
    public required string IncidentId { get; init; }
    /// <summary><see cref="WebSpreadTracer.Version"/> when it was traced; other versions are ignored on read.</summary>
    public required int Version { get; init; }
    /// <summary>Stored as string.</summary>
    public required Spread.WebSpreadStatus Status { get; init; }
    /// <summary>The serialised <see cref="Spread.WebSpreadResult"/> (<see cref="WebSpreadJson.Options"/>).</summary>
    public required string ResultJson { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}

public sealed class WebSpreadResultEntryConfiguration : IEntityTypeConfiguration<WebSpreadResultEntry>
{
    public void Configure(EntityTypeBuilder<WebSpreadResultEntry> b)
    {
        b.ToTable("WebSpreadResults");
        b.HasKey(e => e.IncidentId);
        b.Property(e => e.IncidentId).HasMaxLength(200);
        b.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.ResultJson).IsRequired();
    }
}
