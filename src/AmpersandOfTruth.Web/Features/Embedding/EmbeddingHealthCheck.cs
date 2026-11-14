using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace AmpersandOfTruth.Web.Features.Embedding;

/// <summary>Is any Ollama endpoint serving the model? Failure status <c>Degraded</c>: never fails startup or <c>/health</c>.</summary>
public sealed class EmbeddingHealthCheck(OllamaQueryEmbedder ollama, IOptions<EmbeddingOptions> options) : IHealthCheck
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(Timeout);
        var model = options.Value.Model;
        var endpoint = await ollama.FindServingEndpointAsync(cts.Token);
        return endpoint is not null
            ? HealthCheckResult.Healthy($"{model} served by {endpoint}")
            : new HealthCheckResult(context.Registration.FailureStatus,
                $"no Ollama with '{model}' at {string.Join(", ", options.Value.Endpoints())} "
                + $"(`ollama pull {model}` locally, or scripts/corpus-tunnel.sh for the VPS)");
    }
}
