using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AmpersandOfTruth.Web.Features.Embedding;

public sealed class QueryEmbeddingConfiguration : IEntityTypeConfiguration<QueryEmbedding>
{
    public void Configure(EntityTypeBuilder<QueryEmbedding> b)
    {
        b.ToTable("QueryEmbeddings");
        b.HasKey(e => new { e.Model, e.Text });
        b.Property(e => e.Model).HasMaxLength(100);
        b.Property(e => e.Vector).IsRequired();
    }
}
