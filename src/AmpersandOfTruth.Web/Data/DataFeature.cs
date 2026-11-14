using Microsoft.EntityFrameworkCore;

namespace AmpersandOfTruth.Web.Data;

public static class DataFeature
{
    public static IServiceCollection AddAppData(this IServiceCollection services)
    {
        // Factory (not AddDbContext): Blazor Server circuits are long-lived and a DbContext isn't
        // thread-safe, so services create a short-lived context per operation.
        // Connection string is resolved lazily so tests can override it via configuration.
        services.AddDbContextFactory<AppDbContext>((sp, options) =>
            options.UseSqlite(sp.GetRequiredService<IConfiguration>().GetConnectionString("App")
                ?? throw new InvalidOperationException("Missing ConnectionStrings:App")));

        services.AddHealthChecks().AddDbContextCheck<AppDbContext>();
        return services;
    }

    /// <summary>Applies pending migrations on startup. Fine for a hackathon; don't do this with multiple instances.</summary>
    public static async Task MigrateDatabaseAsync(this WebApplication app)
    {
        var factory = app.Services.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        await db.Database.MigrateAsync();
    }
}
