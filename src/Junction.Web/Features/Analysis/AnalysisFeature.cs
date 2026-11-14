using Junction.Web.Features.Analysis.Gemini;
using Junction.Web.Features.Analysis.Prompts;
using Junction.Web.Features.Analysis.Store;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Junction.Web.Features.Analysis;

/// <summary>Gemini analysis client (issue #16). Everything is a singleton: shared by all circuits, all thread-safe.</summary>
public static class AnalysisFeature
{
    public static IServiceCollection AddAnalysisFeature(this IServiceCollection services)
    {
        services.AddOptions<GeminiOptions>().BindConfiguration(GeminiOptions.Section);

        // One long-lived HttpClient; pooled connections recycled so DNS changes are picked up.
        // Per-attempt timeouts are enforced by GeminiClient (GeminiOptions.RequestTimeout), not HttpClient.Timeout.
        services.AddSingleton(sp => ActivatorUtilities.CreateInstance<GeminiClient>(sp,
            new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
            {
                Timeout = Timeout.InfiniteTimeSpan,
            }));

        services.AddSingleton<PromptLibrary>();
        services.AddSingleton<AnalysisCache>();
        services.AddSingleton<AnalysisLedger>();
        services.AddSingleton<ModelPricing>();
        services.AddSingleton<AnalysisBudget>();
        services.AddSingleton<GeminiAnalysisModel>();
        services.AddSingleton<IAnalysisModel>(sp => sp.GetRequiredService<GeminiAnalysisModel>());

        services.AddHealthChecks().AddCheck<GeminiHealthCheck>("gemini", HealthStatus.Degraded, ["gemini"]);
        return services;
    }
}
