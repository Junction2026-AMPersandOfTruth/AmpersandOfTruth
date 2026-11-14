using System.Net;
using System.Runtime.CompilerServices;
using AmpersandOfTruth.Tests.Features.Analysis.Store;
using AmpersandOfTruth.Tests.Features.Articles;
using AmpersandOfTruth.Tests.Features.Investigations;
using AmpersandOfTruth.Tests.Features.Investigations.View;
using AmpersandOfTruth.Web.Features.Articles;
using AmpersandOfTruth.Web.Features.Demo;
using AmpersandOfTruth.Web.Features.Evidence;
using AmpersandOfTruth.Web.Features.Investigations;
using AmpersandOfTruth.Web.Features.Investigations.Overview;
using AmpersandOfTruth.Web.Features.Investigations.Pipeline;
using AmpersandOfTruth.Web.Features.Investigations.View;
using Bunit;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace AmpersandOfTruth.Tests.Features.Demo;

public sealed class StartLimiterTests
{
    [Fact]
    public void Sliding_window_allows_N_refuses_N_plus_1_and_allows_again_after_window()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 14, 16, 0, 0, TimeSpan.Zero));
        var limiter = new StartLimiter(startsPerClientPerMinute: 3, clock);

        Assert.True(limiter.TryAcquire("203.0.113.10"));
        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.True(limiter.TryAcquire("203.0.113.10"));
        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.True(limiter.TryAcquire("203.0.113.10"));

        // 4th attempt within the 1-minute window is refused; another client IP is unaffected.
        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.False(limiter.TryAcquire("203.0.113.10"));
        Assert.True(limiter.TryAcquire("203.0.113.99"));

        // Advance past the 60s mark of the first request (t = 60s): 1 slot reopens, then full again until t = 70s.
        clock.Advance(TimeSpan.FromSeconds(30));
        Assert.True(limiter.TryAcquire("203.0.113.10"));
        Assert.False(limiter.TryAcquire("203.0.113.10"));

        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.True(limiter.TryAcquire("203.0.113.10"));
    }

    [Fact]
    public void Zero_limit_disables_rate_limiting()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 14, 16, 0, 0, TimeSpan.Zero));
        var monitor = new TestOptionsMonitor<DemoOptions>(new DemoOptions { StartsPerClientPerMinute = 0 });
        var limiter = new StartLimiter(monitor, clock);

        for (var i = 0; i < 50; i++)
        {
            Assert.True(limiter.TryAcquire("203.0.113.10"));
        }
    }

    [Fact]
    public void InvestigationPage_captures_client_ip_at_init_and_shows_rate_limited_banner_without_running()
    {
        using var bunit = new BunitContext();
        bunit.JSInterop.Mode = JSRuntimeMode.Loose;

        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 14, 16, 0, 0, TimeSpan.Zero));
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.42");
        var accessor = new HttpContextAccessor { HttpContext = httpContext };

        var countingRunner = new CountingCachedRunner();
        var demoMonitor = new TestOptionsMonitor<DemoOptions>(new DemoOptions { StartsPerClientPerMinute = 2 });

        bunit.Services.AddSingleton<TimeProvider>(clock);
        bunit.Services.AddSingleton<IHttpContextAccessor>(accessor);
        bunit.Services.AddSingleton<IOptionsMonitor<DemoOptions>>(demoMonitor);
        bunit.Services.AddSingleton<StartLimiter>();
        bunit.Services.AddScoped<ClientIdentity>();
        bunit.Services.AddSingleton<IInvestigationRunner>(countingRunner);
        bunit.Services.AddSingleton<IIncidentCorpus>(new FakeIncidentCorpus());
        bunit.Services.AddSingleton<IRevisionStore>(new FakeRevisionStore());
        bunit.Services.AddSingleton<IncidentMapper>();
        bunit.Services.AddSingleton<RecentRuns>();
        bunit.Services.AddSingleton<IOptions<InvestigationSourceOptions>>(
            Options.Create(new InvestigationSourceOptions { Source = IncidentSourceKind.Pipeline }));
        bunit.Services.AddSingleton<IArticleCorpus>(new FakeArticleCorpus());
        bunit.Services.AddSingleton<ArticleTimelineService>();

        // Start #1 (cached replay) succeeds and captures IP at component init.
        var cut = bunit.Render<InvestigationPage>(p => p.Add(x => x.Topic, "topic-one"));
        cut.WaitForAssertion(() => Assert.Equal(1, countingRunner.RunCount));

        // Clear HttpContext after initial render to prove IP stays captured in the scoped service.
        accessor.HttpContext = null;
        Assert.Equal("198.51.100.42", bunit.Services.GetRequiredService<ClientIdentity>().Key);

        // Start #2 succeeds.
        cut.Render(p => p.Add(x => x.Topic, "topic-two"));
        cut.WaitForAssertion(() => Assert.Equal(2, countingRunner.RunCount));

        // Start #3 exceeds StartsPerClientPerMinute=2: shows rate-limited StatusBanner and does NOT run.
        cut.Render(p => p.Add(x => x.Topic, "topic-three"));
        cut.WaitForAssertion(() =>
        {
            var banner = cut.Find("[data-testid=rate-limited]");
            Assert.Contains(StartLimiter.RateLimitedMessage, banner.TextContent);
            Assert.Empty(CopyRules.FindViolations(banner.TextContent, includeJargon: true));
        });
        Assert.Equal(2, countingRunner.RunCount);

        // Advance past the 1-minute window: starting another topic runs again.
        clock.Advance(TimeSpan.FromMinutes(1));
        cut.Render(p => p.Add(x => x.Topic, "topic-four"));
        cut.WaitForAssertion(() =>
        {
            Assert.Equal(3, countingRunner.RunCount);
            Assert.Empty(cut.FindAll("[data-testid=rate-limited]"));
        });
    }

    private sealed class CountingCachedRunner : IInvestigationRunner
    {
        public int RunCount { get; private set; }

        public async IAsyncEnumerable<InvestigationEvent> RunAsync(
            string topic, bool refresh = false, [EnumeratorCancellation] CancellationToken ct = default)
        {
            RunCount++;
            await Task.Yield();
            yield return new ArticlesFound(1, [new TopicArticle("fi", 1256, "Talvisota", 0.05)]);
            yield return new RevisionsScanned(10, []);
            yield return new Completed(FromCache: true, [], TimeSpan.FromMilliseconds(5));
        }
    }
}
