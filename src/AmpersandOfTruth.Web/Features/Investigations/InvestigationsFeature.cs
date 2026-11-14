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

        // Pipeline stages (#39). Each is swapped for the winning P1 experiment without touching the rest.
        services.AddSingleton<ITopicSearch, VectorTopicSearch>();
        services.AddSingleton<ICandidateSource, RevertCandidateSource>();
        services.AddSingleton<IEditReader, CorpusEditReader>();
        services.AddSingleton<IEditClassifier, GeminiEditClassifier>();

        // P2.0 seam (#98 §1): sample fixtures, mapper, and source switch (default = Fixtures).
        services.AddSingleton<SampleIncidents>();
        services.AddSingleton<IncidentMapper>();
        services.AddSingleton<RecentRuns>();
        services.AddSingleton<HybridFixtureRunner>();
        services.AddSingleton<InvestigationPipeline>();
        services.AddSingleton<FixtureIncidentSource>();
        services.AddSingleton<RecentRunIncidentSource>();

        services.AddSingleton<IInvestigationRunner>(sp =>
            sp.GetRequiredService<IOptions<InvestigationSourceOptions>>().Value.Source == IncidentSourceKind.Pipeline
                ? sp.GetRequiredService<InvestigationPipeline>()
                : sp.GetRequiredService<HybridFixtureRunner>());

        services.AddSingleton<IIncidentSource>(sp =>
            sp.GetRequiredService<IOptions<InvestigationSourceOptions>>().Value.Source == IncidentSourceKind.Pipeline
                ? sp.GetRequiredService<RecentRunIncidentSource>()
                : sp.GetRequiredService<FixtureIncidentSource>());

        return services;
    }
}
