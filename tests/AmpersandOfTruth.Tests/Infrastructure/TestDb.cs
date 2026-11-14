using AmpersandOfTruth.Web.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AmpersandOfTruth.Tests.Infrastructure;

/// <summary>
/// Real SQLite, in memory, schema created by the app's actual migrations.
/// Every context opens its own connection to one named in-memory database, as the app does with its file. A
/// <see cref="SqliteConnection"/> isn't thread-safe, so sharing one broke code that queries from parallel tasks (#106).
/// The DB lives as long as <see cref="_keepAlive"/> is open → dispose with the test.
/// </summary>
public sealed class TestDb : IDisposable
{
    private readonly string _connectionString = $"Data Source=test-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Pooling=False";
    private readonly SqliteConnection _keepAlive;

    public TestDb()
    {
        _keepAlive = new SqliteConnection(_connectionString);
        _keepAlive.Open();
        using var db = CreateContext();
        db.Database.Migrate();
    }

    public DbContextOptions<AppDbContext> Options =>
        new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connectionString).Options;

    public AppDbContext CreateContext() => new(Options);

    public IDbContextFactory<AppDbContext> Factory => new SimpleFactory(this);

    /// <summary>Registers this DB as the app's IDbContextFactory (for bUnit / DI-based tests).</summary>
    public IServiceCollection AddTo(IServiceCollection services) =>
        services.AddSingleton(Factory);

    public void Dispose() => _keepAlive.Dispose();

    private sealed class SimpleFactory(TestDb db) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => db.CreateContext();
    }
}
