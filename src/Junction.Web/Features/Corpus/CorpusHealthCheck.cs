using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Junction.Web.Features.Corpus;

/// <summary>
/// Reports whether the corpus is reachable. Registered with failure status <c>Degraded</c>, so a missing
/// tunnel never fails startup or turns <c>/health</c> into a 503 — the app keeps working without the corpus.
/// </summary>
public sealed class CorpusHealthCheck(CorpusConnectionFactory corpus) : IHealthCheck
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(Timeout);
        try
        {
            await using var conn = await corpus.OpenAsync(cts.Token);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT VERSION()";
            var version = await cmd.ExecuteScalarAsync(cts.Token);
            return HealthCheckResult.Healthy($"corpus {corpus.Server} — MariaDB {version}");
        }
        catch (Exception ex)
        {
            return new HealthCheckResult(context.Registration.FailureStatus,
                $"corpus {corpus.Server} unreachable (is scripts/corpus-tunnel.sh running?)", ex);
        }
    }
}
