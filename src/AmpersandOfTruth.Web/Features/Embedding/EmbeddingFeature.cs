using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AmpersandOfTruth.Web.Features.Embedding;

public static class EmbeddingFeature
{
    public static IServiceCollection AddEmbeddingFeature(this IServiceCollection services)
    {
        services.AddOptions<EmbeddingOptions>().BindConfiguration("Embedding");
        // No client-level timeout: OllamaQueryEmbedder applies Embedding:TimeoutSeconds per endpoint.
        services.AddHttpClient(OllamaQueryEmbedder.HttpClientName, c => c.Timeout = Timeout.InfiniteTimeSpan);
        services.AddSingleton<OllamaQueryEmbedder>();
        services.AddSingleton<IQueryEmbedder>(sp =>
            ActivatorUtilities.CreateInstance<CachedQueryEmbedder>(sp, (IQueryEmbedder)sp.GetRequiredService<OllamaQueryEmbedder>()));
        services.AddHealthChecks().AddCheck<EmbeddingHealthCheck>("embedding", HealthStatus.Degraded, ["embedding"]);
        return services;
    }
}
