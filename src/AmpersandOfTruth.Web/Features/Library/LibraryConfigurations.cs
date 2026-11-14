using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AmpersandOfTruth.Web.Features.Library;

public sealed class SavedInvestigationConfiguration : IEntityTypeConfiguration<SavedInvestigation>
{
    public void Configure(EntityTypeBuilder<SavedInvestigation> b)
    {
        b.ToTable("SavedInvestigations");
        b.HasKey(e => e.Id);
        b.Property(e => e.Title).HasMaxLength(200).IsRequired();
        b.Property(e => e.Topic).HasMaxLength(500).IsRequired();
        b.Property(e => e.TopicSlug).HasMaxLength(500).IsRequired();
        b.Property(e => e.OwnerKey).HasMaxLength(64).IsRequired();
        b.Property(e => e.PipelineVersion).HasMaxLength(64).IsRequired();
        b.Property(e => e.SnapshotJson).IsRequired();
        b.Property(e => e.TierCountsJson).IsRequired();
        b.HasIndex(e => e.OwnerKey);
    }
}

public sealed class PinnedIncidentConfiguration : IEntityTypeConfiguration<PinnedIncident>
{
    public void Configure(EntityTypeBuilder<PinnedIncident> b)
    {
        b.ToTable("PinnedIncidents");
        b.HasKey(e => new { e.InvestigationId, e.IncidentId });
        b.Property(e => e.IncidentId).HasMaxLength(100).IsRequired();
        b.Property(e => e.Note).HasMaxLength(2000);
        b.HasOne<SavedInvestigation>()
            .WithMany()
            .HasForeignKey(e => e.InvestigationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
