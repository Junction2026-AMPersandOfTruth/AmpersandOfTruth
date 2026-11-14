using AmpersandOfTruth.Web.Features.Investigations.Cache;
using AmpersandOfTruth.Web.Features.Investigations.Pipeline;

namespace AmpersandOfTruth.Web.Features.Investigations;

public static class InvestigationsFeature
{
    public static IServiceCollection AddInvestigationsFeature(this IServiceCollection services)
    {
        services.AddOptions<InvestigationPipelineOptions>().BindConfiguration(InvestigationPipelineOptions.Section);

        services.AddSingleton<IInvestigationCorpus, MariaDbInvestigationCorpus>();
        services.AddSingleton<IRevisionStore, MariaDbRevisionStore>();

        // Pipeline stages (#39). Each is swapped for the winning P1 experiment without touching the rest.
        services.AddSingleton<ITopicSearch, VectorTopicSearch>();
        services.AddSingleton<ICandidateSource, RevertCandidateSource>();
        services.AddSingleton<CorpusEditReader>();
        services.AddSingleton<IEditReader>(sp =>
            ActivatorUtilities.CreateInstance<CachedEditReader>(sp, (IEditReader)sp.GetRequiredService<CorpusEditReader>()));
        services.AddSingleton<IEditClassifier, GeminiEditClassifier>();
        services.AddSingleton<InvestigationPipeline>();

        // Run cache (#40): a topic analysed before replays at once; the page's "Re-run" bypasses it.
        services.AddSingleton<PipelineFingerprint>();
        services.AddSingleton<InvestigationRunCache>();
        services.AddSingleton<IInvestigationRunner, CachingInvestigationRunner>();
        return services;
    }
}
