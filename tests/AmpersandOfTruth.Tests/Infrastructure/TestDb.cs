using AmpersandOfTruth.Web.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AmpersandOfTruth.Tests.Infrastructure;

/// <summary>
/// Real SQLite, in memory, schema created by the app's actual migrations.
/// The DB lives as long as the open connection → dispose with the test.
/// </summary>
public sealed class TestDb : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public TestDb()
    {
        _connection.Open();
        using var db = CreateContext();
        db.Database.Migrate();
    }

    public DbContextOptions<AppDbContext> Options =>
        new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;

    public AppDbContext CreateContext() => new(Options);

    public IDbContextFactory<AppDbContext> Factory => new SimpleFactory(this);

    /// <summary>Registers this DB as the app's IDbContextFactory (for bUnit / DI-based tests).</summary>
    public IServiceCollection AddTo(IServiceCollection services) =>
        services.AddSingleton(Factory);

    public void Dispose() => _connection.Dispose();

    private sealed class SimpleFactory(TestDb db) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => db.CreateContext();
    }
}
