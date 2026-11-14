using AmpersandOfTruth.Web.Features.Library;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AmpersandOfTruth.Web.Features.Annotations;

public sealed class IncidentAnnotationConfiguration : IEntityTypeConfiguration<IncidentAnnotation>
{
    public void Configure(EntityTypeBuilder<IncidentAnnotation> b)
    {
        b.ToTable("IncidentAnnotations");
        b.HasKey(e => new { e.InvestigationId, e.IncidentId });
        b.Property(e => e.IncidentId).HasMaxLength(100).IsRequired();
        b.Property(e => e.Verdict).HasConversion<string>().HasMaxLength(32);
        b.Property(e => e.DismissReason).HasConversion<string>().HasMaxLength(32);
        b.Property(e => e.Note).HasMaxLength(4000);
        b.Property(e => e.Source).HasMaxLength(32);
        b.HasOne<SavedInvestigation>()
            .WithMany()
            .HasForeignKey(e => e.InvestigationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
