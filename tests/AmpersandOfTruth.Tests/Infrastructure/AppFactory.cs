using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AmpersandOfTruth.Tests.Infrastructure;

/// <summary>Boots the real app against a throwaway SQLite file (migrations run on startup as in prod).</summary>
public sealed class AppFactory : WebApplicationFactory<Program>
{
    static AppFactory() =>
        Environment.SetEnvironmentVariable("DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE", "false");

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"junction-test-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.UseSetting("ConnectionStrings:App", $"Data Source={_dbPath};Pooling=False");

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        File.Delete(_dbPath);
    }
}
