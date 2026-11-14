using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AmpersandOfTruth.Web.Features.Analysis.Store;

public sealed class AnalysisCallRecordConfiguration : IEntityTypeConfiguration<AnalysisCallRecord>
{
    public void Configure(EntityTypeBuilder<AnalysisCallRecord> b)
    {
        b.ToTable("AnalysisCalls");
        b.HasKey(r => r.Id);
        b.Property(r => r.Id).ValueGeneratedOnAdd();
        b.Property(r => r.Scope).HasMaxLength(200);
        b.Property(r => r.PromptId).HasMaxLength(100).IsRequired();
        b.Property(r => r.PromptVersion).HasMaxLength(100).IsRequired();
        b.Property(r => r.Model).HasMaxLength(100).IsRequired();
        b.Property(r => r.Job).HasConversion<string>().HasMaxLength(20);
        b.Property(r => r.Outcome).HasConversion<string>().HasMaxLength(20);
        b.Property(r => r.Error).HasMaxLength(1000);
        b.HasIndex(r => r.At);
        b.HasIndex(r => r.Scope);
        b.HasIndex(r => r.PromptId);
    }
}
