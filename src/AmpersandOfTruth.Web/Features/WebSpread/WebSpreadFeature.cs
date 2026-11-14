using AmpersandOfTruth.Web.Features.WebSpread.Dating;
using AmpersandOfTruth.Web.Features.WebSpread.Matching;
using AmpersandOfTruth.Web.Features.WebSpread.Oxylabs;
using AmpersandOfTruth.Web.Features.WebSpread.Planning;
using AmpersandOfTruth.Web.Features.WebSpread.Store;
using Microsoft.Extensions.Options;

namespace AmpersandOfTruth.Web.Features.WebSpread;

/// <summary>
/// P3 web spread (epic #5): trace an incident's claim across the web with Oxylabs. Lives on the <c>oxylabs</c> branch
/// until it is demo-ready. Everything is a singleton (shared by all circuits, thread-safe). Without Oxylabs credentials
/// the app runs as before; a trace then ends with <see cref="Spread.WebSpreadStatus.NotConfigured"/>.
/// </summary>
public static class WebSpreadFeature
{
    public static IServiceCollection AddWebSpreadFeature(this IServiceCollection services)
    {
        services.AddOptions<OxylabsOptions>().BindConfiguration(OxylabsOptions.Section);
        services.AddOptions<WebSpreadOptions>().BindConfiguration(WebSpreadOptions.Section);

        // Per-attempt timeouts are enforced by OxylabsClient (OxylabsOptions.RequestTimeout), not HttpClient.Timeout.
        services.AddSingleton(sp => ActivatorUtilities.CreateInstance<OxylabsClient>(sp,
            new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
            {
                Timeout = Timeout.InfiniteTimeSpan,
            }));
        services.AddSingleton<WebSpreadStore>();

        // The app sees only the gateway: skip list → cache → budget → Oxylabs.
        services.AddSingleton(sp =>
        {
            var raw = sp.GetRequiredService<OxylabsClient>();
            return new WebSpreadGateway(raw, raw, sp.GetRequiredService<WebSpreadStore>(),
                sp.GetRequiredService<IOptionsMonitor<OxylabsOptions>>(), sp.GetRequiredService<TimeProvider>(),
                sp.GetRequiredService<ILogger<WebSpreadGateway>>());
        });
        services.AddSingleton<IWebSearchClient>(sp => sp.GetRequiredService<WebSpreadGateway>());
        services.AddSingleton<IPageFetcher>(sp => sp.GetRequiredService<WebSpreadGateway>());

        services.AddSingleton<IWebArchive>(sp => ActivatorUtilities.CreateInstance<WaybackArchive>(sp,
            new HttpClient { Timeout = TimeSpan.FromSeconds(30) }));

        services.AddSingleton<ClaimSearchPlanner>();
        services.AddSingleton<ClaimMatcher>();
        services.AddSingleton<WebSpreadTracer>();
        return services;
    }
}
