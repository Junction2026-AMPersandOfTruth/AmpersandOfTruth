using AmpersandOfTruth.Web.Features.Library;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AmpersandOfTruth.Web.Features.Sharing;

public sealed class ShareLinkConfiguration : IEntityTypeConfiguration<ShareLink>
{
    public void Configure(EntityTypeBuilder<ShareLink> b)
    {
        b.ToTable("ShareLinks");
        b.HasKey(e => e.TokenHash);
        b.Property(e => e.TokenHash).HasMaxLength(64).IsRequired();
        b.HasIndex(e => e.InvestigationId);
        b.HasOne<SavedInvestigation>()
            .WithMany()
            .HasForeignKey(e => e.InvestigationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
