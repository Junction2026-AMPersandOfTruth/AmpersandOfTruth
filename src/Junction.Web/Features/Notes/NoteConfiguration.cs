using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Junction.Web.Features.Notes;

public sealed class NoteConfiguration : IEntityTypeConfiguration<Note>
{
    public void Configure(EntityTypeBuilder<Note> b)
    {
        b.ToTable("Notes");
        b.HasKey(n => n.Id);
        b.Property(n => n.Text).HasMaxLength(500).IsRequired();
        b.HasIndex(n => n.CreatedAt);
    }
}
