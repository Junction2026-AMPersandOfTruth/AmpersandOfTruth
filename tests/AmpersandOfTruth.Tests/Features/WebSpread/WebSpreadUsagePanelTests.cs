using AmpersandOfTruth.Tests.Infrastructure;
using AmpersandOfTruth.Web.Features.WebSpread;
using AmpersandOfTruth.Web.Features.WebSpread.Store;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace AmpersandOfTruth.Tests.Features.WebSpread;

public sealed class WebSpreadUsagePanelTests : BunitContext
{
    private readonly TestDb _db = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 14, 10, 0, 0, TimeSpan.Zero));
    private readonly OxylabsOptions _options = new() { ApiKey = "k", DailyRequestCap = 1000, PerScopeRequestCap = 60 };
    private readonly WebSpreadStore _store;

    public WebSpreadUsagePanelTests()
    {
        _store = new WebSpreadStore(_db.Factory, _time);
        Services.AddSingleton(_store);
        Services.AddSingleton<IOptionsMonitor<OxylabsOptions>>(new StaticOptions<OxylabsOptions>(_options));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _db.Dispose();
        base.Dispose(disposing);
    }

    private Task Record(bool live, bool fromCache) => _store.RecordAsync(new WebCallRecord
    {
        At = _time.GetUtcNow(), Scope = "webspread:inc-1", Kind = "fetch", Outcome = WebCallOutcome.Ok,
        Live = live, FromCache = fromCache, Request = "https://a.example/",
    });

    [Fact]
    public async Task Shows_live_requests_against_the_daily_cap_and_cache_hits_and_updates_while_a_trace_runs()
    {
        await Record(live: true, fromCache: false);
        await Record(live: false, fromCache: true);

        var cut = Render<WebSpreadUsagePanel>();

        cut.WaitForAssertion(() => Assert.Equal("1 / 1,000", cut.Find("[data-testid=web-usage-live]").TextContent.Trim()));
        Assert.Equal("1", cut.Find("[data-testid=web-usage-cache]").TextContent.Trim());
        Assert.Contains("Per-incident cap: 60", cut.Markup);

        await Record(live: true, fromCache: false); // the ledger's Changed event refreshes the counter

        cut.WaitForAssertion(() => Assert.Equal("2 / 1,000", cut.Find("[data-testid=web-usage-live]").TextContent.Trim()));
    }

    [Fact]
    public void Says_when_web_search_is_not_configured()
    {
        _options.ApiKey = null;

        var cut = Render<WebSpreadUsagePanel>();

        cut.WaitForAssertion(() => Assert.Contains("not configured", cut.Find("[data-testid=web-usage]").TextContent));
    }

    [Fact]
    public void Renders_nothing_without_the_feature()
    {
        using var bare = new BunitContext();
        var cut = bare.Render<WebSpreadUsagePanel>();
        Assert.Empty(cut.FindAll("[data-testid=web-usage]"));
    }
}
