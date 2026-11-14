using AmpersandOfTruth.Web.Features.Embedding;
using AmpersandOfTruth.Web.Features.Investigations;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AmpersandOfTruth.Web.Features.Search;

public static class SearchFeature
{
    public static IServiceCollection AddSearchFeature(this IServiceCollection services)
    {
        services.AddOptions<SearchOptions>()
            .Configure<IServiceProvider>((options, sp) =>
            {
                var section = sp.GetService<IConfiguration>()?.GetSection(SearchOptions.Section);
                if (section is not null && section.Exists())
                {
                    if (section.GetSection(nameof(SearchOptions.SuggestedTopics)).Exists())
                    {
                        options.SuggestedTopics.Clear();
                    }
                    section.Bind(options);
                }
            });

        services.AddSingleton<IScopeCheck>(sp => new EmbeddingScopeCheck(
            sp.GetService<IQueryEmbedder>(),
            sp.GetService<IInvestigationCorpus>(),
            sp.GetRequiredService<IOptions<SearchOptions>>(),
            sp.GetService<ILogger<EmbeddingScopeCheck>>() ?? NullLogger<EmbeddingScopeCheck>.Instance));
        services.AddSingleton(sp => new SearchServiceStatus(
            sp.GetRequiredService<Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckService>(),
            sp.GetService<TimeProvider>() ?? TimeProvider.System));
        services.AddScoped<RecentTopics>();
        return services;
    }
}
