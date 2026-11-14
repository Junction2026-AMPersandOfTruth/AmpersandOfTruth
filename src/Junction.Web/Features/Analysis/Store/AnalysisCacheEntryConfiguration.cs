using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Junction.Web.Features.Analysis.Store;

public sealed class AnalysisCacheEntryConfiguration : IEntityTypeConfiguration<AnalysisCacheEntry>
{
    public void Configure(EntityTypeBuilder<AnalysisCacheEntry> b)
    {
        b.ToTable("AnalysisCache");
        b.HasKey(e => e.Key);
        b.Property(e => e.Key).HasMaxLength(64);
        b.Property(e => e.PromptId).HasMaxLength(100).IsRequired();
        b.Property(e => e.PromptVersion).HasMaxLength(100).IsRequired();
        b.Property(e => e.Model).HasMaxLength(100).IsRequired();
        b.Property(e => e.Outcome).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.Error).HasMaxLength(2000);
        b.HasIndex(e => e.PromptId);
    }
}
