using MySqlConnector;

namespace Junction.Web.Features.Corpus;

/// <summary>
/// Opens connections to the shared Wikipedia corpus (MariaDB on the VPS, reached through
/// <c>scripts/corpus-tunnel.sh</c>). Raw SQL only — no EF, no migrations for the corpus.
/// Singleton: wraps a pooled, thread-safe <see cref="MySqlDataSource"/>.
/// </summary>
/// <remarks>
/// Connection string = <c>ConnectionStrings:Corpus</c> (no secrets; defaults to the tunnel address)
/// + <c>Corpus:Password</c> from user-secrets / env (<c>Corpus__Password</c>).
/// </remarks>
public sealed class CorpusConnectionFactory : IAsyncDisposable
{
    private readonly MySqlDataSource _dataSource;

    public CorpusConnectionFactory(IConfiguration config)
    {
        var builder = new MySqlConnectionStringBuilder(config.GetConnectionString("Corpus") ?? "");
        if (config["Corpus:Password"] is { Length: > 0 } password)
            builder.Password = password;
        ConnectionString = builder.ConnectionString;
        Server = $"{builder.Server}:{builder.Port}/{builder.Database}";
        _dataSource = new MySqlDataSource(ConnectionString);
    }

    /// <summary>Includes the password — never log it.</summary>
    public string ConnectionString { get; }

    /// <summary>Host:port/database, safe to log or show (no credentials).</summary>
    public string Server { get; }

    /// <summary>Open a pooled connection. Dispose it per operation: <c>await using var c = await corpus.OpenAsync(ct);</c></summary>
    public async Task<MySqlConnection> OpenAsync(CancellationToken ct = default) =>
        await _dataSource.OpenConnectionAsync(ct);

    public ValueTask DisposeAsync() => _dataSource.DisposeAsync();
}
