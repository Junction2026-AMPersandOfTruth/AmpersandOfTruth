using AmpersandOfTruth.Tests.Infrastructure;
using AmpersandOfTruth.Web.Features.WebSpread;
using AmpersandOfTruth.Web.Features.WebSpread.Oxylabs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;

namespace AmpersandOfTruth.Tests.Features.WebSpread;

/// <summary>
/// Real Oxylabs calls through the app's DI (real config, a throwaway SQLite cache). Uses a few requests of quota.
/// Skipped unless <c>OXYLABS_TESTS=1</c>. Credentials from user-secrets (<c>Oxylabs:ApiKey</c>, or <c>Oxylabs:Username</c> +
/// <c>Oxylabs:Password</c>) or env (<c>Oxylabs__ApiKey</c> …). Run:
/// <c>OXYLABS_TESTS=1 dotnet test --filter Category=Oxylabs --logger "console;verbosity=detailed"</c>
/// </summary>
[Trait("Category", "Oxylabs")]
public sealed class OxylabsLiveTests(AppFactory app, ITestOutputHelper output) : IClassFixture<AppFactory>
{
    [OxylabsFact]
    public async Task Finnish_exact_phrase_search_returns_results_then_replays_from_cache()
    {
        var mode = app.Services.GetRequiredService<IOptions<OxylabsOptions>>().Value.EffectiveMode;
        var search = app.Services.GetRequiredService<IWebSearchClient>();
        output.WriteLine($"mode={mode} supportsDateWindow={search.SupportsDateWindow}");

        var request = new WebSearchRequest("\"Mainilan laukaukset\" -site:wikipedia.org", "fi") { Scope = $"live-{Guid.NewGuid():N}" };
        var first = await search.SearchAsync(request);
        foreach (var r in first.Results) output.WriteLine($"{r.Rank}. {r.Url} serp_date={r.SerpDate} | {r.Title}");

        Assert.True(first.Outcome == WebCallOutcome.Ok, $"{first.Outcome}: {first.Error}");
        Assert.NotEmpty(first.Results);

        var second = await search.SearchAsync(request);
        Assert.True(second.FromCache);
    }

    [OxylabsFact]
    public async Task Date_windowed_search_is_applied_when_the_product_supports_it()
    {
        var search = app.Services.GetRequiredService<IWebSearchClient>();
        var window = new DateWindow(new DateOnly(2014, 1, 1), new DateOnly(2014, 12, 31));

        var r = await search.SearchAsync(new WebSearchRequest("Mainilan laukaukset", "fi", window));
        foreach (var x in r.Results) output.WriteLine($"{x.Rank}. {x.Url} serp_date={x.SerpDate}");

        Assert.True(r.Outcome == WebCallOutcome.Ok, $"{r.Outcome}: {r.Error}");
        Assert.Equal(search.SupportsDateWindow, r.WindowApplied);
    }

    [OxylabsFact]
    public async Task Page_fetch_returns_text_and_html_for_dating()
    {
        var fetcher = app.Services.GetRequiredService<IPageFetcher>();

        var r = await fetcher.FetchAsync("https://yle.fi/");
        output.WriteLine($"title={r.Page?.Title} text={r.Page?.Text.Length} chars html={r.Page?.Html?.Length} chars");

        Assert.True(r.Outcome == WebCallOutcome.Ok, $"{r.Outcome}: {r.Error}");
        Assert.False(string.IsNullOrWhiteSpace(r.Page!.Text));
    }
}

public sealed class OxylabsFactAttribute : FactAttribute
{
    public OxylabsFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("OXYLABS_TESTS") != "1")
            Skip = "Live Oxylabs test — set OXYLABS_TESTS=1 (and Oxylabs credentials in user-secrets) to enable.";
    }
}
