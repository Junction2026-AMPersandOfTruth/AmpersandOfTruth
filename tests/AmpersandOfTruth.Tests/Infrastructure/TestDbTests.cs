using Microsoft.EntityFrameworkCore;

namespace AmpersandOfTruth.Tests.Infrastructure;

public class TestDbTests
{
    /// <summary>
    /// #106: with one shared connection, the pipeline's parallel workers corrupted it now and then. The app opens a
    /// connection per context, so the test DB must too.
    /// </summary>
    [Fact]
    public async Task Each_context_has_its_own_connection_to_the_same_database()
    {
        using var testDb = new TestDb();
        await using var a = testDb.CreateContext();
        await using var b = await testDb.Factory.CreateDbContextAsync();

        await a.Database.ExecuteSqlRawAsync("CREATE TABLE Probe (X INTEGER); INSERT INTO Probe VALUES (7);");

        Assert.NotSame(a.Database.GetDbConnection(), b.Database.GetDbConnection());
        Assert.Equal(7, await b.Database.SqlQueryRaw<int>("SELECT X AS Value FROM Probe").SingleAsync());
    }
}
