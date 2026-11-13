using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Junction.Web.Data;

/// <summary>
/// Single DbContext for the app. Features do NOT add DbSet properties here — they add an
/// IEntityTypeConfiguration&lt;T&gt; in their own folder (picked up automatically) and use db.Set&lt;T&gt;().
/// That keeps parallel feature work from colliding on this file.
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // SQLite can't ORDER BY / compare DateTimeOffset natively → store as UTC ticks (sortable, lossless).
        // The offset is normalized to UTC on read; keep all times UTC (TimeProvider.GetUtcNow()).
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<UtcTicksConverter>();
    }

    private sealed class UtcTicksConverter() : ValueConverter<DateTimeOffset, long>(
        v => v.UtcTicks,
        v => new DateTimeOffset(v, TimeSpan.Zero));
}
