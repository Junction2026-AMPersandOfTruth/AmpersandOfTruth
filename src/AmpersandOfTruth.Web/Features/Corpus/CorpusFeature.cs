using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AmpersandOfTruth.Web.Features.Corpus;

public static class CorpusFeature
{
    public static IServiceCollection AddCorpusFeature(this IServiceCollection services)
    {
        services.AddSingleton<CorpusConnectionFactory>();
        services.AddHealthChecks().AddCheck<CorpusHealthCheck>("corpus", HealthStatus.Degraded, ["corpus"]);
        return services;
    }
}
