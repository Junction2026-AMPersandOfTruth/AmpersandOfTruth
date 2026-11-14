using AmpersandOfTruth.Web.Features.Investigations.Cache;
using AmpersandOfTruth.Web.Features.Investigations.Fixtures;
using AmpersandOfTruth.Web.Features.Investigations.Pipeline;
using AmpersandOfTruth.Web.Features.Investigations.View;
using Microsoft.Extensions.Options;

namespace AmpersandOfTruth.Web.Features.Investigations;

public static class InvestigationsFeature
{
    public static IServiceCollection AddInvestigationsFeature(this IServiceCollection services)
    {
        services.AddOptions<InvestigationSourceOptions>().BindConfiguration(InvestigationSourceOptions.Section);
        services.AddOptions<InvestigationPipelineOptions>().BindConfiguration(InvestigationPipelineOptions.Section);
        services.AddOptions<HybridFixtureOptions>().BindConfiguration(HybridFixtureOptions.Section);

        services.AddSingleton<IInvestigationCorpus, MariaDbInvestigationCorpus>();
        services.AddSingleton<IRevisionStore, MariaDbRevisionStore>();
        services.AddSingleton<IIncidentCorpus, MariaDbIncidentCorpus>();
        services.AddSingleton<IConflictEpisodes, MariaDbConflictEpisodes>();   // P1.S4's precomputed episodes (research.s4_*)

        // Pipeline stages (#39). Each is swapped for the winning P1 experiment without touching the rest.
        // Stage 1 = R3-D (R6 decision, #27) with R1 as its fallback; Investigations:Pipeline:TopicSearch=Vector forces R1.
        services.AddSingleton<VectorTopicSearch>();
        services.AddSingleton<ExpandedTopicSearch>();
        services.AddSingleton<ITopicSearch>(sp =>
            sp.GetRequiredService<IOptions<InvestigationPipelineOptions>>().Value.TopicSearch == TopicSearchMethod.Vector
                ? sp.GetRequiredService<VectorTopicSearch>()
                : sp.GetRequiredService<ExpandedTopicSearch>());
        services.AddSingleton<ICandidateSource, RevertCandidateSource>();
        services.AddSingleton<CorpusEditReader>();
        services.AddSingleton<IEditReader>(sp =>
            ActivatorUtilities.CreateInstance<CachedEditReader>(sp, (IEditReader)sp.GetRequiredService<CorpusEditReader>()));
        services.AddSingleton<IEditClassifier, GeminiEditClassifier>();
        services.AddSingleton<InvestigationPipeline>();

        // Run cache (#40): a topic analysed before replays at once; the page's "Re-run" bypasses it.
        services.AddSingleton<PipelineFingerprint>();
        services.AddSingleton<InvestigationRunCache>();
        services.AddSingleton<CachingInvestigationRunner>();

        // P2.0 seam (#98 §1): Investigations:Source = Fixtures (default: stages 1–2 real, sample incidents replayed)
        // | Pipeline (the cached real pipeline). Pages resolve IInvestigationRunner / IIncidentSource and never change.
        services.AddSingleton<SampleIncidents>();
        services.AddSingleton<IncidentMapper>();
        services.AddSingleton<RecentRuns>();
        services.AddSingleton<HybridFixtureRunner>();
        services.AddSingleton<FixtureIncidentSource>();
        services.AddSingleton<RecentRunIncidentSource>();

        services.AddSingleton<IInvestigationRunner>(sp =>
            sp.GetRequiredService<IOptions<InvestigationSourceOptions>>().Value.Source == IncidentSourceKind.Pipeline
                ? sp.GetRequiredService<CachingInvestigationRunner>()
                : sp.GetRequiredService<HybridFixtureRunner>());

        services.AddSingleton<IIncidentSource>(sp =>
            sp.GetRequiredService<IOptions<InvestigationSourceOptions>>().Value.Source == IncidentSourceKind.Pipeline
                ? sp.GetRequiredService<RecentRunIncidentSource>()
                : sp.GetRequiredService<FixtureIncidentSource>());

        services.AddSingleton<IHostedService>(SampleIncidentWarmup.Create);
        return services;
    }
}
