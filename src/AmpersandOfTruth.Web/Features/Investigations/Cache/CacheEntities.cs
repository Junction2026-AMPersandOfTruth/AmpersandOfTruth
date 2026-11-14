using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AmpersandOfTruth.Web.Features.Investigations.Cache;

/// <summary>
/// One read removed edit (span + diff + passages), content-addressed by revision ids. Revisions never change, so a row
/// is valid until <see cref="Key"/>'s version part changes (reader logic, wikidiff contract, prompt text cap).
/// </summary>
public sealed class EditReadingCacheRow
{
    public required string Key { get; init; }
    public required long PageId { get; init; }
    public required long RevertRevId { get; init; }
    public required string Json { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}

/// <summary>A whole finished investigation of one topic, replayed instantly on the next search (#40 warm path).</summary>
public sealed class InvestigationRunRow
{
    /// <summary><see cref="PipelineFingerprint.Value"/>: changes whenever anything that shapes the result changes.</summary>
    public required string Fingerprint { get; init; }

    /// <summary>Whitespace-normalised topic, case kept (same rule as the query-embedding cache).</summary>
    public required string Topic { get; init; }

    public required string Json { get; init; }
    public required int IncidentCount { get; init; }
    public required double ElapsedMs { get; init; }
    public required DateTimeOffset CreatedAt { get; set; }
}

public sealed class EditReadingCacheRowConfiguration : IEntityTypeConfiguration<EditReadingCacheRow>
{
    public void Configure(EntityTypeBuilder<EditReadingCacheRow> b)
    {
        b.ToTable("EditReadingCache");
        b.HasKey(e => e.Key);
        b.Property(e => e.Key).HasMaxLength(100);
        b.HasIndex(e => e.PageId);
    }
}

public sealed class InvestigationRunRowConfiguration : IEntityTypeConfiguration<InvestigationRunRow>
{
    public void Configure(EntityTypeBuilder<InvestigationRunRow> b)
    {
        b.ToTable("InvestigationRuns");
        b.HasKey(e => new { e.Fingerprint, e.Topic });
        b.Property(e => e.Fingerprint).HasMaxLength(32);
        b.Property(e => e.Topic).HasMaxLength(500);
        b.HasIndex(e => e.CreatedAt);
    }
}
